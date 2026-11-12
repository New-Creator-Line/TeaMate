using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Resilience;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Llm;

/// <summary>
/// Единый интерфейс взаимодействия с LLM (NVIDIA NIM / Nemotron).
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Вызов модели LLM с защитой от превышения RPM и автоматическими повторами.
    /// </summary>
    Task<GeminiGenerateResponse> GenerateContentAsync(
        string prompt,
        string? systemInstruction = null,
        double? temperature = null,
        int? maxOutputTokens = null,
        string? traceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Диалоговый шаг с поддержкой Function Calling (Tool Calling в формате OpenAI/NVIDIA).
    /// </summary>
    Task<GeminiTurnResponse> ChatTurnAsync(
        List<object> contents,
        object? tools = null,
        string? systemInstruction = null,
        double? temperature = null,
        int? maxOutputTokens = null,
        string? traceId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Диалоговый шаг без thinking-режима (быстрый синтез финального ответа по данным MCP).
    /// </summary>
    Task<GeminiTurnResponse> ChatTurnNoThinkingAsync(
        List<object> contents,
        object? tools = null,
        string? systemInstruction = null,
        double? temperature = null,
        int? maxOutputTokens = null,
        string? traceId = null,
        CancellationToken ct = default)
        => ChatTurnAsync(contents, tools, systemInstruction, temperature, maxOutputTokens, traceId, ct);

    /// <summary>
    /// Быстрый вызов без thinking-режима (оптимально для JSON-классификаторов).
    /// </summary>
    Task<GeminiGenerateResponse> GenerateJsonNoThinkingAsync(
        string prompt,
        string? systemInstruction = null,
        double? temperature = null,
        int? maxOutputTokens = null,
        string? traceId = null,
        CancellationToken ct = default)
        => GenerateContentAsync(prompt, systemInstruction, temperature, maxOutputTokens, traceId, ct);

    /// <summary>
    /// Статистика вызовов LLM: скользящее окно 60 сек, остаток лимита RPM.
    /// </summary>
    GeminiCallStats GetStats();
}

public interface INvidiaClient : ILlmClient { }
public interface IGeminiClient : ILlmClient { }

public record GeminiFunctionCall(string Name, JsonElement Arguments, string? CallId = null);

public record GeminiTurnResponse(
    bool IsSuccess,
    string? Text,
    GeminiFunctionCall? FunctionCall,
    string? FinishReason,
    int PromptTokens,
    int CandidatesTokens,
    int TotalTokens,
    int AttemptsMade,
    string? ErrorMessage,
    IReadOnlyList<GeminiFunctionCall>? FunctionCalls = null,
    string? Model = null);

public record GeminiCallStats(
    int TotalCalls,
    int CallsInLast60Seconds,
    int RpmLimit,
    int RemainingQuotaInCurrentMinute,
    double CurrentUsagePercent,
    DateTime? OldestCallInWindow,
    int EstimatedWaitMsUntilNextSlot);

public record GeminiGenerateResponse(
    bool IsSuccess,
    string? Text,
    string? FinishReason,
    int PromptTokens,
    int CandidatesTokens,
    int TotalTokens,
    int AttemptsMade,
    string? ErrorMessage,
    string? Model = null);

/// <summary>
/// Клиент вызова моделей NVIDIA NIM (ChatModel / FastModel / FallbackModel).
/// Реализует стандартный OpenAI-совместимый протокол с поддержкой reasoning (enable_thinking),
/// function calling, мультимодальности и интеграции с Langfuse и балансировщиком нагрузки.
/// </summary>
public class NvidiaClient : INvidiaClient, IGeminiClient
{
    private readonly HttpClient _httpClient;
    private readonly FotoprintConfig _config;
    private readonly ILlmLoadBalancer _loadBalancer;
    private readonly IRollbackManager _rollbackManager;
    private readonly ILangfuseClient? _langfuse;
    private readonly ILogger<NvidiaClient>? _logger;

    private static readonly JsonSerializerOptions Utf8JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _apiKey;
    private readonly string[] _apiKeys;
    private int _keyIndex = -1;
    private readonly string _model;
    private readonly string _fastModel;
    private readonly string _fallbackModel;
    private readonly string _baseUrl;
    private readonly int _rpmLimit;
    private long _primaryModelCircuitOpenUntilTicks = DateTime.MinValue.Ticks;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _interactionsByTraceId = new(StringComparer.OrdinalIgnoreCase);

    // Счётчик вызовов в скользящем окне 60 секунд
    private readonly Queue<DateTime> _callTimestamps = new();
    private readonly object _counterLock = new();
    private int _totalCalls = 0;

    public NvidiaClient(
        HttpClient httpClient,
        FotoprintConfig config,
        ILlmLoadBalancer loadBalancer,
        IRollbackManager rollbackManager,
        ILangfuseClient? langfuse = null,
        ILogger<NvidiaClient>? logger = null)
    {
        _httpClient = httpClient;
        _config = config;
        _loadBalancer = loadBalancer;
        _rollbackManager = rollbackManager;
        _langfuse = langfuse;
        _logger = logger;

        var apiKeyEnvVar = _config.Llm.Proxy.ApiKeyEnvVar;
        if (string.IsNullOrWhiteSpace(apiKeyEnvVar)) apiKeyEnvVar = "NVIDIA_API_KEY";

        _apiKey = Environment.GetEnvironmentVariable(apiKeyEnvVar)
            ?? Environment.GetEnvironmentVariable("NVIDIA_API_KEYS")
            ?? Environment.GetEnvironmentVariable("NVIDIA_API_KEY")
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? string.Empty;

        _apiKeys = _apiKey
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        _model = Environment.GetEnvironmentVariable("LLM_MODEL")
            ?? _config.Llm.Models.ChatModel;

        if (string.IsNullOrWhiteSpace(_model))
        {
            _model = "gemma-4-31b-it";
        }

        _fastModel = !string.IsNullOrWhiteSpace(_config.Llm.Models.FastModel)
            ? _config.Llm.Models.FastModel
            : "gemini-3.5-flash-lite";

        _fallbackModel = !string.IsNullOrWhiteSpace(_config.Llm.Models.FallbackModel) &&
                         !string.Equals(_config.Llm.Models.FallbackModel, _model, StringComparison.OrdinalIgnoreCase)
            ? _config.Llm.Models.FallbackModel
            : "gemini-3.5-flash-lite";

        if (string.Equals(_fallbackModel, _model, StringComparison.OrdinalIgnoreCase))
        {
            _fallbackModel = string.Equals(_model, "gemini-3.5-flash-lite", StringComparison.OrdinalIgnoreCase)
                ? "gemma-4-31b-it"
                : "gemini-3.5-flash-lite";
        }

        var configBaseUrl = Environment.GetEnvironmentVariable("LLM_BASE_URL")
            ?? _config.Llm.Proxy.BaseUrl;

        _baseUrl = !string.IsNullOrWhiteSpace(configBaseUrl)
            ? configBaseUrl.TrimEnd('/')
            : (_config.Llm.Provider?.Equals("gemini", StringComparison.OrdinalIgnoreCase) == true && string.IsNullOrWhiteSpace(_config.Llm.Proxy.BaseUrl)
                ? "https://generativelanguage.googleapis.com/v1beta/interactions"
                : "https://integrate.api.nvidia.com/v1");

        _rpmLimit = _config.Llm.RateLimits.Rpm > 0 ? _config.Llm.RateLimits.Rpm : 60;
    }

    public Task<GeminiGenerateResponse> GenerateContentAsync(
        string prompt,
        string? systemInstruction = null,
        double? temperature = null,
        int? maxOutputTokens = null,
        string? traceId = null,
        CancellationToken ct = default)
        => GenerateContentCoreAsync(prompt, systemInstruction, temperature, maxOutputTokens, traceId, enableThinking: true, preferredModel: _model, ct: ct);

    public Task<GeminiGenerateResponse> GenerateJsonNoThinkingAsync(
        string prompt,
        string? systemInstruction = null,
        double? temperature = null,
        int? maxOutputTokens = null,
        string? traceId = null,
        CancellationToken ct = default)
        => GenerateContentCoreAsync(prompt, systemInstruction, temperature, maxOutputTokens, traceId, enableThinking: false, preferredModel: _fastModel, ct: ct);

    private async Task<GeminiGenerateResponse> GenerateContentCoreAsync(
        string prompt,
        string? systemInstruction,
        double? temperature,
        int? maxOutputTokens,
        string? traceId,
        bool enableThinking,
        string preferredModel,
        CancellationToken ct)
    {
        var contents = new List<object>
        {
            new { role = "user", content = prompt }
        };

        var turn = await ChatTurnCoreAsync(
            contents: contents,
            tools: null,
            systemInstruction: systemInstruction,
            temperature: temperature,
            maxOutputTokens: maxOutputTokens,
            traceId: traceId,
            enableThinking: enableThinking,
            preferredModel: preferredModel,
            ct: ct);

        return new GeminiGenerateResponse(
            IsSuccess: turn.IsSuccess,
            Text: turn.Text,
            FinishReason: turn.FinishReason,
            PromptTokens: turn.PromptTokens,
            CandidatesTokens: turn.CandidatesTokens,
            TotalTokens: turn.TotalTokens,
            AttemptsMade: turn.AttemptsMade,
            ErrorMessage: turn.ErrorMessage,
            Model: turn.Model);
    }

    public Task<GeminiTurnResponse> ChatTurnAsync(
        List<object> contents,
        object? tools = null,
        string? systemInstruction = null,
        double? temperature = null,
        int? maxOutputTokens = null,
        string? traceId = null,
        CancellationToken ct = default)
        => ChatTurnCoreAsync(contents, tools, systemInstruction, temperature, maxOutputTokens, traceId, enableThinking: true, preferredModel: _model, ct: ct);

    public Task<GeminiTurnResponse> ChatTurnNoThinkingAsync(
        List<object> contents,
        object? tools = null,
        string? systemInstruction = null,
        double? temperature = null,
        int? maxOutputTokens = null,
        string? traceId = null,
        CancellationToken ct = default)
        => ChatTurnCoreAsync(contents, tools, systemInstruction, temperature, maxOutputTokens, traceId, enableThinking: false, preferredModel: _fastModel, ct: ct);

    private async Task<GeminiTurnResponse> ChatTurnCoreAsync(
        List<object> contents,
        object? tools,
        string? systemInstruction,
        double? temperature,
        int? maxOutputTokens,
        string? traceId,
        bool enableThinking,
        string preferredModel,
        CancellationToken ct)
    {
        var temp = temperature ?? _config.Llm.Parameters.Temperature;
        var maxTokens = maxOutputTokens ?? _config.Llm.Parameters.MaxTokens;

        return await _loadBalancer.ScheduleAsync(
            taskName: $"Nvidia:{preferredModel}:ChatTurn",
            work: async token =>
            {
                var lastAttemptedModel = preferredModel;
                var rollbackResult = await _rollbackManager.ExecuteWithRollbackAsync(
                    nodeName: $"Nvidia:{preferredModel}:Turn",
                    action: async (attempt, retryToken) =>
                    {
                        RecordCall();

                        // Если основная тяжелая модель временно недоступна (Circuit Breaker открыт после таймаута/503) или это повторная попытка — сразу используем быструю модель
                        bool isCircuitOpen = DateTime.UtcNow.Ticks < Volatile.Read(ref _primaryModelCircuitOpenUntilTicks) &&
                                             string.Equals(preferredModel, _model, StringComparison.OrdinalIgnoreCase);

                        var activeModel = (attempt == 1 && !isCircuitOpen) ? preferredModel : _fallbackModel;
                        lastAttemptedModel = activeModel;
                        if (attempt > 1 && !string.Equals(activeModel, preferredModel, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger?.LogInformation("🔀 [NVIDIA Fallback] Попытка {Attempt}: переключение с '{Primary}' на резервную модель '{Fallback}'",
                                attempt, preferredModel, activeModel);
                        }

                        var isInteractionsApi = _baseUrl.Contains("interactions", StringComparison.OrdinalIgnoreCase);
                        var endpointUrl = isInteractionsApi ? _baseUrl : $"{_baseUrl}/chat/completions";
                        var requestBody = isInteractionsApi
                            ? BuildInteractionsRequestBody(contents, tools, systemInstruction, temp, maxTokens, activeModel, traceId)
                            : BuildNvidiaRequestBody(contents, tools, systemInstruction, temp, maxTokens, activeModel, enableThinking);

                        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
                        request.Content = JsonContent.Create(requestBody, options: Utf8JsonOptions);

                        var activeKey = GetNextApiKey();
                        if (!string.IsNullOrWhiteSpace(activeKey))
                        {
                            if (isInteractionsApi)
                            {
                                request.Headers.Add("x-goog-api-key", activeKey);
                                request.Headers.Add("Api-Revision", "2026-05-20");
                            }
                            else
                            {
                                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", activeKey);
                            }
                        }

                        // Защита от зависаний модели (HTTP TTFB timeout 15 сек на 1-й попытке, если настроен отличный fallback)
                        using var ttftCts = CancellationTokenSource.CreateLinkedTokenSource(retryToken);
                        if (attempt == 1 && (string.Equals(activeModel, _model, StringComparison.OrdinalIgnoreCase) || string.Equals(activeModel, _fastModel, StringComparison.OrdinalIgnoreCase)) &&
                            !string.Equals(activeModel, _fallbackModel, StringComparison.OrdinalIgnoreCase))
                        {
                            ttftCts.CancelAfter(TimeSpan.FromSeconds(15));
                        }

                        HttpResponseMessage response;
                        try
                        {
                            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ttftCts.Token);
                            ttftCts.CancelAfter(Timeout.InfiniteTimeSpan);
                        }
                        catch (Exception ex) when (!retryToken.IsCancellationRequested)
                        {
                            if (isInteractionsApi && !string.IsNullOrEmpty(traceId))
                            {
                                _interactionsByTraceId.TryRemove(traceId, out _);
                            }
                            if (string.Equals(activeModel, _model, StringComparison.OrdinalIgnoreCase))
                            {
                                Interlocked.Exchange(ref _primaryModelCircuitOpenUntilTicks, DateTime.UtcNow.AddMinutes(3).Ticks);
                                _logger?.LogWarning("⚡ [CircuitBreaker] Основная модель '{Model}' не ответила вовремя ({Message}). Переключаем трафик на '{Fallback}' на 3 мин.",
                                    _model, ex.Message, _fallbackModel);
                            }
                            throw new NodeTransientException($"Таймаут или обрыв соединения с моделью {activeModel}: {ex.Message}", 503, ex);
                        }

                        using (response)
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                var errContent = await response.Content.ReadAsStringAsync(retryToken);
                                var statusCode = (int)response.StatusCode;
                                _logger?.LogWarning("⚠️ [NVIDIA API] Ошибка {StatusCode}: {ErrorBody}", statusCode, errContent);

                                if (string.Equals(activeModel, _model, StringComparison.OrdinalIgnoreCase) && statusCode >= 500)
                                {
                                    Interlocked.Exchange(ref _primaryModelCircuitOpenUntilTicks, DateTime.UtcNow.AddMinutes(3).Ticks);
                                }

                                throw new NodeTransientException(
                                    $"LLM API вернул код {statusCode}: {response.ReasonPhrase}. Детали: {errContent}",
                                    statusCode);
                            }

                            var mediaType = response.Content.Headers.ContentType?.MediaType;
                            if (!string.IsNullOrWhiteSpace(mediaType) &&
                                mediaType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
                            {
                                return (await ParseNvidiaStreamResponseAsync(response, attempt, retryToken)) with { Model = activeModel };
                            }

                            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: retryToken);
                            if (json.TryGetProperty("steps", out _))
                            {
                                return ParseInteractionsTurnResponse(json, attempt, activeModel, traceId);
                            }
                            return ParseNvidiaTurnResponse(json, attempt) with { Model = activeModel };
                        }
                    },
                    compensatingRollbackAction: (ex, rollbackToken) =>
                    {
                        _logger?.LogWarning("🔄 [NVIDIA Rollback] Откат состояния после ошибки: {Message}", ex.Message);
                        return Task.CompletedTask;
                    },
                    traceId: traceId,
                    ct: token);

                if (!rollbackResult.IsSuccess)
                {
                    var lastErr = rollbackResult.ErrorMessage ?? rollbackResult.LastException?.Message ?? "Неизвестная ошибка вызова NVIDIA API";
                    _logger?.LogError("❌ [NVIDIA Failed] Все попытки исчерпаны: {Error}", lastErr);

                    if (_langfuse != null && !string.IsNullOrEmpty(traceId))
                    {
                        _ = _langfuse.LogEventAsync(
                            traceId,
                            "Nvidia:TurnFailed",
                            "ERROR",
                            lastErr,
                            metadata: new { model = lastAttemptedModel, attempts = rollbackResult.AttemptsMade });
                    }

                    return new GeminiTurnResponse(
                        IsSuccess: false,
                        Text: null,
                        FunctionCall: null,
                        FinishReason: "ERROR",
                        PromptTokens: 0,
                        CandidatesTokens: 0,
                        TotalTokens: 0,
                        AttemptsMade: rollbackResult.AttemptsMade,
                        ErrorMessage: lastErr,
                        Model: lastAttemptedModel);
                }

                return rollbackResult.Value!;
            },
            traceId: traceId,
            ct: ct);
    }

    public GeminiCallStats GetStats()
    {
        lock (_counterLock)
        {
            CleanExpiredCalls();
            var activeCount = _callTimestamps.Count;
            var remaining = Math.Max(0, _rpmLimit - activeCount);
            var usagePct = _rpmLimit > 0 ? Math.Round((double)activeCount / _rpmLimit * 100.0, 1) : 0.0;
            DateTime? oldest = activeCount > 0 ? _callTimestamps.Peek() : null;

            int waitMs = 0;
            if (activeCount >= _rpmLimit && oldest.HasValue)
            {
                var expiresAt = oldest.Value.AddSeconds(60);
                var diff = (int)(expiresAt - DateTime.UtcNow).TotalMilliseconds;
                waitMs = Math.Max(0, diff);
            }

            return new GeminiCallStats(
                TotalCalls: _totalCalls,
                CallsInLast60Seconds: activeCount,
                RpmLimit: _rpmLimit,
                RemainingQuotaInCurrentMinute: remaining,
                CurrentUsagePercent: usagePct,
                OldestCallInWindow: oldest,
                EstimatedWaitMsUntilNextSlot: waitMs);
        }
    }

    private string GetNextApiKey()
    {
        if (_apiKeys.Length == 0) return _apiKey;
        if (_apiKeys.Length == 1) return _apiKeys[0];
        var idx = (Interlocked.Increment(ref _keyIndex) & 0x7FFFFFFF) % _apiKeys.Length;
        return _apiKeys[idx];
    }

    private void RecordCall()
    {
        lock (_counterLock)
        {
            _callTimestamps.Enqueue(DateTime.UtcNow);
            _totalCalls++;
            CleanExpiredCalls();
        }
    }

    private void CleanExpiredCalls()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        while (_callTimestamps.Count > 0 && _callTimestamps.Peek() < cutoff)
        {
            _callTimestamps.Dequeue();
        }
    }

    private static object BuildNvidiaRequestBody(
        List<object> contents,
        object? tools,
        string? systemInstruction,
        double temperature,
        int maxOutputTokens,
        string model,
        bool enableThinking = true)
    {
        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            messages.Add(new { role = "system", content = systemInstruction });
        }

        foreach (var c in contents)
        {
            if (c is JsonElement el)
            {
                messages.Add(el);
            }
            else
            {
                var jsonStr = JsonSerializer.Serialize(c, Utf8JsonOptions);
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                string role = root.TryGetProperty("role", out var r) ? r.GetString() ?? "user" : "user";
                if (role == "model") role = "assistant";

                // Проверяем наличие content в OpenAI формате (строка или массив мультимодальных блоков)
                if (root.TryGetProperty("content", out var contentProp))
                {
                    if (role != "tool" && contentProp.ValueKind == JsonValueKind.Array)
                    {
                        var blocks = new List<object>();
                        foreach (var b in contentProp.EnumerateArray())
                        {
                            blocks.Add(JsonSerializer.Deserialize<object>(b.GetRawText())!);
                        }
                        messages.Add(new { role, content = blocks });
                    }
                    else
                    {
                        var contentText = PiiMasker.MaskSecrets(contentProp.ValueKind switch
                        {
                            JsonValueKind.String => contentProp.GetString() ?? "",
                            JsonValueKind.Null or JsonValueKind.Undefined => "",
                            _ => contentProp.GetRawText()
                        });

                        var msgObj = new Dictionary<string, object>
                        {
                            ["role"] = role,
                            ["content"] = contentText
                        };
                        if (root.TryGetProperty("tool_call_id", out var tcid))
                        {
                            msgObj["tool_call_id"] = tcid.GetString() ?? "";
                        }
                        if (root.TryGetProperty("name", out var tname))
                        {
                            msgObj["name"] = tname.GetString() ?? "";
                        }
                        if (root.TryGetProperty("tool_calls", out var tcArray))
                        {
                            msgObj["tool_calls"] = JsonSerializer.Deserialize<object>(tcArray.GetRawText())!;
                        }
                        messages.Add(msgObj);
                    }
                }
                // Либо распаковываем Gemini-формат parts: [{ text }]
                else if (root.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
                {
                    string text = "";
                    foreach (var p in parts.EnumerateArray())
                    {
                        if (p.TryGetProperty("text", out var pt))
                        {
                            text += pt.GetString();
                        }
                    }
                    messages.Add(new { role, content = PiiMasker.MaskSecrets(text) });
                }
                else
                {
                    messages.Add(new { role, content = jsonStr });
                }
            }
        }

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["top_p"] = 0.95,
            ["max_tokens"] = maxOutputTokens,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true },
            ["chat_template_kwargs"] = new
            {
                enable_thinking = enableThinking
            }
        };

        if (tools != null)
        {
            body["parallel_tool_calls"] = true;
            body["tools"] = NormalizeTools(tools);
        }

        return body;
    }

    private static object NormalizeTools(object rawTools)
    {
        var jsonStr = JsonSerializer.Serialize(rawTools);
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;

        var normalized = new List<object>();

        // Если это массив объектов:
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                // Уже в OpenAI формате: { type: "function", function: { ... } }
                if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "function")
                {
                    normalized.Add(JsonSerializer.Deserialize<object>(item.GetRawText())!);
                }
                // Gemini формат: { functionDeclarations: [ { name, description, parameters } ] }
                else if (item.TryGetProperty("functionDeclarations", out var decls) && decls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fn in decls.EnumerateArray())
                    {
                        var fnObj = JsonSerializer.Deserialize<object>(fn.GetRawText())!;
                        normalized.Add(new
                        {
                            type = "function",
                            function = fnObj
                        });
                    }
                }
                else
                {
                    normalized.Add(new
                    {
                        type = "function",
                        function = JsonSerializer.Deserialize<object>(item.GetRawText())!
                    });
                }
            }
            return normalized;
        }

        return rawTools;
    }

    private static GeminiTurnResponse ParseNvidiaTurnResponse(JsonElement json, int attempt)
    {
        string? text = null;
        string? finishReason = null;
        var allCalls = new List<GeminiFunctionCall>();
        int promptTokens = 0;
        int candidatesTokens = 0;
        int totalTokens = 0;

        if (json.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var msg))
            {
                if (msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                {
                    text = c.GetString();
                }

                // Поддержка reasoning_content модели Nemotron-3.5
                if (string.IsNullOrWhiteSpace(text) && msg.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                {
                    text = rc.GetString();
                }

                if (msg.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    foreach (var toolItem in toolCalls.EnumerateArray())
                    {
                        var callId = toolItem.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        if (toolItem.TryGetProperty("function", out var fn))
                        {
                            var fnName = fn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            var fnArgsStr = fn.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}";
                            try
                            {
                                using var doc = JsonDocument.Parse(fnArgsStr);
                                allCalls.Add(new GeminiFunctionCall(fnName, doc.RootElement.Clone(), callId));
                            }
                            catch
                            {
                                // fallback
                            }
                        }
                    }
                }
            }

            if (firstChoice.TryGetProperty("finish_reason", out var fr))
            {
                finishReason = fr.GetString();
            }
        }

        if (json.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct)) candidatesTokens = ct.GetInt32();
            if (usage.TryGetProperty("total_tokens", out var tt)) totalTokens = tt.GetInt32();
        }

        return new GeminiTurnResponse(
            IsSuccess: true,
            Text: text,
            FunctionCall: allCalls.FirstOrDefault(),
            FinishReason: finishReason ?? "stop",
            PromptTokens: promptTokens,
            CandidatesTokens: candidatesTokens,
            TotalTokens: totalTokens,
            AttemptsMade: attempt,
            ErrorMessage: null,
            FunctionCalls: allCalls.Count > 0 ? allCalls : null);
    }

    private static async Task<GeminiTurnResponse> ParseNvidiaStreamResponseAsync(
        HttpResponseMessage response,
        int attempt,
        CancellationToken ct)
    {
        var contentBuilder = new System.Text.StringBuilder();
        var reasoningBuilder = new System.Text.StringBuilder();
        var toolBuilders = new SortedDictionary<int, (string? Id, System.Text.StringBuilder Name, System.Text.StringBuilder Args)>();
        string? finishReason = null;
        int promptTokens = 0;
        int candidatesTokens = 0;
        int totalTokens = 0;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line[5..].Trim();
            if (data == "[DONE]")
                break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var comp)) candidatesTokens = comp.GetInt32();
                if (usage.TryGetProperty("total_tokens", out var tt)) totalTokens = tt.GetInt32();
            }

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var ch = choices[0];
                if (ch.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                {
                    finishReason = fr.GetString();
                }

                if (ch.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                {
                    if (delta.TryGetProperty("content", out var dc) && dc.ValueKind == JsonValueKind.String)
                    {
                        contentBuilder.Append(dc.GetString());
                    }

                    if (delta.TryGetProperty("reasoning_content", out var dr) && dr.ValueKind == JsonValueKind.String)
                    {
                        reasoningBuilder.Append(dr.GetString());
                    }

                    if (delta.TryGetProperty("tool_calls", out var dtc) && dtc.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in dtc.EnumerateArray())
                        {
                            var idx = tc.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                            if (!toolBuilders.TryGetValue(idx, out var entry))
                            {
                                entry = (null, new System.Text.StringBuilder(), new System.Text.StringBuilder());
                            }

                            if (tc.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                            {
                                entry.Id = idProp.GetString();
                            }

                            if (tc.TryGetProperty("function", out var fn))
                            {
                                if (fn.TryGetProperty("name", out var nProp) && nProp.ValueKind == JsonValueKind.String)
                                    entry.Name.Append(nProp.GetString());
                                if (fn.TryGetProperty("arguments", out var aProp) && aProp.ValueKind == JsonValueKind.String)
                                    entry.Args.Append(aProp.GetString());
                            }

                            toolBuilders[idx] = entry;
                        }
                    }
                }
            }
        }

        var text = contentBuilder.Length > 0
            ? contentBuilder.ToString()
            : (reasoningBuilder.Length > 0 ? reasoningBuilder.ToString() : null);

        var allCalls = new List<GeminiFunctionCall>();
        foreach (var kvp in toolBuilders)
        {
            var fnName = kvp.Value.Name.ToString();
            if (string.IsNullOrWhiteSpace(fnName)) continue;

            var argsStr = kvp.Value.Args.Length > 0 ? kvp.Value.Args.ToString() : "{}";
            try
            {
                using var argDoc = JsonDocument.Parse(argsStr);
                allCalls.Add(new GeminiFunctionCall(fnName, argDoc.RootElement.Clone(), kvp.Value.Id));
            }
            catch
            {
                // fallback
            }
        }

        return new GeminiTurnResponse(
            IsSuccess: true,
            Text: text,
            FunctionCall: allCalls.FirstOrDefault(),
            FinishReason: finishReason ?? "stop",
            PromptTokens: promptTokens,
            CandidatesTokens: candidatesTokens,
            TotalTokens: totalTokens,
            AttemptsMade: attempt,
            ErrorMessage: null,
            FunctionCalls: allCalls.Count > 0 ? allCalls : null);
    }

    private object BuildInteractionsRequestBody(
        List<object> contents,
        object? tools,
        string? systemInstruction,
        double temperature,
        int maxOutputTokens,
        string model,
        string? traceId)
    {
        // 1. Проверяем, есть ли результаты выполнения инструментов от предыдущего шага
        var lastAssistantIdx = -1;
        for (int i = contents.Count - 1; i >= 0; i--)
        {
            if (GetMessageRole(contents[i]) == "assistant")
            {
                lastAssistantIdx = i;
                break;
            }
        }

        var toolResults = new List<object>();
        if (lastAssistantIdx >= 0)
        {
            for (int i = lastAssistantIdx + 1; i < contents.Count; i++)
            {
                var msg = contents[i];
                if (GetMessageRole(msg) == "tool")
                {
                    var (callId, fnName, resultObj) = ExtractToolData(msg);
                    toolResults.Add(new
                    {
                        type = "function_result",
                        call_id = callId,
                        name = fnName,
                        result = resultObj
                    });
                }
            }
        }

        if (toolResults.Count > 0 && !string.IsNullOrEmpty(traceId) && _interactionsByTraceId.TryGetValue(traceId, out var prevInterId))
        {
            return new Dictionary<string, object>
            {
                ["model"] = model,
                ["previous_interaction_id"] = prevInterId,
                ["input"] = toolResults
            };
        }

        // 2. Если это первый шаг или история сообщений без tools
        var inputList = new List<object>();
        foreach (var c in contents)
        {
            var role = GetMessageRole(c);
            var text = GetMessageText(c);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (role == "user")
                {
                    inputList.Add(new { type = "text", text });
                }
                else if (role == "assistant")
                {
                    inputList.Add(new { type = "text", text = $"Ассистент: {text}" });
                }
            }
        }

        if (inputList.Count == 0)
        {
            inputList.Add(new { type = "text", text = "Привет" });
        }

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["input"] = inputList
        };

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            body["system_instruction"] = systemInstruction;
        }

        if (tools != null)
        {
            body["tools"] = NormalizeToolsForInteractions(tools);
        }

        return body;
    }

    private static string GetMessageRole(object msg)
    {
        if (msg is JsonElement el && el.TryGetProperty("role", out var r))
            return r.GetString() ?? "user";

        var jsonStr = JsonSerializer.Serialize(msg, Utf8JsonOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        return doc.RootElement.TryGetProperty("role", out var r2) ? r2.GetString() ?? "user" : "user";
    }

    private static string GetMessageText(object msg)
    {
        if (msg is JsonElement el)
        {
            if (el.TryGetProperty("content", out var c))
                return c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : c.GetRawText();
            return "";
        }

        var jsonStr = JsonSerializer.Serialize(msg, Utf8JsonOptions);
        using var doc = JsonDocument.Parse(jsonStr);
        if (doc.RootElement.TryGetProperty("content", out var c2))
            return c2.ValueKind == JsonValueKind.String ? c2.GetString() ?? "" : c2.GetRawText();
        return "";
    }

    private static (string CallId, string Name, object Result) ExtractToolData(object msg)
    {
        string callId = "";
        string name = "";
        object resultObj = new object();

        JsonElement root;
        JsonDocument? doc = null;
        if (msg is JsonElement el)
        {
            root = el;
        }
        else
        {
            doc = JsonDocument.Parse(JsonSerializer.Serialize(msg, Utf8JsonOptions));
            root = doc.RootElement;
        }

        if (root.TryGetProperty("tool_call_id", out var tid)) callId = tid.GetString() ?? "";
        if (root.TryGetProperty("name", out var n)) name = n.GetString() ?? "";
        if (root.TryGetProperty("content", out var cnt))
        {
            var raw = cnt.ValueKind == JsonValueKind.String ? cnt.GetString() ?? "" : cnt.GetRawText();
            try
            {
                resultObj = JsonSerializer.Deserialize<JsonElement>(raw);
            }
            catch
            {
                resultObj = raw;
            }
        }

        doc?.Dispose();
        return (callId, name, resultObj);
    }

    private static object NormalizeToolsForInteractions(object rawTools)
    {
        var jsonStr = JsonSerializer.Serialize(rawTools);
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;

        var normalized = new List<object>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                // Если передано в OpenAI формате { type: "function", function: { name, description, parameters } }
                if (item.TryGetProperty("function", out var fnObj))
                {
                    var name = fnObj.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var desc = fnObj.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    object? parameters = null;
                    if (fnObj.TryGetProperty("parameters", out var p))
                    {
                        parameters = JsonSerializer.Deserialize<object>(p.GetRawText());
                    }
                    normalized.Add(new
                    {
                        type = "function",
                        name = name,
                        description = desc,
                        parameters = parameters ?? new object()
                    });
                }
                // Если уже плоский формат { type: "function", name, description, parameters }
                else if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "function")
                {
                    normalized.Add(JsonSerializer.Deserialize<object>(item.GetRawText())!);
                }
                // Gemini functionDeclarations формат
                else if (item.TryGetProperty("functionDeclarations", out var decls) && decls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var fn in decls.EnumerateArray())
                    {
                        var name = fn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var desc = fn.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                        object? parameters = null;
                        if (fn.TryGetProperty("parameters", out var p))
                        {
                            parameters = JsonSerializer.Deserialize<object>(p.GetRawText());
                        }
                        normalized.Add(new
                        {
                            type = "function",
                            name = name,
                            description = desc,
                            parameters = parameters ?? new object()
                        });
                    }
                }
            }
            return normalized;
        }

        return rawTools;
    }

    private GeminiTurnResponse ParseInteractionsTurnResponse(JsonElement json, int attempt, string activeModel, string? traceId)
    {
        string? text = null;
        string? finishReason = null;
        var allCalls = new List<GeminiFunctionCall>();
        int promptTokens = 0;
        int candidatesTokens = 0;
        int totalTokens = 0;

        if (json.TryGetProperty("id", out var idProp))
        {
            var interId = idProp.GetString();
            if (!string.IsNullOrWhiteSpace(interId) && !string.IsNullOrWhiteSpace(traceId))
            {
                _interactionsByTraceId[traceId] = interId;
            }
        }

        if (json.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("total_input_tokens", out var inTok))
                promptTokens = inTok.GetInt32();
            else if (usage.TryGetProperty("initial_input_tokens", out var initTok))
                promptTokens = initTok.GetInt32();

            if (usage.TryGetProperty("total_output_tokens", out var outTok))
                candidatesTokens = outTok.GetInt32();

            if (usage.TryGetProperty("total_tokens", out var totTok))
                totalTokens = totTok.GetInt32();
            else
                totalTokens = promptTokens + candidatesTokens;
        }

        if (json.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in steps.EnumerateArray())
            {
                var stepType = step.TryGetProperty("type", out var st) ? st.GetString() : null;

                if (string.Equals(stepType, "function_call", StringComparison.OrdinalIgnoreCase))
                {
                    var fnName = step.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var callId = step.TryGetProperty("id", out var cid) ? cid.GetString() : null;
                    JsonElement args = default;
                    if (step.TryGetProperty("arguments", out var a))
                    {
                        args = a.Clone();
                    }
                    if (!string.IsNullOrWhiteSpace(fnName))
                    {
                        allCalls.Add(new GeminiFunctionCall(fnName, args, callId));
                    }
                }
                else if (string.Equals(stepType, "model_output", StringComparison.OrdinalIgnoreCase))
                {
                    if (step.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var part in content.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var t))
                            {
                                var txt = t.GetString();
                                if (!string.IsNullOrEmpty(txt))
                                {
                                    text = (text == null) ? txt : text + txt;
                                }
                            }
                        }
                    }
                }
            }
        }

        if (allCalls.Count > 0)
        {
            finishReason = "tool_calls";
        }
        else if (!string.IsNullOrEmpty(text))
        {
            finishReason = "stop";
        }

        return new GeminiTurnResponse(
            IsSuccess: true,
            Text: text,
            FunctionCall: allCalls.Count > 0 ? allCalls[0] : null,
            FinishReason: finishReason,
            PromptTokens: promptTokens,
            CandidatesTokens: candidatesTokens,
            TotalTokens: totalTokens,
            AttemptsMade: attempt,
            ErrorMessage: null,
            FunctionCalls: allCalls.Count > 0 ? allCalls : null,
            Model: activeModel);
    }

}

public class GeminiClient : NvidiaClient
{
    public GeminiClient(
        HttpClient httpClient,
        FotoprintConfig config,
        ILlmLoadBalancer loadBalancer,
        IRollbackManager rollbackManager,
        ILangfuseClient? langfuse = null,
        ILogger<NvidiaClient>? logger = null)
        : base(httpClient, config, loadBalancer, rollbackManager, langfuse, logger)
    {
    }
}
