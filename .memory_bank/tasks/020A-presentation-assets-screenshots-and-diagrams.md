# Task 020A: Capture Live UI Screenshots & Generate Architecture/Metric Visuals

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (Visual Assets & Diagrams Specialist)
- **File Boundaries**:
  - `docs/presentation/assets/` (output directory)
  - Read-only access to `docs/screenshots/`, `infra/nginx/html/`, `README.md`, `docker-compose.yml`
- **Frozen Contract**: `.memory_bank/contracts/hackathon_presentation_contract.md`

---

## Skills to Use (Why & How)

1. **`.agents/skills/mermaid-diagrams/SKILL.md`**
   - **Why**: Guarantees clean, readable, syntax-valid architecture and security-flow diagrams instead of crooked manual boxes.
   - **How**: Read `SKILL.md`, author Mermaid definitions for:
     - `arch_pipeline.png` (Slide 3: Web UI → Nginx → .NET AI Gateway → n8n → MCP Servers → PostgreSQL / Whisper Metal STT)
     - `security_rbac_flow.png` (Slide 6: User Request → Prompt-Injection Guard & Rate Limiter → `employee_reader` Read-Only RBAC Check → Human-in-the-Loop Confirmation → MCP Execution)
     - `observability_alerts.png` (Slide 8: AI Gateway & n8n → Langfuse Trace/Audit + Grafana Logs → Automated Alert Channel to IT/On-Call)
     Render to high-DPI PNG/SVG (transparent or dark `#0f172a` background) into `docs/presentation/assets/`.
2. **`.agents/skills/chart-visualization/SKILL.md`** & **`.agents/skills/antv-g2-chart/SKILL.md`**
   - **Why**: Produces crisp, executive-ready data visualizations for ТЗ coverage and system metrics.
   - **How**: Read `SKILL.md`, generate `tz_coverage_chart.png` (Slide 2/9: 100% coverage across ТЗ criteria weights: 25% Business Value, 20% UX, 15% Data/API, 15% Tech, 15% Security, 5% Scale, 5% Demo) and `system_metrics_chart.png` (Slide 9: real project test/latency metrics from `tests/` and `README.md`).

---

## Implementation Guidelines

1. Reuse existing verified screenshots from `docs/screenshots/` (e.g., `cal_drag_create_*.png`) and/or capture fresh `1440x900` Playwright screenshots of `infra/nginx/html/index.html` covering:
   - `ui_liquid_glass.png`, `ui_amber.png`, `ui_obsidian.png` (Chat + Voice Bar + MoM Card)
   - `cal_week_ticktick.png`, `cal_drag_popover.png`
2. Store all assets cleanly in `docs/presentation/assets/` with 16:9-friendly aspect ratios and high resolution (≥1200px wide).

---

## Acceptance Criteria

- `docs/presentation/assets/` contains all required screenshots, `arch_pipeline.png`, `security_rbac_flow.png`, `observability_alerts.png`, and metric charts.
- All diagrams and charts have crisp contrast on dark/glass backgrounds and zero overlapping labels.
