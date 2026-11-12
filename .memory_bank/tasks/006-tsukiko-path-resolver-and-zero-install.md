# Task 006: TsukikoPathResolver & Zero-Install Cloud Mode Integration

## 1. Scope & Assigned Worktree
- **Target Branch**: `worktree/006-zero-install-resolver` (branched after merging 001, 002, 003)
- **Objective**: Implement `TsukikoPathResolver` in C# (`src/Fotoprint.Core/Audio/TsukikoPathResolver.cs`), integrate it into `TsukikoTranscriber` and `CompositeAudioTranscriber` for Zero-Install Cloud Mode, add unit tests in `tests/Fotoprint.Tests/AudioTranscriptionTests.cs`, and add the 2-variant setup guide to `README.md`.

## 2. File Boundaries (STRICT ALLOWLIST)
- `src/Fotoprint.Core/Audio/TsukikoPathResolver.cs`
- `src/Fotoprint.Core/Audio/TsukikoTranscriber.cs`
- `src/Fotoprint.Core/Audio/CompositeAudioTranscriber.cs`
- `tests/Fotoprint.Tests/AudioTranscriptionTests.cs`
- `README.md`

## 3. Frozen Contract
Read Section 6 of `/Users/test/LittleProjects/SpaceHackathonWithIGreatStone/.memory_bank/contracts/audio_transcription_contract.md`.

## 4. Implementation Guidelines
1. **`TsukikoPathResolver.cs`**:
   - Implement `ResolveCliPath(string? configuredPath = null, string? customSearchRoot = null)` checking:
     1. `Environment.GetEnvironmentVariable("TSUKIKO_CLI_PATH")` (if file exists).
     2. `configuredPath` (if non-empty and `File.Exists(configuredPath)`).
     3. Portable project folder `tools/tsukiko/` searching upwards from `customSearchRoot ?? Directory.GetCurrentDirectory()` and `AppContext.BaseDirectory` (checking `tools/tsukiko/tsukiko-transcribe.exe`, `tools/tsukiko/tsukiko-transcribe`, `tools/tsukiko/Helpers/tsukiko-transcribe.exe`, `tools/tsukiko/tsukiko.app/Contents/Helpers/tsukiko-transcribe`).
     4. Standard OS paths:
        - Windows (`%LOCALAPPDATA%\Programs\tsukiko\tsukiko-transcribe.exe`, `C:\Program Files\tsukiko\tsukiko-transcribe.exe`)
        - macOS (`/Applications/tsukiko.app/Contents/Helpers/tsukiko-transcribe`, `~/Applications/tsukiko.app/Contents/Helpers/tsukiko-transcribe`)
     5. Return `null` if not found anywhere.
   - Implement `ResolveModelPath(string? customSearchRoot = null)` checking `TSUKIKO_MODEL_PATH`, `tools/tsukiko/models/`, `%APPDATA%\app.yuko.tsukiko\models\`, `%LOCALAPPDATA%\app.yuko.tsukiko\models\`, and `~/Library/Application Support/app.yuko.tsukiko/models/`.
2. **`TsukikoTranscriber.cs` & `CompositeAudioTranscriber.cs`**:
   - Use `TsukikoPathResolver.ResolveCliPath(_config.Audio.Tsukiko.CliPath)` to locate the CLI binary.
   - In `CompositeAudioTranscriber`, when `primaryTranscriber.IsAvailableAsync` is `false`, log:
     `"ℹ️ Локальный Tsukiko не обнаружен. Автоматически активирован режим Zero-Install (Gemini / Groq Audio API). Установка локальных компонентов не требуется."`
     and delegate to `fallbackTranscriber` (`GeminiAudioTranscriber`).
3. **`AudioTranscriptionTests.cs` & `README.md`**:
   - Add unit tests for `TsukikoPathResolver` (discovering portable `tools/tsukiko/tsukiko-transcribe.exe` and returning `null` when absent to trigger Zero-Install Cloud Mode).
   - Update `README.md` with the `### 🎙️ Настройка модуля распознавания речи (2 варианта)` section (`Вариант 1 (Быстрый старт без установки, Zero-Install)` and `Вариант 2 (Локально на GPU в 1 клик, 100% оффлайн)` for Windows PowerShell and macOS Bash).
