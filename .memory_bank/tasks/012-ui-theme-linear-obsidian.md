# Task 012: UI Redesign - Linear & Raycast Obsidian Pro

## 1. Goal
Develop the **Linear & Raycast Obsidian Pro** UI theme based on `prompts/PROMPT_UI_LINEAR_OBSIDIAN.md`, the `frontend-design` and `distinctive-frontend` skills, and the shared UI contract (`.memory_bank/contracts/ui_themes_and_calendar_contract.md`).

## 2. Git Branch & Worktree
- **Branch**: `ui/theme-linear-obsidian` (branched from `feature/calendar-integration`).
- **Target Files**:
  - `infra/nginx/html/themes/theme-obsidian.css`
  - `infra/nginx/html/index-obsidian.html` (standalone working preview for visual verification).

## 3. Design System Requirements
- Matte graphite background `#08090c`, surfaces `#0f1015`, subpixel borders `1px solid rgba(255, 255, 255, 0.07)`.
- 95% monochrome aesthetic with surgical status indicators (Indigo `#6366f1` focus, Emerald `#10b981` success).
- Raycast-style keyboard shortcut chips (`<kbd class="kbd-chip">Enter</kbd>`, `<kbd class="kbd-chip">Esc</kbd>`).
- Lucide Icons (stroke-width 1.5, zero system emojis).
- Clean, thin, high-density audio waveform bars on `#voice-waveform`.
- Full preservation of 100% functional invariants: `/api/agent/chat`, `/api/agent/users`, `/api/audio/transcribe`, `/api/meetings/process-audio`, Bitrix24 tasks, and Calendar components (`#calendar-drawer`, `.slot-chip`, `.meeting-confirm-card`).

## 4. Acceptance Criteria
- [ ] Visual inspection confirms ultra-minimalist, high-density engineering look.
- [ ] Keyboard badges properly styled.
- [ ] Zero emojis; Lucide SVG icons active.
- [ ] All voice and chat interactions work cleanly without console errors.
