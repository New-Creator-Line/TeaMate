# Task 020B: Build Interactive 9-Slide HTML Presentation & 1920×1080 PDF Export

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (Frontend Presentation Developer)
- **Dependencies**: `020A` (assets in `docs/presentation/assets/`)
- **File Boundaries**:
  - `docs/presentation/fotoprint-pitch.html`
  - `docs/presentation/fotoprint-pitch.pdf`
- **Frozen Contract**: `.memory_bank/contracts/hackathon_presentation_contract.md`

---

## Skills to Use (Why & How)

1. **`.agents/skills/frontend-slides/SKILL.md`** (and `STYLE_PRESETS.md`, `viewport-base.css`, `animation-patterns.md`, `scripts/export-pdf.sh`)
   - **Why**: Builds a zero-dependency, single-file HTML5 presentation with fixed 16:9 slide staging, keyboard/click navigation, smooth transitions, and pixel-accurate 1920×1080 PDF export via Playwright.
   - **How**: Read `SKILL.md` and `STYLE_PRESETS.md` (choose a cohesive dark-executive / tech-glass preset such as *Electric Studio* or *Bold Signal* matching Fotoprint's Obsidian/Amber/Liquid-Glass brand). Embed `viewport-base.css` rules so every slide fits `100vh` without scrolling or clipping. Export PDF via `bash .agents/skills/frontend-slides/scripts/export-pdf.sh docs/presentation/fotoprint-pitch.html docs/presentation/fotoprint-pitch.pdf`.
2. **`.agents/skills/pro-slides/SKILL.md`** & **`.agents/skills/distinctive-frontend/SKILL.md`**
   - **Why**: Prevents generic "wall-of-text" slides; enforces bold action titles, visual hierarchy, and scannable KPI/feature cards.
   - **How**: Read `pro-slides/SKILL.md` before writing slide copy; keep each slide concise (≤60 words per slide outside diagrams/screenshots) with strong contrast and clear visual anchors.

---

## Implementation Guidelines

- Build **exactly 9 slides** following Section 1 of `.memory_bank/contracts/hackathon_presentation_contract.md`:
  1. Title (`Fotoprint AI Copilot` — Трек 3)
  2. **100% ТЗ Compliance** (explicitly opening with closing all mandatory ТЗ conditions: `ОТ-01..08`, `AI-01..07`, 6 scenarios)
  3. Architecture & End-to-End Flow (`arch_pipeline.png`)
  4. Features & Multi-Theme UX (3 themes, Voice STT, MoM → `.docx` + Bitrix24)
  5. TickTick Calendar & Smart 1,000+ FIO Search
  6. **Security & RBAC**: Human-in-the-loop confirmation for all creating/modifying MCP tools, prompt-injection defense, anti-spam/rate-limiting, and **`employee_reader`** table (read-only for LLM; access granted only by manager)
  7. **Honest AI & Error Handling**: zero hallucinations (admits when it cannot do something), clear structured errors
  8. **Observability**: **Langfuse** (full audit + catching unauthorized attempts) & **Grafana + Alerts** (IT error logs + instant alerts to responsible channels)
  9. Metrics & Pilot Roadmap (`tz_coverage_chart.png` / `system_metrics_chart.png`)
- Include slide counter (`1 / 9`), keyboard navigation (`←`/`→`, `Space`, `F` fullscreen), and responsive scaling.

---

## Acceptance Criteria

- `docs/presentation/fotoprint-pitch.html` opens cleanly in a browser with 9 distinct 16:9 slides, zero horizontal/vertical overflow, and all images loaded from `assets/`.
- `docs/presentation/fotoprint-pitch.pdf` is generated at 1920×1080 with all 9 pages intact.
