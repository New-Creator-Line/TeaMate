# Task 003: SOLID Audio Unit Tests & README Documentation

## 1. Scope & Assigned Worktree
- **Target Branch**: `worktree/003-tests-docs`
- **Role**: Test & Docs Developer (`coder-worker`)

## 2. File Boundaries (Strict Allowlist)
You may ONLY create or modify:
- `tests/Fotoprint.Tests/AudioTranscriptionTests.cs`
- `tests/Fotoprint.Tests/FotoprintAiAgentTests.cs`
- `tests/Fotoprint.Tests/FotoprintConfigTests.cs`
- `tests/Fotoprint.Tests/Fotoprint.Tests.csproj`
- `README.md`

Do NOT touch `src/**`, `scripts/**`, `tools/**`, `config.yaml`, or `config/vocabulary.yaml`.

## 3. Frozen Contract Reference
Read and strictly implement `.memory_bank/contracts/audio_transcription_contract.md` (v2.0).

## 4. Implementation Requirements
1. **Update `tests/Fotoprint.Tests/FotoprintConfigTests.cs`**:
   - Update audio configuration assertions to verify `config.Audio.Enabled == true`, `config.Audio.Provider == "auto"`, `config.Audio.VocabularyPath == "config/vocabulary.yaml"`, `config.Audio.Engine.TimeoutSeconds == 300`, `config.Audio.Gemini.Model == "gemini-1.5-flash"`, and `config.Audio.MaxFileSizeMb == 100` (remove old `config.Audio.Tsukiko` assertions).
2. **Update `tests/Fotoprint.Tests/AudioTranscriptionTests.cs`** to test the SOLID v2.0 components:
   - `YamlVocabularyProvider_Loads_PromptTerms_And_Replacements_From_Yaml`: verifies loading `config/vocabulary.yaml` (or a temp YAML file), checking `Domain`, `GetPromptTerms()`, `GetPromptString()`, and `GetReplacements()`.
   - `VocabularyPostProcessor_Replaces_Homophones_CaseInsensitively_In_Text_And_Segments`: verifies that `"один эс"`, `"бетрикс"`, `"фитрикс"`, `"хайдельберг"`, `"фото принт"` are replaced with `"1С"`, `"Битрикс24"`, `"Heidelberg"`, `"Фотопринт"` in both `TranscriptionResult.Text` and `AudioSegment.Text`.
   - `AudioEngineResolver_Resolves_Mac_And_Windows_Engine_Binaries_And_Models_Or_Returns_Null`: verifies `ResolveEnginePath(tempDir)` finding `tools/engine/macos/tsukiko-recognizer` and `tools/engine/win/tsukiko-recognizer-vulkan.exe` (and returning `null` on empty dir), and `ResolveModelPath(tempDir)` finding `models/ggml-large-v3-turbo.bin` and `models/ggml-base.bin`.
   - `LocalEngineAudioTranscriber_ParseEngineOutput_Parses_WhisperCpp_Json_And_Timestamped_Text_And_EdgeCases`: verifies `LocalEngineAudioTranscriber.ParseEngineOutput(...)` on whisper.cpp JSON (`transcription` array), standard segments JSON, timestamped lines, and empty/error inputs.
   - `CompositeAudioTranscriber_Returns_Primary_When_LocalEngine_Succeeds`, `CompositeAudioTranscriber_Falls_Back_To_CloudGemini_When_Local_Unavailable_Or_Fails`, and `CompositeAudioTranscriber_Respects_Explicit_Provider_Configuration`.
   - `MeetingTranscriptAnalyzer_Extracts_Tasks_And_Enforces_NoHallucinations_Rule` and `MeetingProtocolGenerator_Renders_RequiresClarification_For_Missing_Deadline_And_Responsible`.
3. **Update `README.md`**:
   - Replace references to installing the Tsukiko desktop app or running `setup-tsukiko-*` with the new SOLID architecture:
     - In-repo C++ inference engines (`tools/engine/macos/tsukiko-recognizer` with Metal GPU and `tools/engine/win/tsukiko-recognizer-vulkan.exe` with Vulkan GPU + `ffmpeg.exe`).
     - External domain dictionary `config/vocabulary.yaml` (`prompt_terms` + `replacements`) replaceable for any industry without changing C# code.
     - 2 startup options:
       - **Вариант 1 (Быстрый старт — Облачный режим Zero-Install)**: just set `GEMINI_API_KEY` in `.env` and run `dotnet run --project src/Fotoprint.AiGateway.Api`.
       - **Вариант 2 (Полный локальный режим с GPU Metal / Vulkan — 0 руб, 100% оффлайн)**: C++ engines are already in `tools/engine/`; run `bash scripts/download-model.sh` (macOS) or `powershell -ExecutionPolicy Bypass -File scripts/download-model.ps1` (Windows) to download only the `.bin` model (`models/ggml-large-v3-turbo.bin`) if not already present on disk.

## 5. Acceptance Criteria
1. All unit tests in `tests/Fotoprint.Tests` compile and pass (`0 errors, 0 failed`) when merged with `worktree/001-core-audio`.
2. Zero references to `TsukikoPathResolver` or `TsukikoTranscriber` remain in `tests/**` or `README.md`.
