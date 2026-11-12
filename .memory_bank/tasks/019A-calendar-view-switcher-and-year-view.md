# Task 019A: Implement Calendar Views (`Day`, `Month`, `Year`) & Fix `#cal-view-select`

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (Frontend Calendar Logic & Core Layout)
- **File Boundaries**:
  - `infra/nginx/html/index.html`
  - `infra/nginx/html/themes/theme-core.css`
- **Frozen Contract**: `.memory_bank/contracts/calendar_view_switcher_contract.md`

---

## Implementation Guidelines

1. **HTML Updates (`infra/nginx/html/index.html`)**:
   - Update `#cal-view-select` options to:
     - `<option value="day">День</option>`
     - `<option value="week" selected>Неделя</option>`
     - `<option value="month">Месяц</option>`
     - `<option value="year">Год</option>`
   - Inside `#cal-workspace-body`, add:
     - `<div id="cal-month-grid" class="cal-month-grid" style="display: none;"></div>`
     - `<div id="cal-year-grid" class="cal-year-grid" style="display: none;"></div>`

2. **Core CSS Updates (`infra/nginx/html/themes/theme-core.css`)**:
   - Replace hardcoded `grid-template-columns: repeat(7, 1fr);` on `.cal-week-columns-header`, `.cal-allday-cells-container`, and `.cal-grid-columns` with:
     ```css
     grid-template-columns: repeat(var(--cal-grid-cols, 7), minmax(0, 1fr));
     ```
   - Add structural layout CSS for **Month View**:
     - `.cal-month-grid` (flex column, `flex: 1`, `overflow: hidden`), `.cal-month-weekdays` (7-col header), `.cal-month-cells` (`grid-template-columns: repeat(7, minmax(0, 1fr)); grid-template-rows: repeat(6, minmax(0, 1fr)); flex: 1`), `.cal-month-cell` (border-right/bottom hairlines, `overflow: hidden`, hover state, `.other-month` dimmed), `.cal-month-cell-header` (day number + `.cal-month-week-num` on Mondays), `.cal-month-events-list`, `.cal-month-event-pill` (compact row with icon, title, right-aligned time), `.cal-month-more-badge` (`+N`).
   - Add structural layout CSS for **Year View**:
     - `.cal-year-grid` (`grid-template-columns: repeat(4, minmax(0, 1fr))` on desktop, `3` cols `<= 1200px`, `2` cols `<= 820px`, `overflow-y: auto`), `.cal-year-month-card`, `.cal-year-month-title`, `.cal-year-weekdays` (`grid-template-columns: 20px repeat(7, 1fr)`), `.cal-year-week-row` (`grid-template-columns: 20px repeat(7, 1fr)`), `.cal-year-week-num`, `.cal-year-day-cell` (`data-density="1|2|3"` heatmap shading, `.is-today`, `.other-month`).

3. **JavaScript Engine (`infra/nginx/html/index.html`)**:
   - Add `let currentCalendarViewMode = 'week';`.
   - Update `changeCalendarView(viewMode)`:
     - Validate `['day', 'week', 'month', 'year']`.
     - Set `currentCalendarViewMode = viewMode`, sync `#cal-view-select.value`, call `closeDragCreatePopover()`.
     - If `viewMode === 'year'` or `'month'`, call `refreshCalendarEvents()` so full-year/month events are available.
     - Render active view via `renderActiveCalendarView()` (or inside `renderWeekViewGrid()`).
   - Update `renderWeekViewGrid()` (and add `renderMonthViewGrid()` + `renderYearViewGrid()`):
     - Toggle visibility of `.cal-week-header`, `.cal-allday-strip`, `.cal-timegrid-scroll`, `#cal-month-grid`, and `#cal-year-grid` based on `currentCalendarViewMode`.
     - **Day (`day`) & Week (`week`)**:
       - `numCols = (currentCalendarViewMode === 'day') ? 1 : 7`.
       - Set `--cal-grid-cols` on `#cal-workspace-body` to `String(numCols)`.
       - Anchor start date: `selectedCalendarDate` for `day`, `currentWeekStart` for `week`.
       - Loop `i = 0 .. numCols - 1` (preserving `setupDayColDrag(colDiv, isoDate)` and `layoutDayTimedEvents`).
       - Update `#cal-now-dot` left position using `numCols`: `calc(56px + ${todayColIdx} * ((100% - 56px) / ${numCols}))`.
     - **Month (`month`)**:
       - Render 7 weekday headers + 42 `.cal-month-cell` cells (Monday-first) for `currentCalendarDate`'s month/year.
       - Show `W<nn>` in Monday cells, compact `.cal-month-event-pill` items (up to 5 per cell + `+N` badge), click event pill -> `openEventPopover()`, click day number / `+N` -> `day` view, click empty cell -> `openQuickAddModal(isoDate, 10, 0)`.
     - **Year (`year`)**:
       - Set `#current-month-label` to `${year}`.
       - Render 12 `.cal-year-month-card` cards with left week-number column (`1`..`52`), 7 weekday initials, and 6 week rows of `.cal-year-day-cell`.
       - Compute visible event count per `YYYY-MM-DD` (`isEventVisible(ev)`) to assign `data-density="0|1|2|3"` and tooltip `${count} событий`.
       - Click day cell -> `day` view on that date; click month title -> `month` view for that month.
   - Update `changeWeek(delta)`:
     - `day`: `±1` day; `week`: `±7` days; `month`: `±1` month; `year`: `±1` year.
   - Update `refreshCalendarEvents()`:
     - When `currentCalendarViewMode === 'year'`, fetch `start_date=${year}-01-01` .. `end_date=${year}-12-31`.
   - Also ensure `onCalendarFilterChange()` re-renders whichever view (`day`, `week`, `month`, `year`) is active.

---

## Acceptance Criteria

- `#cal-view-select` switches cleanly between `День` (1-col time-grid), `Неделя` (7-col time-grid), `Месяц` (7×6 grid with week numbers and event pills), and `Год` (4×3 12-month heatmap grid with week numbers).
- Prev/Next navigation steps by 1 day, 7 days, 1 month, or 1 year depending on active mode.
- Drag-to-create works in both `День` and `Неделя`; event popover works in `День`, `Неделя`, and `Месяц`; Year View heatmap reflects event density and drills down to `День` / `Месяц`.
