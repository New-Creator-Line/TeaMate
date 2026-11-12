# TickTick-Style Standalone Calendar Window & Multi-Theme Contract

## 1. Architectural Overview
The Calendar UI is upgraded from a narrow slide-over drawer (`#calendar-drawer`) into a **full-window standalone Calendar Workspace (`#calendar-window-view`)** modeled 1:1 after the **TickTick Desktop Calendar** reference (`Интерфейс календаря Reference.png`).

Users can switch seamlessly between the **AI Copilot Chat View** and the **Standalone Calendar Window** via the far-left navigation rail or the top header button. The implementation is split into two strict phases:
- **Phase 1 (Core UI & Functional Implementation — Tasks `015A`, `015B`, `015C`)**: Build the complete 3-column TickTick calendar interface, vertical hourly timeline, 7-day horizontal grid, all-day tasks strip, overlapping event layout engine, sidebar mini-calendar, list/tag/employee filters, and backend data feed (meetings + tasks + employee busy states).
- **Phase 2 (Multi-Theme Styling & QA — Tasks `016`, `QA-015`)**: Apply and polish the three visual themes (**Linear Obsidian / TickTick Reference Dark**, **Apple Liquid Glass**, and **Business Noir & Electric Amber**) across all calendar components using the installed design skills in `.agents/skills/`.

---

## 2. Visual & Structural Layout Specification (1:1 Reference Match)

### 2.1. Three-Column Workspace Architecture (`#calendar-window-view`)
When active, `#calendar-window-view` occupies the full viewport (`100vw x 100vh` or full main workspace frame) and consists of three columns:

1. **Column 1: Left Navigation Rail (`.cal-nav-rail`, width: `48px`)**
   - **Top Avatar Badge (`.cal-rail-avatar`)**: Rounded square badge showing current user's initial (e.g. `А` for Алексей Смирнов) with a role crown/indicator for `Director`.
   - **Primary Navigation Icons**:
     - `#rail-btn-chat`: Switch to AI Copilot Chat (`data-lucide="message-square"`).
     - `#rail-btn-tasks`: Switch to Tasks / Action Items view or filter (`data-lucide="check-square"`).
     - `#rail-btn-calendar`: Active Calendar Workspace view (`data-lucide="calendar"`).
     - `#rail-btn-matrix`: Priority / Department Matrix view (`data-lucide="layout-grid"`).
     - `#rail-btn-search`: Quick filter / search focus (`data-lucide="search"`).
   - **Bottom Utility Icons**:
     - `#rail-btn-sync`: Refresh calendar data from `/api/calendar/events` (`data-lucide="refresh-cw"`).
     - `#rail-btn-notifications`: Notification status (`data-lucide="bell"`).
     - `#rail-btn-help`: Legend & keyboard shortcuts (`data-lucide="help-circle"`).

2. **Column 2: Left Sidebar Panel (`.cal-sidebar`, width: `236px`, collapsible)**
   - **Mini Month Calendar (`.cal-mini-month`)**:
     - Header: `#mini-month-label` (e.g., `Ноя 2026` / `Nov 2026`) + navigation buttons `#mini-prev-month` (`<`), `#mini-today-dot` (`o`), `#mini-next-month` (`>`).
     - Weekday headers: `Пн Вт Ср Чт Пт Сб Вс` (`M T W T F S S`).
     - 6-row x 7-column day grid (`#mini-month-grid`):
       - **Active Week Pill**: Days belonging to the currently displayed 7-day week have `.in-active-week` (contiguous pill background across the row).
       - **Today Highlight**: Current date (`12`) has `.is-today` (solid accent circle `#3b82f6` with white bold text).
       - **Event Indicator Dots**: Days with scheduled meetings or tasks render a `3px` dot underneath the number (`.mini-day-dot`).
   - **Scrollable Filter Lists (`.cal-sidebar-filters`)**:
     - **Master Toggle**: `All` (`Все записи`) with right-aligned circular/square checkbox (`#filter-all-checkbox`).
     - **Section 1 — `Lists` (`Календари и отделы`)**:
       - Filter items with colored Lucide icons and right-aligned checkboxes (`input[type="checkbox"]`):
         - `production` — `Производство (Цех)` (`printer`)
         - `sales` — `Продажи и клиенты` (`briefcase`)
         - `accounting` — `Бухгалтерия` (`calculator`)
         - `management` — `Руководство / Планерки` (`award`)
         - `tasks` — `Задачи Битрикс24` (`check-square`)
         - `routine` — `Регулярные / Смены` (`repeat`)
     - **Section 2 — `Employees / Busy` (`Занятость сотрудников`)**:
       - Filter checkboxes by employee (`Смирнов А.С.`, `Кузнецов И.П.`, `Морозова Е.А.`, `Соколова О.В.`) to immediately see who is busy or free.
     - **Section 3 — `Tag` (`Теги и проекты`)**:
       - Filter checkboxes with `tag` icon (`#заказ-44102`, `#heidelberg`, `#альфа-маркет`, `#срочно`, `#протокол-mom`).

3. **Column 3: Main Time-Grid Workspace (`.cal-main-area`, `flex: 1`)**
   - **Top Workspace Toolbar (`.cal-toolbar`, height: `52px`)**:
     - Left: Sidebar collapse toggle `#cal-sidebar-toggle` (`panel-left`), Bold Month/Year title `#cal-header-title` (`Ноябрь 2026` / `November 2026`), and RBAC Scope Switcher (`#scope-my-btn` "Мои" / `#scope-all-btn` "Вся компания").
     - Center: Current live time pill `#cal-top-clock` (`11:08`).
     - Right:
       - Quick add button `#cal-add-event-btn` (`+`).
       - View mode selector `#cal-view-mode` (`Week` / `Неделя`, `3 Days` / `3 дня`, `Day` / `День`).
       - Navigation group: `#cal-prev-btn` (`<`), `#cal-today-btn` (`Today` / `Сегодня`), `#cal-next-btn` (`>`).
       - Theme switcher selector `#theme-select` (`Obsidian / Reference`, `Liquid Glass`, `Business Amber`) & close/back-to-chat button.
   - **Multi-Day Header & All-Day Strip (`.cal-week-header`)**:
     - Left gutter cell (`56px`): Week number badge `#cal-week-number` (`W46`).
     - 7 Day Header Columns (`.cal-day-header-col`, `Thu 12` .. `Wed 18` or `Пн` .. `Вс`):
       - Top line: 3-letter weekday name (`Thu`, `Fri`, `Sat`, `Sun`, `Mon`, `Tue`, `Wed` / `Чт`, `Пт`...).
       - Day number badge: `12` (highlighted in accent circle if Today), `13`, `14`, `15`, `16`, `17`, `18`.
       - **All-Day / Pinned Tasks Container (`.cal-allday-strip`)**:
         - Renders stacked horizontal pills (`height: 20px`, `border-radius: 4px`) for all-day events, daily routines (`↻ Поощрение себя` / `↻ Контроль смены Heidelberg` in warm amber/brown `#5c4028`), and unscheduled day tasks (`○ Сделать уборку` / `○ Проверить спецификацию валов` in deep slate/indigo `#263147`).
   - **Scrollable 24-Hour Vertical Time-Grid (`.cal-timegrid-scroll`)**:
     - **Left Time Axis (`.cal-time-axis`, width: `56px`)**:
       - 24 hourly slots (`60px` height per hour; `1px` = `1 minute`).
       - Labels: `8 AM`, `9 AM`, `10 AM`, `11 AM`, `Noon`, `1 PM` .. `8 PM` (or `08:00` .. `20:00` with configurable 12h/24h format).
     - **7 Day Timeline Columns (`.cal-day-col`)**:
       - Background hourly hairline grid (`border-bottom: 1px solid var(--cal-grid-line)`) and half-hour dashed/subtle lines.
       - **Current Time Indicator Line (`.cal-now-indicator`)**:
         - Red pill label (`11:08`) over the time axis (`background: #dc2626; color: #fff`).
         - Red circle dot (`8px`) on the left border of Today's column.
         - 1px horizontal red line (`#ef4444`) spanning across the week grid at `top = (hours * 60 + minutes) * pxPerMinute`.
       - **Timed Event & Task Blocks (`.cal-event-block`)**:
         - Computed vertical geometry:
           - `top = ((startHour * 60 + startMin) - gridStartMin) * pxPerMinute`
           - `height = Math.max(22, durationMinutes * pxPerMinute)`
         - **Collision & Overlap Algorithm**:
           - When multiple events in the same day column have overlapping intervals `[startA, endA)` and `[startB, endB)`, group them into a collision cluster and assign `width: calc(100% / clusterCols - 4px)` and `left: calc((colIndex * 100%) / clusterCols)`.
         - **Block Content**:
           - Status checkbox or category icon (`square`, `check-square`, `repeat`, `users`).
           - Event/Task Title (`font-weight: 600`, e.g., `ЛР АМД — Оперативка №44102`).
           - Time range subtitle (`10:05 AM - 11:30 AM`).
           - Busy employees pill (`Кузнецов И.П., Морозова Е.А.`).
         - **Block Variants**:
           - `type="meeting"`: Slate-steel translucent card (`#3e4c5e` / `rgba(71, 85, 105, 0.65)`) with left accent border (`3px solid #94a3b8`).
           - `type="task"`: Compact indigo/blue pill (`#25355a` / `rgba(59, 130, 246, 0.35)`) with radio circle `○` and left border `#3b82f6`.
           - `type="routine"`: Warm bronze/amber pill (`#4e3824`) with repeat icon `↻` and left border `#f59e0b`.

---

## 3. Backend API Contract Extensions (`/api/calendar/events`)

### `GET /api/calendar/events`
Query Parameters:
- `start_date` (`YYYY-MM-DD`)
- `end_date` (`YYYY-MM-DD`)
- `employee_id` (`int`, optional)
- `scope` (`"my"` | `"all"`)

Response Payload (`200 OK`):
```json
{
  "status": "success",
  "scope": "my",
  "events": [
    {
      "id": "bbbbbbb1-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "meeting_id": "bbbbbbb1-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "item_type": "meeting",
      "category": "production",
      "title": "Оперативка производства: тираж №44102",
      "description": "Разбор юстировки валов Heidelberg XL 75",
      "start_time": "2026-11-12T10:05:00+03:00",
      "end_time": "2026-11-12T11:30:00+03:00",
      "is_all_day": false,
      "is_recurring": false,
      "status": "scheduled",
      "organizer_id": 1,
      "organizer_name": "Смирнов А.С.",
      "participant_ids": [1, 2, 3],
      "participants": ["Смирнов А.С.", "Кузнецов И.П.", "Морозова Е.А."],
      "tags": ["heidelberg", "заказ-44102", "планерка"]
    }
  ]
}
```
> **Backward Compatibility Invariant**: Existing fields (`meeting_id`, `title`, `description`, `start_time`, `end_time`, `status`, `organizer_id`, `participant_ids`) MUST remain intact so all 99 existing .NET unit tests pass without regression.

---

## 4. Multi-Theme CSS Token Contract (`.cal-*` Variables)

All three stylesheets (`theme-obsidian.css`, `theme-liquid-glass.css`, `theme-amber.css`) MUST define the following calendar tokens:
- `--cal-bg-canvas`: Main calendar background (`#191919` in Obsidian, translucent deep space in Liquid Glass, `#090a0d` in Amber).
- `--cal-bg-sidebar`: Left sidebar background (`#141414` in Obsidian).
- `--cal-bg-rail`: Far-left icon rail background (`#0f0f0f` in Obsidian).
- `--cal-grid-line`: Hourly hairline border (`rgba(255, 255, 255, 0.06)`).
- `--cal-grid-half-line`: Half-hour subtle line (`rgba(255, 255, 255, 0.025)`).
- `--cal-event-meeting-bg`: Background for timed meeting blocks.
- `--cal-event-meeting-border`: Left border accent for timed meeting blocks.
- `--cal-event-task-bg`: Background for task pills.
- `--cal-event-routine-bg`: Background for all-day recurring bars.
- `--cal-now-line`: Current time line color (`#ef4444`).
- `--cal-today-accent`: Active day circle highlight (`#3b82f6` in Obsidian/Glass, `#f59e0b` in Amber).
