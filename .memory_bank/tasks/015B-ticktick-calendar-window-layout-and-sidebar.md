# Task 015B: TickTick Standalone Calendar Window — 3-Column Layout, Rail & Sidebar

## 1. Scope & Assigned Worktree
- **Role**: Frontend Calendar Layout Developer (`coder-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Phase**: Phase 1 (Core Calendar UI Structure)

## 2. Required Installed Skills
Before writing HTML/CSS/JS, consult the instructions in:
- `.agents/skills/ux-image-to-code/SKILL.md` (1:1 structural decomposition of `Интерфейс календаря Reference.png`)
- `.agents/skills/interface-design/SKILL.md` (strict spatial hierarchy, panel sizing, and component ergonomics)
- `.agents/skills/frontend-design/SKILL.md` (zero emojis, clean SVG Lucide iconography, disciplined typography)

## 3. File Boundaries (Strict Allowlist)
- `infra/nginx/html/index.html`
- `infra/nginx/html/themes/theme-core.css` (or shared layout styles)

## 4. Frozen Contract Reference
Follow `.memory_bank/contracts/ticktick_calendar_window_contract.md` (Section 2.1, Columns 1 & 2, and Toolbar).

### Key Deliverables:
1. **Replace Slide-Over Drawer with Full-Window Calendar View (`#calendar-window-view`)**:
   - Instead of a tiny right-hand slide-over drawer, clicking `#calendar-toggle-btn` ("Календарь" in header) or `#rail-btn-calendar` opens the full-screen standalone calendar workspace (`#calendar-window-view`, `position: fixed; inset: 0; z-index: 200; display: flex;`), matching the TickTick desktop app window in the reference screenshot.
   - Maintain backward compatibility ID aliases (`#calendar-drawer` or helper functions `toggleCalendarDrawer()`) so existing test scripts do not break.
2. **Column 1 — Far-Left Icon Rail (`.cal-nav-rail`, `48px` width)**:
   - Top user avatar tile (`#cal-user-avatar`) showing current user initial (`А`, `И`, `Е`, `О`, `Г`) with warm orange/amber background (`#d97706`) and crown badge for `Director`.
   - Navigation buttons with Lucide icons:
     - `#rail-btn-chat` (`message-square` — returns to AI Copilot Chat window)
     - `#rail-btn-tasks` (`check-square` — filters/highlights tasks)
     - `#rail-btn-calendar` (`calendar` — active state)
     - `#rail-btn-matrix` (`layout-grid` — toggles employee/department matrix overlay)
     - `#rail-btn-clock` (`clock` — jumps to current time `Now`)
     - `#rail-btn-search` (`search` — focuses quick search input)
   - Bottom utility icons: `#rail-btn-sync` (`refresh-cw`), `#rail-btn-bell` (`bell`), `#rail-btn-help` (`help-circle`).
3. **Column 2 — Left Sidebar Panel (`.cal-sidebar`, `236px` width)**:
   - **Mini Month Calendar (`.cal-mini-month`)**:
     - Header with month/year (`Nov 2026` / `Ноя 2026`) and `<`, `o`, `>` buttons (`changeMiniMonth(-1)`, `goToToday()`, `changeMiniMonth(1)`).
     - Weekday row: `Пн Вт Ср Чт Пт Сб Вс`.
     - 42-cell compact grid (`#mini-month-grid` / `#calendar-days-grid`):
       - Highlights the currently active 7-day week with a continuous rounded pill background (`.in-active-week`).
       - Highlights Today (`12`) with a solid accent circle (`.today`).
       - Displays a subtle blue dot (`.mini-day-dot`) below dates that have scheduled events.
       - Clicking any day jumps the main 7-day week view to include that date.
   - **Interactive Filter Sections (`.cal-sidebar-filters`)**:
     - `All` master toggle row with right-aligned radio/checkbox (`#filter-all`).
     - **`Lists` (`Календари и отделы`)**:
       - Checkboxes for `production` (Производство), `sales` (Продажи), `accounting` (Бухгалтерия), `management` (Руководство), `tasks` (Задачи Битрикс24), `routine` (Регулярные).
     - **`Сотрудники (Кто занят)`**:
       - Checkboxes for each employee (`Смирнов А.С.`, `Кузнецов И.П.`, `Морозова Е.А.`, `Соколова О.В.`) with live busy status indicator.
     - **`Tag` (`Теги`)**:
       - Checkboxes for `#заказ-44102`, `#heidelberg`, `#альфа-маркет`, `#планерка`, `#битрикс24`.
     - Toggling any checkbox immediately filters the visible blocks on the main 7-day time grid!
4. **Column 3 Top Header Toolbar (`.cal-toolbar`)**:
   - Left: Sidebar collapse button (`panel-left`), bold title `#current-month-label` (`November 2026` / `Ноябрь 2026`), RBAC Scope toggle (`#scope-my-btn` "Мои встречи" / `#scope-all-btn` "Вся компания" with `#scope-lock-icon`).
   - Top-Center: Dark pill displaying current time `#cal-top-clock` (`11:08`).
   - Right:
     - Quick add event button `#cal-add-btn` (`+`).
     - View mode dropdown `#cal-view-select` (`Week` / `Неделя`, `3 Days` / `3 дня`, `Day` / `День`).
     - Week navigation group: `<` (`changeWeek(-1)`), `Today` (`goToToday()`), `>` (`changeWeek(1)`).
     - Theme selector & Back to Chat button (`Закрыть / Чат`).

## 5. Acceptance Criteria
1. Opening the calendar displays a full-window 3-column TickTick workspace matching `Интерфейс календаря Reference.png`.
2. Zero system emojis are used anywhere in the markup (100% Lucide SVG icons).
3. Sidebar mini-calendar highlights the active week pill, current day circle, and event indicator dots.
4. Checking/unchecking Lists, Employees, or Tags in the left sidebar dynamically filters the calendar state.
