# QA Task 018: Calendar Window Layout Gap, Style Leakage & Time-Grid Compression Verification

## 1. Verification Scope
Verify that Task 018 fixes:
1. The black void / gap between `.cal-sidebar` and `.cal-main-area`.
2. The vertical compression of `.cal-time-axis`.
3. The coordinate alignment between events, time lines, and the red current time indicator.
4. The styling isolation of `main` and `header` across `theme-amber.css`, `theme-obsidian.css`, and `theme-liquid-glass.css`.

---

## 2. Verification Checklist
- [ ] **Step 1: Code Review & Boundary Check**:
  - Ensure only `infra/nginx/html/themes/*.css` and `infra/nginx/html/index.html` were modified.
  - Verify `main` and `header` selectors are scoped (`main:not(.cal-main-area)` or `.chat-main-container`).
  - Verify `.cal-time-axis`, `.cal-grid-columns`, and `.cal-day-col` have rigid `height: 1440px; min-height: 1440px; flex-shrink: 0;`.
  - Verify `.cal-axis-hour` has `height: 60px; min-height: 60px; max-height: 60px; flex-shrink: 0;`.
- [ ] **Step 2: DOM & Style Inspection**:
  - Run automated DOM inspection test verifying computed margin, max-width, and padding of `.cal-main-area` are 0.
  - Verify 24 hour blocks in `#cal-time-axis` have total height 1440px (each 60px).
- [ ] **Step 3: Event & Now-Line Alignment**:
  - Verify event at 15:30 has `top: 930px` and corresponds to 3:30 PM on the axis.
  - Verify live red indicator at 13:40 has `top: 820px` and sits between 1 PM (780px) and 2 PM (840px).
- [ ] **Step 4: Regression Tests**:
  - Run `python3 tests/qa_comprehensive_audit.py` (all 13 tests must pass).
  - Run `dotnet test Fotoprint.slnx` (all 139 tests must pass).
