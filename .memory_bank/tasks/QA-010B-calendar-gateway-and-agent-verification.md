# QA Task 010B: Calendar Gateway Endpoints & Agent ActionCard Verification

## 1. Objective
Verify the end-to-end integration of AI Gateway endpoints, deterministic C# `ActionCard` generation in `FotoprintAiAgent`, and the Stateless Human-in-the-Loop booking lifecycle.

---

## 2. Edge Case Matrix & Verification Checklist

### EC-10: Deterministic C# ActionCard in AI Agent Response
- **Condition**: User sends chat prompt asking to schedule a meeting:
  `"Назначь встречу с Кузнецовым на 30 минут завтра"`
- **Verification**:
  - Model must execute tool `find_free_slots`.
  - C# gateway intercepts output and attaches `action_card`:
    * `action_card.type == "slot_selection"`
    * `action_card.slots` is a non-empty array of `{ start, end, iso_start, iso_end }`.
    * `action_card.duration_buttons` has `15`, `30`, `45`, `60` min options.
  - JSON parse test: verifying `action_card` is valid JSON and not raw markdown text embedded in chat.

---

### EC-11: Stateless Human-in-the-Loop Integrity Gate (Crucial)
- **Step 1**: Count existing records in PostgreSQL:
  ```bash
  psql -h localhost -p 5434 -U fotoprint -d fotoprint -t -c "SELECT COUNT(*) FROM meetings;"
  ```
  Note value $C_0$.
- **Step 2**: Trigger AI agent slot search and slot selection.
- **Step 3**: Re-count records in PostgreSQL:
  ```bash
  psql -h localhost -p 5434 -U fotoprint -d fotoprint -t -c "SELECT COUNT(*) FROM meetings;"
  ```
  Assert value is **strictly equal to $C_0$** (ZERO temporary/draft meetings created).
- **Step 4**: Execute explicit booking confirmation:
  ```bash
  curl -s -X POST http://localhost:8899/api/calendar/confirm \
    -H "Content-Type: application/json" \
    -H "X-Telegram-Id: 100001" \
    -d '{
      "title": "QA Test Meeting",
      "startTime": "2026-11-20T14:30:00+03:00",
      "endTime": "2026-11-20T15:00:00+03:00",
      "organizerId": 1,
      "participantIds": [2]
    }' | jq .
  ```
- **Step 5**: Re-count records in PostgreSQL:
  Assert value is **strictly equal to $C_0 + 1$**.

---

### EC-12: Monthly Calendar Grid Aggregation (`/api/calendar/events`)
- **Condition**: Request monthly events for November 2026:
  ```bash
  curl -s "http://localhost:8899/api/calendar/events?start_date=2026-11-01&end_date=2026-11-30" \
    -H "X-Telegram-Id: 100001" | jq .
  ```
- **Verification**:
  - Response contains meetings mapped to ISO dates.
  - Includes created QA test meeting from EC-11.
  - Returns `density_level` or count per day for front-end green/yellow/red dots.

---

### EC-13: Full Regression Suite Check
- **Command**:
  ```bash
  DOTNET_ROLL_FORWARD=Major dotnet test tests/Fotoprint.Tests/Fotoprint.Tests.csproj
  ```
- **Expectation**: 90+ tests passed, 0 failed, 0 skipped.

---

## 3. Definition of Done for QA-010B
1. [ ] Stateless integrity verified (zero DB inserts before user click).
2. [ ] `ActionCard` validated as strictly typed JSON object.
3. [ ] Full test suite execution report logged with 0 errors.
