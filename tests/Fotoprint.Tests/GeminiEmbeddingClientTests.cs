using System.Net;
using System.Text;
using System.Text.Json;
using Fotoprint.Core.Agents;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Llm;
using Fotoprint.Core.Models;
using Fotoprint.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fotoprint.Tests;

public class GeminiEmbeddingClientTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public string? LastRequestUrl { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUrl = request.RequestUri?.ToString();
            LastRequestBody = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            return await _handler(request, cancellationToken);
        }
    }

    [Fact]
    public async Task EmbedTextAsync_Calls_GeminiEmbedding2_And_Normalizes_Vector()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var json = JsonSerializer.Serialize(new
            {
                embedding = new { values = new[] { 3.0f, 4.0f } }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        var config = new FotoprintConfig();
        config.Llm.Models.EmbeddingModel = "gemini-embedding-2";
        config.Rag.EmbeddingDimensions = 1536;

        var client = new GeminiEmbeddingClient(new HttpClient(handler), config, NullLogger<GeminiEmbeddingClient>.Instance, apiKeyOverride: "AQ.test-key");
        var vec = await client.EmbedTextAsync("Допустимый процент брака", "RETRIEVAL_QUERY");

        Assert.NotNull(vec);
        Assert.Equal(2, vec!.Length);
        Assert.InRange(vec[0], 0.59f, 0.61f);
        Assert.InRange(vec[1], 0.79f, 0.81f);
        Assert.Contains("models/gemini-embedding-2:embedContent", handler.LastRequestUrl);
        Assert.Contains("\"outputDimensionality\":1536", handler.LastRequestBody);
        Assert.Contains("\"taskType\":\"RETRIEVAL_QUERY\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task EmbedBatchAsync_Calls_BatchEmbedContents_And_Handles_Empty_Entries()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var json = JsonSerializer.Serialize(new
            {
                embeddings = new[]
                {
                    new { values = new[] { 1.0f, 0.0f } },
                    new { values = new[] { 0.0f, 1.0f } }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });

        var config = new FotoprintConfig();
        var client = new GeminiEmbeddingClient(new HttpClient(handler), config, NullLogger<GeminiEmbeddingClient>.Instance, apiKeyOverride: "AQ.test-key");
        var vectors = await client.EmbedBatchAsync(new[] { "Первый чанк", "", "Третий чанк" });

        Assert.Equal(3, vectors.Count);
        Assert.NotNull(vectors[0]);
        Assert.Null(vectors[1]);
        Assert.NotNull(vectors[2]);
        Assert.Contains("models/gemini-embedding-2:batchEmbedContents", handler.LastRequestUrl);
    }

    [Fact]
    public async Task EmbedTextAsync_Returns_Null_When_ApiKey_Missing()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HTTP should not be called when API key is empty"));

        var config = new FotoprintConfig();
        var client = new GeminiEmbeddingClient(new HttpClient(handler), config, NullLogger<GeminiEmbeddingClient>.Instance, apiKeyOverride: "");

        Assert.False(client.IsConfigured);
        var vec = await client.EmbedTextAsync("Тест");
        Assert.Null(vec);
    }

    [Fact]
    public async Task Agent_Uses_Vector_Search_For_Regulations_When_EmbeddingClient_Configured()
    {
        var llm = new RagToolFakeLlmClient();
        var repo = new VectorSearchTrackingRepo();
        var embeddingClient = new FakeEmbeddingClient();
        var config = new FotoprintConfig();

        var agent = new FotoprintAiAgent(
            llm,
            repo,
            new DummyHttpClientFactory(),
            config,
            logger: NullLogger<FotoprintAiAgent>.Instance,
            embeddingClient: embeddingClient);

        var res = await agent.ProcessMessageAsync(100001, 55555, "Какой допустимый брак?");

        Assert.True(res.IsSuccess);
        Assert.Contains("search_typography_regulations", res.ToolsInvoked);
        Assert.True(repo.VectorSearchCalled);
        Assert.False(repo.TextSearchCalled);
        Assert.Contains("2.5%", res.AnswerText);
    }

    private sealed class FakeEmbeddingClient : IEmbeddingClient
    {
        public string ModelName => "gemini-embedding-2";
        public int Dimensions => 1536;
        public bool IsConfigured => true;

        public Task<float[]?> EmbedTextAsync(string text, string taskType = "RETRIEVAL_DOCUMENT", CancellationToken ct = default)
            => Task.FromResult<float[]?>(new float[1536]);

        public Task<List<float[]?>> EmbedBatchAsync(IReadOnlyList<string> texts, string taskType = "RETRIEVAL_DOCUMENT", CancellationToken ct = default)
            => Task.FromResult(texts.Select(_ => (float[]?)new float[1536]).ToList());
    }

    private sealed class RagToolFakeLlmClient : IGeminiClient
    {
        private int _step;

        public Task<GeminiGenerateResponse> GenerateContentAsync(string prompt, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)
            => Task.FromResult(new GeminiGenerateResponse(true, "ok", "STOP", 1, 1, 2, 1, null));

        public Task<GeminiTurnResponse> ChatTurnAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)
        {
            _step++;
            if (_step == 1)
            {
                var args = JsonSerializer.SerializeToElement(new { query = "допустимый процент брака" });
                return Task.FromResult(new GeminiTurnResponse(true, null, new GeminiFunctionCall("search_typography_regulations", args, "c1"), "tool_calls", 10, 10, 20, 1, null));
            }
            return Task.FromResult(new GeminiTurnResponse(true, "Допустимый брак составляет 2.5% при приладке.", null, "stop", 20, 15, 35, 1, null));
        }

        public GeminiCallStats GetStats() => new(1, 1, 60, 59, 1.0, DateTime.UtcNow, 0);
    }

    private sealed class VectorSearchTrackingRepo : IFotoprintRepository
    {
        public bool VectorSearchCalled { get; private set; }
        public bool TextSearchCalled { get; private set; }

        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextAsync(long telegramId, CancellationToken ct = default)
            => Task.FromResult<EmployeeSecurityContext?>(new EmployeeSecurityContext
            {
                EmployeeId = 1,
                MasterId = 100001,
                FullName = "Алексей Смирнов",
                TelegramId = telegramId,
                AccessGroups = new() { "all", "production" }
            });

        public Task<List<CorporateDocumentChunk>> SearchSimilarChunksAsync(float[] queryEmbedding, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default)
        {
            VectorSearchCalled = true;
            return Task.FromResult(new List<CorporateDocumentChunk>
            {
                new() { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 1, ChunkText = "Допустимый брак 2.5%" }
            });
        }

        public Task<List<CorporateDocumentChunk>> SearchMeetingSimilarChunksAsync(float[] queryEmbedding, List<string> meetingAccessGroups, int topK = 5, CancellationToken ct = default)
            => Task.FromResult(new List<CorporateDocumentChunk>());

        public Task<List<CorporateDocumentChunk>> SearchTextChunksAsync(string queryText, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default)
        {
            TextSearchCalled = true;
            return Task.FromResult(new List<CorporateDocumentChunk>());
        }

        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextByMasterIdAsync(long masterId, CancellationToken ct = default) => Task.FromResult<EmployeeSecurityContext?>(null);
        public Task<long?> ResolveMasterIdByMessengerAsync(string messengerType, string messengerUserId, CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task<Employee?> GetEmployeeByIdAsync(int id, CancellationToken ct = default) => Task.FromResult<Employee?>(null);
        public Task<List<Employee>> GetAllEmployeesAsync(CancellationToken ct = default) => Task.FromResult(new List<Employee>());
        public Task<ChatSession> GetOrCreateActiveSessionByMasterIdAsync(long masterId, long messengerChatId, string messengerType = "telegram", CancellationToken ct = default) => Task.FromResult(new ChatSession { Id = Guid.NewGuid(), MasterId = masterId, IsActive = true });
        public Task<ChatSession> SwitchToNewSessionAsync(Guid currentSessionId, long masterId, long messengerChatId, string? newBranch = null, string messengerType = "telegram", CancellationToken ct = default) => Task.FromResult(new ChatSession { Id = Guid.NewGuid() });
        public Task<List<ChatMessage>> GetMessagesBySessionIdAsync(Guid sessionId, int limit = 20, CancellationToken ct = default) => Task.FromResult(new List<ChatMessage>());
        public Task SaveMessageToSessionAsync(Guid sessionId, long masterId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ChatSession> GetOrCreateSessionAsync(long telegramChatId, long? telegramUserId = null, CancellationToken ct = default) => Task.FromResult(new ChatSession { Id = Guid.NewGuid() });
        public Task SaveMessageAsync(Guid sessionId, string contextId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<ChatMessage>> GetRecentMessagesAsync(Guid sessionId, int limit = 20, CancellationToken ct = default) => Task.FromResult(new List<ChatMessage>());
        public Task FreezeContextAsync(Guid sessionId, string contextId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid> SaveDocumentAsync(string title, string sourceType, string? filePath, string? department, List<string> allowedAccessGroups, object? metadata = null, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task SaveDocumentChunksAsync(Guid documentId, List<string> chunkTexts, List<float[]?> embeddings, List<string> allowedAccessGroups, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<CorporateDocumentChunk>> GetChunksWithoutEmbeddingAsync(int limit = 200, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());
        public Task UpdateChunkEmbeddingAsync(Guid chunkId, float[] embedding, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid> CreateMeetingAsync(string title, string? description, DateTime startTime, DateTime? endTime = null, string status = "completed", int? organizerId = null, List<int>? participantIds = null, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task<List<Meeting>> GetUpcomingMeetingsAsync(int timeWindowMinutes = 30, CancellationToken ct = default) => Task.FromResult(new List<Meeting>());
        public Task<Meeting?> GetMeetingByIdAsync(Guid meetingId, CancellationToken ct = default) => Task.FromResult<Meeting?>(null);
        public Task<List<MeetingNote>> GetMeetingNotesAsync(Guid meetingId, CancellationToken ct = default) => Task.FromResult(new List<MeetingNote>());
        public Task SaveMeetingNoteAsync(Guid meetingId, int? authorId, string noteText, string category = "general", CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid> SaveMeetingProtocolAsync(Guid meetingId, string title, string summary, List<string> decisions, string? rawTranscript = null, string? docxPath = null, string? pdfPath = null, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task<MeetingProtocol?> GetMeetingProtocolAsync(Guid meetingId, CancellationToken ct = default) => Task.FromResult<MeetingProtocol?>(null);
        public Task<Guid> CreateActionItemAsync(Guid meetingId, Guid? protocolId, string title, string? description, int? responsibleId, DateTime? deadline, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task<List<ActionItem>> GetActionItemsByMeetingAsync(Guid meetingId, CancellationToken ct = default) => Task.FromResult(new List<ActionItem>());
        public Task<List<ActionItem>> GetActionItemsByResponsibleAsync(int employeeId, string? statusFilter = null, CancellationToken ct = default) => Task.FromResult(new List<ActionItem>());
        public Task UpdateActionItemStatusAsync(Guid actionItemId, string status, int? bitrix24TaskId = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task EnsureNotificationSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<Employee?> GetEmployeeByTelegramIdAsync(long telegramId, CancellationToken ct = default) => Task.FromResult<Employee?>(null);
        public Task<bool> SetEmployeeNotificationsEnabledAsync(long masterId, bool enabled, CancellationToken ct = default) => Task.FromResult(false);
        public Task<List<Meeting>> GetMeetingsInRangeUtcAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default) => Task.FromResult(new List<Meeting>());
        public Task<List<ActionItem>> GetOpenActionItemsAsync(int? employeeId = null, CancellationToken ct = default) => Task.FromResult(new List<ActionItem>());
        public Task<Guid?> TryClaimNotificationDeliveryAsync(string dedupKey, string notificationType, int? recipientEmployeeId, long recipientTelegramId, Guid? entityId = null, CancellationToken ct = default) => Task.FromResult<Guid?>(Guid.NewGuid());
        public Task CompleteNotificationDeliveryAsync(Guid deliveryId, string status, string? errorMessage = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}

