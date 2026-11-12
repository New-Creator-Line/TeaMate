# Task QA-019: Verification of Calendar Views (`Day`, `Week`, `Month`, `Year`) & 3 Themes

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (QA Verifier)
- **Dependencies**: `019A`, `019B`
- **File Boundaries**: Read-only audit of application code; may run Playwright/Python/Node checks and capture verification screenshots in `docs/screenshots/`.
- **Frozen Contract**: `.memory_bank/contracts/calendar_view_switcher_contract.md`

---

## Verification Checklist

1. **View Mode Switching (`#cal-view-select`)**:
   - `day`: Verify `.cal-day-col` count === `1`, `.cal-day-header-col` count === `1`, `--cal-grid-cols` === `1`, full-width events, and `#cal-now-dot` on left edge of column.
   - `week`: Verify `.cal-day-col` count === `7`, `--cal-grid-cols` === `7` (no regression).
   - `month`: Verify `#cal-month-grid` is visible, `42` `.cal-month-cell` elements rendered (7×6), Monday cells show `W44`–`W49`, event pills show title + time, and `+N` overflow badge appears if >5 events.
   - `year`: Verify `#cal-year-grid` is visible, `12` `.cal-year-month-card` elements rendered, each week row has left week-number (`1`..`52`), Nov 12 has `.is-today` and `data-density` > 0.
2. **Navigation (`#cal-prev-btn`, `#cal-next-btn`, `#cal-today-btn`)**:
   - `day`: `Next` / `Prev` steps `±1` day.
   - `week`: `Next` / `Prev` steps `±7` days.
   - `month`: `Next` / `Prev` steps `±1` month.
   - `year`: `Next` / `Prev` steps `±1` year (`2026` -> `2027` -> `2026`).
3. **Interactive Drill-Down & Creation**:
   - In `day`: drag-to-create opens `#cal-drag-create-popover` cleanly.
   - In `month`: clicking an event pill opens `#cal-event-popover`; clicking a day number switches to `day` view.
   - In `year`: clicking a day cell switches to `day` view on that date; clicking a month title switches to `month` view.
4. **Multi-Theme & Regression Check**:
   - Capture/verify `day`, `month`, and `year` views across `obsidian`, `liquid-glass`, and `amber` themes with zero console errors and no horizontal overflow.
   - Run existing tests in `tests/` to ensure no regressions.

---

## Output Format

Report `VERDICT: PASS` or `VERDICT: FAIL` with concrete counts, test results, and any discrepancies.
