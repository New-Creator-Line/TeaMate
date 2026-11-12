# Task 013: UI Redesign - Apple Liquid Glass & VisionOS

## 1. Goal
Develop the **Apple Liquid Glass / VisionOS** UI theme based on `prompts/PROMPT_UI_LIQUID_GLASS.md`, the `frontend-design` and `distinctive-frontend` skills, and the shared UI contract (`.memory_bank/contracts/ui_themes_and_calendar_contract.md`).

## 2. Git Branch & Worktree
- **Branch**: `ui/theme-liquid-glass` (branched from `feature/calendar-integration`).
- **Target Files**:
  - `infra/nginx/html/themes/theme-liquid-glass.css`
  - `infra/nginx/html/index-glass.html` (standalone working preview for visual verification).

## 3. Design System Requirements
- Deep dark cosmic background `#060810` with two floating CSS ambient mesh aura spheres (Sky Blue & Indigo).
- Frosted Liquid Glass panels: `background: rgba(255, 255, 255, 0.035)`, `backdrop-filter: blur(24px) saturate(190%)`.
- Optical glass bevel borders (`border-top: 1px solid rgba(255, 255, 255, 0.28)`).
- Lucide Icons (zero system emojis).
- Liquid glass glowing audio waveform on `#voice-waveform`.
- Full preservation of 100% functional invariants: `/api/agent/chat`, `/api/agent/users`, `/api/audio/transcribe`, `/api/meetings/process-audio`, Bitrix24 tasks, and Calendar components (`#calendar-drawer`, `.slot-chip`, `.meeting-confirm-card`).

## 4. Acceptance Criteria
- [ ] Visual inspection confirms premium frosted glass refraction and ambient depth.
- [ ] Zero emojis; Lucide SVG icons active.
- [ ] All voice and chat interactions work cleanly without console errors.
