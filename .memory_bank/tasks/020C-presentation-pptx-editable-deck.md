# Task 020C: Build Native Editable 9-Slide PowerPoint Deck (`.pptx`)

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (PowerPoint Deck Developer)
- **Dependencies**: `020A` (assets in `docs/presentation/assets/`)
- **File Boundaries**:
  - `docs/presentation/fotoprint-pitch.pptx`
  - `scripts/generate_pitch_pptx.js` (or `.mjs`)
- **Frozen Contract**: `.memory_bank/contracts/hackathon_presentation_contract.md`

---

## Skills to Use (Why & How)

1. **`.agents/skills/pptx/SKILL.md`** (Official Anthropic `pptx` skill)
   - **Why**: Enforces reliable `pptxgenjs` OOXML generation (safe `LAYOUT_WIDE` 13.3"×7.5" or `LAYOUT_16x9`, 6-digit hex colors without `#`, non-mutated shadow objects, native `addChart()` graphs, SVG→PNG icons via `react-icons` + `sharp`, speaker notes via `addNotes()`, and mandatory OOXML + LibreOffice visual QA).
   - **How**: Read `.agents/skills/pptx/SKILL.md` completely before coding. Write `scripts/generate_pitch_pptx.js` using `pptxgenjs`, run `python .agents/skills/pptx/scripts/office/validate.py docs/presentation/fotoprint-pitch.pptx`, then render every slide to JPG via `scripts/office/soffice.py` + `pdftoppm` and inspect for text overflow or overlap.
2. **`.agents/skills/ppt-design-skill/SKILL.md`** & **`.agents/skills/high-quality-slides/SKILL.md`**
   - **Why**: Adds executive slide layout variety (2-column splits, 2×3 feature cards, KPI callouts, comparison columns) so no two consecutive slides look identical.
   - **How**: Consult layout patterns in both skills; use a cohesive dark-executive palette (e.g., Charcoal/Obsidian `#141414` + Electric Amber `#F59E0B` + Ice Slate `#F8FAFC`) and safe-list fonts (`Arial` / `Calibri`) for true-to-width rendering.

---

## Implementation Guidelines

- Generate **9 editable slides** matching `.memory_bank/contracts/hackathon_presentation_contract.md` (same narrative arc: 100% ТЗ compliance opening → features/dashboards/calendar → Human-in-the-Loop confirm, prompt-injection defense, anti-spam, `employee_reader` read-only RBAC managed only by manager → honest AI & clear errors → Langfuse audit + Grafana logs & alerts → metrics & pilot roadmap).
- Add concise Russian **speaker notes** (`slide.addNotes(...)`) on each slide for the 5–7 minute hackathon pitch.

---

## Acceptance Criteria

- `docs/presentation/fotoprint-pitch.pptx` passes `validate.py` with zero OOXML/schema/chart errors.
- All 9 slides render cleanly in LibreOffice/pdftoppm with zero clipped text, zero overlapping shapes, and embedded assets from `docs/presentation/assets/`.
