# Frozen Contract: Calendar Views (`Day`, `Week`, `Month`, `Year`)

- **Version**: 2.0 (Aligned to TickTick Reference Screenshots `1.png`, `2.png`, `3.png`)
- **Status**: FROZEN
- **Scope**: `infra/nginx/html/index.html`, `infra/nginx/html/themes/theme-core.css`, `infra/nginx/html/themes/theme-obsidian.css`, `infra/nginx/html/themes/theme-liquid-glass.css`, `infra/nginx/html/themes/theme-amber.css`

---

## 1. Supported View Modes & Selector (`#cal-view-select`)

`<select id="cal-view-select" class="cal-view-dropdown">` in `.cal-toolbar-right` MUST expose the 4 TickTick views:

| Option `value` | Russian Label | Layout Container | Navigation Step (`#cal-prev-btn` / `#cal-next-btn`) | Toolbar Title (`#current-month-label`) |
| :--- | :--- | :--- | :--- | :--- |
| `day` | `День` | 1-column time-grid (`--cal-grid-cols: 1`) | `±1` day | `Месяц YYYY` (e.g., `Ноябрь 2026`) |
| `week` | `Неделя` | 7-column time-grid (`--cal-grid-cols: 7`, already implemented) | `±7` days | `Месяц YYYY` |
| `month` | `Месяц` | `#cal-month-grid` (7×6 full-month grid) | `±1` month | `Месяц YYYY` |
| `year` | `Год` | `#cal-year-grid` (4×3 12-month heatmap grid) | `±1` year | `YYYY` (e.g., `2026`) |

- State variable: `let currentCalendarViewMode = 'week';`
- `changeCalendarView(viewMode)` validates `['day', 'week', 'month', 'year']`, updates `currentCalendarViewMode`, syncs `#cal-view-select.value`, closes any active drag popover (`closeDragCreatePopover()`), calls `refreshCalendarEvents()` when entering `year` or `month` (to ensure full-year / full-month data), and renders the active view.

---

## 2. Day View (`viewMode === 'day'`, Reference `1.png`)

- Uses existing `.cal-week-header`, `.cal-allday-strip`, and `.cal-timegrid-scroll` with `--cal-grid-cols: 1` on `#cal-workspace-body`.
- `.cal-week-columns-header`, `.cal-allday-cells-container`, and `.cal-grid-columns` in `theme-core.css` MUST use `grid-template-columns: repeat(var(--cal-grid-cols, 7), minmax(0, 1fr));`.
- Shows 1 full-width column for `selectedCalendarDate` (gutter `#cal-week-number` shows `W<nn>`).
- Current-time red dot (`#cal-now-dot`) uses `numCols = 1`: `left: calc(56px + ${todayColIdx} * ((100% - 56px) / 1))` (flush on left border of the day column when Today is shown).
- All-day strip, timed event blocks, `setupDayColDrag()`, and `openEventPopover()` work full-width.

---

## 3. Month View (`viewMode === 'month'`, Reference `2.png`)

- Container inside `#cal-workspace-body`: `<div id="cal-month-grid" class="cal-month-grid" style="display: none;"></div>`.
- When `currentCalendarViewMode === 'month'`:
  - Hide `.cal-week-header`, `.cal-allday-strip`, `.cal-timegrid-scroll`, and `#cal-year-grid`.
  - Show `#cal-month-grid` (`display: flex; flex-direction: column; flex: 1;`).
- Structure:
  - `.cal-month-weekdays`: 7-column header (`Пн`, `Вт`, `Ср`, `Чт`, `Пт`, `Сб`, `Вс`).
  - `.cal-month-cells`: 7 columns × 6 rows (`42` `.cal-month-cell` cells, Monday-first, covering previous/current/next month days).
  - Each `.cal-month-cell` (`data-date="YYYY-MM-DD"`):
    - Top row `.cal-month-cell-header`: day number `.cal-month-day-num` (if 1st of month and not current month, e.g. `Дек 1`; if Today `2026-11-12`, highlighted circle `.is-today`), plus on every Monday (`colIndex === 0`) a subtle right-aligned week label `.cal-month-week-num` (`W44`–`W49`).
    - `.cal-month-events-list`: renders compact `.cal-month-event-pill` items (with `.cal-event-routine`, `.cal-event-task`, or `.cal-event-meeting` color coding, Lucide icon, truncated title, and right-aligned start time for timed events).
    - Overflow cap: render up to `5` event pills directly; if more exist, render a `.cal-month-more-badge` (`+N`) that switches to `day` view for that date on click.
    - Clicking any `.cal-month-event-pill` stops propagation and calls `openEventPopover(ev, pill)`.
    - Clicking an empty area of `.cal-month-cell` calls `openQuickAddModal(isoDate, 10, 0)` (or switches to `day` view on double-click / day-number click).
    - Cells outside the active month receive `.other-month` (dimmed text and muted event pills).

---

## 4. Year View (`viewMode === 'year'`, Reference `3.png`)

- Container inside `#cal-workspace-body`: `<div id="cal-year-grid" class="cal-year-grid" style="display: none;"></div>`.
- When `currentCalendarViewMode === 'year'`:
  - Hide `.cal-week-header`, `.cal-allday-strip`, `.cal-timegrid-scroll`, and `#cal-month-grid`.
  - Show `#cal-year-grid` (`display: grid`).
  - `#current-month-label` displays `${year}` (e.g., `2026`).
- Structure:
  - 4 columns × 3 rows on desktop (`repeat(4, minmax(0, 1fr))`), responsive to 3/2 columns on narrower viewports.
  - 12 `.cal-year-month-card` elements (`data-month="0"`..`11`).
  - Each card has:
    - `.cal-year-month-title` (clickable month name, e.g. `Ноябрь`) — clicking navigates to that month in `month` view (`changeCalendarView('month')`).
    - `.cal-year-weekdays` with an empty corner spacer + 7 weekday initials (`П`, `В`, `С`, `Ч`, `П`, `С`, `В`).
    - `.cal-year-weeks`: 6 rows (`.cal-year-week-row`), each containing:
      - `.cal-year-week-num`: ISO week number (`1`..`52`) on the far left.
      - 7 `.cal-year-day-cell` elements (`data-date="YYYY-MM-DD"`):
        - `.other-month` for leading/trailing days.
        - **Event density heatmap classes** based on visible event count `n` on `YYYY-MM-DD`: `data-density="0|1|2|3"` (`0` = none, `1` = 1 event, `2` = 2–3 events, `3` = 4+ events), shaded with progressive theme accent opacity (`--cal-year-heat-1`, `--cal-year-heat-2`, `--cal-year-heat-3`).
        - `.is-today` on `2026-11-12` (solid `--cal-today-accent` background with high-contrast text).
        - Clicking any `.cal-year-day-cell` sets `selectedCalendarDate`, `currentCalendarDate`, `currentWeekStart` to that date and switches to `day` view (`changeCalendarView('day')`).

---

## 5. Theme Tokens (All 3 Themes)

`theme-obsidian.css`, `theme-liquid-glass.css`, and `theme-amber.css` MUST define:
- `--cal-month-cell-bg`, `--cal-month-cell-other-bg`, `--cal-month-cell-hover`
- `--cal-year-card-bg`, `--cal-year-card-border`, `--cal-year-card-hover`
- `--cal-year-heat-1`, `--cal-year-heat-2`, `--cal-year-heat-3`
