import json
import os
import re
import time
import urllib.parse
import urllib.request
from datetime import datetime, timezone

_SAFE_TS_RE = re.compile(r"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(\.\d+)?Z?$")


def safe_ts(val: str | None) -> str:
    if not val or not _SAFE_TS_RE.match(str(val)):
        raise ValueError(f"Invalid watermark timestamp format: {val!r}")
    return str(val)

CLICKHOUSE_URL = os.environ.get("CLICKHOUSE_URL", "http://clickhouse:8123")
CLICKHOUSE_USER = os.environ.get("CLICKHOUSE_USER", "clickhouse")
CLICKHOUSE_PASSWORD = os.environ.get("CLICKHOUSE_PASSWORD", "clickhouse_secret")
LOKI_URL = os.environ.get("LOKI_URL", "http://loki:3100/loki/api/v1/push")
POLL_INTERVAL = int(os.environ.get("POLL_INTERVAL_SECONDS", "10"))
BACKFILL_HOURS = int(os.environ.get("BACKFILL_HOURS", "168"))


def ch_query(sql: str) -> list[dict]:
    url = f"{CLICKHOUSE_URL}/?database=default&default_format=JSONEachRow"
    data = sql.encode("utf-8")
    req = urllib.request.Request(url, data=data, method="POST")
    req.add_header("X-ClickHouse-User", CLICKHOUSE_USER)
    req.add_header("X-ClickHouse-Key", CLICKHOUSE_PASSWORD)
    with urllib.request.urlopen(req, timeout=15) as resp:
        raw = resp.read().decode("utf-8", errors="replace")
    rows = []
    for line in raw.splitlines():
        line = line.strip()
        if line:
            rows.append(json.loads(line))
    return rows


def to_ns(ts_str: str | None) -> str:
    if not ts_str:
        return str(int(time.time() * 1_000_000_000))
    # ClickHouse DateTime64(3) format: "2026-11-12 08:57:40.123"
    s = ts_str.replace("T", " ").rstrip("Z")
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            dt = datetime.strptime(s, fmt).replace(tzinfo=timezone.utc)
            return str(int(dt.timestamp() * 1_000_000_000))
        except ValueError:
            continue
    return str(int(time.time() * 1_000_000_000))


def push_to_loki(streams_map: dict[tuple, list[list[str]]]) -> None:
    if not streams_map:
        return
    streams = []
    for labels_tuple, values in streams_map.items():
        if not values:
            continue
        values.sort(key=lambda x: x[0])
        streams.append({
            "stream": dict(labels_tuple),
            "values": values,
        })
    if not streams:
        return
    payload = json.dumps({"streams": streams}, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(LOKI_URL, data=payload, method="POST")
    req.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(req, timeout=15) as resp:
        resp.read()


def truncate(val: str | None, limit: int = 300) -> str:
    if not val:
        return ""
    s = str(val).replace("\r", " ").replace("\n", " ")
    return s[:limit] + ("..." if len(s) > limit else "")


def sync_once(watermarks: dict[str, str]) -> None:
    streams: dict[tuple, list[list[str]]] = {}

    def add_entry(labels: dict[str, str], ts_str: str, obj: dict) -> None:
        key = tuple(sorted(labels.items()))
        streams.setdefault(key, []).append([to_ns(ts_str), json.dumps(obj, ensure_ascii=False)])

    # 1. Traces
    wm_traces = safe_ts(watermarks["traces"])
    traces = ch_query(
        f"""
        SELECT id, timestamp, event_ts, name, user_id, tags, input, output
        FROM traces FINAL
        WHERE is_deleted = 0 AND event_ts > parseDateTime64BestEffort('{wm_traces}', 3)
        ORDER BY event_ts ASC
        LIMIT 500
        """
    )
    for row in traces:
        ts = row.get("timestamp") or row.get("event_ts")
        add_entry(
            {"service": "langfuse", "kind": "trace", "level": "INFO"},
            ts,
            {
                "kind": "trace",
                "trace_id": row.get("id"),
                "name": row.get("name"),
                "user_id": row.get("user_id") or "",
                "tags": ",".join(row.get("tags") or []),
                "input_preview": truncate(row.get("input"), 200),
                "output_preview": truncate(row.get("output"), 200),
            },
        )
        watermarks["traces"] = safe_ts(row["event_ts"])

    # 2. Observations (Generations, Spans, Events, Errors)
    wm_obs = safe_ts(watermarks["observations"])
    observations = ch_query(
        f"""
        SELECT
            id, trace_id, type, name, level, status_message,
            provided_model_name, start_time, end_time, event_ts,
            usage_details, total_cost, input, output
        FROM observations FINAL
        WHERE is_deleted = 0 AND event_ts > parseDateTime64BestEffort('{wm_obs}', 3)
        ORDER BY event_ts ASC
        LIMIT 1000
        """
    )
    for row in observations:
        ts = row.get("start_time") or row.get("event_ts")
        usage = row.get("usage_details") or {}
        in_tok = int(usage.get("input", 0) or 0)
        out_tok = int(usage.get("output", 0) or 0)
        tot_tok = int(usage.get("total", in_tok + out_tok) or 0)
        dur_ms = 0
        if row.get("start_time") and row.get("end_time"):
            dur_ms = max(0, (int(to_ns(row["end_time"])) - int(to_ns(row["start_time"]))) // 1_000_000)
        lvl = (row.get("level") or "DEFAULT").upper()
        if lvl == "DEFAULT":
            lvl = "INFO"
        add_entry(
            {"service": "langfuse", "kind": "observation", "level": lvl},
            ts,
            {
                "kind": "observation",
                "obs_type": row.get("type"),
                "trace_id": row.get("trace_id"),
                "name": row.get("name"),
                "level": lvl,
                "model": row.get("provided_model_name") or "",
                "duration_ms": dur_ms,
                "input_tokens": in_tok,
                "output_tokens": out_tok,
                "total_tokens": tot_tok,
                "total_cost": float(row.get("total_cost") or 0),
                "status_message": truncate(row.get("status_message"), 300),
                "input_preview": truncate(row.get("input"), 150),
                "output_preview": truncate(row.get("output"), 150),
            },
        )
        watermarks["observations"] = safe_ts(row["event_ts"])

    # 3. Scores (Faithfulness, Formatting, Security Alerts, RAG Found)
    wm_scores = safe_ts(watermarks["scores"])
    scores = ch_query(
        f"""
        SELECT id, timestamp, event_ts, trace_id, name, value, string_value, comment
        FROM scores FINAL
        WHERE is_deleted = 0 AND event_ts > parseDateTime64BestEffort('{wm_scores}', 3)
        ORDER BY event_ts ASC
        LIMIT 500
        """
    )
    for row in scores:
        ts = row.get("timestamp") or row.get("event_ts")
        name = row.get("name") or ""
        val = float(row.get("value") or 0)
        lvl = "INFO"
        if "security_alert" in name or (name in ("faithfulness", "formatting_and_policy") and val < 0.5):
            lvl = "ERROR"
        add_entry(
            {"service": "langfuse", "kind": "score", "level": lvl},
            ts,
            {
                "kind": "score",
                "trace_id": row.get("trace_id"),
                "score_name": name,
                "score_value": val,
                "string_value": row.get("string_value") or "",
                "comment": truncate(row.get("comment"), 300),
            },
        )
        watermarks["scores"] = safe_ts(row["event_ts"])

    # 4. ClickHouse Query Errors (system.query_log)
    wm_ch = safe_ts(watermarks["ch_errors"])
    ch_errors = ch_query(
        f"""
        SELECT event_time_microseconds, type, query_duration_ms, exception_code, exception, query
        FROM system.query_log
        WHERE exception_code != 0 AND event_time_microseconds > parseDateTime64BestEffort('{wm_ch}', 6)
        ORDER BY event_time_microseconds ASC
        LIMIT 200
        """
    )
    for row in ch_errors:
        ts = row.get("event_time_microseconds")
        add_entry(
            {"service": "clickhouse_query_error", "kind": "query_error", "level": "ERROR"},
            ts,
            {
                "kind": "query_error",
                "type": row.get("type"),
                "duration_ms": row.get("query_duration_ms"),
                "exception_code": row.get("exception_code"),
                "exception": truncate(row.get("exception"), 400),
                "query": truncate(row.get("query"), 250),
            },
        )
        watermarks["ch_errors"] = safe_ts(ts)

    push_to_loki(streams)


def main() -> None:
    since_ts = datetime.fromtimestamp(time.time() - BACKFILL_HOURS * 3600, tz=timezone.utc).strftime("%Y-%m-%d %H:%M:%S.000")
    watermarks = {
        "traces": since_ts,
        "observations": since_ts,
        "scores": since_ts,
        "ch_errors": since_ts,
    }
    print(f"[clickhouse-to-loki] Starting sync from {since_ts}", flush=True)
    while True:
        try:
            sync_once(watermarks)
        except Exception as exc:
            print(f"[clickhouse-to-loki] Sync error: {exc}", flush=True)
        time.sleep(POLL_INTERVAL)


if __name__ == "__main__":
    main()

