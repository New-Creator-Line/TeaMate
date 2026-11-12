# Task 018: Fix Calendar Window Layout Gap, Semantic Style Leakage & Time-Grid Compression

## 1. Context & Root Cause Analysis
During high-DPI desktop verification of the TickTick 3-column standalone calendar window (`#calendar-window-view`), three critical defects were diagnosed:

1. **Defect A: Black Void / Gap Between Sidebar and Calendar Area (~160px gap)**:
   - Root Cause: Unscoped semantic tag selector `main { max-width: 980px; margin: 0 auto; padding: 16px 20px; }` in `theme-amber.css` (line 336), `theme-obsidian.css` (line 363), and `theme-liquid-glass.css` (line 475) leaked into `<main class="cal-main-area">`.
   - Effect: The calendar workspace was centered with `margin: 0 auto` and capped to 980px width inside a 1920+px viewport, producing an empty canvas-black gap (`#090a0d`) between `.cal-sidebar` and `.cal-main-area`, and padding offsets.
   - Similar Leakage: Unscoped `header { padding: 12px 24px; box-shadow: ... }` leaked into `<header class="cal-toolbar">`.

2. **Defect B: 24-Hour Vertical Axis Compression & Event Dislocation (15:30 Event Pushed Below 11 PM)**:
   - Root Cause: In `theme-core.css`, `.cal-time-axis` lacked an explicit `height: 1440px; min-height: 1440px; flex-shrink: 0;`, and `.cal-axis-hour` lacked `flex-shrink: 0;`.
   - Effect: The 24 hourly marks collapsed from 60px down to ~30px to fit the viewport height (~700px). However, timed events use absolute coordinates `block.style.top = item.startMin + 'px'`. For a meeting at 15:30 (3:30 PM), `startMin = 930px`. Because the visual 24h timeline ended at ~700px, the 15:30 event rendered below 23:00 (11 PM) at the bottom of the grid. Similarly, the red "current time" line (`top: 820px` for 13:40) rendered below 22:00.

3. **Defect C: 12 AM Overlap with All-Day Strip**:
   - Root Cause: `.cal-axis-hour` has `transform: translateY(-8px)`. For hour 0 (`00:00` / `12 AM`), the upward shift causes text overlap with `.cal-allday-strip`.

---

## 2. File Boundaries (Strict Allowlist)
- `infra/nginx/html/themes/theme-core.css`
- `infra/nginx/html/themes/theme-amber.css`
- `infra/nginx/html/themes/theme-obsidian.css`
- `infra/nginx/html/themes/theme-liquid-glass.css`
- `infra/nginx/html/index.html`

**FORBIDDEN**: Any backend C# code, SQL scripts, or files outside `infra/nginx/html/`.

---

## 3. Implementation Requirements

### 3.1 Scope Global Theme Tag Rules
- In `theme-amber.css`, `theme-obsidian.css`, and `theme-liquid-glass.css`:
  - Change `main { ... }` to `main:not(.cal-main-area)` or scope to `#chat-area` / `.chat-main-container`.
  - Change `header { ... }` to `header:not(.cal-toolbar)` or scope to `.app-header`.

### 3.2 Reset & Protect `.cal-main-area` and `.cal-toolbar` in `theme-core.css`
- Ensure `.cal-main-area`:
  ```css
  .cal-main-area {
    flex: 1;
    max-width: none !important;
    width: auto !important;
    margin: 0 !important;
    padding: 0 !important;
    height: 100%;
    display: flex;
    flex-direction: column;
    background: var(--cal-bg-canvas);
    overflow: hidden;
    position: relative;
    box-sizing: border-box;
  }
  ```
- Ensure `.cal-toolbar`:
  ```css
  .cal-toolbar {
    margin: 0 !important;
    box-shadow: none !important;
    height: 52px;
    min-height: 52px;
    box-sizing: border-box;
  }
  ```

### 3.3 Rigid 1440px Timeline Grid (1 Minute = 1 Pixel)
- In `theme-core.css`:
  - `.cal-time-axis`:
    ```css
    .cal-time-axis {
      width: 56px;
      min-width: 56px;
      max-width: 56px;
      height: 1440px;
      min-height: 1440px;
      flex-shrink: 0;
      border-right: 1px solid var(--cal-grid-line);
      display: flex;
      flex-direction: column;
      position: relative;
      background: var(--cal-bg-toolbar);
      user-select: none;
      box-sizing: border-box;
    }
    ```
  - `.cal-axis-hour`:
    ```css
    .cal-axis-hour {
      height: 60px;
      min-height: 60px;
      max-height: 60px;
      flex-shrink: 0;
      box-sizing: border-box;
      font-size: 11px;
      color: var(--cal-text-dim);
      text-align: right;
      padding-right: 8px;
      transform: translateY(-8px);
    }
    .cal-axis-hour:first-child {
      transform: translateY(2px);
    }
    ```
  - `.cal-grid-columns`:
    ```css
    .cal-grid-columns {
      flex: 1;
      display: grid;
      grid-template-columns: repeat(7, 1fr);
      position: relative;
      height: 1440px;
      min-height: 1440px;
      flex-shrink: 0;
      box-sizing: border-box;
    }
    ```
  - `.cal-day-col`:
    ```css
    .cal-day-col {
      border-right: 1px solid var(--cal-grid-line);
      position: relative;
      height: 1440px;
      min-height: 1440px;
      box-sizing: border-box;
    }
    ```

### 3.4 Auto-Scroll to Current Time on Open
- In `index.html`: When `#calendar-window-view` is opened, auto-scroll `.cal-timegrid-scroll` smoothly to current time minus 90 minutes (e.g. `scrollTop = Math.max(0, currentMin - 90)`), ensuring users immediately see the live red indicator and afternoon events without manual deep scroll.

---

## 4. Acceptance Criteria
1. **Zero Gap**: The right border of `.cal-sidebar` connects directly and seamlessly to `.cal-main-area` (gap = 0px).
2. **True 60px Hour Height**: Every hour slot in `.cal-time-axis` is exactly 60px tall and aligns 1:1 with horizontal lines in `.cal-day-col`.
3. **Accurate Event Placement**: Event at 15:30 renders between `3 PM` (top: 900px) and `4 PM` (top: 960px). Red current time indicator at 13:40 renders between `1 PM` and `2 PM`.
4. **First Hour Visibility**: `12 AM` label does not clip into `.cal-allday-strip`.
5. **No Regressions**: All 13 tests in `tests/qa_comprehensive_audit.py` and all 139 dotnet tests continue to pass.
