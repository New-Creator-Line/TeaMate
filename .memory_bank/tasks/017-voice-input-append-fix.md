# Task 017: Voice Input Text Append Fix (Preserve Existing User Input)

## 1. Scope & Objective
- **Problem**: When a user dictates text via speech-to-text voice input (`/api/audio/transcribe`), the returned transcript overwrites `input.value` completely (`input.value = data.text.trim();`), destroying any text the user had previously typed manually.
- **Goal**: Preserve existing text in `#prompt-input` / `#message-input` and intelligently append the recognized transcript (supporting cursor insertion or end-append with automatic whitespace separation).

## 2. File Boundaries
- `infra/nginx/html/index.html`
- `infra/nginx/html/index-obsidian.html`
- `infra/nginx/html/index-glass.html`
- `infra/nginx/html/index-amber.html`

## 3. Implementation Details
In `stopVoiceRecording()` / `transcribeAudio()` response handler:
Replace:
```javascript
if (data && data.success && data.text) {
  input.value = data.text.trim();
  input.focus();
```
With cursor-aware non-destructive insertion:
```javascript
if (data && data.success && data.text) {
  const recognizedText = data.text.trim();
  const start = input.selectionStart;
  const end = input.selectionEnd;
  const currentVal = input.value || '';

  if (currentVal.trim().length === 0) {
    input.value = recognizedText;
  } else if (typeof start === 'number' && typeof end === 'number' && start <= currentVal.length) {
    const before = currentVal.substring(0, start);
    const after = currentVal.substring(end);
    const sepBefore = (before.length > 0 && !/\s$/.test(before)) ? ' ' : '';
    const sepAfter = (after.length > 0 && !/^\s/.test(after)) ? ' ' : '';
    input.value = before + sepBefore + recognizedText + sepAfter + after;
    const newPos = (before + sepBefore + recognizedText).length;
    input.setSelectionRange(newPos, newPos);
  } else {
    const sep = (!/\s$/.test(currentVal)) ? ' ' : '';
    input.value = currentVal + sep + recognizedText;
  }

  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.focus();
```

## 4. Acceptance Criteria
1. If the textarea is empty, recognized text becomes `input.value`.
2. If the user already typed "Привет, подготовь мне" and then dictates "список встреч", `input.value` becomes "Привет, подготовь мне список встреч".
3. If the user places the cursor in the middle of existing text, the recognized speech is cleanly inserted at the cursor position with appropriate word spacing.
4. `input.dispatchEvent(new Event('input'))` is triggered.
5. All existing frontend tests pass.
