# Employee / Client Smart FIO & Role Search Contract

## 1. Objective
Replace the hardcoded 4-checkbox employee/participant lists across **all** calendar interfaces (`#cal-drag-create-popover`, `#cal-quick-modal`, and `#cal-sidebar` "Сотрудники (Кто занят)") with a scalable, order-independent, relevance-ranked **Smart FIO, Position & Department Search** capable of handling 1,000+ records smoothly, with theme-colored match highlighting (`<mark class="cal-search-highlight">`).

---

## 2. Search Algorithm Contract (Frontend & Backend Parity)

### 2.1. Normalization & Tokenization
- Normalize strings: `str.toLowerCase().replace(/ё/g, 'е').trim()`.
- Split query by whitespace/punctuation into unique non-empty query tokens: `Q = [q_1, q_2, ..., q_k]`.
- Pre-build a search index entry per employee/client when data loads (`buildEmployeeSearchIndex(list)`):
  - `fioTokens`: tokenized `[lastName, firstName, middleName]`
  - `roleTokens`: tokenized `position` + `department`
  - `fullNameNorm`, `positionNorm`, `departmentNorm`

### 2.2. Order-Independent Matching & Relevance Scoring
- **Order Independence**: Every query token `q_i` MUST match at least one field (`fioTokens`, `roleTokens`, or `email/telegram_id`), regardless of token order (e.g., `"Алексей Смирнов"` and `"Смирнов Алексей"` and `"директор Смирнов"` all match `Смирнов Алексей Сергеевич — Генеральный директор`).
- **Scoring Weights per Query Token**:
  1. Exact match on `fioTokens[j]` (`token === q_i`): **+100** (bonus `+20` if `j === 0` Last Name)
  2. Prefix match on `fioTokens[j]` (`token.startsWith(q_i)`): **+70** (bonus `+15` if `j === 0`)
  3. Exact or prefix match on `roleTokens` (position / department): **+50**
  4. Substring match inside `fullNameNorm` or `positionNorm` / `departmentNorm`: **+25**
  5. Bounded Fuzzy Match (Damerau-Levenshtein distance `<= 1` for `q_i.length >= 4`, `<= 2` for `q_i.length >= 7`): **+12**
- **Result Ranking**:
  - Filter out records with `score === 0` (or where any `q_i` failed to match).
  - Sort descending by `totalScore`, tie-break alphabetically by `full_name`.
  - Return top `limit` results (default `5` for popover/modal dropdowns with total-match indicator, or `10` for sidebar).

### 2.3. Theme-Colored Match Highlighting
- Highlight all matched substrings/tokens in `full_name`, `position`, and `department` using `<mark class="cal-search-highlight">...</mark>` (XSS-safe HTML escaping before wrapping).
- If a token matched via fuzzy edit distance without exact substring, highlight the closest matching word prefix.

---

## 3. UI Component Contract (All 3 Interfaces)

### 3.1. Popover & Modal Participant Picker (`#cal-drag-create-popover` & `#cal-quick-modal`)
- **Selected Participants Chips Container**:
  - `#drag-create-selected-chips` (in `#cal-drag-create-popover`) and `#quick-selected-chips` (in `#cal-quick-modal`).
  - Renders selected people as removable chips: `Смирнов А.С. ×` (clicking `×` removes from selection).
- **Search Input Row (`.cal-emp-search-box`)**:
  - `<input type="text" id="drag-create-emp-search" placeholder="Поиск по ФИО, должности или отделу..." autocomplete="off">`
  - Inline clear button `<button type="button" id="drag-create-emp-clear" class="cal-search-clear-btn" aria-label="Очистить поиск">` (Lucide `x` icon), visible whenever input has text; clicking clears input, resets dropdown, and refocuses input.
  - Same pair in `#cal-quick-modal`: `#quick-emp-search` + `#quick-emp-clear`.
- **Results Dropdown (`.cal-emp-dropdown`)**:
  - `#drag-create-emp-dropdown` and `#quick-emp-dropdown`, positioned directly below the search input.
  - Displays up to 5–8 top-ranked matches (each item shows: highlighted Full Name, highlighted Position & Department, busy/free dot, and checkmark if already selected).
  - Keyboard navigation: `ArrowDown` / `ArrowUp` to move active highlight, `Enter` to toggle selection, `Escape` to close dropdown (without closing the whole popover if dropdown is open!).
  - Dynamic Popover Repositioning: When chips are added/removed or dropdown opens/closes inside `#cal-drag-create-popover`, call `requestAnimationFrame(repositionDragCreatePopover)`.
- **Backward Compatibility**:
  - Maintain `selectedDragParticipantIds` and `selectedQuickParticipantIds` (Set/Array of `employee_id`) so `submitDragCreate()` and `submitQuickSchedule()` work seamlessly.

### 3.2. Sidebar Employee Filter (`#cal-sidebar` — "Сотрудники (Кто занят)")
- Add compact search bar above `#cal-sidebar-emp-list`:
  - `#sidebar-emp-search` + inline `#sidebar-emp-clear` (`×` button).
  - Filters and relevance-sorts the visible employee rows in real time, highlighting matched FIO/position text with `<mark class="cal-search-highlight">`.
  - Preserves checked/unchecked filter state (`calendarFilterState.employees`) even while searching!

---

## 4. Backend API Extension (`GET /api/employees/search`)
- **Endpoint**: `GET /api/employees/search?q={query}&limit={limit}` (proxied/served by `Fotoprint.AiGateway.Api`).
- **Behavior**:
  - Tokenizes `q` by whitespace, matches each token order-independently across `last_name`, `first_name`, `middle_name`, `position`, `department`, `email`.
  - Ranks and returns `{ employees: [ { employee_id, telegram_id, full_name, short_name, department, position, roles } ], total_matched }`.
  - Existing `GET /api/agent/users` remains unchanged for full backward compatibility.

---

## 5. Multi-Theme CSS Token Contract
All three themes (`theme-obsidian.css`, `theme-liquid-glass.css`, `theme-amber.css`) MUST define:
- `--cal-highlight-bg`: Highlight background for `<mark class="cal-search-highlight">`
- `--cal-highlight-text`: Highlight text color
- `--cal-chip-bg`, `--cal-chip-border`, `--cal-chip-text`: Selected participant chips
- `--cal-dropdown-bg`, `--cal-dropdown-border`, `--cal-dropdown-shadow`, `--cal-dropdown-hover`: Autocomplete dropdown styling
