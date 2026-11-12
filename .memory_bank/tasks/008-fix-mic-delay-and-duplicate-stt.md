# Task 008: Fix Microphone Latency UI Feedback & Deduplicate Audio Transcription

## Scope & File Boundaries
- `infra/nginx/html/index.html` (Frontend UX, instant optimistic UI on mic click, avoid duplicate text appending, `isTranscribing` guard).
- `src/Fotoprint.Core/Audio/LocalEngineAudioTranscriber.cs` (Backend Whisper repetition loop deduplication).

## Root Causes Identified
1. **Microphone Click Latency / "Задержка несколько секунд"**:
   - When Bluetooth headphones (AirPods, etc.) are used, macOS `getUserMedia({ audio: true })` switches the Bluetooth audio profile (A2DP stereo -> SCO/mSBC bidirectional mono), which takes 1.5–2.5 seconds on the OS level.
   - The UI previously awaited `getUserMedia` before showing ANY visual change. The user experienced 2-3 seconds of an apparently unresponsive UI.
   - **Fix**: Instant optimistic UI feedback. On clicking 🎙️, immediately switch `.input-bar` to `.voice-bar` displaying a pulsating "Подключение микрофона..." state and disabled mic button state, updating to live waveform as soon as the stream resolves.
2. **Duplicate Output ("2 раза подряд выводит результат")**:
   - Frontend: `input.value = currentVal ? `${currentVal} ${data.text.trim()}` : data.text.trim();` appends to whatever was already in the input. If user tests/records twice or if double-events trigger, text duplicates. Also missing `isTranscribing` concurrency guard.
   - Backend: Whisper (especially large-v3) has a known hallucination repetition loop on trailing silence.
   - **Fix**:
     - Frontend: Replace `input.value = data.text.trim();` cleanly upon transcription, add `isTranscribing` boolean lock to prevent duplicate network calls.
     - Backend: Deduplicate consecutive identical segments and trim repeated hallucination loops in `ParseEngineOutput`.
