# Task 018C: Multi-Theme Styling for Employee Search, Clear Button, Chips & Highlights

## 1. Scope & Assigned Worktree
- **Role**: UI Theme & Styling Specialist (`coder-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Depends On**: Parallel with `018B` against `.memory_bank/contracts/employee_search_contract.md`

## 2. Required Installed Skills
Apply rules from:
- `.agents/skills/style-glassmorphism/SKILL.md` (`theme-liquid-glass.css`)
- `.agents/skills/distinctive-frontend/SKILL.md` (`theme-amber.css`, `theme-obsidian.css`)
- `.agents/skills/ux-design-tokens/SKILL.md`
- `.agents/skills/ux-a11y-audit/SKILL.md`

## 3. File Boundaries (Strict Allowlist)
- `infra/nginx/html/themes/theme-obsidian.css`
- `infra/nginx/html/themes/theme-liquid-glass.css`
- `infra/nginx/html/themes/theme-amber.css`

## 4. Theme Specifications
Define `--cal-highlight-*`, `--cal-chip-*`, and `--cal-dropdown-*` tokens plus component styles for `.cal-emp-search-box`, `.cal-search-clear-btn`, `.cal-emp-dropdown`, `.cal-emp-chip`, and `mark.cal-search-highlight` across all 3 themes:

1. **`theme-obsidian.css`**:
   - `mark.cal-search-highlight`: `background: rgba(59, 130, 246, 0.28); color: #93c5fd; border-radius: 3px; padding: 0 2px;`
   - Dropdown: `#1a1a1f` with `1px solid rgba(255, 255, 255, 0.12)`, crisp shadow.
   - Chips: `#25355a` subtle indigo pill with `#93c5fd` text and hover-bright `×` button.
2. **`theme-liquid-glass.css`**:
   - `mark.cal-search-highlight`: `background: rgba(56, 189, 248, 0.25); color: #7dd3fc; box-shadow: 0 0 8px rgba(56, 189, 248, 0.25); border-radius: 3px;`
   - Dropdown: Frosted glass `rgba(12, 18, 34, 0.85)`, `backdrop-filter: blur(24px) saturate(190%)`, specular top border.
   - Chips: Translucent sky-glass capsule `rgba(56, 189, 248, 0.16)` with `1px solid rgba(56, 189, 248, 0.4)`.
3. **`theme-amber.css`**:
   - `mark.cal-search-highlight`: `background: rgba(251, 191, 36, 0.25); color: #fde68a; border-radius: 3px; padding: 0 2px;`
   - Dropdown: `#111319` carbon surface with `1px solid rgba(245, 158, 11, 0.25)`, warm amber active row border.
   - Chips: Dark charcoal pill with `1px solid rgba(245, 158, 11, 0.45)` and `#fbbf24` text.

## 5. Acceptance Criteria
1. Highlighting (`<mark class="cal-search-highlight">`) renders in the exact accent color of each active theme (`obsidian` blue, `liquid-glass` cyan, `amber` gold).
2. Clear `✕` button, chips, and dropdowns maintain WCAG contrast and zero layout overflow in all 3 themes.
