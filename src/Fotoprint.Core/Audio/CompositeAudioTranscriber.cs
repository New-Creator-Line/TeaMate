using Fotoprint.Core.Configuration;
using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Audio;

public class CompositeAudioTranscriber : IAudioTranscriber
{
    public const string ZeroInstallLogMessage =
        "ℹ️ Локальный C++ движок распознавания не обнаружен. Автоматически активирован режим Zero-Install (Gemini Cloud Audio API). Установка локальных компонентов не требуется.";

    private readonly IAudioTranscriber _primaryTranscriber;
    private readonly IAudioTranscriber _fallbackTranscriber;
    private readonly FotoprintConfig _config;
    private readonly ILangfuseClient? _langfuse;
    private readonly ILogger<CompositeAudioTranscriber>? _logger;

    [ActivatorUtilitiesConstructor]
    public CompositeAudioTranscriber(
        LocalEngineAudioTranscriber localTranscriber,
        CloudGeminiAudioTranscriber cloudTranscriber,
        FotoprintConfig config,
        ILangfuseClient? langfuse = null,
        ILogger<CompositeAudioTranscriber>? logger = null)
        : this(
            (IAudioTranscriber)localTranscriber,
            (IAudioTranscriber)cloudTranscriber,
            config,
            langfuse,
            logger)
    {
    }

    public CompositeAudioTranscriber(
        IAudioTranscriber primaryTranscriber,
        IAudioTranscriber fallbackTranscriber,
        FotoprintConfig config,
        ILangfuseClient? langfuse = null,
        ILogger<CompositeAudioTranscriber>? logger = null)
    {
        _primaryTranscriber = primaryTranscriber ?? throw new ArgumentNullException(nameof(primaryTranscriber));
        _fallbackTranscriber = fallbackTranscriber ?? throw new ArgumentNullException(nameof(fallbackTranscriber));
        _config = config ?? new FotoprintConfig();
        _langfuse = langfuse;
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!_config.Audio.Enabled)
        {
            return false;
        }

        var mode = GetProviderMode();
        return mode switch
        {
            "local" or "tsukiko" => await _primaryTranscriber.IsAvailableAsync(ct),
            "gemini" => await _fallbackTranscriber.IsAvailableAsync(ct),
            _ => await _primaryTranscriber.IsAvailableAsync(ct) || await _fallbackTranscriber.IsAvailableAsync(ct)
        };
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string language = "ru",
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        if (!_config.Audio.Enabled)
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: "disabled",
                DurationMs: 0,
                ErrorMessage: "Audio transcription is disabled in configuration.");
        }

        if (audioStream == null)
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: "none",
                DurationMs: 0,
                ErrorMessage: "Audio stream is null.");
        }

        Stream workingStream = audioStream;
        MemoryStream? bufferedCopy = null;

        try
        {
            if (!audioStream.CanSeek)
            {
                bufferedCopy = new MemoryStream();
                await audioStream.CopyToAsync(bufferedCopy, ct);
                bufferedCopy.Position = 0;
                workingStream = bufferedCopy;
            }

            var mode = GetProviderMode();
            TranscriptionResult result;

            if (mode == "gemini")
            {
                ResetStreamIfPossible(workingStream);
                result = await _fallbackTranscriber.TranscribeAsync(workingStream, fileName, language, ct);
            }
            else if (mode == "local" || mode == "tsukiko")
            {
                ResetStreamIfPossible(workingStream);
                result = await _primaryTranscriber.TranscribeAsync(workingStream, fileName, language, ct);
            }
            else
            {
                // "auto" mode: Primary (Local Engine) -> Fallback (Gemini Cloud)
                TranscriptionResult? primaryResult = null;
                var isPrimaryAvailable = await _primaryTranscriber.IsAvailableAsync(ct);

                if (isPrimaryAvailable)
                {
                    ResetStreamIfPossible(workingStream);
                    primaryResult = await _primaryTranscriber.TranscribeAsync(workingStream, fileName, language, ct);
                    if (primaryResult.Success && !string.IsNullOrWhiteSpace(primaryResult.Text))
                    {
                        await LogTelemetrySpanAsync(startTime, fileName, language, mode, primaryResult, ct);
                        return primaryResult;
                    }

                    _logger?.LogWarning(
                        "[CompositeAudioTranscriber] Primary transcriber ({Provider}) failed for {FileName}: {Error}. Falling back to Gemini.",
                        primaryResult.Provider,
                        fileName,
                        primaryResult.ErrorMessage);
                }
                else
                {
                    _logger?.LogInformation(ZeroInstallLogMessage);
                }

                ResetStreamIfPossible(workingStream);
                var fallbackResult = await _fallbackTranscriber.TranscribeAsync(workingStream, fileName, language, ct);
                if (fallbackResult.Success)
                {
                    result = fallbackResult;
                }
                else
                {
                    var combinedErr = primaryResult != null
                        ? $"Primary ({primaryResult.Provider}): {primaryResult.ErrorMessage} | Fallback ({fallbackResult.Provider}): {fallbackResult.ErrorMessage}"
                        : $"Primary unavailable | Fallback ({fallbackResult.Provider}): {fallbackResult.ErrorMessage}";

                    result = fallbackResult with { ErrorMessage = combinedErr };
                }
            }

            await LogTelemetrySpanAsync(startTime, fileName, language, mode, result, ct);
            return result;
        }
        finally
        {
            bufferedCopy?.Dispose();
        }
    }

    public async Task<TranscriptionResult> TranscribeFileAsync(
        string filePath,
        string language = "ru",
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var fileName = Path.GetFileName(filePath);

        if (!_config.Audio.Enabled)
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: "disabled",
                DurationMs: 0,
                ErrorMessage: "Audio transcription is disabled in configuration.");
        }

        var mode = GetProviderMode();
        TranscriptionResult result;

        if (mode == "gemini")
        {
            result = await _fallbackTranscriber.TranscribeFileAsync(filePath, language, ct);
        }
        else if (mode == "local" || mode == "tsukiko")
        {
            result = await _primaryTranscriber.TranscribeFileAsync(filePath, language, ct);
        }
        else
        {
            TranscriptionResult? primaryResult = null;
            var isPrimaryAvailable = await _primaryTranscriber.IsAvailableAsync(ct);

            if (isPrimaryAvailable)
            {
                primaryResult = await _primaryTranscriber.TranscribeFileAsync(filePath, language, ct);
                if (primaryResult.Success && !string.IsNullOrWhiteSpace(primaryResult.Text))
                {
                    await LogTelemetrySpanAsync(startTime, fileName, language, mode, primaryResult, ct);
                    return primaryResult;
                }

                _logger?.LogWarning(
                    "[CompositeAudioTranscriber] Primary file transcription ({Provider}) failed for {FilePath}: {Error}. Falling back to Gemini.",
                    primaryResult.Provider,
                    filePath,
                    primaryResult.ErrorMessage);
            }
            else
            {
                _logger?.LogInformation(ZeroInstallLogMessage);
            }

            var fallbackResult = await _fallbackTranscriber.TranscribeFileAsync(filePath, language, ct);
            if (fallbackResult.Success)
            {
                result = fallbackResult;
            }
            else
            {
                var combinedErr = primaryResult != null
                    ? $"Primary ({primaryResult.Provider}): {primaryResult.ErrorMessage} | Fallback ({fallbackResult.Provider}): {fallbackResult.ErrorMessage}"
                    : $"Primary unavailable | Fallback ({fallbackResult.Provider}): {fallbackResult.ErrorMessage}";

                result = fallbackResult with { ErrorMessage = combinedErr };
            }
        }

        await LogTelemetrySpanAsync(startTime, fileName, language, mode, result, ct);
        return result;
    }

    private string GetProviderMode()
    {
        var raw = _config.Audio.Provider;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "auto";
        }

        return raw.Trim().ToLowerInvariant();
    }

    private static void ResetStreamIfPossible(Stream stream)
    {
        if (stream.CanSeek && stream.Position > 0)
        {
            stream.Position = 0;
        }
    }

    private async Task LogTelemetrySpanAsync(
        DateTime startTime,
        string fileName,
        string language,
        string configuredMode,
        TranscriptionResult result,
        CancellationToken ct)
    {
        if (_langfuse == null) return;

        try
        {
            var traceId = _langfuse.GenerateTraceId();
            await _langfuse.CreateSpanAsync(
                traceId: traceId,
                name: "Span:Audio_Transcription",
                startTime: startTime,
                endTime: DateTime.UtcNow,
                input: new
                {
                    file_name = fileName,
                    language = language,
                    configured_provider = configuredMode
                },
                output: new
                {
                    success = result.Success,
                    provider = result.Provider,
                    text_length = result.Text?.Length ?? 0,
                    segments_count = result.Segments?.Count ?? 0,
                    error = result.ErrorMessage
                },
                metadata: new
                {
                    provider = result.Provider,
                    duration_ms = result.DurationMs,
                    success = result.Success,
                    file_name = fileName
                },
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[CompositeAudioTranscriber] Failed to log Span:Audio_Transcription.");
        }
    }
}
