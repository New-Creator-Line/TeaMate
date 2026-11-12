using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fotoprint.Core.Telegram;

/// <summary>
/// Configuration options for connecting to Telegram Bot API and auto-registering Cloudflare Quick Tunnel webhooks.
/// </summary>
public sealed record TelegramBotOptions
{
    public string BotToken { get; init; } = string.Empty;
    public string? WebhookSecret { get; init; }
    public string? CloudflaredMetricsUrl { get; init; }
    public string ApiBaseUrl { get; init; } = "https://api.telegram.org";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BotToken) &&
        !BotToken.StartsWith("demo", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Represents an inline keyboard button in a Telegram Bot API message.
/// </summary>
public sealed record TelegramInlineButton(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string CallbackData);

/// <summary>
/// Normalized response returned by Telegram Bot API calls.
/// </summary>
public sealed record TelegramApiResponse(
    bool Ok,
    int? ErrorCode = null,
    string? Description = null,
    JsonElement? Result = null);

public interface ITelegramBotClient
{
    bool IsConfigured { get; }

    Task<TelegramApiResponse> SendMessageAsync(
        long chatId,
        string text,
        IReadOnlyList<IReadOnlyList<TelegramInlineButton>>? inlineKeyboard = null,
        string? parseMode = null,
        CancellationToken ct = default);

    Task<TelegramApiResponse> SendChatActionAsync(
        long chatId,
        string action = "typing",
        CancellationToken ct = default);

    Task<TelegramApiResponse> AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default);

    Task<TelegramApiResponse> SetWebhookAsync(
        string webhookUrl,
        string? secretToken = null,
        bool dropPendingUpdates = false,
        CancellationToken ct = default);

    Task<TelegramApiResponse> GetWebhookInfoAsync(CancellationToken ct = default);
}

/// <summary>
/// HTTP client wrapper for Telegram Bot API supporting UTF-8 Cyrillic serialization and message chunking.
/// </summary>
public sealed class TelegramBotClient : ITelegramBotClient
{
    private const int MaxTelegramMessageLength = 4000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly TelegramBotOptions _options;
    private readonly ILogger<TelegramBotClient> _logger;

    public TelegramBotClient(
        HttpClient http,
        TelegramBotOptions options,
        ILogger<TelegramBotClient>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<TelegramBotClient>.Instance;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<TelegramApiResponse> SendMessageAsync(
        long chatId,
        string text,
        IReadOnlyList<IReadOnlyList<TelegramInlineButton>>? inlineKeyboard = null,
        string? parseMode = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return UnconfiguredResponse();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new TelegramApiResponse(false, ErrorCode: 400, Description: "Message text cannot be empty.");
        }

        var chunks = SplitMessage(text, MaxTelegramMessageLength);
        TelegramApiResponse lastResponse = new(true);

        for (var i = 0; i < chunks.Count; i++)
        {
            var isLastChunk = i == chunks.Count - 1;
            var replyMarkup = isLastChunk && inlineKeyboard is { Count: > 0 }
                ? new TelegramReplyMarkup(inlineKeyboard)
                : null;

            var payload = new TelegramSendMessageRequest(
                ChatId: chatId,
                Text: chunks[i],
                ParseMode: parseMode,
                ReplyMarkup: replyMarkup);

            lastResponse = await PostMethodAsync("sendMessage", payload, ct);
            if (!lastResponse.Ok)
            {
                return lastResponse;
            }
        }

        return lastResponse;
    }

    public Task<TelegramApiResponse> SendChatActionAsync(
        long chatId,
        string action = "typing",
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(UnconfiguredResponse());
        }

        var payload = new TelegramChatActionRequest(chatId, action);
        return PostMethodAsync("sendChatAction", payload, ct);
    }

    public Task<TelegramApiResponse> AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(UnconfiguredResponse());
        }

        var payload = new TelegramAnswerCallbackRequest(callbackQueryId, text, showAlert);
        return PostMethodAsync("answerCallbackQuery", payload, ct);
    }

    public Task<TelegramApiResponse> SetWebhookAsync(
        string webhookUrl,
        string? secretToken = null,
        bool dropPendingUpdates = false,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(UnconfiguredResponse());
        }

        var payload = new TelegramSetWebhookRequest(
            Url: webhookUrl,
            SecretToken: string.IsNullOrWhiteSpace(secretToken) ? null : secretToken,
            DropPendingUpdates: dropPendingUpdates ? true : null,
            AllowedUpdates: ["message", "callback_query"]);

        return PostMethodAsync("setWebhook", payload, ct);
    }

    public async Task<TelegramApiResponse> GetWebhookInfoAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return UnconfiguredResponse();
        }

        var endpoint = BuildMethodUri("getWebhookInfo");
        try
        {
            using var response = await _http.GetAsync(endpoint, ct);
            return await ParseTelegramResponseAsync("getWebhookInfo", response, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram getWebhookInfo request failed");
            return new TelegramApiResponse(false, Description: ex.Message);
        }
    }

    /// <summary>
    /// Splits a message cleanly on newline or whitespace boundaries so each part fits Telegram's 4096-character limit.
    /// </summary>
    public static IReadOnlyList<string> SplitMessage(string text, int maxLength = MaxTelegramMessageLength)
    {
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }

        if (text.Length <= maxLength)
        {
            return [text];
        }

        var chunks = new List<string>();
        var offset = 0;

        while (offset < text.Length)
        {
            var remaining = text.Length - offset;
            if (remaining <= maxLength)
            {
                chunks.Add(text[offset..]);
                break;
            }

            var windowEnd = offset + maxLength;
            var searchCount = maxLength + 1;
            var minPreferredSplit = offset + (maxLength / 2);

            var newlineIdx = text.LastIndexOf('\n', windowEnd, searchCount);
            if (newlineIdx >= minPreferredSplit)
            {
                chunks.Add(text[offset..newlineIdx]);
                offset = newlineIdx + 1;
                continue;
            }

            var spaceIdx = text.LastIndexOf(' ', windowEnd, searchCount);
            if (spaceIdx >= minPreferredSplit)
            {
                chunks.Add(text[offset..spaceIdx]);
                offset = spaceIdx + 1;
                continue;
            }

            if (windowEnd > offset + 1 &&
                windowEnd < text.Length &&
                char.IsHighSurrogate(text[windowEnd - 1]) &&
                char.IsLowSurrogate(text[windowEnd]))
            {
                windowEnd--;
            }

            chunks.Add(text[offset..windowEnd]);
            offset = windowEnd;
        }

        return chunks;
    }

    private async Task<TelegramApiResponse> PostMethodAsync<TBody>(
        string method,
        TBody payload,
        CancellationToken ct)
    {
        var endpoint = BuildMethodUri(method);
        try
        {
            using var content = JsonContent.Create(payload, options: JsonOptions);
            using var response = await _http.PostAsync(endpoint, content, ct);
            return await ParseTelegramResponseAsync(method, response, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram Bot API call '{Method}' threw an exception", method);
            return new TelegramApiResponse(false, Description: ex.Message);
        }
    }

    private async Task<TelegramApiResponse> ParseTelegramResponseAsync(
        string method,
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new TelegramApiResponse(
                response.IsSuccessStatusCode,
                ErrorCode: (int)response.StatusCode,
                Description: response.ReasonPhrase);
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            int? errorCode = root.TryGetProperty("error_code", out var errProp) && errProp.ValueKind == JsonValueKind.Number
                ? errProp.GetInt32()
                : (response.IsSuccessStatusCode ? null : (int)response.StatusCode);
            var description = root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String
                ? descProp.GetString()
                : null;
            JsonElement? result = root.TryGetProperty("result", out var resProp)
                ? resProp.Clone()
                : null;

            if (!ok)
            {
                _logger.LogWarning(
                    "Telegram API '{Method}' returned error {ErrorCode}: {Description}",
                    method, errorCode, description ?? raw);
            }

            return new TelegramApiResponse(ok, errorCode, description, result);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON from Telegram API '{Method}' (HTTP {Status})", method, (int)response.StatusCode);
            return new TelegramApiResponse(
                false,
                ErrorCode: (int)response.StatusCode,
                Description: raw);
        }
    }

    private string BuildMethodUri(string method)
    {
        var baseUrl = _options.ApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/bot{_options.BotToken}/{method}";
    }

    private static TelegramApiResponse UnconfiguredResponse() =>
        new(false, ErrorCode: 401, Description: "Telegram bot token is not configured.");

    private sealed record TelegramReplyMarkup(
        [property: JsonPropertyName("inline_keyboard")] IReadOnlyList<IReadOnlyList<TelegramInlineButton>> InlineKeyboard);

    private sealed record TelegramSendMessageRequest(
        [property: JsonPropertyName("chat_id")] long ChatId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("parse_mode")] string? ParseMode,
        [property: JsonPropertyName("reply_markup")] TelegramReplyMarkup? ReplyMarkup);

    private sealed record TelegramChatActionRequest(
        [property: JsonPropertyName("chat_id")] long ChatId,
        [property: JsonPropertyName("action")] string Action);

    private sealed record TelegramAnswerCallbackRequest(
        [property: JsonPropertyName("callback_query_id")] string CallbackQueryId,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("show_alert")] bool ShowAlert);

    private sealed record TelegramSetWebhookRequest(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("secret_token")] string? SecretToken,
        [property: JsonPropertyName("drop_pending_updates")] bool? DropPendingUpdates,
        [property: JsonPropertyName("allowed_updates")] string[] AllowedUpdates);
}

/// <summary>
/// Thread-safe idempotency cache preventing duplicate processing of retried Telegram webhook update_ids.
/// </summary>
public sealed class TelegramUpdateDeduplicator
{
    private readonly ConcurrentDictionary<long, DateTimeOffset> _seen = new();
    private readonly TimeSpan _ttl;
    private readonly TimeProvider _timeProvider;

    public TelegramUpdateDeduplicator(TimeSpan? ttl = null, TimeProvider? timeProvider = null)
    {
        _ttl = ttl ?? TimeSpan.FromMinutes(10);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryAcquire(long updateId)
    {
        var now = _timeProvider.GetUtcNow();
        PruneExpired(now);

        if (_seen.TryGetValue(updateId, out var existingExpiry))
        {
            if (existingExpiry > now)
            {
                return false;
            }

            _seen.TryRemove(new KeyValuePair<long, DateTimeOffset>(updateId, existingExpiry));
        }

        return _seen.TryAdd(updateId, now.Add(_ttl));
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var pair in _seen)
        {
            if (pair.Value <= now)
            {
                _seen.TryRemove(pair);
            }
        }
    }
}

/// <summary>
/// Constant-time validation helper for Telegram's X-Telegram-Bot-Api-Secret-Token webhook header.
/// </summary>
public static class TelegramWebhookSecurity
{
    public static bool IsSecretValid(string? configuredSecret, string? incomingHeader)
    {
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            return true;
        }

        if (string.IsNullOrEmpty(incomingHeader))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredSecret));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(incomingHeader));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}

/// <summary>
/// Extracts public HTTPS webhook URLs from cloudflared quicktunnel metrics JSON.
/// </summary>
public static class CloudflareTunnelParser
{
    public static bool TryExtractWebhookUrl(
        string quickTunnelJson,
        out string? webhookUrl,
        string webhookPath = "/api/telegram/webhook")
    {
        webhookUrl = null;
        if (string.IsNullOrWhiteSpace(quickTunnelJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(quickTunnelJson);
            if (!doc.RootElement.TryGetProperty("hostname", out var hostProp) ||
                hostProp.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var hostname = hostProp.GetString()?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(hostname))
            {
                return false;
            }

            var normalizedPath = webhookPath.StartsWith('/') ? webhookPath : $"/{webhookPath}";
            var candidate = $"https://{hostname}{normalizedPath}";
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            webhookUrl = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
