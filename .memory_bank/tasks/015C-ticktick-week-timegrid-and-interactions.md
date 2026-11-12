# Task 015C: TickTick 7-Day Time-Grid Engine, Overlapping Blocks & Event Modals

## 1. Scope & Assigned Worktree
- **Role**: Frontend Time-Grid & Interactions Developer (`coder-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Phase**: Phase 1 (Core Calendar Time-Grid & Functionality)

## 2. Required Installed Skills
Before implementing the grid engine and interactive modals, consult:
- `.agents/skills/ux-data-dashboard/SKILL.md` (dense temporal data visualization, clear visual hierarchy)
- `.agents/skills/ux-design-component/SKILL.md` (interactive cards, popovers, states, keyboard accessibility)
- `.agents/skills/claude-design-skill/SKILL.md` (polished UI artifacts and micro-interactions)

## 3. File Boundaries (Strict Allowlist)
- `infra/nginx/html/index.html`
- `infra/nginx/html/calendar-workspace.js` (optional modular script included by `index.html`)

## 4. Frozen Contract Reference
Follow `.memory_bank/contracts/ticktick_calendar_window_contract.md` (Section 2.1, Column 3 — Multi-Day Header, All-Day Strip, and 24-Hour Vertical Time-Grid).

### Key Deliverables:
1. **Horizontal 7-Day Header & All-Day Strip (`.cal-week-header`)**:
   - Top-left corner displays ISO week number (`W46`).
   - 7 day columns (`Thu 12`, `Fri 13`, `Sat 14`, `Sun 15`, `Mon 16`, `Tue 17`, `Wed 18` — dynamically calculated from `selectedCalendarDate` or `currentWeekStart`).
   - Today's date number (`12`) is rendered inside a solid blue circle (`24px x 24px`).
   - **All-Day & Daily Tasks Strip (`.cal-allday-cell`)**:
     - Directly beneath each day number, render stacked horizontal bars (`20px` height) for `is_all_day` or `is_recurring` items:
       - Recurring routine bar (`↻ Поощрение себя / Утренняя сводка`) in warm bronze/amber (`#4e3824` with `#d97706` border).
       - Daily task bars (`○ Сделать уборку / Проверить валы`, `○ Упражнения / Контроль 1С`) in deep indigo/slate (`#25355a` with `#3b82f6` border).
2. **Vertical 24-Hour Timeline & Hairline Grid (`.cal-timegrid-scroll`)**:
   - Left vertical axis (`.cal-time-axis`, `56px` wide) with hour labels (`8 AM`, `9 AM`, `10 AM`, `11 AM`, `Noon`, `1 PM` .. `11 PM` / `00:00–23:00`, `60px` per hour).
   - Auto-scroll on open to `07:30 AM` so the `8 AM – 8 PM` working window matches the reference screenshot immediately.
   - **Current Time Red Line Indicator (`.cal-now-line-wrapper`)**:
     - Displays the red time badge (e.g. `11:08` or live current time) on the left axis, a `8px` red circle dot on Today's column left edge, and a `1px` glowing red horizontal line across the week columns.
3. **Time-Positioned Event & Task Blocks with Collision Layout**:
   - Calculate `top` and `height` from `start_time` and `end_time` (`1px` per minute at `60px/hour`).
   - **Overlap / Collision Algorithm**:
     - Detect overlapping events in the same day column (such as `12:00 PM - 1:25 PM ЛР ОМО` and `12:00 PM Выпить воду / Отправить спецификацию` on Thu 12) and split horizontal width (`width: 50%`, `left: 0%` and `left: 50%`) exactly as shown in the TickTick reference.
   - **Card Content**:
     - Checkbox/icon (`[ ]` or `○`) + Title (`font-weight: 600`).
     - Time range subtitle (`10:05 AM - 11:30 AM`).
     - Busy personnel / department badge (`Кузнецов И.П., Морозова Е.А.`) so the user immediately sees **what task, what meeting, and who is busy**.
4. **Interactive Event Inspector Popover & Quick Create Modal**:
   - **Click on any Event/Task Block**: Opens a floating TickTick-style inspector popover showing:
     - Full title, category badge, exact time range, and status (`scheduled`, `completed`, `pending`).
     - **Who is Busy**: Organizer & participant list with roles/departments.
     - Action buttons: Toggle task completion checkbox, Download MoM Protocol `.DOCX` (if meeting has protocol), or Close.
   - **Click on Empty Grid Slot or `+` Button**: Opens a Quick Schedule Meeting / Task Modal pre-filled with the clicked date & hour, allowing the user to pick title, duration (`15m`, `30m`, `60m`), participants, and submit via `POST /api/calendar/confirm` (which saves to DB and refreshes the grid).

## 5. Acceptance Criteria
1. All timed blocks and all-day bars render in their exact day column and vertical hour/minute position matching `Интерфейс календаря Reference.png`.
2. Overlapping events on the same day render side-by-side without obscuring text.
3. The red current-time line (`11:08` / live time) renders with the left red badge and today-column dot.
4. Clicking an event shows who is busy and full task/meeting details; clicking `+` or a time slot creates a new event via `/api/calendar/confirm`.
