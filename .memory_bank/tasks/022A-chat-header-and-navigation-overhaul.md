# Task 022A: Header & Navigation Overhaul (Single-Line 3-Zone Flexbox)

- **Scope & Assigned Worktree**: Frontend UX Architect (`coder-worker`)
- **File Boundaries**:
  - `infra/nginx/html/index.html` (Header structure & navigation JS)
  - `infra/nginx/html/themes/theme-core.css` (Header flexbox layout & segmented control)
- **Frozen Contract**: `.memory_bank/contracts/copilot_chat_ux_contract.md` (Section 1)

---

## 1. Problem Addressed
In the current header, 3 different controls (`#theme-select`, `#toggle-calendar-btn`, and `#current-user-select`) are vertically stacked in the center column, overlapping each other and creating an awkward visual hierarchy (violates Nielsen Heuristics #4 Consistency and #8 Aesthetic/Minimalist Design).

## 2. Implementation Guidelines

1. **Restructure `<header>` into 3 clean horizontal flex zones (`height: 56px`, `align-items: center`)**:
   - **Zone 1 (Left: Brand & System Status)**:
     - Logo `🖨️` + `Fotoprint Copilot`.
     - Model pill: `.model-badge` with subtle green pulse dot and text `NVIDIA Nemotron 120B`.
   - **Zone 2 (Center: Segmented View Navigation)**:
     - Replace standalone button `#toggle-calendar-btn` and vertical container with a sleek segmented control:
       ```html
       <div class="cal-segmented-nav" role="tablist">
         <button id="nav-tab-chat" class="cal-segmented-tab is-active" type="button" role="tab">
           <span class="tab-icon">💬</span> Ассистент
         </button>
         <button id="nav-tab-calendar" class="cal-segmented-tab" type="button" role="tab">
           <span class="tab-icon">📅</span> Календарь
           <span id="cal-events-count-badge" class="cal-badge-pill">0</span>
         </button>
       </div>
       ```
   - **Zone 3 (Right: Tools, Theme & User Profile)**:
     - Custom-styled theme selector pill `#theme-select-container` with custom SVG palette icon and custom chevron.
     - User Profile Pill button `#user-profile-btn` displaying current user's avatar/initials, name, and role badge (e.g. `Алексей Смирнов [Director] ▾`), opening an accessible user switcher modal or dropdown.
     - Group external links (`n8n`, `Langfuse`, `Метрики`) into a clean dropdown button `[ ⚙️ Системы ▾ ]` or subtle icon pills with tooltips.

2. **JavaScript Integration (`index.html`)**:
   - Wire `#nav-tab-chat` to show `#chat-view-container` and hide `#calendar-window-view`.
   - Wire `#nav-tab-calendar` to show `#calendar-window-view` and hide `#chat-view-container`.
   - Update event listeners so switching between Chat and Calendar is instant and smooth without page refresh.

## 3. Acceptance Criteria
1. Header height is strictly 56px with zero vertical overlapping or stacking across all screen widths (`1024px` to `1920px`).
2. Switching between Chat and Calendar works seamlessly via the segmented control.
3. No native unstyled OS `<select>` elements visible in the header.
