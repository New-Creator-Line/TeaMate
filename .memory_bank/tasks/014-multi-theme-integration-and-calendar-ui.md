# Task 014: Multi-Theme Switcher & Calendar UI Integration — [Completed - Verified]

## 1. Goal
Unify the three polished UI themes (`theme-amber.css`, `theme-obsidian.css`, `theme-liquid-glass.css`) into the production `infra/nginx/html/index.html`. Add an interactive Theme Switcher in the top-left navigation, connect calendar components (`#calendar-drawer`, density dots), and ensure seamless 0ms switching without page reload.

## 2. Boundaries & Scope
- **Target Files**:
  - `infra/nginx/html/index.html`
  - `infra/nginx/html/themes/`
  - `src/Fotoprint.AiGateway.Api/Program.cs` (configured `UseStaticFiles` for `infra/nginx/html`)
- **Dependencies**: Tasks 010, 011, 012, 013.

## 3. Key Deliverables & Verified Behavior
1. **Header Theme Switcher**:
   - Integrated into `#theme-select` with instant 0ms in-place CSS swap.
   - Supports `Apple Liquid Glass`, `Business Noir & Amber`, `Linear Obsidian Pro`.
   - Persists state in `localStorage` (`fotoprint_theme`).
   - Dynamically rehydrates Lucide icons and updates live audio visualizer waveforms.
2. **Calendar UI Components**:
   - `#calendar-toggle-btn` with dynamic today count badge (`#calendar-today-badge`).
   - Slide-over month drawer (`#calendar-drawer`) with monthly grid and meeting density dots (green 🟢, amber 🟡, red 🔴).
   - In-chat interactive slot chips (`.slot-chip`) and Human-in-the-Loop confirmation card (`.meeting-confirm-card`).
   - Strict 0-emoji policy: 100% Lucide SVG icons.
3. **Automated Verification**:
   - `infra/nginx/html/tests/test_unified_index.py`: 7/7 tests passed.
   - `infra/nginx/html/tests/test_obsidian_theme.py`: 7/7 tests passed.
   - `infra/nginx/html/themes/test-theme-liquid-glass.js`: 6/6 tests passed.
   - Full Playwright visual snapshots and DOM assertions in browser: 100% passed.
   - Full .NET unit test suite: 99/99 tests passed.
