# Task 019B: Multi-Theme Styling for `Day`, `Month`, and `Year` Views (3 Themes)

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (UI Theme & Styling Specialist)
- **File Boundaries**:
  - `infra/nginx/html/themes/theme-obsidian.css`
  - `infra/nginx/html/themes/theme-liquid-glass.css`
  - `infra/nginx/html/themes/theme-amber.css`
- **Frozen Contract**: `.memory_bank/contracts/calendar_view_switcher_contract.md`

---

## Implementation Guidelines

1. **Theme Tokens & Styling across All 3 Themes**:
   Define `--cal-month-cell-bg`, `--cal-month-cell-other-bg`, `--cal-month-cell-hover`, `--cal-year-card-bg`, `--cal-year-card-border`, `--cal-year-card-hover`, `--cal-year-heat-1`, `--cal-year-heat-2`, `--cal-year-heat-3`, and style `.cal-view-dropdown`, `.cal-month-grid`, `.cal-month-cell`, `.cal-month-event-pill`, `.cal-month-more-badge`, `.cal-year-month-card`, and `.cal-year-day-cell` in each theme:
   - **`theme-obsidian.css` (Linear Obsidian Pro)**:
     - Matte dark `#141414` / `#191919` cells & cards, hairline `rgba(255, 255, 255, 0.08)` borders, indigo/blue (`#3b82f6` / `#6366f1`) progressive heatmap tiers (`heat-1`, `heat-2`, `heat-3`), solid `#3b82f6` Today badge.
   - **`theme-liquid-glass.css` (Apple Liquid Glass)**:
     - Frosted translucent glass cells & year cards (`backdrop-filter: blur(18px) saturate(160%)`), specular `rgba(255, 255, 255, 0.14)` borders, sky-blue `#38bdf8` progressive heatmap tiers and Today glow.
   - **`theme-amber.css` (Business Noir & Electric Amber)**:
     - Deep noir `#0f1117` cells & cards, gold-tinted `rgba(245, 158, 11, 0.18)` borders, electric amber `#f59e0b` progressive heatmap tiers (`rgba(245, 158, 11, 0.18 / 0.36 / 0.60)`) and crisp black-on-amber Today badge.
2. **Contrast & Overflow**:
   - Ensure `.cal-month-event-pill` text & time are legible in all 3 themes, `.other-month` cells are clearly dimmed, and `#cal-year-grid` / `#cal-month-grid` have zero horizontal overflow at `1440x900` and `1024x768`.

---

## Acceptance Criteria

- All 3 themes (`obsidian`, `liquid-glass`, `amber`) provide cohesive, distinct styling for `Day`, `Month`, and `Year` views matching the TickTick reference structure.
- Heatmap tiers (`data-density="1|2|3"`) and Today highlights are visually distinct and accessible in all 3 themes.
