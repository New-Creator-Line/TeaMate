# Task 015A: Calendar Backend Enrichment — Tasks, Busy Status & Weekly Seed Schedule

## 1. Scope & Assigned Worktree
- **Role**: Backend API & MCP Developer (`coder-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Phase**: Phase 1 (Core Calendar Functionality)

## 2. File Boundaries (Strict Allowlist)
You may ONLY view and modify:
- `src/Fotoprint.Mcp.Calendar/Program.cs`
- `src/Fotoprint.Core/Data/FotoprintRepository.cs`
- `src/Fotoprint.Core/Models/Entities.cs`
- `infra/postgres/init/03_seed_data.sql`
- `tests/Fotoprint.Tests/` (to add or update calendar tests while keeping all existing tests green)

*Do NOT modify `infra/nginx/html/**` in this task.*

## 3. Frozen Contract Reference
Read and implement the backend section of `.memory_bank/contracts/ticktick_calendar_window_contract.md`.

### Required Behavior in `GET /tools/get_calendar_events` (Proxied by `/api/calendar/events`):
1. **Preserve Backward Compatibility**:
   - Keep `status: "success"`, `scope`, and `events` array with `meeting_id`, `title`, `description`, `start_time`, `end_time`, `status`, `organizer_id`, `participant_ids`.
   - Maintain strict RBAC check: if `scope == "all"` and caller is not `Director` or `Admin`, return `403 Forbidden`.
2. **Enrich Each Event Item**:
   - Resolve employee names from `GetAllEmployeesAsync()` so each event includes:
     - `id`: string/GUID
     - `item_type`: `"meeting"` | `"task"` | `"routine"`
     - `category`: `"production"` | `"sales"` | `"accounting"` | `"management"` | `"tasks"` | `"routine"`
     - `is_all_day`: boolean
     - `is_recurring`: boolean
     - `organizer_name`: full name or short name (`Смирнов А.С.`)
     - `participants`: array of formatted employee names (`["Смирнов А.С.", "Кузнецов И.П."]`)
     - `tags`: array of string tags (e.g., `["heidelberg", "заказ-44102"]`)
3. **Include Action Items (Tasks) on the Calendar**:
   - Query `action_items` for the date range (or uncompleted tasks with deadlines in the range) and map them into calendar items (`item_type = "task"`, `category = "tasks"`) so tasks appear alongside meetings on the timeline.
4. **Rich Demo / Seed Schedule for the Reference Week (Nov 12–18, 2026)**:
   - When querying the reference week (or when the DB has only minimal seed rows), provide a realistic, rich set of corporate Fotoprint schedule items + reference blocks across **Thu Nov 12 – Wed Nov 18, 2026** that mirrors the visual structure of `Интерфейс календаря Reference.png`:
     - **All-Day / Daily Routine Bars** across days 12–18 (e.g., `Утренняя сводка цеха / Поощрение себя`, `Контроль очереди Битрикс24`, `Проверка норм брака Heidelberg`).
     - **Thu Nov 12**:
       - `10:05 – 11:30`: `Оперативка производства: тираж №44102 (ЛР АМД)` (`production`, participants: `[1, 2, 3]`)
       - `12:00 – 13:25`: `Планерка ОМО / Снабжение бумаги` (`production`, participants: `[1, 2]`)
       - `12:00 – 12:30` (Overlapping task!): `Отправить спецификацию валов в Битрикс24` (`item_type: "task"`, `participants: [2]`)
       - `13:35 – 15:00`: `Техконтроль печатного цеха Heidelberg (ПЗ ФизК)` (`production`, participants: `[2]`)
       - `15:30 – 16:55`: `Согласование коммерческих условий Альфа-Маркет (ЛК АМД)` (`sales`, participants: `[1, 3]`)
       - `19:00 – 19:30`: `Вечерний чек-ин смены` (`item_type: "task"`, `participants: [1, 2]`)
     - **Fri Nov 13**:
       - `19:00 – 20:25`: `Итоги недели по отгрузкам (ЛК МПДиС)` (`sales`, participants: `[1, 3]`)
     - **Sat Nov 14**:
       - `12:00 – 13:25`: `Аудит профилактики валов (ЛР АМД)` (`production`, participants: `[2]`)
       - `13:35 – 15:00`: `Сверка складских остатков и БД (ЛР СУБД)` (`accounting`, participants: `[4]`)
     - **Mon Nov 16**:
       - `17:05 – 18:30`: `Финансовый комитет и закрытие счетов (ЛР СУБД)` (`accounting`, participants: `[1, 4]`)
       - `19:00 – 20:25`: `План продаж корпоративным клиентам (ЛК ОМО)` (`sales`, participants: `[1, 3]`)
     - **Tue Nov 17**:
       - `15:30 – 16:55`: `Калибровка цветопробы цеха (ПЗ ФизК)` (`production`, participants: `[2]`)
       - `17:05 – 18:30`: `Разбор аналитики Langfuse и SLA (ЛК САиИО)` (`management`, participants: `[1, 2]`)
       - `19:00 – 20:25`: `Синхронизация задач CRM (ПЗ ТиМАУ)` (`sales`, participants: `[3]`)
     - **Wed Nov 18**:
       - `15:30 – 16:55`: `Совещание по автоматизации n8n (ЛК САиИО)` (`management`, participants: `[1, 2, 3]`)
       - `17:05 – 18:30`: `Ревизия себестоимости тиражей (ЛК СУБД)` (`accounting`, participants: `[1, 4]`)
       - `19:00 – 20:25`: `Контроль дебиторской задолженности (ПЗ СУБД)` (`accounting`, participants: `[4]`)

## 4. Implementation Guidelines
- Use parameterized SQL / Dapper queries only.
- Wrap enrichment logic in `try-catch` so that even if PostgreSQL is offline during standalone frontend testing, fallback schedule items are returned cleanly.
- Ensure `dotnet build Fotoprint.slnx` and `dotnet test` pass with 0 errors.

## 5. Acceptance Criteria
1. `GET /api/calendar/events?start_date=2026-11-01&end_date=2026-11-30&scope=my` returns enriched events with `item_type`, `category`, `is_all_day`, `is_recurring`, `organizer_name`, `participants`, and `tags`.
2. Non-director users requesting `scope=all` receive HTTP `403 Forbidden`.
3. All existing unit tests in `tests/Fotoprint.Tests` pass (`dotnet test`).
