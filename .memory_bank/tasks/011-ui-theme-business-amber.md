# Task 011: UI Redesign - Business Noir & Electric Amber

## 1. Goal
Develop the **Business Noir & Electric Amber** UI theme based on `prompts/PROMPT_UI_BUSINESS_AMBER.md`, the `frontend-design` and `distinctive-frontend` skills, and the shared UI contract (`.memory_bank/contracts/ui_themes_and_calendar_contract.md`).

## 2. Git Branch & Worktree
- **Branch**: `ui/theme-business-amber` (branched from `feature/calendar-integration`).
- **Target Files**:
  - `infra/nginx/html/themes/theme-amber.css`
  - `infra/nginx/html/index-amber.html` (standalone working preview for visual verification).

## 3. Design System Requirements
- Deep onyx base `#090a0d`, surfaces `#111319`, gold borders `rgba(245, 158, 11, 0.15)`.
- Electric Amber gradient: `linear-gradient(135deg, #f59e0b 0%, #fbbf24 45%, #d97706 100%)`.
- Lucide Icons (zero system emojis).
- High-contrast typography: bold white headers, platinum body text `#f4f4f5`.
- Golden audio waveform on `#voice-waveform`.
- Full preservation of 100% functional invariants: `/api/agent/chat`, `/api/agent/users`, `/api/audio/transcribe`, `/api/meetings/process-audio`, Bitrix24 tasks, and Calendar components (`#calendar-drawer`, `.slot-chip`, `.meeting-confirm-card`).

## 4. Acceptance Criteria
- [ ] Visual inspection confirms dark noir aesthetic with rich golden gradients and no visual regression.
- [ ] No emojis present; all replaced with Lucide SVG icons.
- [ ] Audio recording, canvas waveform, and MoM parsing function flawlessly.
