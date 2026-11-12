using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Fotoprint.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Audio;

public class CloudGeminiAudioTranscriber : IAudioTranscriber
{
    public const string ProviderName = "gemini";

    private readonly HttpClient _httpClient;
    private readonly FotoprintConfig _config;
    private readonly IVocabularyProvider? _vocabularyProvider;
    private readonly IVocabularyPostProcessor? _postProcessor;
    private readonly ILogger<CloudGeminiAudioTranscriber>? _logger;

    public CloudGeminiAudioTranscriber(
        HttpClient httpClient,
        FotoprintConfig config,
        IVocabularyProvider? vocabularyProvider = null,
        IVocabularyPostProcessor? postProcessor = null,
        ILogger<CloudGeminiAudioTranscriber>? logger = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _config = config ?? new FotoprintConfig();
        _vocabularyProvider = vocabularyProvider;
        _postProcessor = postProcessor;
        _logger = logger;
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!_config.Audio.Enabled)
        {
            return Task.FromResult(false);
        }

        var apiKey = ResolveApiKey();
        return Task.FromResult(!string.IsNullOrWhiteSpace(apiKey));
    }

    public async Task<TranscriptionResult> TranscribeFileAsync(
        string filePath,
        string language = "ru",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: ProviderName,
                DurationMs: 0,
                ErrorMessage: $"Audio file not found: {filePath}");
        }

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await TranscribeAsync(fs, Path.GetFileName(filePath), language, ct);
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string language = "ru",
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (audioStream == null)
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: ProviderName,
                DurationMs: 0,
                ErrorMessage: "Audio stream is null.");
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: ProviderName,
                DurationMs: sw.ElapsedMilliseconds,
                ErrorMessage: "GEMINI_API_KEY is not configured for CloudGeminiAudioTranscriber.");
        }

        try
        {
            if (audioStream.CanSeek && audioStream.Position > 0)
            {
                audioStream.Position = 0;
            }

            using var ms = new MemoryStream();
            await audioStream.CopyToAsync(ms, ct);
            var audioBytes = ms.ToArray();

            if (audioBytes.Length == 0)
            {
                return new TranscriptionResult(
                    Success: false,
                    Text: string.Empty,
                    Segments: new List<AudioSegment>(),
                    Provider: ProviderName,
                    DurationMs: sw.ElapsedMilliseconds,
                    ErrorMessage: "Audio stream is empty (0 bytes).");
            }

            var mimeType = ResolveMimeType(fileName);
            var model = !string.IsNullOrWhiteSpace(_config.Audio.Gemini.Model)
                ? _config.Audio.Gemini.Model
                : "gemini-1.5-flash";

            var baseUrl = _config.Llm.Proxy.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl) ||
                baseUrl.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase) ||
                baseUrl.Contains("api.nvidia.com", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://generativelanguage.googleapis.com/v1beta";
            }

            var endpointUrl = $"{baseUrl.TrimEnd('/')}/models/{model}:generateContent";
            var prompt = string.IsNullOrWhiteSpace(language) || string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase)
                ? "Транскрибируй это аудио дословно на русском языке с сохранением таймкодов в формате [00:00:00.000 --> 00:00:05.000] Текст. Не добавляй комментарии от себя, выведи только распознанную речь."
                : $"Transcribe this audio verbatim in '{language}' with timestamps in format [00:00:00.000 --> 00:00:05.000] Text.";

            var vocabularyPrompt = _vocabularyProvider?.GetPromptString();
            object? systemInstruction = null;
            if (!string.IsNullOrWhiteSpace(vocabularyPrompt))
            {
                systemInstruction = new
                {
                    parts = new object[]
                    {
                        new
                        {
                            text = $"Ты — профессиональный транскрибатор речи типографии. При расшифровке обязательно учитывай специализированный словарь терминов и названий:\n{vocabularyPrompt}"
                        }
                    }
                };
            }

            object requestBody;
            if (systemInstruction != null)
            {
                requestBody = new
                {
                    system_instruction = systemInstruction,
                    contents = new object[]
                    {
                        new
                        {
                            role = "user",
                            parts = new object[]
                            {
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = mimeType,
                                        data = Convert.ToBase64String(audioBytes)
                                    }
                                },
                                new
                                {
                                    text = prompt
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.1,
                        maxOutputTokens = 4096
                    }
                };
            }
            else
            {
                requestBody = new
                {
                    contents = new object[]
                    {
                        new
                        {
                            role = "user",
                            parts = new object[]
                            {
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = mimeType,
                                        data = Convert.ToBase64String(audioBytes)
                                    }
                                },
                                new
                                {
                                    text = prompt
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.1,
                        maxOutputTokens = 4096
                    }
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = JsonContent.Create(requestBody);

            using var response = await _httpClient.SendAsync(request, ct);
            var rawBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning(
                    "[CloudGeminiAudioTranscriber] Gemini API returned {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    rawBody);

                return new TranscriptionResult(
                    Success: false,
                    Text: string.Empty,
                    Segments: new List<AudioSegment>(),
                    Provider: ProviderName,
                    DurationMs: sw.ElapsedMilliseconds,
                    ErrorMessage: $"Gemini Audio API returned {(int)response.StatusCode}: {rawBody}");
            }

            var extractedText = ExtractTextFromGeminiJson(rawBody);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return new TranscriptionResult(
                    Success: false,
                    Text: string.Empty,
                    Segments: new List<AudioSegment>(),
                    Provider: ProviderName,
                    DurationMs: sw.ElapsedMilliseconds,
                    ErrorMessage: "Gemini returned an empty transcription response.");
            }

            var parsedResult = LocalEngineAudioTranscriber.ParseEngineOutput(extractedText, sw.ElapsedMilliseconds, ProviderName);
            if (_postProcessor != null)
            {
                parsedResult = _postProcessor.ProcessResult(parsedResult);
            }

            return parsedResult;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[CloudGeminiAudioTranscriber] Error transcribing {FileName}", fileName);
            return new TranscriptionResult(
                Success: false,
                Text: string.Empty,
                Segments: new List<AudioSegment>(),
                Provider: ProviderName,
                DurationMs: sw.ElapsedMilliseconds,
                ErrorMessage: $"Gemini audio transcription exception: {ex.Message}");
        }
    }

    private string ResolveApiKey()
    {
        var envVarName = !string.IsNullOrWhiteSpace(_config.Llm.Proxy.ApiKeyEnvVar)
            ? _config.Llm.Proxy.ApiKeyEnvVar
            : "GEMINI_API_KEY";

        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                  ?? Environment.GetEnvironmentVariable(envVarName);

        if (!string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        // Fallback: check .env in current or parent directories
        try
        {
            var dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 7; i++)
            {
                var envPath = Path.Combine(dir, ".env");
                if (File.Exists(envPath))
                {
                    foreach (var line in File.ReadAllLines(envPath))
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
                        var eqIdx = trimmed.IndexOf('=');
                        if (eqIdx <= 0) continue;

                        var k = trimmed[..eqIdx].Trim();
                        var v = trimmed[(eqIdx + 1)..].Trim().Trim('"', '\'');
                        if ((string.Equals(k, "GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(k, envVarName, StringComparison.OrdinalIgnoreCase)) &&
                            !string.IsNullOrWhiteSpace(v))
                        {
                            return v;
                        }
                    }
                }

                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
        }
        catch
        {
            // Ignore .env read errors
        }

        return string.Empty;
    }

    private static string ResolveMimeType(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".ogg" or ".oga" or ".opus" => "audio/ogg",
            ".mp3" => "audio/mp3",
            ".m4a" or ".mp4" => "audio/mp4",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            ".webm" => "audio/webm",
            _ => "audio/wav"
        };
    }

    private static string? ExtractTextFromGeminiJson(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return null;

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return rawBody.Trim();

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0)
            {
                var partsBuilder = new List<string>();
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        {
                            var str = t.GetString();
                            if (!string.IsNullOrWhiteSpace(str))
                            {
                                partsBuilder.Add(str.Trim());
                            }
                        }
                    }
                }

                return string.Join("\n", partsBuilder).Trim();
            }

            if (root.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                return textProp.GetString()?.Trim();
            }
        }
        catch
        {
            // Ignore JSON parse failure and return raw body
        }

        return rawBody.Trim();
    }
}
