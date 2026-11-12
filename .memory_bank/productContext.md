# Product Context: Fotoprint AI Copilot

## User Personas
- **CEO (General Director)**: Needs weekly KPI summaries, rapid meeting protocols, and oversight of overdue tasks.
- **Head of Production**: Needs tracking of Heidelberg Speedmaster press issues, paper/ink shortages, and shift assignments.
- **Head of Sales**: Needs VIP client order statuses and approval flows.
- **Chief Accountant**: Needs invoice payment tracking and overtime approval alignment.

## User Journeys Added in `feature/audio-transcription`
1. **Direct Audio Transcription (`POST /api/audio/transcribe`)**:
   Uploads `.m4a`, `.wav`, `.ogg`, `.oga`, or `.mp3` files and receives structured JSON with transcript text, timestamped segments, provider (`tsukiko-metal` or `gemini`), and latency in milliseconds.
2. **Telegram Voice & Audio Ingestion (`POST /api/telegram/webhook`)**:
   Intercepts `message.voice` and `message.audio` updates, downloads the file via Telegram Bot API (`getFile`), transcribes it via `IAudioTranscriber`, and feeds the resulting text into the existing `ContextSwitchDetector` -> `FotoprintAiAgent` / `n8n` pipeline.
3. **End-to-End Meeting Audio Processing (`POST /api/meetings/process-audio`)**:
   Transcribes a full meeting recording, analyzes it via Gemini (with deterministic fallback), saves the meeting and protocol in PostgreSQL, generates an official `.docx` Word protocol, persists `pending` action items, and produces a Telegram message payload with interactive `[✅ Поставить в Битрикс: "..."]` (`create_bitrix_task:<id>`) buttons.
