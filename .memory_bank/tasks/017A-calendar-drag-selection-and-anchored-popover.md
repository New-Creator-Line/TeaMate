# Task 017A: TickTick Drag-to-Create Selection & Dynamic Left/Right Anchored Popover

## 1. Scope & Assigned Worktree
- **Role**: Frontend Calendar Interactions Developer (`coder-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Depends On**: Tasks `015B`, `015C`

## 2. Required Installed Skills
Consult before coding:
- `.agents/skills/ux-design-component/SKILL.md` (popover positioning, keyboard `Esc` handling, pointer capture ergonomics)
- `.agents/skills/interface-design/SKILL.md` (compact form hierarchy and spatial alignment)

## 3. File Boundaries (Strict Allowlist)
- `infra/nginx/html/index.html`
- `infra/nginx/html/themes/theme-core.css`

*Do NOT modify `theme-obsidian.css`, `theme-liquid-glass.css`, or `theme-amber.css` in this task (handled by Task `017B`).*

## 4. Frozen Contract Reference
Implement all functional and structural requirements from `.memory_bank/contracts/calendar_drag_create_contract.md`:

1. **Replace Simple Click-to-Open-Modal on `.cal-day-col` with Drag-to-Select Engine**:
   - On `mousedown` on `.cal-day-col` (ignoring `.cal-event-block` and non-primary mouse buttons):
     - Calculate `anchorMin` snapped to 15-minute steps (`15px`).
     - Render `.cal-drag-ghost` inside that `.cal-day-col` with live duration label (`HH:MM – HH:MM`).
   - On `mousemove` (`window` listener while dragging):
     - Support dragging both **downwards** and **upwards** from `anchorMin`.
     - Clamp `[0, 1440]` and enforce minimum `15 minutes` duration.
     - Update `.cal-drag-ghost` `top`, `height`, and time label in real time.
   - On `mouseup`:
     - Finalize selection (if click without drag > 5px, default to `30 minutes`).
     - Open `#cal-drag-create-popover` anchored next to `.cal-drag-ghost`.
2. **Dynamic Context-Anchored Popover (`#cal-drag-create-popover`)**:
   - Must appear on the **right** of `.cal-drag-ghost` when there is space on the right, and automatically flip to the **left** when the selected column is near the right edge of the viewport (or when sidebar state changes).
   - Implement `repositionDragCreatePopover()` using `requestAnimationFrame`:
     - Bind to `#cal-timegrid-scroll` `scroll` event (so scrolling the timeline moves the popover dynamically with the ghost block).
     - Bind to `window` `resize` event.
     - Invoke inside `toggleCalSidebar()` (and after CSS transition `transitionend` on `#cal-sidebar`) so collapsing/expanding the sidebar immediately recalculates left/right placement!
3. **Exact Form Field Order (Top to Bottom)**:
   1. **Название** (`#drag-create-title`) — auto-focused.
   2. **Описание** (`#drag-create-desc`).
   3. **Время** (`#drag-create-date`, `#drag-create-start`, `#drag-create-end`) — pre-filled from drag selection; editing start/end time updates `.cal-drag-ghost` and calls `repositionDragCreatePopover()` live.
   4. **Участник / Участники** (`input[name="drag-create-emp"]`) at the very bottom above the buttons.
   5. Submit (`#drag-create-submit-btn`) calls `POST /api/calendar/confirm` (same contract as `submitQuickSchedule`), adds the event to `cachedCalendarEvents`, re-renders the grid, and closes the popover + ghost block.
4. **Keep `#cal-quick-modal` Working**:
   - The top toolbar `+ Добавить` button can still open `#cal-quick-modal` (or focus `#drag-create-title`). Do not break existing IDs.

## 5. Acceptance Criteria
1. Dragging on any day column creates a 15-minute-snapped `.cal-drag-ghost` block showing the live time range.
2. Releasing the mouse opens `#cal-drag-create-popover` to the right (for left columns) or left (for right columns) of the ghost block.
3. Scrolling `#cal-timegrid-scroll`, resizing the window, or toggling `#cal-sidebar` dynamically repositions `#cal-drag-create-popover` and flips left/right if space changes.
4. Field order inside `#cal-drag-create-popover` is strictly: Название → Описание → Время → Участники (at bottom).
5. Submitting creates the meeting via `/api/calendar/confirm` and renders it on the calendar grid.
