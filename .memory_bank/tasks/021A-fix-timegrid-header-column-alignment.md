# Task 021A: Fix Time-Grid / Header Column Misalignment & Cumulative Drift

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (Frontend Layout Engineer)
- **File Boundaries**:
  - `infra/nginx/html/themes/theme-core.css`
  - `infra/nginx/html/index.html`
- **Related Defect**: User Screenshot `uploaded_media_1794488018172.png` (Red marker at TUE/WED showing 22px column misalignment).

---

## 1. Problem Description & Root Cause Analysis

### Measured Evidence:
- In `.cal-week-header` and `.cal-allday-strip`:
  - Monday (Col 1) divider: `x = 379.5px`
  - Tuesday (Col 2) divider: `x = 639.5px`
  - Wednesday (Col 3) divider: `x = 898.5px`
  - Thursday (Col 4) divider: `x = 1158.5px`
  - Friday (Col 5) divider: `x = 1417.5px`
  - Saturday (Col 6) divider: `x = 1677.5px`
  - Average column width: `~259.5px`.
- In `.cal-timegrid-scroll` (`.cal-grid-columns`):
  - Monday (Col 1) divider: `x = 376.5px` (Shift = -3.0px)
  - Tuesday (Col 2) divider: `x = 632.5px` (Shift = -7.0px)
  - Wednesday (Col 3) divider: `x = 888.5px` (Shift = -10.0px)
  - Thursday (Col 4) divider: `x = 1144.5px` (Shift = -14.0px)
  - Friday (Col 5) divider: `x = 1400.5px` (Shift = -17.0px)
  - Saturday (Col 6) divider: `x = 1656.5px` (Shift = -21.0px to -22.0px!)
  - Average column width: `256.0px`.

### Root Cause:
1. `.cal-timegrid-scroll` is a vertical scroll container with `overflow-y: auto; overflow-x: hidden;`. Because the 24-hour timegrid is 1440px high, the browser permanently displays a vertical scrollbar (taking ~17px to ~24px of width depending on browser and OS).
2. The width available for `.cal-grid-columns` inside the scroll container is `clientWidth - 56px = (totalWidth - scrollbarWidth - 56px)`.
3. In contrast, `.cal-week-header` and `.cal-allday-strip` sit outside the scroll container and do NOT reserve any space for the vertical scrollbar. Their available width is `totalWidth - 56px`.
4. Because both containers use `grid-template-columns: repeat(7, 1fr)`, each column in the grid below is `scrollbarWidth / 7` (~3.5px) narrower than the header column above it.
5. This creates a **cumulative drift** `k * (scrollbarWidth / 7)`, which reaches 21–22px at column 6, making the vertical dividers look completely broken and out-of-sync.
6. Additionally, `.cal-week-gutter-header`, `.cal-day-header-col`, `.cal-allday-gutter`, and `.cal-allday-cell` lacked explicit `box-sizing: border-box;`, introducing subpixel border rounding mismatches.

---

## 2. Implementation Directive

1. **Scrollbar Gutter Reservation**:
   - Apply `scrollbar-gutter: stable;` to `.cal-timegrid-scroll`.
   - In `.cal-week-header` and `.cal-allday-strip`, reserve the exact scrollbar width on the right:
     ```css
     .cal-week-header,
     .cal-allday-strip {
       padding-right: var(--cal-scrollbar-width, 0px);
       box-sizing: border-box;
     }
     ```
   - In `index.html`, compute `--cal-scrollbar-width` dynamically on mount and window resize:
     ```javascript
     function syncCalendarScrollbarGutter() {
       const scrollEl = document.getElementById('cal-timegrid-scroll');
       const workspaceEl = document.getElementById('cal-workspace-body');
       if (!scrollEl || !workspaceEl) return;
       const scrollbarWidth = scrollEl.offsetWidth - scrollEl.clientWidth;
       workspaceEl.style.setProperty('--cal-scrollbar-width', `${scrollbarWidth}px`);
     }
     ```
     Hook this function into `window.addEventListener('resize')`, view switches, and initialization.

2. **Explicit Box-Sizing & Border Consistency**:
   - Ensure `box-sizing: border-box;` is applied to:
     - `.cal-week-gutter-header`
     - `.cal-week-columns-header`
     - `.cal-day-header-col`
     - `.cal-allday-gutter`
     - `.cal-allday-cells-container`
     - `.cal-allday-cell`
     - `.cal-grid-columns`
     - `.cal-day-col`
   - Verify that `:last-child` has identical right border handling across header, allday, and grid:
     ```css
     .cal-day-header-col:last-child,
     .cal-allday-cell:last-child,
     .cal-day-col:last-child {
       border-right: none !important;
     }
     ```

---

## 3. Acceptance Criteria

- Vertical dividers in `.cal-week-header`, `.cal-allday-strip`, and `.cal-grid-columns` align with **0px** offset from Column 1 through Column 7.
- No cumulative drift across days of the week regardless of browser scrollbar settings (overlay or classic 17px scrollbars).
- The red marker offset (22px gap) identified in user screenshot `uploaded_media_1794488018172.png` is reduced to 0px.
