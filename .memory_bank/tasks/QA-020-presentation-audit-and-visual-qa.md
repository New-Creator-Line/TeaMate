# Task QA-020: Visual & Content Audit of Startup Hackathon Presentation

- **Scope & Assigned Worktree**: Branch `feature/ticktick-calendar-ui` (QA Verifier)
- **Dependencies**: `020A`, `020B`, `020C`
- **File Boundaries**: Read-only audit of `docs/presentation/*` and `scripts/generate_pitch_pptx.js`; may render slide previews to `/tmp/` or `docs/presentation/qa/`.
- **Frozen Contract**: `.memory_bank/contracts/hackathon_presentation_contract.md`

---

## Verification Checklist

1. **ТЗ & Mandatory Content Completeness (8–10 Slides)**:
   - Confirm slide count is `9` (within 8–10) in HTML, PDF, and `.pptx`.
   - Confirm Slide 2 opens with **100% closure of mandatory ТЗ requirements** (`ОТ-01`–`ОТ-08`, `AI-01`–`AI-07`, 6 scenarios).
   - Confirm explicit coverage of:
     - Dashboards, 3 themes, voice STT, MoM `.docx`, TickTick calendar & smart search.
     - **Confirmation required** for all creating/modifying MCP tools (`AI-03`).
     - **Prompt-injection protection** & **anti-spam / rate-limiting**.
     - **MCP access control** via **`employee_reader`** table (read-only for LLM, access granted only by manager).
     - **Langfuse** tracing & detection of unauthorized attempts.
     - **Grafana** centralized error logs for IT + **incident alerts** to responsible channels.
     - **Honest AI** (admits unknowns, never fabricates) & **clear error messages** (`ОТ-05`).
2. **HTML & PDF Verification (`020B`)**:
   - Open `docs/presentation/fotoprint-pitch.html` in Playwright at `1920x1080` and `1280x720`; verify zero console errors, zero broken `<img>` tags, and zero overflow across all 9 slides.
   - Verify `docs/presentation/fotoprint-pitch.pdf` exists and has 9 pages.
3. **PPTX Validation & Visual QA (`020C`)**:
   - Run `python .agents/skills/pptx/scripts/office/validate.py docs/presentation/fotoprint-pitch.pptx` → must pass.
   - Convert `.pptx` via `soffice.py` + `pdftoppm` and visually inspect all 9 rendered slide images for text overflow, low contrast, or overlapping elements.

---

## Output Format

Report `VERDICT: PASS` or `VERDICT: FAIL` with per-slide verification notes.
