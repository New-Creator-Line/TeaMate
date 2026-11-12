# Task 001: SOLID Audio Architecture, External Vocabulary (`config/vocabulary.yaml`), and Core Transcribers

## 1. Scope & Assigned Worktree
- **Target Branch**: `worktree/001-core-audio`
- **Role**: Backend Core Developer (`coder-worker`)

## 2. File Boundaries (Strict Allowlist)
You may ONLY create, modify, or delete files within:
- `config.yaml`
- `config/vocabulary.yaml`
- `src/Fotoprint.Core/Configuration/FotoprintConfig.cs`
- `src/Fotoprint.Core/Audio/**`
- `src/Fotoprint.Core/Data/FotoprintRepository.cs`
- `src/Fotoprint.Core/Protocols/MeetingProtocolGenerator.cs`

Do NOT touch `src/Fotoprint.AiGateway.Api/**`, `tests/**`, `scripts/**`, or `README.md`.

## 3. Frozen Contract Reference
Read and strictly implement `.memory_bank/contracts/audio_transcription_contract.md` (v2.0).

## 4. Implementation Requirements
1. **Cleanup Legacy Tsukiko App Coupling**:
   - Delete `src/Fotoprint.Core/Audio/TsukikoPathResolver.cs` and `src/Fotoprint.Core/Audio/TsukikoTranscriber.cs` (`git rm`).
   - Delete `src/Fotoprint.Core/Audio/GeminiAudioTranscriber.cs` (`git rm`) and replace with `src/Fotoprint.Core/Audio/CloudGeminiAudioTranscriber.cs`.
2. **External Domain Vocabulary (`config/vocabulary.yaml`)**:
   - Create `config/vocabulary.yaml` with `version: "1.0"`, `domain: "typography_fotoprint"`, `prompt_terms` (`Фотопринт`, `Битрикс24`, `1С:Управление нашей типографией`, `1С`, `Heidelberg Speedmaster XL 75`, `Heidelberg`, `приладка`, `выработка`, `флексомашина`, `разнотон`, `постпресс`, `лакирование`, `мелованная бумага`), and `replacements` (`"один эс": "1С"`, `"один с": "1С"`, `"битрикс 24": "Битрикс24"`, `"бетрикс": "Битрикс24"`, `"фитрикс": "Битрикс24"`, `"китрекс": "Битрикс24"`, `"гейдельберг": "Heidelberg"`, `"хайдельберг": "Heidelberg"`, `"фото принт": "Фотопринт"`).
   - Update `config.yaml` and `src/Fotoprint.Core/Configuration/FotoprintConfig.cs` (`AudioConfig` with `Enabled`, `Provider`, `VocabularyPath = "config/vocabulary.yaml"`, `Engine = new LocalEngineAudioConfig { TimeoutSeconds = 300 }`, `Gemini = new GeminiAudioConfig { Model = "gemini-1.5-flash" }`, `MaxFileSizeMb = 100`).
3. **`IVocabularyProvider` & `YamlVocabularyProvider` (`src/Fotoprint.Core/Audio/VocabularyProvider.cs`)**:
   - Implement `IVocabularyProvider` and `YamlVocabularyProvider`.
   - Support constructors:
     - `public YamlVocabularyProvider(FotoprintConfig? config = null, ILogger<YamlVocabularyProvider>? logger = null)`
     - `public YamlVocabularyProvider(string vocabularyFilePath, ILogger<YamlVocabularyProvider>? logger = null)`
   - Locate `config/vocabulary.yaml` across explicit path, `config.Audio.VocabularyPath`, `Directory.GetCurrentDirectory()`, and `AppContext.BaseDirectory` (walking up to 6 parent directories).
   - Parse with `YamlDotNet` (`UnderscoredNamingConvention.Instance` + `.IgnoreUnmatchedProperties()`).
4. **`IVocabularyPostProcessor` & `VocabularyPostProcessor` (`src/Fotoprint.Core/Audio/VocabularyPostProcessor.cs`)**:
   - Implement `IVocabularyPostProcessor` and `VocabularyPostProcessor(IVocabularyProvider vocabularyProvider)`.
   - `ProcessText(string? rawText)` replaces all configured homophones case-insensitively (`RegexOptions.IgnoreCase | RegexOptions.CultureInvariant`, ordering longer keys first so `"битрикс 24"` matches before `"битрикс"`).
   - `ProcessResult(TranscriptionResult result)` applies `ProcessText` to `result.Text` and every segment's `Text` in `result.Segments`.
5. **`IAudioEngineResolver` & `AudioEngineResolver` (`src/Fotoprint.Core/Audio/AudioEngineResolver.cs`)**:
   - Implement `IAudioEngineResolver` and `AudioEngineResolver(FotoprintConfig? config = null)`.
   - `ResolveEnginePath(string? customSearchRoot = null)`:
     - Checks `AUDIO_ENGINE_PATH` env var (when `customSearchRoot == null`).
     - Searches from `customSearchRoot` (or `AppContext.BaseDirectory` and `Directory.GetCurrentDirectory()` up to 6 parent levels):
       - macOS: `tools/engine/macos/tsukiko-recognizer`
       - Windows: `tools\engine\win\tsukiko-recognizer-vulkan.exe`, fallback to `tools\engine\win\tsukiko-recognizer-cpu.exe`
       - When `customSearchRoot` is non-null, check the current OS binary first, and if not found in `customSearchRoot`, also check the other OS paths inside `customSearchRoot` so cross-platform unit tests work on any OS.
   - `ResolveAudioConverterPath(string? customSearchRoot = null)`:
     - macOS: `/usr/bin/afconvert`
     - Windows: `tools/engine/win/ffmpeg.exe` (searching up to 6 parent levels from `customSearchRoot` / `AppContext.BaseDirectory` / `Directory.GetCurrentDirectory()`).
   - `ResolveModelPath(string? customSearchRoot = null)`:
     - Checks `WHISPER_MODEL_PATH` / `TSUKIKO_MODEL_PATH` env vars (when `customSearchRoot == null`).
     - Searches `models/ggml-large-v3-turbo.bin` and `models/ggml-base.bin` up to 6 parent levels from `customSearchRoot` / `AppContext.BaseDirectory` / `Directory.GetCurrentDirectory()`.
     - When `customSearchRoot == null`, also checks `~/Library/Application Support/app.yuko.tsukiko/models/` (macOS) and `%APPDATA%\app.yuko.tsukiko\models\` / `%LOCALAPPDATA%\app.yuko.tsukiko\models\` (Windows).
   - `GetProviderName()` returns `"local-metal"` on macOS, `"local-vulkan"` on Windows (or `"local-cpu"` if resolved engine path ends with `-cpu.exe`).
6. **`LocalEngineAudioTranscriber` (`src/Fotoprint.Core/Audio/LocalEngineAudioTranscriber.cs`)**:
   - Constructor: `(IAudioEngineResolver engineResolver, IVocabularyProvider vocabularyProvider, IVocabularyPostProcessor postProcessor, FotoprintConfig config, ILogger<LocalEngineAudioTranscriber>? logger = null)`.
   - `IsAvailableAsync`: returns `true` when `_config.Audio.Enabled` is `true`, `_engineResolver.ResolveEnginePath()` exists, and `_engineResolver.ResolveModelPath()` exists.
   - Converts non-WAV files (`.m4a`, `.oga`, `.ogg`, `.mp3`, `.aac`, `.webm`) to 16kHz mono PCM WAV via `/usr/bin/afconvert -f WAVE -d LEI16@16000 -c 1 "{input}" "{tempWav}"` (macOS) or `"{ffmpegPath}" -y -i "{input}" -ar 16000 -ac 1 -c:a pcm_s16le "{tempWav}"` (Windows).
   - Runs C++ engine directly:
     `{engineExe} -m "{modelPath}" -l {language} -np --prompt "{vocabularyPrompt}" -oj -of "{outputBase}" -f "{wavPath}"`
   - Reads `{outputBase}.json` (or stdout fallback), parses via `public static TranscriptionResult ParseEngineOutput(string rawOutput, long durationMs, string provider = "local-metal")`, applies `_postProcessor.ProcessResult(...)`, and cleans up temporary `.wav` and `.json` files in `finally`.
7. **`CloudGeminiAudioTranscriber` (`src/Fotoprint.Core/Audio/CloudGeminiAudioTranscriber.cs`)**:
   - Constructor: `(HttpClient httpClient, FotoprintConfig config, IVocabularyProvider? vocabularyProvider = null, IVocabularyPostProcessor? postProcessor = null, ILogger<CloudGeminiAudioTranscriber>? logger = null)`.
   - Injects `_vocabularyProvider?.GetPromptString()` into `system_instruction` and runs `_postProcessor?.ProcessResult(...)` on the parsed result.
8. **`CompositeAudioTranscriber` (`src/Fotoprint.Core/Audio/CompositeAudioTranscriber.cs`)**:
   - Update constructor to accept `(LocalEngineAudioTranscriber localTranscriber, CloudGeminiAudioTranscriber cloudTranscriber, FotoprintConfig config, ILangfuseClient? langfuse = null, ILogger<CompositeAudioTranscriber>? logger = null)` as well as the `IAudioTranscriber` interface overload.
   - Support `Provider` values `"auto"`, `"local"`, and `"gemini"`.

## 5. Acceptance Criteria
1. `dotnet build src/Fotoprint.Core/Fotoprint.Core.csproj --no-restore` succeeds with 0 errors and 0 warnings.
2. Zero references to `TsukikoPathResolver` or `TsukikoTranscriber` remain in `src/Fotoprint.Core`.
3. `config/vocabulary.yaml` exists and has zero hardcoded terms in C#.
