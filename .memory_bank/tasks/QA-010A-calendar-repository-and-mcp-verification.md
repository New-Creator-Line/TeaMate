# QA Task 010A: Calendar Repository, MCP Service & Slot Algorithm Verification

## 1. Objective
Execute rigorous, empirical, anti-hallucination verification of Task 010A (PostgreSQL repository, `FindFreeSlotsAsync` collision engine, timezone handling, notification stub, and `Fotoprint.Mcp.Calendar` port 8084).

---

## 2. Edge Case Matrix & Verification Checklist

### EC-01: Strict Working Hours Boundary (09:00 - 18:00 MSK)
- **Condition**: Request slots with duration = 30 minutes on a completely free day.
- **Verification**:
  - The first slot MUST be `09:00 - 09:30`.
  - The last slot MUST end at `18:00` (i.e. `17:30 - 18:00`).
  - No slot may start prior to `09:00` or end after `18:00`.
- **Command**:
  ```bash
  curl -s -X POST http://127.0.0.1:8084/tools/find_free_slots \
    -H "Content-Type: application/json" \
    -H "X-Internal-Service-Key: local-dev-service-key" \
    -H "X-Telegram-Id: 100001" \
    -d '{
      "organizer_employee_id": 1,
      "participant_employee_ids": [2],
      "date": "2026-11-20",
      "duration_minutes": 30,
      "working_start": "09:00",
      "working_end": "18:00",
      "buffer_minutes": 10
    }' | jq .
  ```

---

### EC-02: 10-Minute Buffer Collision Arithmetic
- **Condition**: Participant 2 has an existing scheduled meeting from `14:00` to `14:30`.
- **Mathematical Expectation**:
  - Protected interval with buffer: `[13:50, 14:40]`.
  - Slot `13:30 - 14:00`: ends at 14:00 > 13:50 $\implies$ **EXCLUDED**.
  - Slot `13:00 - 13:30`: ends at 13:30 <= 13:50 $\implies$ **AVAILABLE**.
  - Slot `14:30 - 15:00`: starts at 14:30 < 14:40 $\implies$ **EXCLUDED**.
  - Slot `14:40 - 15:10` (or `15:00 - 15:30` on 30m grid) $\implies$ **AVAILABLE**.
- **Verification Method**: Unit test in `tests/Fotoprint.Tests/` asserting that `available_slots` does NOT contain `13:30` or `14:30`.

---

### EC-03: Past-Time Cutoff for Today's Date
- **Condition**: Target date is `today` (current date in MSK).
- **Expectation**:
  - All slots whose start time is before `DateTime.UtcNow(MSK).AddMinutes(5)` must be pruned.
  - If current MSK time is 15:20, no slot starting at or before 15:25 may appear in `available_slots`.
- **Verification Command**:
  ```bash
  TODAY=$(TZ="Europe/Moscow" date +%Y-%m-%d)
  curl -s -X POST http://127.0.0.1:8084/tools/find_free_slots \
    -H "Content-Type: application/json" \
    -H "X-Internal-Service-Key: local-dev-service-key" \
    -H "X-Telegram-Id: 100001" \
    -d "{
      \"organizer_employee_id\": 1,
      \"participant_employee_ids\": [2],
      \"date\": \"$TODAY\",
      \"duration_minutes\": 30
    }" | jq '{date, available_slots: [.available_slots[0], .available_slots[-1]]}'
  ```

---

### EC-04: Multi-Participant Union of Busy Schedules
- **Condition**:
  - Participant 2 is busy `10:00 - 11:00`.
  - Participant 3 is busy `14:00 - 15:00`.
  - Organizer 1 is busy `16:00 - 17:00`.
- **Expectation**: A 3-way meeting search for `[1, 2, 3]` must exclude ALL THREE intervals (plus 10m buffers).

---

### EC-05: Completely Booked Day (Zero Slots)
- **Condition**: All working hours are booked.
- **Expectation**:
  - `available_slots`: `[]`.
  - `has_free_slots`: `false`.
  - API returns HTTP 200 without throwing exceptions or null references.

---

### EC-06: RBAC Authorization Gate (`scope=all` vs `scope=my`)
- **Director (100001)**:
  ```bash
  curl -s -w "%{http_code}\n" -H "X-Internal-Service-Key: local-dev-service-key" -H "X-Telegram-Id: 100001" "http://127.0.0.1:8084/tools/get_calendar_events?scope=all"
  # MUST return 200
  ```
- **Guest (999999)**:
  ```bash
  curl -s -w "%{http_code}\n" -H "X-Internal-Service-Key: local-dev-service-key" -H "X-Telegram-Id: 999999" "http://127.0.0.1:8084/tools/get_calendar_events?scope=all"
  # MUST return 403
  ```

---

### EC-07: Notification Dispatch Stub Validation
- **Condition**: Call `POST /tools/create_meeting`.
- **Expectation**:
  - HTTP 200 with valid `meeting_id`.
  - JSON contains `notification_dispatch.status == "simulated"`.
  - PostgreSQL query verifies `metadata->'notifications'->>'status'` equals `"simulated"`.

---

## 3. Definition of Done for QA-010A
1. [ ] All 7 Edge Cases verified with real curl/unit test executions.
2. [ ] Zero regression on existing 90 unit tests (`dotnet test`).
3. [ ] Signed Proof-of-Verification report submitted to Orchestrator.
