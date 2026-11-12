# Task 022D: Multi-Theme Styling for Header, Hero & Composer (3 Themes)

- **Scope & Assigned Worktree**: UI Theme & Styling Specialist (`coder-worker`)
- **File Boundaries**:
  - `infra/nginx/html/themes/theme-obsidian.css`
  - `infra/nginx/html/themes/theme-liquid-glass.css`
  - `infra/nginx/html/themes/theme-amber.css`
- **Frozen Contract**: `.memory_bank/contracts/copilot_chat_ux_contract.md` (Section 4)

---

## 1. Goal
Provide cohesive, craft-first styling for the newly introduced Header, Bento Hero cards, Smart Chips, and Floating Composer across all 3 official themes without any raw unstyled browser controls.

## 2. Theme Directives

1. **`theme-obsidian.css` (Linear Obsidian Pro)**:
   - Header: Solid matte `#141414`, subtle border `rgba(255, 255, 255, 0.08)`.
   - Segmented Tab Active: Deep grey `#27272a` pill with crisp white text.
   - Bento Cards: Matte `#18181b` with hairline `rgba(255, 255, 255, 0.08)` border and indigo `#6366f1` icon accents.
   - Composer: `#1c1c1f` with focus ring in indigo `rgba(99, 102, 241, 0.35)`.
   - Primary Button: Indigo `#6366f1` / `#4f46e5` gradient.

2. **`theme-liquid-glass.css` (Apple Liquid Glass)**:
   - Header: Translucent glass with `backdrop-filter: blur(20px) saturate(180%)`, specular `rgba(255, 255, 255, 0.15)` top border.
   - Segmented Tab Active: Frosted glass pill with subtle inner glow and sky-blue `#38bdf8` accent.
   - Bento Cards: Frosted translucent cards with blurred backdrop and sky-blue icons.
   - Composer: Deep glass capsule with specular border and sky-blue glowing focus.
   - Primary Button: Sky-blue `#0284c7` to `#38bdf8` glass button.

3. **`theme-amber.css` (Business Noir & Electric Amber)**:
   - Header: Deep Noir `#0d0f14`, gold-tinted hairline `rgba(245, 158, 11, 0.18)`.
   - Segmented Tab Active: Dark amber tint `#1f1a10` with electric amber `#f59e0b` border.
   - Bento Cards: Noir cards `#12151c` with amber accent icons and hover glow.
   - Composer: Dark noir capsule with electric amber focus border `rgba(245, 158, 11, 0.4)`.
   - Primary Button: Electric Amber `#f59e0b` with black high-contrast bold typography (`#000000`).

## 3. Acceptance Criteria
1. Switching themes updates Header, Bento Hero, Chips, and Composer instantly.
2. Zero unstyled native `<select>` elements in any theme.
3. WCAG contrast and readability pass across all 3 themes.
