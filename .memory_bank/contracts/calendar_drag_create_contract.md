# Calendar Drag-to-Create & Context-Anchored Popover Contract

## 1. Objective
Implement a TickTick-authentic **Click-and-Drag Time-Range Selection** on the 7-day vertical time-grid (`#cal-grid-columns`) paired with a **Context-Anchored Floating Creation Popover (`#cal-drag-create-popover`)** that dynamically positions itself to the left or right of the selected block and tracks scroll, sidebar collapse, and viewport resize across all three themes (`obsidian`, `liquid-glass`, `amber`).

---

## 2. Drag-to-Create State Machine & Geometry

### 2.1. Pointer Interaction (`mousedown` / `mousemove` / `mouseup`)
- **Trigger Target**: `.cal-day-col` inside `#cal-grid-columns` (ignoring clicks on existing `.cal-event-block` elements).
- **Scale & Snapping**:
  - Grid scale: `60px = 1 hour` (`1px = 1 minute`).
  - Snap interval: `15 minutes` (`15px`).
  - Minimum duration: `15 minutes` (if user clicks without dragging > 5px, default to `30 minutes` starting at snapped quarter-hour).
- **Bidirectional Drag Support**:
  - Track `anchorMin` (mouse-down Y snapped to 15 min) and `currentMin` (mouse-move Y snapped to 15 min).
  - `startMin = Math.max(0, Math.min(anchorMin, currentMin))`
  - `endMin = Math.min(1440, Math.max(anchorMin, currentMin))`
  - Ensure `endMin - startMin >= 15`.
- **Ghost Selection Block (`.cal-drag-ghost`)**:
  - Appended inside the active `.cal-day-col`.
  - Positioned at `top: ${startMin}px`, `height: ${endMin - startMin}px`, `left: 4px`, `right: 4px`.
  - Displays live time range label (e.g., `10:00 – 11:30 (1 ч 30 мин)`) updating smoothly on every `mousemove`.
  - Remains visible while `#cal-drag-create-popover` is open. Removed on `Cancel`, `Esc`, backdrop click, or successful submit.

---

## 3. Context-Anchored Floating Popover (`#cal-drag-create-popover`)

### 3.1. Dynamic Left/Right Positioning & Tracking
- **Placement Algorithm (`repositionDragCreatePopover()`)**:
  - Measure `ghostRect = ghostEl.getBoundingClientRect()` and `popoverRect` (`width: 340px`, `max-width: calc(100vw - 24px)`).
  - Measure available horizontal space inside the main grid / viewport:
    - `spaceRight = window.innerWidth - ghostRect.right - 12`
    - `spaceLeft = ghostRect.left - 12`
  - **Side Selection**:
    - Prefer **Right** (`left = ghostRect.right + 12`) if `spaceRight >= popoverWidth` (typical for left/center columns like Thu–Sun).
    - Flip to **Left** (`left = ghostRect.left - popoverWidth - 12`) if `spaceRight < popoverWidth` and `spaceLeft >= popoverWidth` (typical for right columns like Mon–Wed).
    - Fallback clamp within `[12, window.innerWidth - popoverWidth - 12]` on narrow screens.
  - **Vertical Alignment**:
    - Align top near `ghostRect.top`, clamped within `[60, window.innerHeight - popoverHeight - 12]` so the popover never overflows above the toolbar or below the screen.
  - **Side Indicator**:
    - Set `data-placement="right"` or `data-placement="left"` on `#cal-drag-create-popover` for directional arrow/glow styling.
- **Dynamic Repositioning Triggers**:
  - `scroll` event on `#cal-timegrid-scroll` (via `requestAnimationFrame` throttle).
  - `resize` event on `window`.
  - Sidebar collapse/expand (`toggleCalSidebar()`, using `transitionend` / `ResizeObserver` on `.cal-main-area`).
  - If the ghost block scrolls completely out of the visible scroll container, keep the popover clamped cleanly inside the viewport bounds.

### 3.2. Exact Field Order & DOM IDs (Strict Order)
Inside `#cal-drag-create-popover`:
1. **Header**: Compact header with selected date badge + close button (`#drag-create-close`).
2. **Field 1 — Название (Title)**:
   - `<input type="text" id="drag-create-title" placeholder="Название встречи или задачи..." required>` (auto-focused on open).
3. **Field 2 — Описание (Description)**:
   - `<textarea id="drag-create-desc" rows="2" placeholder="Описание / повестка..."></textarea>`
4. **Field 3 — Время (Time)**:
   - Compact row containing:
     - `<input type="date" id="drag-create-date" required>`
     - `<input type="time" id="drag-create-start" step="900" required>`
     - `<span>–</span>`
     - `<input type="time" id="drag-create-end" step="900" required>`
   - **Live Two-Way Sync**: Changing `#drag-create-start` or `#drag-create-end` dynamically updates `.cal-drag-ghost` (`top` and `height`) and calls `repositionDragCreatePopover()`.
5. **Field 4 — Участники (Participants — Bottom Section)**:
   - Placed at the very bottom of the form, immediately above the action footer:
   - Checkboxes `input[name="drag-create-emp"]` for employees (`1`: Смирнов А.С., `2`: Кузнецов И.П., `3`: Морозова Е.А., `4`: Соколова О.В.).
6. **Action Footer**:
   - `#drag-create-cancel-btn` ("Отмена") and `#drag-create-submit-btn` ("Запланировать").

---

## 4. Multi-Theme CSS Token Contract
All three themes (`theme-obsidian.css`, `theme-liquid-glass.css`, `theme-amber.css`) MUST define:
- `--cal-ghost-bg`: Translucent fill for `.cal-drag-ghost`.
- `--cal-ghost-border`: Dashed/solid accent border for `.cal-drag-ghost`.
- `--cal-ghost-text`: High-contrast time label color inside `.cal-drag-ghost`.
- `--cal-popover-bg`: Surface background for `#cal-drag-create-popover`.
- `--cal-popover-border`: Border color for `#cal-drag-create-popover`.
- `--cal-popover-shadow`: Elevation shadow / glow for `#cal-drag-create-popover`.
- `--cal-popover-blur`: Backdrop filter (`none` in Obsidian, `blur(28px) saturate(190%)` in Liquid Glass, `blur(16px)` in Amber).
