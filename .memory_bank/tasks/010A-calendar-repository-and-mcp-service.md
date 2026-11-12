# Task 010A: Calendar Repository, MCP Service & Slot Algorithm (Port 8084)

## 1. Goal
Implement the repository layer, slot calculation algorithm with past-hour cutoff, notification stub, and MCP tools in `Fotoprint.Mcp.Calendar` according to `docs/calendar_integration/01_ARCHITECTURE_AND_CONTRACTS.md` and `02_PHASE_1_BACKEND_AND_MCP.md`.

## 2. Boundaries & Scope
- **Target Files**:
  - `src/Fotoprint.Core/Data/IFotoprintRepository.cs` & `src/Fotoprint.Core/Data/FotoprintRepository.cs`:
    - Add `TimeSlot` record/class (`StartTimeUtc`, `EndTimeUtc`, `Label`).
    - Implement `FindFreeSlotsAsync(organizerId, participantIds, targetDate, durationMinutes, workingStart, workingEnd, bufferMinutes, ct)`:
      - MSK timezone (`Europe/Moscow` / `Russian Standard Time`).
      - Cutoff past slots if `targetDate == todayMsk` (earliest slot > nowMsk + 5 min).
      - Respect 10-minute buffer between meetings.
    - Implement `GetMeetingsForRangeAsync(DateTime fromUtc, DateTime toUtc, int? employeeId = null, CancellationToken ct = default)`.
  - `docker-compose.yml`:
    - Add port mapping `127.0.0.1:8084:8084` to `fotoprint-mcp-calendar`.
  - `src/Fotoprint.Core/Configuration/FotoprintConfig.cs` & `config.yaml`:
    - Add `CalendarConfig` (Timezone, WorkingHours, DefaultDuration, Buffer).
  - `src/Fotoprint.Mcp.Calendar/Program.cs`:
    - Expose `POST /tools/find_free_slots`.
    - Expose `POST /tools/create_meeting`:
      - Atomically insert `meetings` record (`status: 'scheduled'`).
      - Attach `metadata.notifications` stub with simulated status.
      - Return `notification_dispatch` stub in JSON.
    - Expose `GET /tools/get_calendar_events`:
      - Support `scope=my` (returns caller's meetings).
      - Support `scope=all` with strict RBAC: allow only `Director` or `Admin` roles (via `ISecurityValidator`), return HTTP 403 Forbidden for others.
  - `tests/Fotoprint.Tests/`:
    - Add unit tests for `FindFreeSlotsAsync` (collision, 10-min buffer, past time cutoff).
    - Maintain existing 90/90 tests passing (update `FakeFotoprintRepository` in `FotoprintAiAgentTests.cs` to implement new methods).

## 3. Acceptance Criteria
- [ ] `curl -f http://127.0.0.1:8084/health` returns HTTP 200.
- [ ] `POST /tools/find_free_slots` excludes past hours for today with 5-minute buffer.
- [ ] `POST /tools/create_meeting` inserts meeting and returns `notification_dispatch.status == "simulated"`.
- [ ] `GET /tools/get_calendar_events?scope=all` returns 200 for Director (100001) and 403 for Guest (999999).
- [ ] `dotnet test` passes 100% of unit tests with 0 errors.
