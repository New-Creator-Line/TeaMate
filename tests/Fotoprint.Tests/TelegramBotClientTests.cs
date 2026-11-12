using System.Net;
using System.Text;
using System.Text.Json;
using Fotoprint.Core.Telegram;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fotoprint.Tests;

public sealed class TelegramBotClientTests
{
    private sealed record CapturedRequest(string Method, string Uri, string Body);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
        public List<CapturedRequest> Requests { get; } = [];

        public StubHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                body));

            return _responder(request, body);
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset startTime)
        {
            _utcNow = startTime;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private static HttpResponseMessage JsonOk(string resultJson = "{}") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"ok":true,"result":{{resultJson}}}""",
                Encoding.UTF8,
                "application/json")
        };

    private static TelegramBotClient CreateClient(
        StubHttpMessageHandler handler,
        string token = "123456:ABC-DEF-test-token")
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.telegram.org")
        };
        var options = new TelegramBotOptions
        {
            BotToken = token,
            WebhookSecret = "test-secret",
            ApiBaseUrl = "https://api.telegram.org"
        };
        return new TelegramBotClient(http, options, NullLogger<TelegramBotClient>.Instance);
    }

    [Fact]
    public async Task SendMessageAsync_SendsUtf8CyrillicAndInlineKeyboard()
    {
        var handler = new StubHttpMessageHandler((_, _) => JsonOk("""{"message_id":42}"""));
        var client = CreateClient(handler);

        var keyboard = new[]
        {
            new[]
            {
                new TelegramInlineButton("Помогло", "rate:like:trace-1"),
                new TelegramInlineButton("Не помогло", "rate:dislike:trace-1")
            }
        };

        var result = await client.SendMessageAsync(1980443994, "Привет, Денис! Запрос выполнен.", keyboard);

        Assert.True(result.Ok);
        Assert.Single(handler.Requests);
        var req = handler.Requests[0];
        Assert.Equal("POST", req.Method);
        Assert.EndsWith("/bot123456:ABC-DEF-test-token/sendMessage", req.Uri);
        Assert.Contains("Привет, Денис! Запрос выполнен.", req.Body);
        Assert.Contains("\"reply_markup\"", req.Body);
        Assert.Contains("\"callback_data\":\"rate:like:trace-1\"", req.Body);
    }

    [Fact]
    public async Task SendMessageAsync_LongMessage_SplitsIntoChunksAndAttachesMarkupOnlyToLastChunk()
    {
        var handler = new StubHttpMessageHandler((_, _) => JsonOk("""{"message_id":99}"""));
        var client = CreateClient(handler);

        var firstPart = new string('А', 3900);
        var secondPart = new string('Б', 600);
        var longText = $"{firstPart}\n{secondPart}";

        var keyboard = new[]
        {
            new[] { new TelegramInlineButton("ОК", "ok:1") }
        };

        var result = await client.SendMessageAsync(1980443994, longText, keyboard);

        Assert.True(result.Ok);
        Assert.Equal(2, handler.Requests.Count);

        using var firstDoc = JsonDocument.Parse(handler.Requests[0].Body);
        using var secondDoc = JsonDocument.Parse(handler.Requests[1].Body);

        var firstChunkText = firstDoc.RootElement.GetProperty("text").GetString()!;
        var secondChunkText = secondDoc.RootElement.GetProperty("text").GetString()!;

        Assert.True(firstChunkText.Length <= 4000);
        Assert.True(secondChunkText.Length <= 4000);
        Assert.False(firstDoc.RootElement.TryGetProperty("reply_markup", out _));
        Assert.True(secondDoc.RootElement.TryGetProperty("reply_markup", out _));
        Assert.Equal(longText, $"{firstChunkText}\n{secondChunkText}");
    }

    [Fact]
    public async Task SendMessageAsync_UnconfiguredDemoToken_ReturnsFailureWithoutNetworkCall()
    {
        var handler = new StubHttpMessageHandler((_, _) => JsonOk());
        var client = CreateClient(handler, token: "demo-bot-token");

        Assert.False(client.IsConfigured);
        var result = await client.SendMessageAsync(123, "Привет");

        Assert.False(result.Ok);
        Assert.Equal(401, result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendChatActionAsync_PostsTypingAction()
    {
        var handler = new StubHttpMessageHandler((_, _) => JsonOk("true"));
        var client = CreateClient(handler);

        var result = await client.SendChatActionAsync(1980443994, "typing");

        Assert.True(result.Ok);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/bot123456:ABC-DEF-test-token/sendChatAction", handler.Requests[0].Uri);
        Assert.Contains("\"chat_id\":1980443994", handler.Requests[0].Body);
        Assert.Contains("\"action\":\"typing\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task AnswerCallbackQueryAsync_PostsCallbackPayload()
    {
        var handler = new StubHttpMessageHandler((_, _) => JsonOk("true"));
        var client = CreateClient(handler);

        var result = await client.AnswerCallbackQueryAsync("cb-123", "Спасибо за оценку!", showAlert: true);

        Assert.True(result.Ok);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/bot123456:ABC-DEF-test-token/answerCallbackQuery", handler.Requests[0].Uri);
        Assert.Contains("\"callback_query_id\":\"cb-123\"", handler.Requests[0].Body);
        Assert.Contains("\"text\":\"Спасибо за оценку!\"", handler.Requests[0].Body);
        Assert.Contains("\"show_alert\":true", handler.Requests[0].Body);
    }

    [Fact]
    public async Task SetWebhookAsync_SendsUrlAndSecretToken()
    {
        var handler = new StubHttpMessageHandler((_, _) => JsonOk("true"));
        var client = CreateClient(handler);

        var result = await client.SetWebhookAsync(
            "https://example.trycloudflare.com/api/telegram/webhook",
            secretToken: "my-secret-token",
            dropPendingUpdates: false);

        Assert.True(result.Ok);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/bot123456:ABC-DEF-test-token/setWebhook", handler.Requests[0].Uri);
        Assert.Contains("\"url\":\"https://example.trycloudflare.com/api/telegram/webhook\"", handler.Requests[0].Body);
        Assert.Contains("\"secret_token\":\"my-secret-token\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task GetWebhookInfoAsync_ParsesCurrentWebhookUrl()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            JsonOk("""{"url":"https://abc.trycloudflare.com/api/telegram/webhook","pending_update_count":0}"""));
        var client = CreateClient(handler);

        var result = await client.GetWebhookInfoAsync();

        Assert.True(result.Ok);
        Assert.True(result.Result.HasValue);
        Assert.Equal(
            "https://abc.trycloudflare.com/api/telegram/webhook",
            result.Result.Value.GetProperty("url").GetString());
        Assert.Single(handler.Requests);
        Assert.Equal("GET", handler.Requests[0].Method);
        Assert.EndsWith("/bot123456:ABC-DEF-test-token/getWebhookInfo", handler.Requests[0].Uri);
    }

    [Fact]
    public void TelegramUpdateDeduplicator_RejectsDuplicateWithinTtlAndAllowsAfterExpiry()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 11, 19, 0, 0, TimeSpan.Zero));
        var dedup = new TelegramUpdateDeduplicator(TimeSpan.FromMinutes(10), clock);

        Assert.True(dedup.TryAcquire(1001));
        Assert.False(dedup.TryAcquire(1001));

        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.False(dedup.TryAcquire(1001));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(dedup.TryAcquire(1001));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", "anything", true)]
    [InlineData("secret-1", "secret-1", true)]
    [InlineData("secret-1", "secret-2", false)]
    [InlineData("secret-1", null, false)]
    public void TelegramWebhookSecurity_ValidatesSecretHeaderInConstantTime(
        string? configuredSecret,
        string? incomingHeader,
        bool expectedValid)
    {
        Assert.Equal(expectedValid, TelegramWebhookSecurity.IsSecretValid(configuredSecret, incomingHeader));
    }

    [Fact]
    public void CloudflareTunnelParser_ExtractsQuickTunnelHostname()
    {
        const string json = """{"hostname":"bright-moon-42.trycloudflare.com"}""";
        var ok = CloudflareTunnelParser.TryExtractWebhookUrl(json, out var webhookUrl);

        Assert.True(ok);
        Assert.Equal("https://bright-moon-42.trycloudflare.com/api/telegram/webhook", webhookUrl);
    }

    [Fact]
    public void SplitMessage_DoesNotSplitUtf16SurrogatePairAcrossChunkBoundary()
    {
        var prefix = new string('A', 9);
        var text = prefix + "👍" + "B";

        var chunks = TelegramBotClient.SplitMessage(text, maxLength: 10);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(prefix, chunks[0]);
        Assert.Equal("👍B", chunks[1]);
        Assert.False(char.IsHighSurrogate(chunks[0][^1]));
        Assert.False(char.IsLowSurrogate(chunks[1][0]));
    }
}
