using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fotoprint.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Llm;

/// <summary>
/// Клиент генерации векторных эмбеддингов текста для RAG-поиска.
/// </summary>
public interface IEmbeddingClient
{
    string ModelName { get; }
    int Dimensions { get; }
    bool IsConfigured { get; }

    Task<float[]?> EmbedTextAsync(
        string text,
        string taskType = "RETRIEVAL_DOCUMENT",
        CancellationToken ct = default);

    Task<List<float[]?>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        string taskType = "RETRIEVAL_DOCUMENT",
        CancellationToken ct = default);
}

/// <summary>
/// Клиент эмбеддингов Google Gemini API (по умолчанию gemini-embedding-2, 1536 измерений для pgvector).
/// </summary>
public class GeminiEmbeddingClient : IEmbeddingClient
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const int MaxBatchSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiEmbeddingClient>? _logger;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public string ModelName { get; }
    public int Dimensions { get; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public GeminiEmbeddingClient(
        HttpClient httpClient,
        FotoprintConfig config,
        ILogger<GeminiEmbeddingClient>? logger = null,
        string? apiKeyOverride = null,
        string? baseUrlOverride = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        config ??= new FotoprintConfig();

        var envModel = ResolveConfigValue("GEMINI_EMBEDDING_MODEL");
        ModelName = !string.IsNullOrWhiteSpace(envModel)
            ? envModel
            : (!string.IsNullOrWhiteSpace(config.Llm.Models.EmbeddingModel)
                ? config.Llm.Models.EmbeddingModel.Trim()
                : "gemini-embedding-2");

        Dimensions = config.Rag.EmbeddingDimensions > 0 ? config.Rag.EmbeddingDimensions : 1536;
        _baseUrl = (!string.IsNullOrWhiteSpace(baseUrlOverride) ? baseUrlOverride : DefaultBaseUrl).TrimEnd('/');
        _apiKey = apiKeyOverride != null ? apiKeyOverride.Trim() : ResolveApiKey();
    }

    public async Task<float[]?> EmbedTextAsync(
        string text,
        string taskType = "RETRIEVAL_DOCUMENT",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (!IsConfigured)
        {
            _logger?.LogDebug("[GeminiEmbeddingClient] GEMINI_API_KEY не задан, пропуск вычисления эмбеддинга.");
            return null;
        }

        try
        {
            var url = $"{_baseUrl}/models/{Uri.EscapeDataString(ModelName)}:embedContent?key={Uri.EscapeDataString(_apiKey)}";
            var payload = new EmbedContentRequest
            {
                Model = $"models/{ModelName}",
                Content = new EmbedContentBody { Parts = new[] { new EmbedTextPart { Text = text } } },
                TaskType = taskType,
                OutputDimensionality = Dimensions
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger?.LogWarning("[GeminiEmbeddingClient] Ошибка embedContent ({StatusCode}): {Error}", (int)response.StatusCode, err);
                return null;
            }

            var parsed = await response.Content.ReadFromJsonAsync<EmbedContentResponse>(JsonOptions, ct);
            return NormalizeL2(parsed?.Embedding?.Values);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "[GeminiEmbeddingClient] Исключение при вызове embedContent для модели {Model}", ModelName);
            return null;
        }
    }

    public async Task<List<float[]?>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        string taskType = "RETRIEVAL_DOCUMENT",
        CancellationToken ct = default)
    {
        var results = new List<float[]?>(texts.Count);
        for (int i = 0; i < texts.Count; i++)
        {
            results.Add(null);
        }

        if (texts.Count == 0 || !IsConfigured)
        {
            return results;
        }

        var nonEmptyIndices = new List<int>(texts.Count);
        for (int i = 0; i < texts.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(texts[i]))
            {
                nonEmptyIndices.Add(i);
            }
        }

        for (int offset = 0; offset < nonEmptyIndices.Count; offset += MaxBatchSize)
        {
            var batchIndices = nonEmptyIndices.Skip(offset).Take(MaxBatchSize).ToList();
            var requests = batchIndices.Select(idx => new EmbedContentRequest
            {
                Model = $"models/{ModelName}",
                Content = new EmbedContentBody { Parts = new[] { new EmbedTextPart { Text = texts[idx] } } },
                TaskType = taskType,
                OutputDimensionality = Dimensions
            }).ToArray();

            try
            {
                var url = $"{_baseUrl}/models/{Uri.EscapeDataString(ModelName)}:batchEmbedContents?key={Uri.EscapeDataString(_apiKey)}";
                var payload = new BatchEmbedContentsRequest { Requests = requests };
                using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(url, content, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(ct);
                    _logger?.LogWarning("[GeminiEmbeddingClient] Ошибка batchEmbedContents ({StatusCode}): {Error}", (int)response.StatusCode, err);
                    continue;
                }

                var parsed = await response.Content.ReadFromJsonAsync<BatchEmbedContentsResponse>(JsonOptions, ct);
                if (parsed?.Embeddings == null) continue;

                for (int j = 0; j < batchIndices.Count && j < parsed.Embeddings.Count; j++)
                {
                    results[batchIndices[j]] = NormalizeL2(parsed.Embeddings[j]?.Values);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "[GeminiEmbeddingClient] Исключение при вызове batchEmbedContents для модели {Model}", ModelName);
            }
        }

        return results;
    }

    private static float[]? NormalizeL2(float[]? vector)
    {
        if (vector == null || vector.Length == 0) return null;

        double sumSq = 0;
        for (int i = 0; i < vector.Length; i++)
        {
            sumSq += (double)vector[i] * vector[i];
        }

        if (sumSq <= 0) return vector;

        var norm = Math.Sqrt(sumSq);
        if (Math.Abs(norm - 1.0) < 1e-4) return vector;

        var normalized = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            normalized[i] = (float)(vector[i] / norm);
        }
        return normalized;
    }

    private static string ResolveApiKey()
    {
        var raw = ResolveConfigValue("GEMINI_API_KEY") ?? ResolveConfigValue("GOOGLE_API_KEY") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains("YourGeminiApiKey", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }
        return raw.Trim();
    }

    private static string? ResolveConfigValue(string keyName)
    {
        var envValue = Environment.GetEnvironmentVariable(keyName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue.Trim();
        }

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
                        if (string.Equals(k, keyName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v))
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
            // Ignore .env discovery errors
        }

        return null;
    }

    private sealed class EmbedContentRequest
    {
        public string Model { get; set; } = string.Empty;
        public EmbedContentBody Content { get; set; } = new();
        public string? TaskType { get; set; }
        public int? OutputDimensionality { get; set; }
    }

    private sealed class BatchEmbedContentsRequest
    {
        public EmbedContentRequest[] Requests { get; set; } = Array.Empty<EmbedContentRequest>();
    }

    private sealed class EmbedContentBody
    {
        public EmbedTextPart[] Parts { get; set; } = Array.Empty<EmbedTextPart>();
    }

    private sealed class EmbedTextPart
    {
        public string Text { get; set; } = string.Empty;
    }

    private sealed class EmbedContentResponse
    {
        public EmbeddingValues? Embedding { get; set; }
    }

    private sealed class BatchEmbedContentsResponse
    {
        public List<EmbeddingValues?>? Embeddings { get; set; }
    }

    private sealed class EmbeddingValues
    {
        public float[]? Values { get; set; }
    }
}

