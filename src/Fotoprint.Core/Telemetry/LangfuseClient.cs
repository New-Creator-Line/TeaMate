using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Fotoprint.Core.Audit;
using Fotoprint.Core.Security;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Telemetry;

public interface ILangfuseClient
{
    string GenerateTraceId();
    string GenerateObservationId() => Guid.NewGuid().ToString("N");

    Task CreateTraceAsync(
        string traceId,
        string name,
        string? userId = null,
        string? sessionId = null,
        object? metadata = null,
        string[]? tags = null,
        CancellationToken ct = default);

    Task CreateTraceAsync(
        string traceId,
        string name,
        string? userId,
        string? sessionId,
        object? metadata,
        string[]? tags,
        object? input,
        object? output = null,
        CancellationToken ct = default) => CreateTraceAsync(traceId, name, userId, sessionId, metadata, tags, ct);

    Task CreateSpanAsync(
        string traceId,
        string name,
        DateTime startTime,
        DateTime endTime,
        object? input = null,
        object? output = null,
        object? metadata = null,
        string? parentObservationId = null,
        string? spanId = null,
        CancellationToken ct = default) => Task.CompletedTask;

    Task CreateGenerationAsync(
        string traceId,
        string name,
        string model,
        DateTime startTime,
        DateTime endTime,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        int cachedTokens = 0,
        object? input = null,
        object? output = null,
        object? metadata = null,
        double? calculatedCost = null,
        string? parentObservationId = null,
        string? generationId = null,
        CancellationToken ct = default) => Task.CompletedTask;

    Task LogEventAsync(
        string traceId,
        string name,
        string level = "DEFAULT",
        string? message = null,
        object? metadata = null,
        CancellationToken ct = default);

    Task LogSecurityAlertAsync(
        string traceId,
        long telegramId,
        string toolOrAction,
        string requiredPermission,
        string reason,
        CancellationToken ct = default);

    Task LogScoreAsync(
        string traceId,
        string name,
        double value,
        string? comment = null,
        string? observationId = null,
        CancellationToken ct = default) => Task.CompletedTask;

    double CalculateCost(int promptTokens, int completionTokens, int cachedTokens = 0) => 0.0;
}

public class LangfuseOptions
{
    public string BaseUrl { get; set; } = "http://localhost:3000";
    public string PublicKey { get; set; } = "pk-lf-fotoprint-local";
    public string SecretKey { get; set; } = "sk-lf-fotoprint-local";
    public bool Enabled { get; set; } = true;
    public double PromptTokenCostPer1k { get; set; } = 0.000075;
    public double CompletionTokenCostPer1k { get; set; } = 0.000300;
    public double CachedTokenCostPer1k { get; set; } = 0.00001875;
}

public class LangfuseClient : ILangfuseClient
{
    private readonly HttpClient _httpClient;
    private readonly LangfuseOptions _options;
    private readonly ILogger<LangfuseClient> _logger;

    public LangfuseClient(HttpClient httpClient, LangfuseOptions options, ILogger<LangfuseClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        }

        var authBytes = Encoding.UTF8.GetBytes($"{_options.PublicKey}:{_options.SecretKey}");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }

    public string GenerateTraceId() => Guid.NewGuid().ToString("N");
    public string GenerateObservationId() => Guid.NewGuid().ToString("N");

    public double CalculateCost(int promptTokens, int completionTokens, int cachedTokens = 0)
    {
        var promptCost = (promptTokens / 1000.0) * _options.PromptTokenCostPer1k;
        var completionCost = (completionTokens / 1000.0) * _options.CompletionTokenCostPer1k;
        var cachedCost = (cachedTokens / 1000.0) * _options.CachedTokenCostPer1k;
        return Math.Round(promptCost + completionCost + cachedCost, 6);
    }

    public Task CreateTraceAsync(
        string traceId,
        string name,
        string? userId = null,
        string? sessionId = null,
        object? metadata = null,
        string[]? tags = null,
        CancellationToken ct = default)
        => CreateTraceAsync(traceId, name, userId, sessionId, metadata, tags, input: null, output: null, ct: ct);

    public async Task CreateTraceAsync(
        string traceId,
        string name,
        string? userId,
        string? sessionId,
        object? metadata,
        string[]? tags,
        object? input,
        object? output = null,
        CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        var bodyDict = new Dictionary<string, object?>
        {
            ["id"] = traceId,
            ["name"] = name,
            ["userId"] = userId,
            ["sessionId"] = sessionId,
            ["metadata"] = metadata,
            ["tags"] = tags ?? Array.Empty<string>()
        };

        if (input != null) bodyDict["input"] = input;
        if (output != null) bodyDict["output"] = output;

        var payload = new
        {
            batch = new object[]
            {
                new
                {
                    id = Guid.NewGuid().ToString("N"),
                    type = "trace-create",
                    timestamp = DateTime.UtcNow.ToString("O"),
                    body = bodyDict
                }
            }
        };

        await SendBatchAsync(payload, ct);
    }

    public async Task CreateSpanAsync(
        string traceId,
        string name,
        DateTime startTime,
        DateTime endTime,
        object? input = null,
        object? output = null,
        object? metadata = null,
        string? parentObservationId = null,
        string? spanId = null,
        CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        var id = spanId ?? GenerateObservationId();
        var latencyMs = Math.Max(1, (endTime - startTime).TotalMilliseconds);

        var enrichedMeta = new Dictionary<string, object>
        {
            ["latency_ms"] = Math.Round(latencyMs, 2)
        };

        if (metadata != null)
        {
            try
            {
                var json = JsonSerializer.Serialize(metadata);
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict) enrichedMeta[kvp.Key] = kvp.Value;
                }
            }
            catch { }
        }

        var payload = new
        {
            batch = new object[]
            {
                new
                {
                    id = Guid.NewGuid().ToString("N"),
                    type = "span-create",
                    timestamp = DateTime.UtcNow.ToString("O"),
                    body = new
                    {
                        id = id,
                        traceId = traceId,
                        name = name,
                        startTime = startTime.ToString("O"),
                        endTime = endTime.ToString("O"),
                        input = input,
                        output = output,
                        parentObservationId = parentObservationId,
                        metadata = enrichedMeta
                    }
                }
            }
        };

        await SendBatchAsync(payload, ct);
    }

    public async Task CreateGenerationAsync(
        string traceId,
        string name,
        string model,
        DateTime startTime,
        DateTime endTime,
        int promptTokens,
        int completionTokens,
        int totalTokens,
        int cachedTokens = 0,
        object? input = null,
        object? output = null,
        object? metadata = null,
        double? calculatedCost = null,
        string? parentObservationId = null,
        string? generationId = null,
        CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        var id = generationId ?? GenerateObservationId();
        var promptCost = Math.Round((promptTokens / 1000.0) * _options.PromptTokenCostPer1k, 6);
        var completionCost = Math.Round((completionTokens / 1000.0) * _options.CompletionTokenCostPer1k, 6);
        var cost = calculatedCost ?? CalculateCost(promptTokens, completionTokens, cachedTokens);
        var latencyMs = Math.Max(1, (endTime - startTime).TotalMilliseconds);

        var enrichedMeta = new Dictionary<string, object>
        {
            ["latency_ms"] = Math.Round(latencyMs, 2),
            ["cached_tokens"] = cachedTokens,
            ["calculated_cost_usd"] = cost
        };

        if (metadata != null)
        {
            try
            {
                var json = JsonSerializer.Serialize(metadata);
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (dict != null)
                {
                    foreach (var kvp in dict) enrichedMeta[kvp.Key] = kvp.Value;
                }
            }
            catch { }
        }

        var payload = new
        {
            batch = new object[]
            {
                new
                {
                    id = Guid.NewGuid().ToString("N"),
                    type = "generation-create",
                    timestamp = DateTime.UtcNow.ToString("O"),
                    body = new
                    {
                        id = id,
                        traceId = traceId,
                        name = name,
                        model = model,
                        startTime = startTime.ToString("O"),
                        endTime = endTime.ToString("O"),
                        input = input,
                        output = output,
                        parentObservationId = parentObservationId,
                        usage = new
                        {
                            input = promptTokens,
                            output = completionTokens,
                            total = totalTokens,
                            unit = "TOKENS",
                            inputCost = promptCost,
                            outputCost = completionCost,
                            totalCost = cost
                        },
                        usageDetails = new
                        {
                            input = promptTokens,
                            output = completionTokens,
                            total = totalTokens
                        },
                        costDetails = new
                        {
                            input = promptCost,
                            output = completionCost,
                            total = cost
                        },
                        metadata = enrichedMeta
                    }
                }
            }
        };

        await SendBatchAsync(payload, ct);
    }

    public async Task LogEventAsync(
        string traceId,
        string name,
        string level = "DEFAULT",
        string? message = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        // Langfuse v3 ObservationLevel поддерживает только: DEBUG | DEFAULT | WARNING | ERROR
        var normalizedLevel = level?.Trim().ToUpperInvariant() switch
        {
            "DEBUG" => "DEBUG",
            "WARNING" or "WARN" => "WARNING",
            "ERROR" or "CRITICAL" or "FATAL" => "ERROR",
            _ => "DEFAULT"
        };

        var payload = new
        {
            batch = new object[]
            {
                new
                {
                    id = Guid.NewGuid().ToString("N"),
                    type = "event-create",
                    timestamp = DateTime.UtcNow.ToString("O"),
                    body = new
                    {
                        id = Guid.NewGuid().ToString("N"),
                        traceId = traceId,
                        name = name,
                        level = normalizedLevel,
                        statusMessage = message,
                        metadata = metadata
                    }
                }
            }
        };

        await SendBatchAsync(payload, ct);
    }

    public async Task LogSecurityAlertAsync(
        string traceId,
        long telegramId,
        string toolOrAction,
        string requiredPermission,
        string reason,
        CancellationToken ct = default)
    {
        _logger.LogWarning("SECURITY ALERT: User {TelegramId} blocked from {ToolOrAction}. Missing permission: {Permission}. Reason: {Reason}",
            telegramId, toolOrAction, requiredPermission, reason);

        // ОТ-04 / 7.5: отказ в доступе фиксируется в журнале аудита (PostgreSQL), а не только в Langfuse.
        await AuditTrail.Instance.RecordAsync(new AuditEvent(telegramId, "api", "security.access_denied", "permission", requiredPermission,
            AuditResult.Denied, traceId, new { action = toolOrAction, reason }), ct);

        // 1. Событие Event:Security_Violation для дашборда безопасности
        await LogEventAsync(
            traceId: traceId,
            name: "Event:Security_Violation",
            level: "ERROR",
            message: $"Security Violation: User {telegramId} attempted unauthorized action {toolOrAction}",
            metadata: new
            {
                telegram_id = telegramId,
                action = toolOrAction,
                required_permission = requiredPermission,
                reason = reason,
                timestamp = DateTime.UtcNow
            },
            ct: ct
        );

        // 2. Детальный лог для перехватчика
        await LogEventAsync(
            traceId: traceId,
            name: "SecurityInterceptor:AccessDenied",
            level: "WARNING",
            message: $"Access Denied for user {telegramId} on {toolOrAction}",
            metadata: new
            {
                telegram_id = telegramId,
                action = toolOrAction,
                required_permission = requiredPermission,
                reason = reason,
                timestamp = DateTime.UtcNow
            },
            ct: ct
        );
    }

    public async Task LogScoreAsync(
        string traceId,
        string name,
        double value,
        string? comment = null,
        string? observationId = null,
        CancellationToken ct = default)
    {
        if (!_options.Enabled) return;

        var payload = new
        {
            batch = new object[]
            {
                new
                {
                    id = Guid.NewGuid().ToString("N"),
                    type = "score-create",
                    timestamp = DateTime.UtcNow.ToString("O"),
                    body = new
                    {
                        id = Guid.NewGuid().ToString("N"),
                        traceId = traceId,
                        name = name,
                        value = value,
                        comment = comment,
                        observationId = observationId
                    }
                }
            }
        };

        await SendBatchAsync(payload, ct);
    }

    private static readonly JsonSerializerOptions MaskedJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private async Task SendBatchAsync(object payload, CancellationToken ct)
    {
        try
        {
            // AI-06: чувствительные данные (контакты, платёжные реквизиты, зарплаты, ФИО) маскируются до отправки в телеметрию.
            var json = PiiMasker.MaskForLogs(JsonSerializer.Serialize(payload, MaskedJsonOptions));
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("api/public/ingestion", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogDebug("Langfuse ingestion returned {StatusCode}: {Body}", response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send telemetry event to Langfuse. Ingestion ignored.");
        }
    }
}
