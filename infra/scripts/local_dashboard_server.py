#!/usr/bin/env python3
"""
Local development server for Fotoprint Web UI + Track 1 Dashboard Backend (/api/system/stats, /api/decisions, /api/agent/users).
Mirrors the C# DashboardEndpoints.cs contract when running without Docker/.NET SDK.
"""
import json
import os
from datetime import datetime, timezone, timedelta
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HTML_ROOT = os.path.join(REPO_ROOT, "infra", "nginx", "html")

EMPLOYEES = {
    100001: {
        "masterId": 100001,
        "telegramId": 100001,
        "name": "Алексей Смирнов",
        "role": "Director",
        "roleLabel": "Генеральный директор",
        "department": "Руководство",
        "email": "a.smirnov@fotoprint.by",
        "allowedDashboards": ["pulse", "sales", "production", "finance", "decisions"],
    },
    100002: {
        "masterId": 100002,
        "telegramId": 100002,
        "name": "Иван Кузнецов",
        "role": "Production",
        "roleLabel": "Начальник производства",
        "department": "Производство",
        "email": "i.kuznetsov@fotoprint.by",
        "allowedDashboards": ["pulse", "production", "decisions"],
    },
    100003: {
        "masterId": 100003,
        "telegramId": 100003,
        "name": "Елена Морозова",
        "role": "Sales",
        "roleLabel": "Руководитель отдела продаж",
        "department": "Продажи",
        "email": "e.morozova@fotoprint.by",
        "allowedDashboards": ["pulse", "sales", "decisions"],
    },
    100004: {
        "masterId": 100004,
        "telegramId": 100004,
        "name": "Ольга Соколова",
        "role": "Accounting",
        "roleLabel": "Главный бухгалтер",
        "department": "Бухгалтерия",
        "email": "o.sokolova@fotoprint.by",
        "allowedDashboards": ["pulse", "finance", "decisions"],
    },
}

DECISIONS = [
    {
        "id": "DEC-104",
        "problem": "Брак на линии фотокниг Premium вырос до 8,1% (норма <= 5%)",
        "impact": "Потери маржи ~4 200 BYN/нед., риск срыва SLA доставки",
        "cause": "Партия мелованной бумаги 170 г/м² с повышенной влажностью + увод прижима на Kolbus",
        "action": "Заменить партию бумаги со склада B2, провести калибровку Kolbus и тестовый тираж 30 кн.",
        "owner": "Иван Кузнецов · Производство",
        "dueDate": "2026-11-14",
        "priority": "Высокий",
        "status": "В работе",
        "beforeKpi": "8,1% брака",
        "afterKpi": "Ожидается <= 4,8% (проверка 15.11)",
        "department": "production",
    },
    {
        "id": "DEC-103",
        "problem": "Ожидаемый дефицит мощности −640 ч на 47-й неделе (преддверие пика сезона)",
        "impact": "Риск увеличения Lead Time с 2,8 до 4,5 дней по фотокнигам и календарям",
        "cause": "Сезонный рост спроса + 2 открытые вакансии операторов послепечатного участка",
        "action": "Вывести дополнительную смену на выходные и перераспределить тиражи Lite на линию B",
        "owner": "Алексей Смирнов · Руководство",
        "dueDate": "2026-11-18",
        "priority": "Высокий",
        "status": "Назначено",
        "beforeKpi": "Дефицит −640 ч",
        "afterKpi": "Прогноз после меры: +80 ч резерва",
        "department": "production",
    },
    {
        "id": "DEC-101",
        "problem": "Просадка повторных покупок в B2C сегменте на 2,8 п.п.",
        "impact": "Недобор ~18 500 BYN оборота за 30 дней",
        "cause": "Задержка триггерной рассылки после вручения заказа в Битрикс24",
        "action": "Исправить вебхук статуса delivered → запуск промокода на следующую фотокнигу через 48 ч",
        "owner": "Елена Морозова · Продажи",
        "dueDate": "2026-11-10",
        "priority": "Средний",
        "status": "Проверено",
        "beforeKpi": "38,9% повторных",
        "afterKpi": "41,2% (+2,3 п.п. подтверждено)",
        "department": "sales",
    },
]

IDEMPOTENCY = {}


class DashboardHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=HTML_ROOT, **kwargs)

    def _send_json(self, status, payload):
        raw = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def _resolve_employee(self, qs):
        tg_hdr = self.headers.get("X-Telegram-Id") or self.headers.get("X-Master-Id")
        tg_q = qs.get("telegram_id", [None])[0] or qs.get("master_id", [None])[0]
        raw_id = tg_q or tg_hdr
        try:
            emp_id = int(raw_id) if raw_id else 100001
        except ValueError:
            emp_id = 100001
        return EMPLOYEES.get(emp_id, EMPLOYEES[100001])

    def do_GET(self):
        parsed = urlparse(self.path)
        qs = parse_qs(parsed.query)

        if parsed.path == "/dashboard":
            suffix = f"?{parsed.query}" if parsed.query else ""
            self.send_response(302)
            self.send_header("Location", f"/dashboard/{suffix}")
            self.end_headers()
            return

        if parsed.path == "/api/agent/users":
            users = [
                {
                    "employee_id": idx + 1,
                    "telegram_id": emp["telegramId"],
                    "full_name": emp["name"],
                    "department": emp["department"],
                    "position": emp["roleLabel"],
                    "roles": [emp["role"]],
                    "permissions": ["*"] if emp["role"] == "Director" else [f"dashboard:{d}" for d in emp["allowedDashboards"]],
                }
                for idx, emp in enumerate(EMPLOYEES.values())
            ]
            self._send_json(200, {"users": users})
            return

        if parsed.path == "/api/decisions":
            self._send_json(200, {"decisions": DECISIONS})
            return

        if parsed.path == "/api/system/stats":
            accept = self.headers.get("Accept", "")
            if "dashboard" not in qs and "format" not in qs and "text/html" in accept and "application/json" not in accept:
                suffix = f"?{parsed.query}" if parsed.query else ""
                self.send_response(302)
                self.send_header("Location", f"/dashboard/{suffix}")
                self.end_headers()
                return

            emp = self._resolve_employee(qs)
            screen = (qs.get("dashboard", ["pulse"])[0] or "pulse").lower()
            if screen not in emp["allowedDashboards"]:
                self._send_json(
                    403,
                    {
                        "error": "Access Denied",
                        "message": f"403 Forbidden: раздел «{screen}» закрыт для роли {emp['roleLabel']} ({emp['department']}) — УП-05",
                        "required_permission": f"dashboard:{screen}",
                        "employee": emp,
                    },
                )
                return

            now_iso = datetime.now(timezone(timedelta(hours=3))).isoformat(timespec="seconds")
            self._send_json(
                200,
                {
                    "service": "Fotoprint.AiGateway.Api",
                    "currency": "BYN",
                    "timezone": "Europe/Minsk",
                    "updatedAt": now_iso,
                    "employee": emp,
                    "dashboard": screen,
                    "allowedDashboards": emp["allowedDashboards"],
                    "decisions": DECISIONS,
                    "llm_config": {
                        "provider": "nvidia",
                        "chat_model": "nvidia/nemotron-3.5-super-vl-120b-a12b",
                        "rpm_limit": 15,
                    },
                },
            )
            return

        super().do_GET()

    def do_POST(self):
        parsed = urlparse(self.path)
        if parsed.path == "/api/decisions":
            length = int(self.headers.get("Content-Length", "0"))
            body = json.loads(self.rfile.read(length).decode("utf-8") or "{}")
            idem_key = self.headers.get("Idempotency-Key")
            if idem_key and idem_key in IDEMPOTENCY:
                self._send_json(200, {"idempotent_replay": True, "decision": IDEMPOTENCY[idem_key], "decisions": DECISIONS})
                return

            new_item = {
                "id": f"DEC-{105 + len(DECISIONS)}",
                "problem": body.get("problem", ""),
                "impact": body.get("impact", "Влияние зафиксировано в карточке KPI"),
                "cause": body.get("cause", ""),
                "action": body.get("action", ""),
                "owner": body.get("owner", "Иван Кузнецов · Производство"),
                "dueDate": body.get("dueDate", "2026-11-16"),
                "priority": body.get("priority", "Высокий"),
                "status": "Назначено",
                "beforeKpi": "Зафиксирован срез KPI",
                "afterKpi": f"Проверка {body.get('dueDate', '2026-11-16')}",
                "department": body.get("department", "production"),
            }
            DECISIONS.insert(0, new_item)
            if idem_key:
                IDEMPOTENCY[idem_key] = new_item
            self._send_json(201, {"idempotent_replay": False, "decision": new_item, "decisions": DECISIONS})
            return

        self._send_json(404, {"error": "Not Found"})


if __name__ == "__main__":
    server = ThreadingHTTPServer(("127.0.0.1", 8765), DashboardHandler)
    print("Listening on http://127.0.0.1:8765")
    server.serve_forever()
