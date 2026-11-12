# Task 016: Multi-Theme Styling for TickTick Calendar Window (Obsidian, Liquid Glass, Business Amber)

## 1. Scope & Assigned Worktree
- **Role**: UI Theme & Styling Specialist (`coder-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Phase**: Phase 2 (Multi-Theme Styling After Core Calendar Implementation)
- **Depends On**: Tasks `015A`, `015B`, `015C`

## 2. Required Installed Skills
Apply the aesthetic rules and token systems from:
- `.agents/skills/distinctive-frontend/SKILL.md` (atmospheric depth, contrast typography, zero AI slop)
- `.agents/skills/style-glassmorphism/SKILL.md` (optical translucency, blur layers, subtle specular borders)
- `.agents/skills/ux-design-tokens/SKILL.md` (semantic CSS variables `--cal-*` across themes)
- `.agents/skills/ux-a11y-audit/SKILL.md` (WCAG contrast compliance on time blocks and labels)

## 3. File Boundaries (Strict Allowlist)
- `infra/nginx/html/themes/theme-obsidian.css`
- `infra/nginx/html/themes/theme-liquid-glass.css`
- `infra/nginx/html/themes/theme-amber.css`

## 4. Theme Specifications for the TickTick Calendar Window

### 1. `theme-obsidian.css` — 1:1 TickTick Reference Dark (Baseline)
- Matches `Интерфейс календаря Reference.png` pixel-for-pixel in color and contrast:
  - Rail background: `#111111`; Sidebar background: `#181818`; Main grid background: `#1e1e1e`.
  - Grid hairlines: `1px solid #2a2a2a` (hours) and `1px dashed #232323` (half-hours).
  - Active day circle (`12`) & mini-calendar active week pill: `#3b82f6` circle, `rgba(59, 130, 246, 0.18)` week pill.
  - Timed meeting blocks (`ЛР АМД`, `Оперативка`): `#465260` (slate-steel) with `3px solid #7c93b0` left bar, `#f1f5f9` title, `#cbd5e1` time label.
  - Compact task blocks (`Выпить воду`, `English`): `#283566` (deep royal indigo) with `#60a5fa` icon/border.
  - All-day recurring bars (`Поощрение себя`): `#4d3824` (warm umber/amber) with `#d97706` left accent.
  - Current time line: `#ef4444` line + `#dc2626` pill badge (`11:08`).

### 2. `theme-liquid-glass.css` — Apple VisionOS / Liquid Glass
- Preserves the exact TickTick 3-column geometry while transforming surfaces into optical frosted glass:
  - Main window & sidebar panels use `backdrop-filter: blur(28px) saturate(190%)`, `background: rgba(10, 14, 26, 0.65)`, allowing the floating ambient aura spheres to softly refract behind the time grid.
  - Timed meeting blocks use translucent glass cards (`background: rgba(56, 189, 248, 0.14)`, `backdrop-filter: blur(12px)`, top optical bevel `border-top: 1px solid rgba(255, 255, 255, 0.28)`, left neon cyan accent `3px solid #38bdf8`).
  - Task pills use frosted indigo glass (`rgba(99, 102, 241, 0.22)`).
  - Current time indicator glows with a soft laser red/coral aura (`box-shadow: 0 0 12px rgba(244, 63, 94, 0.6)`).

### 3. `theme-amber.css` — Business Noir & Electric Amber (Black-White-Gold)
- High-contrast executive terminal aesthetic:
  - Carbon-black grid canvas (`#090a0d`), sidebar (`#0d0f14`), and rail (`#060709`) with warm gold hairline borders (`rgba(245, 158, 11, 0.12)`).
  - Today circle (`12`) and active week pill rendered in signature Electric Amber gradient (`linear-gradient(135deg, #f59e0b, #fbbf24)` with `#000` bold number).
  - Timed meeting blocks styled in executive charcoal (`#161922`) with a `3px solid #fbbf24` gold left bar and crisp white typography.
  - All-day recurring bars styled with a warm gold mesh gradient (`rgba(245, 158, 11, 0.22)`).

## 5. Acceptance Criteria
1. Switching `#theme-select` between `obsidian`, `liquid-glass`, and `amber` updates the entire TickTick Calendar Window in 0ms without page reload or layout shift.
2. Text contrast on all event blocks, time labels, and sidebar filters meets WCAG AA/AAA readability across all three themes.
3. All existing CSS tests in `infra/nginx/html/tests/` pass.
