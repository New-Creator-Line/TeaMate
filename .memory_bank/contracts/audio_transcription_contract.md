# Frozen Contract: SOLID Audio Transcription & Meeting MoM Architecture (v2.0)

## 1. Configuration & External Vocabulary Schema

### 1.1 External Domain Vocabulary (`config/vocabulary.yaml`)
No domain terms or homophone replacements may be hardcoded in C#. They live exclusively in `config/vocabulary.yaml`:
```yaml
version: "1.0"
domain: "typography_fotoprint"

# 1. Термины для подсказки модели (--prompt в Whisper и system_instruction в Gemini)
prompt_terms:
  - "Фотопринт"
  - "Битрикс24"
  - "1С:Управление нашей типографией"
  - "1С"
  - "Heidelberg Speedmaster XL 75"
  - "Heidelberg"
  - "приладка"
  - "выработка"
  - "флексомашина"
  - "разнотон"
  - "постпресс"
  - "лакирование"
  - "мелованная бумага"

# 2. Правила точной автозамены (если нейросеть всё же ошиблась на слух)
replacements:
  "один эс": "1С"
  "один с": "1С"
  "битрикс 24": "Битрикс24"
  "бетрикс": "Битрикс24"
  "фитрикс": "Битрикс24"
  "китрекс": "Битрикс24"
  "гейдельберг": "Heidelberg"
  "хайдельберг": "Heidelberg"
  "фото принт": "Фотопринт"
```

### 1.2 `config.yaml` Audio Section
```yaml
# -----------------------------------------------------------------------------
# 11. РАСПОЗНАВАНИЕ РЕЧИ И АУДИОПРОТОКОЛИРОВАНИЕ (SPEECH-TO-TEXT & AUDIO MOM)
# -----------------------------------------------------------------------------
audio:
  enabled: true
  provider: "auto" # "auto" | "local" | "gemini"
  vocabulary_path: "config/vocabulary.yaml"
  engine:
    timeout_seconds: 300
  gemini:
    model: "gemini-1.5-flash"
  max_file_size_mb: 100
```

### 1.3 C# Configuration Models (`Fotoprint.Core.Configuration`)
```csharp
public class FotoprintConfig
{
    // ... existing properties ...
    public AudioConfig Audio { get; set; } = new();
}

public class AudioConfig
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "auto";
    public string VocabularyPath { get; set; } = "config/vocabulary.yaml";
    public LocalEngineAudioConfig Engine { get; set; } = new();
    public GeminiAudioConfig Gemini { get; set; } = new();
    public int MaxFileSizeMb { get; set; } = 100;
}

public class LocalEngineAudioConfig
{
    public int TimeoutSeconds { get; set; } = 300;
}

public class GeminiAudioConfig
{
    public string Model { get; set; } = "gemini-1.5-flash";
}
```

---

## 2. SOLID Audio Contracts (`namespace Fotoprint.Core.Audio`)

```csharp
namespace Fotoprint.Core.Audio;

public record AudioSegment(
    double StartSeconds,
    double EndSeconds,
    string Text,
    string? Speaker = null
);

public record TranscriptionResult(
    bool Success,
    string Text,
    List<AudioSegment>? Segments,
    string Provider,
    long DurationMs,
    string? ErrorMessage = null
);

public interface IAudioTranscriber
{
    Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string language = "ru",
        CancellationToken ct = default);

    Task<TranscriptionResult> TranscribeFileAsync(
        string filePath,
        string language = "ru",
        CancellationToken ct = default);

    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}

public interface IVocabularyProvider
{
    string Domain { get; }
    IReadOnlyList<string> GetPromptTerms();
    string GetPromptString();
    IReadOnlyDictionary<string, string> GetReplacements();
}

public interface IVocabularyPostProcessor
{
    string ProcessText(string? rawText);
    TranscriptionResult ProcessResult(TranscriptionResult result);
}

public interface IAudioEngineResolver
{
    string? ResolveEnginePath(string? customSearchRoot = null);
    string? ResolveAudioConverterPath(string? customSearchRoot = null);
    string? ResolveModelPath(string? customSearchRoot = null);
    string GetProviderName();
}
```

### Concrete SOLID Implementations (`namespace Fotoprint.Core.Audio`)

1. **`YamlVocabularyProvider : IVocabularyProvider`** (`VocabularyProvider.cs`):
   - Constructors:
     - `public YamlVocabularyProvider(FotoprintConfig? config = null, ILogger<YamlVocabularyProvider>? logger = null)`
     - `public YamlVocabularyProvider(string vocabularyFilePath, ILogger<YamlVocabularyProvider>? logger = null)`
   - Locates `config/vocabulary.yaml` across `config.Audio.VocabularyPath`, `Directory.GetCurrentDirectory()`, and `AppContext.BaseDirectory` (walking up to 6 parent directories).
   - `GetPromptString()` joins `prompt_terms` with `", "`.
   - `GetReplacements()` returns the dictionary of homophone replacements.

2. **`VocabularyPostProcessor : IVocabularyPostProcessor`** (`VocabularyPostProcessor.cs`):
   - Constructor:
     - `public VocabularyPostProcessor(IVocabularyProvider vocabularyProvider)`
   - `ProcessText(string? rawText)` applies all `GetReplacements()` rules case-insensitively (`RegexOptions.IgnoreCase | RegexOptions.CultureInvariant`), matching whole phrases/words cleanly.
   - `ProcessResult(TranscriptionResult result)` post-processes `result.Text` and every `AudioSegment.Text` in `result.Segments`.

3. **`AudioEngineResolver : IAudioEngineResolver`** (`AudioEngineResolver.cs`):
   - Constructor:
     - `public AudioEngineResolver(FotoprintConfig? config = null)`
   - `ResolveEnginePath(string? customSearchRoot = null)`:
     - Checks `AUDIO_ENGINE_PATH` env var first.
     - Searches from `customSearchRoot`, `AppContext.BaseDirectory`, and `Directory.GetCurrentDirectory()` up to 6 parent levels:
       - On macOS (`OSPlatform.OSX`): `tools/engine/macos/tsukiko-recognizer`
       - On Windows (`OSPlatform.Windows`): `tools\engine\win\tsukiko-recognizer-vulkan.exe`, then `tools\engine\win\tsukiko-recognizer-cpu.exe`
       - When `customSearchRoot` is explicitly provided (e.g. in unit tests), checks the OS-native path first and then checks both `tools/engine/macos/tsukiko-recognizer` and `tools/engine/win/tsukiko-recognizer-vulkan.exe` / `tsukiko-recognizer-cpu.exe` inside `customSearchRoot`.
   - `ResolveAudioConverterPath(string? customSearchRoot = null)`:
     - On macOS: checks `/usr/bin/afconvert`.
     - On Windows: checks `tools\engine\win\ffmpeg.exe` (up to 6 parent levels).
   - `ResolveModelPath(string? customSearchRoot = null)`:
     - Priority:
       1. `WHISPER_MODEL_PATH` or `TSUKIKO_MODEL_PATH` env var (when `customSearchRoot == null`)
       2. `models/ggml-large-v3-turbo.bin`, `models/ggml-base.bin` (relative to `customSearchRoot` or `AppContext.BaseDirectory` / `Directory.GetCurrentDirectory()` up to 6 parent levels)
       3. Standard OS model caches (when `customSearchRoot == null`):
          - macOS: `~/Library/Application Support/app.yuko.tsukiko/models/`
          - Windows: `%APPDATA%\app.yuko.tsukiko\models\`, `%LOCALAPPDATA%\app.yuko.tsukiko\models\`
   - `GetProviderName()`:
     - Returns `"local-metal"` on macOS, `"local-vulkan"` on Windows (or `"local-cpu"` if resolved to `tsukiko-recognizer-cpu.exe`).

4. **`LocalEngineAudioTranscriber : IAudioTranscriber`** (`LocalEngineAudioTranscriber.cs`):
   - Constructor:
     ```csharp
     public LocalEngineAudioTranscriber(
         IAudioEngineResolver engineResolver,
         IVocabularyProvider vocabularyProvider,
         IVocabularyPostProcessor postProcessor,
         FotoprintConfig config,
         ILogger<LocalEngineAudioTranscriber>? logger = null)
     ```
   - Converts non-WAV files (`.m4a`, `.oga`, `.ogg`, `.mp3`, `.aac`) to 16kHz mono PCM WAV via `/usr/bin/afconvert -f WAVE -d LEI16@16000 -c 1` (macOS) or `ffmpeg.exe -y -i "{input}" -ar 16000 -ac 1 -c:a pcm_s16le "{wav}"` (Windows).
   - Executes C++ binary directly:
     `{engineExe} -m "{modelPath}" -l {language} -np --prompt "{vocabularyPrompt}" -oj -of "{outputBase}" -f "{wavPath}"`
   - Reads `{outputBase}.json` (or stdout fallback), parses via `ParseEngineOutput`, runs `_postProcessor.ProcessResult(...)`, and cleans up temporary files in `finally`.
   - Static helper for tests:
     `public static TranscriptionResult ParseEngineOutput(string rawOutput, long durationMs, string provider = "local-metal")`

5. **`CloudGeminiAudioTranscriber : IAudioTranscriber`** (`CloudGeminiAudioTranscriber.cs`):
   - Constructor:
     ```csharp
     public CloudGeminiAudioTranscriber(
         HttpClient httpClient,
         FotoprintConfig config,
         IVocabularyProvider? vocabularyProvider = null,
         IVocabularyPostProcessor? postProcessor = null,
         ILogger<CloudGeminiAudioTranscriber>? logger = null)
     ```
   - Passes `vocabularyProvider?.GetPromptString()` in `system_instruction` and post-processes result via `postProcessor?.ProcessResult(...)`.
   - Provider identifier: `"gemini"`.

6. **`CompositeAudioTranscriber : IAudioTranscriber`** (`CompositeAudioTranscriber.cs`):
   - Constructors:
     ```csharp
     public CompositeAudioTranscriber(
         LocalEngineAudioTranscriber localTranscriber,
         CloudGeminiAudioTranscriber cloudTranscriber,
         FotoprintConfig config,
         ILangfuseClient? langfuse = null,
         ILogger<CompositeAudioTranscriber>? logger = null)

     public CompositeAudioTranscriber(
         IAudioTranscriber primaryTranscriber,
         IAudioTranscriber fallbackTranscriber,
         FotoprintConfig config,
         ILangfuseClient? langfuse = null,
         ILogger<CompositeAudioTranscriber>? logger = null)
     ```

---

## 3. In-Repository Engine Binaries & Model Download Scripts

### 3.1 `tools/engine/` (Committed to Git)
- `tools/engine/macos/tsukiko-recognizer` (6.7 MB, executable `+x`, Apple Metal GPU)
- `tools/engine/win/tsukiko-recognizer-vulkan.exe` (54 MB, Windows Vulkan GPU)
- `tools/engine/win/tsukiko-recognizer-cpu.exe` (1.8 MB, Windows CPU fallback)
- `tools/engine/win/ffmpeg.exe` (2.7 MB, Windows audio converter)

### 3.2 Model Download Scripts (`scripts/download-model.sh` & `scripts/download-model.ps1`)
- Old `scripts/setup-tsukiko-mac.sh` and `scripts/setup-tsukiko-win.ps1` MUST be deleted.
- `scripts/download-model.sh` and `scripts/download-model.ps1` ONLY check/download the `.bin` Whisper model (`ggml-large-v3-turbo.bin`, or `ggml-base.bin` with `--light` / `-LightModel`):
  - Checks `models/` and OS cache (`~/Library/Application Support/app.yuko.tsukiko/models/` on macOS, `%APPDATA%\app.yuko.tsukiko\models\` on Windows).
  - If already present, prints green status and downloads nothing (`exit 0`).
  - If missing, downloads from `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin` (or `ggml-base.bin` when `--light` / `-LightModel`) into `models/`.
  - Supports `--help`, `--light`, `--check-only` (exits 0 without downloading), and rejects unknown flags with `exit 1`.
- `.gitignore` ignores `models/*.bin`.

---

## 4. Meeting Transcript Analysis, Repository, and HTTP Endpoints
Unchanged from v1.0 (`IMeetingTranscriptAnalyzer`, `MeetingTranscriptAnalyzer`, `IFotoprintRepository.CreateMeetingAsync`, `POST /api/audio/transcribe`, `POST /api/telegram/webhook`, `POST /api/meetings/process-audio`).
