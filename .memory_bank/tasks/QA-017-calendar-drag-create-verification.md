# Task QA-017: Verification of Drag-to-Create Selection & Anchored Popover Across 3 Themes

## 1. Scope & Assigned Role
- **Role**: Independent QA Verifier (`qa-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Audits**: Tasks `017A`, `017B`

## 2. Verification Checklist

### A. Drag-to-Create Geometry & Snapping
1. Open `#calendar-window-view` in headless browser (Playwright).
2. Perform `mousedown` → `mousemove` → `mouseup` on column `Thu 12` from `10:00` (`y = 600`) to `11:30` (`y = 690`).
3. Verify `.cal-drag-ghost` is rendered with `top: 600px`, `height: 90px` (15-min snapped), and displays `10:00 – 11:30`.
4. Test **upward drag** (from `15:00` up to `13:30`) and verify `startTime = 13:30`, `endTime = 15:00`.

### B. Dynamic Left/Right Anchoring & Repositioning
1. After dragging on `Thu 12` (left column), assert `#cal-drag-create-popover` has `data-placement="right"` and its `rect.left > ghostRect.right`.
2. After dragging on `Wed 18` (rightmost column), assert `#cal-drag-create-popover` flips to `data-placement="left"` and its `rect.right < ghostRect.left` (no horizontal overflow off-screen).
3. Scroll `#cal-timegrid-scroll` by `+180px` and assert `#cal-drag-create-popover` `rect.top` shifts dynamically to stay anchored to `.cal-drag-ghost`.
4. Click `#cal-sidebar-toggle` to collapse/expand `#cal-sidebar` while `#cal-drag-create-popover` is open and assert the popover recalculates its horizontal position cleanly.

### C. Form Field Order & Persistence
1. Inspect DOM bounding boxes (`getBoundingClientRect().top`) inside `#cal-drag-create-popover`:
   - `titleTop < descTop < timeTop < participantsTop < submitTop`.
2. Edit `#drag-create-end` time input and verify `.cal-drag-ghost` height updates immediately.
3. Fill title, description, select participants, click `#drag-create-submit-btn`, and verify:
   - `POST /api/calendar/confirm` is triggered.
   - New event block appears on the time-grid at the exact selected time.
   - `.cal-drag-ghost` and `#cal-drag-create-popover` are closed/removed.

### D. Multi-Theme & Regression Suite
1. Test drag-create + popover rendering in `obsidian`, `liquid-glass`, and `amber` themes (capture screenshots in `docs/screenshots/`).
2. Run existing frontend tests (`python3 infra/nginx/html/tests/test_unified_index.py`, `test_obsidian_theme.py`, `node infra/nginx/html/themes/test-theme-liquid-glass.js`) and `dotnet test`.
