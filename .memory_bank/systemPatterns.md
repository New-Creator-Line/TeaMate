# System Patterns & Architecture

## 1. Hybrid Audio Transcription Pattern (Chain of Responsibility / Fallback)
- **`IAudioTranscriber`**: Unified contract in `Fotoprint.Core.Audio` supporting stream and file path transcription plus availability checks.
- **`LocalEngineAudioTranscriber`**: Invokes in-repo C++ Whisper inference engine (`tools/engine/macos/tsukiko-recognizer` Metal GPU or `tools/engine/win/tsukiko-recognizer-vulkan.exe`) with phonetic post-processing via `config/vocabulary.yaml`.
- **`CloudGeminiAudioTranscriber`**: Cloud fallback sending audio to Google Gemini API.
- **`CompositeAudioTranscriber`**: Coordinates local-first execution with automatic fallback and Langfuse telemetry (`Span:Audio_Transcribe`).

## 2. Meeting Transcript Analysis (`IMeetingTranscriptAnalyzer`)
- Extracts structured Minutes of Meeting (MoM) from raw transcript text (`Title`, `Summary`, `Decisions`, `Discussed`, `Risks`, `RequiresClarification`, `ActionItems`).
- Enforces the **No Hallucinations** invariant (`"требует уточнения"` when responsible or deadline is omitted).

## 3. Standalone TickTick-Style Calendar Workspace Pattern
- **Contract**: Defined in `.memory_bank/contracts/ticktick_calendar_window_contract.md`.
- **Three-Column Window Architecture (`#calendar-window-view`)**:
  1. **Far-Left Navigation Rail (`.cal-nav-rail`, `48px`)**: Instant workspace switching between AI Copilot Chat and Full-Screen Calendar, plus sync and quick filters.
  2. **Left Sidebar (`.cal-sidebar`, `236px`)**: Interactive Mini Month Calendar (with active week pill highlight `.in-active-week`, Today badge, and event indicator dots) + multi-faceted filter checkboxes (`Lists / Отделы`, `Сотрудники / Кто занят`, `Tag`).
  3. **Main Time-Grid Area (`.cal-main-area`, `flex: 1`)**:
     - Top Toolbar (`November 2026`, `11:08` live clock badge, RBAC scope switcher `Мои / Вся компания`, `+` quick create, `Week / 3 Days / Day` view selector, `< Today >`).
     - 7-Day Horizontal Header (`W46`, `Thu 12`..`Wed 18`) with stacked **All-Day & Recurring Task Bars**.
     - 24-Hour Vertical Timeline (`60px/hour`, `1px/minute`) with live **Red Current-Time Indicator Line**, **Collision-Aware Event Positioning** (splitting width equally when meetings/tasks overlap), **Event Inspector Popover** (showing task/meeting info and who is busy), and **Stateless Slot Booking Modal** (`POST /api/calendar/confirm`).
- **Tokenized Multi-Theme Skinning**:
  - Uses CSS custom properties (`--cal-*`) so the exact same DOM layout switches in 0ms between:
    - `data-theme="obsidian"` (1:1 TickTick Dark Reference baseline)
    - `data-theme="liquid-glass"` (Apple VisionOS frosted glass & optical bevels)
    - `data-theme="amber"` (Business Noir & Electric Amber gold-on-onyx)
