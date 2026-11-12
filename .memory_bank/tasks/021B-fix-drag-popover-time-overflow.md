# Task 021B: Fix Drag-to-Create Popover Time Row Width & Overflow Defect

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (Frontend UI & Styling Specialist)
- **File Boundaries**:
  - `infra/nginx/html/themes/theme-core.css`
  - `infra/nginx/html/themes/theme-obsidian.css`
  - `infra/nginx/html/themes/theme-liquid-glass.css`
  - `infra/nginx/html/themes/theme-amber.css`
- **Related Defect**: User Screenshot `uploaded_media_1794488018172.png` (Red arrow pointing to `06:00` input protruding out of the popover card).

---

## 1. Problem Description & Root Cause Analysis

### Measured Evidence:
- In the user screenshot, the time input box `06 : 00 🕒` sticks out past the right border and shadow of `#cal-drag-create-popover` by ~20px to ~30px.

### Root Cause:
1. `#cal-drag-create-popover` has a fixed width:
   ```css
   #cal-drag-create-popover {
     width: 340px;
     padding: 14px 16px;
     box-sizing: border-box;
   }
   ```
   Total content width inside the popover is `340px - 32px = 308px`.
2. Inside `.cal-drag-time-row`:
   - `<input type="date">`: has native calendar picker icon and formatted date string (`17 . 11 . 2026`). In WebKit/Blink, its minimum intrinsic width is ~140px.
   - Two `<input type="time">`: each has a native clock icon and time string (`03:45`, `06:00`). Their minimum intrinsic width is ~95px each (total 190px).
   - Separator `–` (~12px) and flex gaps (`3 * 8px = 24px` or `3 * 6px = 18px`).
   - Combined intrinsic width: `140 + 95 + 95 + 12 + 18 = 360px`!
3. Because flex children have default `min-width: auto`, the inputs cannot shrink below their intrinsic content size.
4. With 360px required content inside a 308px container, the last input (`06:00`) overflows the card by ~30–50px.
5. In addition, theme files (`theme-amber.css`, `theme-liquid-glass.css`, `theme-obsidian.css`) redundantly declare:
   `#cal-drag-create-popover .cal-drag-time-row { gap: 8px; }`
   and inputs have `padding: 7px 10px;`, further expanding the element width.

---

## 2. Implementation Directive

1. **Widen the Popover**:
   - Update `#cal-drag-create-popover` width in `theme-core.css` and all 3 theme files to `384px` (or `width: min(390px, calc(100vw - 24px));`):
     ```css
     #cal-drag-create-popover {
       width: 384px;
       max-width: calc(100vw - 24px);
       ...
     }
     ```
2. **Compact & Responsive Time Row**:
   - In `theme-core.css`:
     ```css
     .cal-drag-time-row {
       display: flex;
       align-items: center;
       gap: 6px;
       width: 100%;
       box-sizing: border-box;
     }

     .cal-drag-time-row input[type="date"] {
       flex: 1.35;
       min-width: 0;
       padding: 7px 6px;
       font-size: 12.5px;
     }

     .cal-drag-time-row input[type="time"] {
       flex: 1;
       min-width: 0;
       padding: 7px 6px;
       font-size: 12.5px;
     }

     .cal-drag-time-sep {
       color: var(--cal-text-muted);
       font-weight: 600;
       font-size: 13px;
       flex-shrink: 0;
     }
     ```
3. **Synchronize Theme Overrides**:
   - Verify `theme-obsidian.css`, `theme-liquid-glass.css`, and `theme-amber.css` do not re-impose `width: 340px` or oversized `gap: 8px` on `.cal-drag-time-row`.
   - Update JavaScript repositioning logic in `repositionDragCreatePopover()`:
     ```javascript
     const popoverWidth = popover.offsetWidth || 384;
     ```

---

## 3. Acceptance Criteria

- The time range inputs (`date`, `start time`, separator, `end time`) fit comfortably inside `#cal-drag-create-popover` with zero overflow.
- The `06:00` input has at least 8px margin from the right edge of the popover card.
- Tested and visually verified across Obsidian, Liquid Glass, and Business Amber themes at screen resolutions `1920x1080`, `1440x900`, and `1024x768`.
