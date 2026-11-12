# Task QA-021: QA Verification for Timegrid Column Alignment and Popover Overflow

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (QA Systems Auditor)
- **Dependencies**: `021A`, `021B`
- **Related Defect**: User Screenshot `uploaded_media_1794488018172.png`

---

## 1. Verification Checklist

1. **Column Alignment & Divider Synchrony**:
   - Inspect `.cal-week-header`, `.cal-allday-strip`, and `.cal-grid-columns`.
   - Measure the X-coordinate of every vertical divider across all 7 columns (Monday through Sunday).
   - Assert: Offset between header vertical lines and timegrid vertical lines is `<= 0.5px` (0px visually) across all 7 columns.
   - Verify that when the browser window is resized (`1920px`, `1440px`, `1280px`, `1024px`), the dynamic `--cal-scrollbar-width` updates and maintains exact alignment.

2. **Drag-to-Create Popover Time Row Integrity**:
   - Trigger drag selection on the calendar to open `#cal-drag-create-popover`.
   - Verify that `<input type="date">`, `<input type="time">` (start), separator `–`, and `<input type="time">` (end) are fully contained within `#cal-drag-create-popover`.
   - Assert: `dragPopover.getBoundingClientRect().right >= endInput.getBoundingClientRect().right + 8px`.
   - Assert: No horizontal scrollbar appears on the popover and no visual clipping/overflow occurs.

3. **Multi-Theme Visual Verification**:
   - Check alignment and popover layout across:
     - Linear Obsidian (`obsidian`)
     - Apple Liquid Glass (`liquid-glass`)
     - Business Amber (`amber`)

---

## 2. Deliverable Format

- **Final Verdict**: `[VERDICT: PASS / VERDICT: FAIL]`
- Empirical checklist with measured pixel coordinates for all 7 column dividers and popover boundaries.
