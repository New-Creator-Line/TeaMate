# Task 007: ChatGPT-Style Real-Time Voice Waveform Overlay in Web Chat UI

## Scope & Assigned Worktree
- **File Boundaries**: Strictly `infra/nginx/html/index.html`.
- **Target Branch**: Current branch `feature/audio-transcription`.

## Objective
Transform the voice recording UI in `infra/nginx/html/index.html` to mirror ChatGPT's modern voice input:
1. **Overlay / Replace Input Bar**:
   - When recording starts, hide or cover `#user-input` / `.input-bar` with a sleek, full-width voice recording pill/bar (`.voice-bar`).
   - Clean dark glassmorphic styling matching the application theme (`var(--card-bg)`, `var(--border)`, subtle glow/shadow).
2. **Controls on the Voice Bar**:
   - Left: Cancel button (`✕`, gray hover, tooltip "Отменить запись") that stops media stream and discards audio without transcribing.
   - Live status indicator: Pulsing red dot + elapsed timer (`00:00`, updates every second).
   - Center: Real-time dynamic audio visualizer (Canvas).
   - Right: Done/Send button (`✓` or checkmark icon with glowing blue/green button, tooltip "Готово (распознать)").
3. **Real-time Web Audio API Waveform Visualizer**:
   - Initialize `window.AudioContext` and `AnalyserNode` connected to the mic `MediaStream`.
   - Use `requestAnimationFrame` to draw smooth, modern rounded equalizer bars (or smooth curved waveform) on `<canvas id="voice-waveform">`.
   - **Sound Activity Detection**:
     - When user is **speaking**: bars react dynamically to sound frequencies/volume, growing up to 30-36px with smooth transitions and vibrant gradient (`#10b981` to `#3b82f6`).
     - When user is **silent / pauses**: bars smoothly decay down to subtle 2-3px idle dots/lines, clearly indicating silence.
4. **Clean Lifecycle & Error Handling**:
   - Cancel button: stops tracks, closes `AudioContext`, cancels animation frame, resets timer, and restores `.input-bar` with zero network requests.
   - Done button: stops recorder, closes `AudioContext`, cancels animation frame, restores `.input-bar`, and submits audio to `/api/audio/transcribe?language=ru`.
   - Show status badge `⏳ Распознавание речи...` while waiting for the Metal GPU backend.
   - Upon transcription success: insert recognized text into `#user-input`, remove badge, and focus `#user-input`.
   - If user presses `Enter` or clicks Send, message is sent normally.
5. **No Regression**:
   - Attachment button (📎) for meeting files (`handleAudioUpload`) and all other chat functions must remain completely intact.

## Acceptance Criteria
- [ ] Clicking 🎙️ smoothly reveals the voice bar overlay covering the input bar.
- [ ] Real-time audio waveform reacts to microphone voice activity via Web Audio API.
- [ ] During silence, wave flattens to idle dots/bars.
- [ ] Cancel button aborts recording cleanly and restores text input.
- [ ] Done button stops recording, calls `/api/audio/transcribe?language=ru`, and inserts recognized Russian text into input.
