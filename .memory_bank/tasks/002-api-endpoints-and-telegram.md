# Task 002: Gateway SOLID Audio DI Registration & Endpoints

## 1. Scope & Assigned Worktree
- **Target Branch**: `worktree/002-api-gateway`
- **Role**: Backend API Developer (`coder-worker`)

## 2. File Boundaries (Strict Allowlist)
You may ONLY modify:
- `src/Fotoprint.AiGateway.Api/Program.cs`
- `src/Fotoprint.AiGateway.Api/Fotoprint.AiGateway.Api.csproj`

Do NOT touch `src/Fotoprint.Core/**`, `tests/**`, `scripts/**`, `config.yaml`, `config/vocabulary.yaml`, or `README.md`.

## 3. Frozen Contract Reference
Read and strictly implement `.memory_bank/contracts/audio_transcription_contract.md` (v2.0).

## 4. Implementation Requirements
1. In `src/Fotoprint.AiGateway.Api/Program.cs`, replace the old `TsukikoTranscriber` / `GeminiAudioTranscriber` DI registrations with the SOLID Audio services:
   ```csharp
   builder.Services.AddSingleton<IVocabularyProvider, YamlVocabularyProvider>();
   builder.Services.AddSingleton<IVocabularyPostProcessor, VocabularyPostProcessor>();
   builder.Services.AddSingleton<IAudioEngineResolver, AudioEngineResolver>();
   builder.Services.AddSingleton<LocalEngineAudioTranscriber>();
   builder.Services.AddHttpClient<CloudGeminiAudioTranscriber>();
   builder.Services.AddSingleton<IAudioTranscriber>(sp =>
       new CompositeAudioTranscriber(
           sp.GetRequiredService<LocalEngineAudioTranscriber>(),
           sp.GetRequiredService<CloudGeminiAudioTranscriber>(),
           sp.GetRequiredService<FotoprintConfig>(),
           sp.GetService<LangfuseTracingService>(),
           sp.GetRequiredService<ILogger<CompositeAudioTranscriber>>()));
   builder.Services.AddSingleton<IMeetingTranscriptAnalyzer, MeetingTranscriptAnalyzer>();
   ```
2. Ensure NO duplicate `AddSingleton<IAudioTranscriber, CompositeAudioTranscriber>()` line exists so `ValidateOnBuild = true` under the `Development` launch profile succeeds cleanly.
3. Keep `POST /api/audio/transcribe`, `POST /api/meetings/process-audio`, and `POST /api/telegram/webhook` working seamlessly via `IAudioTranscriber`.

## 5. Acceptance Criteria
1. Only `src/Fotoprint.AiGateway.Api/Program.cs` and `src/Fotoprint.AiGateway.Api/Fotoprint.AiGateway.Api.csproj` are modified on `worktree/002-api-gateway`.
2. When merged with `worktree/001-core-audio`, `dotnet build src/Fotoprint.AiGateway.Api/Fotoprint.AiGateway.Api.csproj --no-restore` succeeds with 0 errors and 0 warnings, and startup under `Development` launch profile succeeds.
