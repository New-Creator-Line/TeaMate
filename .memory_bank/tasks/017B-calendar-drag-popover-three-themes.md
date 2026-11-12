# Task 017B: Multi-Theme Styling for Drag Ghost & Anchored Creation Popover (Obsidian, Liquid Glass, Amber)

## 1. Scope & Assigned Worktree
- **Role**: UI Theme & Styling Specialist (`coder-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Depends On**: Task `017A` (can be developed in parallel against `.memory_bank/contracts/calendar_drag_create_contract.md`)

## 2. Required Installed Skills
Apply the aesthetic rules from:
- `.agents/skills/style-glassmorphism/SKILL.md` (for `theme-liquid-glass.css`)
- `.agents/skills/distinctive-frontend/SKILL.md` (for `theme-amber.css` & `theme-obsidian.css`)
- `.agents/skills/ux-design-tokens/SKILL.md` (CSS token consistency)
- `.agents/skills/ux-a11y-audit/SKILL.md` (contrast & focus rings on inputs and checkboxes)

## 3. File Boundaries (Strict Allowlist)
- `infra/nginx/html/themes/theme-obsidian.css`
- `infra/nginx/html/themes/theme-liquid-glass.css`
- `infra/nginx/html/themes/theme-amber.css`

## 4. Theme Specifications
Define the tokens and component overrides for `.cal-drag-ghost` and `#cal-drag-create-popover` (including `[data-placement="left"]` and `[data-placement="right"]` directional indicators) in all three theme stylesheets:

1. **`theme-obsidian.css` (TickTick Reference Dark)**:
   - `.cal-drag-ghost`: `background: rgba(59, 130, 246, 0.25); border: 1.5px dashed #60a5fa; color: #eff6ff;`
   - `#cal-drag-create-popover`: `#1e1e1e` elevated card, `1px solid rgba(255, 255, 255, 0.12)`, crisp dark shadow `0 16px 40px rgba(0, 0, 0, 0.65)`, `#3b82f6` primary submit button.
2. **`theme-liquid-glass.css` (Apple Liquid Glass / VisionOS)**:
   - `.cal-drag-ghost`: Translucent frosted sky glass `rgba(56, 189, 248, 0.2)`, `backdrop-filter: blur(8px)`, `border: 1px solid rgba(56, 189, 248, 0.65)`, subtle cyan glow.
   - `#cal-drag-create-popover`: Optical frosted glass `rgba(12, 18, 34, 0.72)`, `backdrop-filter: blur(28px) saturate(190%)`, top specular bevel `border-top: 1px solid rgba(255, 255, 255, 0.28)`, glowing sky-blue submit button.
3. **`theme-amber.css` (Business Noir & Electric Amber)**:
   - `.cal-drag-ghost`: `rgba(245, 158, 11, 0.2)`, `border: 1.5px dashed #fbbf24`, warm amber glow.
   - `#cal-drag-create-popover`: `#111319` carbon surface, `1px solid rgba(245, 158, 11, 0.28)`, electric amber gradient submit button (`#f59e0b` → `#fbbf24`) with black bold text (`#000000`).

## 5. Acceptance Criteria
1. All three themes visually distinguish `.cal-drag-ghost` and `#cal-drag-create-popover` while preserving 100% text readability and WCAG contrast.
2. Switching themes via `#theme-select` while `#cal-drag-create-popover` is open updates the popover and ghost block in 0ms without layout breakage.
3. Zero system emojis used; all icons use Lucide SVG.
