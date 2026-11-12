# Task 018B: Frontend Smart FIO/Role Search Engine & Autocomplete UI (Popover, Modal, Sidebar)

## 1. Scope & Assigned Worktree
- **Role**: Frontend Calendar & Search Developer (`coder-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)

## 2. Required Installed Skills
Consult before coding:
- `.agents/skills/ux-design-component/SKILL.md` (combobox/autocomplete accessibility, keyboard navigation, chip removal)
- `.agents/skills/interface-design/SKILL.md` (clean compact search bar, clear button, highlight discipline)

## 3. File Boundaries (Strict Allowlist)
- `infra/nginx/html/index.html`
- `infra/nginx/html/themes/theme-core.css`

*Do NOT modify `theme-obsidian.css`, `theme-liquid-glass.css`, or `theme-amber.css` in this task (handled by Task `018C`).*

## 4. Frozen Contract Reference
Implement Sections 2 and 3 of `.memory_bank/contracts/employee_search_contract.md`:

1. **Smart Tokenized & Ranked Search Engine (`searchEmployees(query, limit)`)**:
   - Build an indexed directory `employeeSearchIndex` from `availableUsers` (and optionally fetch `/api/employees/search?q=...` when directory > 100 or on query).
   - Normalize `ё → е`, lowercase, split query into tokens.
   - Support **arbitrary word order** (`"Алексей Смирнов"` == `"Смирнов Алексей"` == `"Смирнов директор"`).
   - Search across **Last Name, First Name, Middle Name, Position, and Department**.
   - Weighted scoring (Exact FIO token > Prefix FIO token > Position/Department > Substring > Bounded 1-edit Fuzzy typo tolerance for tokens >= 4 chars).
   - Return top `5` (or `limit`) closest matches sorted by score descending.
   - Implement XSS-safe `highlightMatches(text, queryTokens)` wrapping matched segments in `<mark class="cal-search-highlight">...</mark>`.
2. **Participant Search & Chips in `#cal-drag-create-popover` (Bottom Section)**:
   - Replace the static 4 checkboxes with:
     - Selected chips row `#drag-create-selected-chips` (`Смирнов А.С. ×`).
     - Search input `#drag-create-emp-search` + inline clear `✕` button `#drag-create-emp-clear` (Lucide `x`).
     - Scrollable autocomplete dropdown `#drag-create-emp-dropdown` showing top 5 ranked matches with highlighted FIO & position/department.
     - Keyboard navigation (`ArrowDown`, `ArrowUp`, `Enter` to toggle, `Esc` to close dropdown first before closing popover).
     - Call `requestAnimationFrame(repositionDragCreatePopover)` whenever chips or dropdown open/close change the popover height!
3. **Participant Search & Chips in `#cal-quick-modal`**:
   - Same smart search + clear `✕` button + chips (`#quick-selected-chips`, `#quick-emp-search`, `#quick-emp-clear`, `#quick-emp-dropdown`) wired to `submitQuickSchedule()`.
4. **Sidebar Employee Filter Search (`#cal-sidebar` — "Сотрудники (Кто занят)")**:
   - Add `#sidebar-emp-search` + `#sidebar-emp-clear` (`✕` button) above `#cal-sidebar-emp-list`.
   - Dynamically renders/filters employee rows sorted by relevance with `<mark class="cal-search-highlight">` while preserving `calendarFilterState.employees` checked state.

## 5. Acceptance Criteria
1. Typing `"Алексей Смирнов"` or `"Смирнов Алексей"` or `"директор"` in any of the 3 search inputs finds `Смирнов Алексей Сергеевич` at rank #1 with matched text wrapped in `<mark class="cal-search-highlight">`.
2. Clicking the `✕` clear button clears the search input, resets the list, and refocuses the input.
3. Selecting/removing participants via chips updates `submitDragCreate()` and `submitQuickSchedule()` payloads accurately.
4. Works smoothly even when `availableUsers` is mocked with 1,000+ generated employees.
