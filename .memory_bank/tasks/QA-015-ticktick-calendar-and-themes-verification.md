# Task QA-015: Comprehensive QA Verification of TickTick Calendar Window & 3 Themes

## 1. Scope & Assigned Role
- **Role**: Independent QA Verifier (`qa-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Audits**: Tasks `015A`, `015B`, `015C`, `016`, `017`

## 2. Verification Checklist

### A. Backend & Unit Test Verification
1. Run `dotnet build Fotoprint.slnx` (must complete with 0 errors, 0 warnings).
2. Run `dotnet test` (all 132+ unit & integration tests must pass 100%).
3. Verify `GET /api/calendar/events?start_date=2026-11-09&end_date=2026-11-20&scope=my` returns enriched meetings and tasks with `item_type`, `category`, `is_all_day`, `participants`, and `tags`.
4. Verify RBAC: `scope=all` returns `200 OK` for Director (`X-Telegram-Id: 100001`) and `403 Forbidden` for non-director roles (`100002`, `100003`, `999999`).

### B. TickTick Reference UI & DOM Verification
1. Verify 3-column standalone window structure (`#calendar-window-view`):
   - Column 1: `.cal-nav-rail` with user avatar and navigation icons.
   - Column 2: `.cal-sidebar` with Mini Month Calendar (`#mini-month-grid`, active week pill, today circle, event dots) and interactive checkboxes for `Lists`, `Employees (Кто занят)`, and `Tag`.
   - Column 3: `.cal-main-area` with top toolbar (`November 2026`, `11:08` clock badge, `+`, `Week` view selector, `< Today >`), 7-day horizontal header (`W46`, `Thu 12`..`Wed 18`), stacked all-day bars, and 24-hour vertical timeline (`8 AM`..`8 PM`).
2. Verify **Collision Layout**: Overlapping events on `Thu 12` (`12:00 PM - 1:25 PM` meeting and `12:00 PM` task) render side-by-side (`width: ~50%`, `left: 0%` and `left: 50%`).
3. Verify **Current Time Red Line**: Red badge on the time axis, red dot on Today's column, and horizontal red line across the grid (`11:08`).
4. Verify **Interactive Flows**:
   - Clicking an event block opens the detail popover showing task/meeting info and who is busy.
   - Toggling sidebar filter checkboxes hides/shows matching blocks on the grid.
   - Clicking `+` or an empty time slot opens the creation modal and persists a new event via `/api/calendar/confirm`.
5. Verify **Multi-Theme Switching**:
   - Test `switchTheme('obsidian')`, `switchTheme('liquid-glass')`, and `switchTheme('amber')` — zero layout shifts, zero system emojis (`0` emojis in DOM), 100% Lucide SVG icons.

### C. Edge Cases & Boundary Conditions
1. **Domain Vocabulary Audit (Zero Lifestyle / Domestic Tasks)**:
   - Grep all calendar code, seed data, and fallbacks (`index.html`, `Program.cs`, `03_seed_data.sql`, `theme-core.css`) for forbidden domestic terms: `уборк`, `упражнен`, `поощрен`, `выпить вод`, `уборка`.
   - Must return ZERO occurrences. All events must be corporate typography operations (Heidelberg maintenance, prepress, ink viscosity check, contract approvals, Bitrix24 MoM tasks).
2. **Date & Timezone Boundaries**:
   - Verify parsing of events spanning midnight, invalid date inputs, and ISO 8601 strings with timezone offset `+03:00`.
3. **Empty & Offline Fallback**:
   - Simulate network failure on `/api/calendar/events` — verify the UI gracefully renders `FALLBACK_CALENDAR_EVENTS` without throwing uncaught JS exceptions or breaking the grid.
4. **Voice Input Non-Destructive Append (Task 017)**:
   - Verify cursor-aware append behavior in text areas: existing text is preserved when speech recognition returns interim or final results. Run `test_unified_index.py`.
