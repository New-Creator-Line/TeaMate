using System.Text.Json;

using Fotoprint.Core.Agents;

using Fotoprint.Core.Configuration;

using Fotoprint.Core.Data;

using Fotoprint.Core.Llm;

using Fotoprint.Core.Models;

using Fotoprint.Core.Security;

using Fotoprint.Core.Telemetry;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Fotoprint.Tests;

public class FotoprintAiAgentTests

{

    private class FakeGeminiClient : IGeminiClient

    {

        private int _callStep = 0;

        private readonly bool _triggerTool;

        public string? LastSystemInstruction { get; private set; }

        public List<object>? LastContents { get; private set; }

        public FakeGeminiClient(bool triggerTool = true)

        {

            _triggerTool = triggerTool;

        }

        public Task<GeminiGenerateResponse> GenerateContentAsync(string prompt, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

        {

            return Task.FromResult(new GeminiGenerateResponse(true, "Ответ", "STOP", 10, 20, 30, 1, null));

        }

        public Task<GeminiTurnResponse> ChatTurnAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

        {

            _callStep++;

            LastSystemInstruction = systemInstruction;

            LastContents = contents;

            if (!_triggerTool)

            {

                // Сразу текстовый ответ без инструментов

                return Task.FromResult(new GeminiTurnResponse(

                    IsSuccess: true,

                    Text: "Здравствуйте! Я корпоративный AI-ассистент типографии Фотопринт.",

                    FunctionCall: null,

                    FinishReason: "STOP",

                    PromptTokens: 25,

                    CandidatesTokens: 15,

                    TotalTokens: 40,

                    AttemptsMade: 1,

                    ErrorMessage: null));

            }

            if (_callStep == 1)

            {

                // Шаг 1: Модель автономно решает вызвать инструмент get_production_kpis

                var argsJson = JsonSerializer.SerializeToElement(new { department = "Офсетный цех" });

                return Task.FromResult(new GeminiTurnResponse(

                    IsSuccess: true,

                    Text: null,

                    FunctionCall: new GeminiFunctionCall("get_production_kpis", argsJson),

                    FinishReason: "STOP",

                    PromptTokens: 50,

                    CandidatesTokens: 12,

                    TotalTokens: 62,

                    AttemptsMade: 1,

                    ErrorMessage: null));

            }

            else

            {

                // Шаг 2: Модель синтезирует отчет после получения данных инструмента

                return Task.FromResult(new GeminiTurnResponse(

                    IsSuccess: true,

                    Text: "📊 **Сводный отчет по производству**:\nВыработка составила 485 000 оттисков, процент брака 1.9% (в норме).",

                    FunctionCall: null,

                    FinishReason: "STOP",

                    PromptTokens: 90,

                    CandidatesTokens: 45,

                    TotalTokens: 135,

                    AttemptsMade: 1,

                    ErrorMessage: null));

            }

        }

        public GeminiCallStats GetStats() =>

            new(2, 2, 60, 58, 3.3, null, 0);

    }

    private class FakeFotoprintRepository : IFotoprintRepository

    {

        private readonly bool _throwOnSecurityContext;

        public FakeFotoprintRepository(bool throwOnSecurityContext = false)

        {

            _throwOnSecurityContext = throwOnSecurityContext;

        }

        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextAsync(long telegramId, CancellationToken ct = default)

        {

            if (_throwOnSecurityContext)

            {

                throw new InvalidOperationException("PostgreSQL connection refused: localhost:5432");

            }

            return Task.FromResult<EmployeeSecurityContext?>(new EmployeeSecurityContext

            {

                EmployeeId = 1,

                MasterId = 100001,

                FullName = "Алексей Смирнов",

                TelegramId = telegramId,

                Department = "Руководство",

                Position = "Генеральный директор",

                Roles = new() { "Director" },

                Permissions = new() { "onec:read_production_kpis", "onec:read_orders" }

            });

        }

        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextByMasterIdAsync(long masterId, CancellationToken ct = default) =>

            Task.FromResult<EmployeeSecurityContext?>(new EmployeeSecurityContext

            {

                EmployeeId = 1,

                MasterId = masterId,

                FullName = "Алексей Смирнов",

                TelegramId = 100001,

                Department = "Руководство",

                Position = "Генеральный директор",

                Roles = new() { "Director" },

                Permissions = new() { "onec:read_production_kpis", "onec:read_orders" }

            });

        public Task<long?> ResolveMasterIdByMessengerAsync(string messengerType, string messengerUserId, CancellationToken ct = default) =>

            Task.FromResult<long?>(100001L);

        public Task<ChatSession> GetOrCreateActiveSessionByMasterIdAsync(long masterId, long messengerChatId, string messengerType = "telegram", CancellationToken ct = default) =>

            Task.FromResult(new ChatSession { Id = Guid.NewGuid(), MasterId = masterId, IsActive = true });

        public Task<ChatSession> SwitchToNewSessionAsync(Guid currentSessionId, long masterId, long messengerChatId, string? newBranch = null, string messengerType = "telegram", CancellationToken ct = default) =>

            Task.FromResult(new ChatSession { Id = Guid.NewGuid(), MasterId = masterId, IsActive = true, CurrentBranch = newBranch ?? "general" });

        public Task<List<ChatMessage>> GetMessagesBySessionIdAsync(Guid sessionId, int limit = 20, CancellationToken ct = default) =>

            Task.FromResult(new List<ChatMessage>());

        public Task SaveMessageToSessionAsync(Guid sessionId, long masterId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default) =>

            Task.CompletedTask;

        public Task<Employee?> GetEmployeeByIdAsync(int id, CancellationToken ct = default) => Task.FromResult<Employee?>(null);

        public Task<List<Employee>> GetAllEmployeesAsync(CancellationToken ct = default) => Task.FromResult(new List<Employee>());

        public Task<ChatSession> GetOrCreateSessionAsync(long telegramChatId, long? telegramUserId = null, CancellationToken ct = default) => Task.FromResult(new ChatSession { Id = Guid.NewGuid() });

        public Task SaveMessageAsync(Guid sessionId, string contextId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<ChatMessage>> GetRecentMessagesAsync(Guid sessionId, int limit = 20, CancellationToken ct = default) => Task.FromResult(new List<ChatMessage>());

        public Task FreezeContextAsync(Guid sessionId, string contextId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<Guid> SaveDocumentAsync(string title, string sourceType, string? filePath, string? department, List<string> allowedAccessGroups, object? metadata = null, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());

        public Task SaveDocumentChunksAsync(Guid documentId, List<string> chunkTexts, List<float[]?> embeddings, List<string> allowedAccessGroups, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<CorporateDocumentChunk>> GetChunksWithoutEmbeddingAsync(int limit = 200, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());

        public Task UpdateChunkEmbeddingAsync(Guid chunkId, float[] embedding, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<CorporateDocumentChunk>> SearchSimilarChunksAsync(float[] queryEmbedding, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());

        public Task<List<CorporateDocumentChunk>> SearchMeetingSimilarChunksAsync(float[] queryEmbedding, List<string> meetingAccessGroups, int topK = 5, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());

        public Task<List<CorporateDocumentChunk>> SearchTextChunksAsync(string queryText, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());

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

    private class FakeHttpClientFactory : IHttpClientFactory

    {

        private readonly bool _simulateNetworkError;

        public FakeHttpClientFactory(bool simulateNetworkError = false)

        {

            _simulateNetworkError = simulateNetworkError;

        }

        public HttpClient CreateClient(string name)

        {

            var handler = new FakeHttpHandler(_simulateNetworkError);

            return new HttpClient(handler);

        }

        private class FakeHttpHandler : HttpMessageHandler

        {

            private readonly bool _simulateNetworkError;

            public FakeHttpHandler(bool simulateNetworkError = false)

            {

                _simulateNetworkError = simulateNetworkError;

            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)

            {

                if (_simulateNetworkError)

                {

                    throw new HttpRequestException("Connection refused to MCP service");

                }

                var kpiData = new

                {

                    department = "Офсетный цех",

                    total_impressions = 485000,

                    scrap_rate_percent = 1.9,

                    equipment_utilization_percent = 87.4

                };

                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)

                {

                    Content = new StringContent(JsonSerializer.Serialize(kpiData), System.Text.Encoding.UTF8, "application/json")

                });

            }

        }

    }

    [Fact]

    public async Task Agent_Answers_Directly_When_No_Tools_Required()

    {

        var gemini = new FakeGeminiClient(triggerTool: false);

        var repo = new FakeFotoprintRepository();

        var httpFactory = new FakeHttpClientFactory();

        var config = new FotoprintConfig();

        var agent = new FotoprintAiAgent(gemini, repo, httpFactory, config, null, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(100001, 55555, "Привет! Расскажи о себе.");

        Assert.True(res.IsSuccess);

        Assert.Equal(1, res.TotalLlmCalls);

        Assert.Empty(res.ToolsInvoked);

        Assert.Contains("корпоративный AI-ассистент", res.AnswerText);

    }

    [Fact]

    public async Task Agent_Autonomously_Decides_To_Call_MCP_Tool_And_Synthesizes_Report()

    {

        var gemini = new FakeGeminiClient(triggerTool: true);

        var repo = new FakeFotoprintRepository();

        var httpFactory = new FakeHttpClientFactory();

        var config = new FotoprintConfig();

        var agent = new FotoprintAiAgent(gemini, repo, httpFactory, config, null, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(

            telegramId: 100001,

            chatId: 55555,

            messageText: "Слушай, подготовь мне дайджест по показателям работы цехов за прошлую неделю");

        // Проверяем:

        // 1. Агент сделал ровно 2 вызова LLM (выбор инструмента + синтез ответа)

        Assert.Equal(2, res.TotalLlmCalls);

        // 2. Инструмент get_production_kpis был автономно выбран и вызван

        Assert.Contains("get_production_kpis", res.ToolsInvoked);

        // 3. Финальный ответ содержит синтезированную сводку на базе данных инструмента

        Assert.Contains("Сводный отчет по производству", res.AnswerText);

        Assert.Contains("485 000 оттисков", res.AnswerText);

        Assert.True(res.IsSuccess);

    }

    [Fact]

    public async Task Agent_Executes_Multiple_ToolCalls_In_Single_Turn()

    {

        var gemini = new BatchToolFakeGeminiClient();

        var repo = new FakeFotoprintRepository();

        var httpFactory = new FakeHttpClientFactory();

        var config = new FotoprintConfig();

        var agent = new FotoprintAiAgent(gemini, repo, httpFactory, config, null, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(100001, 55555, "Заказ 44102, задачи и встречи");

        Assert.True(res.IsSuccess);

        Assert.Equal(2, res.TotalLlmCalls);

        Assert.Equal(3, res.ToolsInvoked.Count);

        Assert.Contains("get_order_status", res.ToolsInvoked);

        Assert.Contains("get_user_tasks", res.ToolsInvoked);

        Assert.Contains("get_upcoming_meetings", res.ToolsInvoked);

        Assert.Contains("Сводка по всем 3 системам", res.AnswerText);

    }

    private class BatchToolFakeGeminiClient : IGeminiClient

    {

        private int _step;

        public Task<GeminiGenerateResponse> GenerateContentAsync(string prompt, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

            => Task.FromResult(new GeminiGenerateResponse(true, "ok", "STOP", 1, 1, 2, 1, null));

        public Task<GeminiTurnResponse> ChatTurnAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

        {

            _step++;

            if (_step == 1)

            {

                var calls = new List<GeminiFunctionCall>

                {

                    new("get_order_status", JsonSerializer.SerializeToElement(new { order_id = "44102" }), "c1"),

                    new("get_user_tasks", JsonSerializer.SerializeToElement(new { employee_id = 1 }), "c2"),

                    new("get_upcoming_meetings", JsonSerializer.SerializeToElement(new { hours_ahead = 24 }), "c3")

                };

                return Task.FromResult(new GeminiTurnResponse(true, null, calls[0], "tool_calls", 50, 20, 70, 1, null, calls));

            }

            return Task.FromResult(new GeminiTurnResponse(true, "Сводка по всем 3 системам готова.", null, "stop", 100, 30, 130, 1, null));

        }

        public GeminiCallStats GetStats() => new(1, 1, 60, 59, 1.0, DateTime.UtcNow, 0);

    }

    [Fact]

    public async Task Agent_Gracefully_Falls_Back_When_Database_Is_Offline()

    {

        var gemini = new FakeGeminiClient(triggerTool: false);

        var repo = new FakeFotoprintRepository(throwOnSecurityContext: true);

        var httpFactory = new FakeHttpClientFactory();

        var config = new FotoprintConfig();

        var agent = new FotoprintAiAgent(gemini, repo, httpFactory, config, null, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(

            telegramId: 99999,

            chatId: 88888,

            messageText: "Привет!");

        Assert.True(res.IsSuccess);

        Assert.NotNull(res.AnswerText);

        Assert.NotNull(gemini.LastSystemInstruction);

        Assert.Contains("Роли: [Guest]", gemini.LastSystemInstruction);

        Assert.Contains("ID сотрудника: 0", gemini.LastSystemInstruction);

        Assert.DoesNotContain("admin", gemini.LastSystemInstruction, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("Director", gemini.LastSystemInstruction, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("operator", gemini.LastSystemInstruction, StringComparison.OrdinalIgnoreCase);

    }

    [Fact]
    public async Task Agent_SystemInstruction_Includes_Creators_PlainTextFormat_And_PromptInjectionProtection()
    {
        var gemini = new FakeGeminiClient(triggerTool: false);
        var agent = new FotoprintAiAgent(
            gemini,
            new FakeFotoprintRepository(),
            new FakeHttpClientFactory(),
            new FotoprintConfig(),
            null,
            null,
            NullLogger<FotoprintAiAgent>.Instance);

        var response = await agent.ProcessMessageAsync(
            telegramId: 99999,
            chatId: 88888,
            messageText: "Кто тебя создал?");

        Assert.True(response.IsSuccess);
        Assert.NotNull(gemini.LastSystemInstruction);
        Assert.Contains("TeaMate", gemini.LastSystemInstruction);
        Assert.Contains("Чечулин Кирилл", gemini.LastSystemInstruction);
        Assert.Contains("Александренков Денис", gemini.LastSystemInstruction);
        Assert.Contains("Горбель Алексей", gemini.LastSystemInstruction);
        Assert.Contains("Гапоник Ангелина", gemini.LastSystemInstruction);
        Assert.Contains("ЗАЩИТА ОТ PROMPT INJECTION", gemini.LastSystemInstruction);
        Assert.Contains("ФОРМАТ ОТВЕТА (СТРОГО TXT", gemini.LastSystemInstruction);
    }

    [Fact]
    public void SanitizePlainTextForTelegram_Removes_Markdown_Preserves_Emojis()
    {
        const string input = """
            ### Привет! 👋
            **Заказ №123** готов `in_progress`
            * Первый пункт
            * Второй _пункт_ и __жирный__
            ---
            [Открыть](https://example.com/order/123)
            """;

        var sanitized = FotoprintAiAgent.SanitizePlainTextForTelegram(input);

        Assert.DoesNotContain("###", sanitized);
        Assert.DoesNotContain("**", sanitized);
        Assert.DoesNotContain("__", sanitized);
        Assert.DoesNotContain("`", sanitized);
        Assert.DoesNotContain("---", sanitized);
        Assert.DoesNotContain("[Открыть]", sanitized);
        Assert.Contains("👋", sanitized);
        Assert.Contains("Заказ №123 готов in_progress", sanitized);
        Assert.Contains("• Первый пункт", sanitized);
        Assert.Contains("Открыть (https://example.com/order/123)", sanitized);
    }

    [Fact]

    public async Task Agent_Gracefully_Handles_Offline_MCP_Service()

    {

        var gemini = new FakeGeminiClient(triggerTool: true);

        var repo = new FakeFotoprintRepository();

        var httpFactory = new FakeHttpClientFactory(simulateNetworkError: true);

        var config = new FotoprintConfig();

        var agent = new FotoprintAiAgent(gemini, repo, httpFactory, config, null, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(

            telegramId: 100001,

            chatId: 55555,

            messageText: "Покажи выработку цехов");

        Assert.True(res.IsSuccess);

        Assert.Contains("get_production_kpis", res.ToolsInvoked);

        var serializedContents = JsonSerializer.Serialize(gemini.LastContents);

        Assert.Contains("Connection refused to MCP service", serializedContents);

        Assert.DoesNotContain("mock_offline", serializedContents);

    }

    [Fact]

    public async Task Agent_Blocks_CreateTask_Without_Confirmation_And_Returns_ActionPlan()

    {

        var gemini = new CreateTaskFakeGeminiClient();

        var repo = new FakeFotoprintRepository();

        var httpFactory = new FakeHttpClientFactory();

        var config = new FotoprintConfig();

        var agent = new FotoprintAiAgent(gemini, repo, httpFactory, config, null, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(

            telegramId: 100001,

            chatId: 55555,

            messageText: "Поставь задачу Сидорову подготовить финансовый отчет до пятницы");

        Assert.True(res.IsSuccess);

        Assert.Empty(res.ToolsInvoked);

        Assert.Contains("План действий (требуется подтверждение)", res.AnswerText);

        Assert.Contains("Подготовить финансовый отчет", res.AnswerText);

        Assert.Contains("подтвердите выполнение плана", res.AnswerText);

    }

    [Fact]

    public async Task Agent_Executes_CreateTask_Only_After_User_Confirmation()

    {

        var history = new List<ChatMessage>

        {

            new() { Role = "user", Content = "Поставь задачу Сидорову подготовить финансовый отчет до пятницы" },

            new() { Role = "assistant", Content = "📋 **План действий (требуется подтверждение):**\n1. Создание новой задачи в Битрикс24\nПожалуйста, подтвердите выполнение плана (напишите «Да, подтверждаю»)." }

        };

        Assert.False(FotoprintAiAgent.IsMutatingActionConfirmed(new List<ChatMessage>(), "Поставь задачу Сидорову"));

        Assert.False(FotoprintAiAgent.IsMutatingActionConfirmed(history, "Нет, отмена"));

        Assert.False(FotoprintAiAgent.IsMutatingActionConfirmed(history, "Поменяй срок на понедельник"));

        Assert.True(FotoprintAiAgent.IsMutatingActionConfirmed(history, "Да, подтверждаю"));

        Assert.True(FotoprintAiAgent.IsMutatingActionConfirmed(history, "Выполняй"));

    }

    [Fact]

    public async Task Agent_Logs_Actual_Turn_Models_To_Langfuse()

    {

        var gemini = new HybridModelFakeGeminiClient();

        var repo = new FakeFotoprintRepository();

        var httpFactory = new FakeHttpClientFactory();

        var langfuse = new RecordingLangfuseClient();

        var config = new FotoprintConfig();

        config.Llm.Models.ChatModel = "nvidia/nemotron-3-ultra-550b-a55b";

        config.Llm.Models.FastModel = "nvidia/nemotron-3.5-bolt-e4b";

        var agent = new FotoprintAiAgent(gemini, repo, httpFactory, config, langfuse, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(100001, 55555, "Дай статус и материалы по производству");

        Assert.True(res.IsSuccess);

        Assert.Equal(2, langfuse.Generations.Count);

        Assert.Equal("nvidia/nemotron-3-ultra-550b-a55b", langfuse.Generations[0].Model);

        Assert.Equal("nvidia/nemotron-3.5-bolt-e4b", langfuse.Generations[1].Model);

    }

    private class CreateTaskFakeGeminiClient : IGeminiClient

    {

        public Task<GeminiGenerateResponse> GenerateContentAsync(string prompt, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

            => Task.FromResult(new GeminiGenerateResponse(true, "ok", "STOP", 1, 1, 2, 1, null));

        public Task<GeminiTurnResponse> ChatTurnAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

        {

            var argsJson = JsonSerializer.SerializeToElement(new

            {

                title = "Подготовить финансовый отчет",

                description = "Финансовый отчет за квартал",

                responsible_id = 4,

                deadline = "2026-11-15"

            });

            return Task.FromResult(new GeminiTurnResponse(

                IsSuccess: true,

                Text: null,

                FunctionCall: new GeminiFunctionCall("create_task", argsJson),

                FinishReason: "tool_calls",

                PromptTokens: 40,

                CandidatesTokens: 15,

                TotalTokens: 55,

                AttemptsMade: 1,

                ErrorMessage: null));

        }

        public GeminiCallStats GetStats() => new(1, 1, 60, 59, 1.0, DateTime.UtcNow, 0);

    }

    private class HybridModelFakeGeminiClient : IGeminiClient

    {

        public Task<GeminiGenerateResponse> GenerateContentAsync(string prompt, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

            => Task.FromResult(new GeminiGenerateResponse(true, "ok", "STOP", 1, 1, 2, 1, null, "nvidia/nemotron-3-ultra-550b-a55b"));

        public Task<GeminiTurnResponse> ChatTurnAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

        {

            var argsJson = JsonSerializer.SerializeToElement(new { department = "Офсетный цех" });

            return Task.FromResult(new GeminiTurnResponse(

                IsSuccess: true,

                Text: null,

                FunctionCall: new GeminiFunctionCall("get_production_kpis", argsJson, "c1"),

                FinishReason: "tool_calls",

                PromptTokens: 40,

                CandidatesTokens: 15,

                TotalTokens: 55,

                AttemptsMade: 1,

                ErrorMessage: null,

                FunctionCalls: new[] { new GeminiFunctionCall("get_production_kpis", argsJson, "c1") },

                Model: "nvidia/nemotron-3-ultra-550b-a55b"));

        }

        public Task<GeminiTurnResponse> ChatTurnNoThinkingAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

            => Task.FromResult(new GeminiTurnResponse(

                IsSuccess: true,

                Text: "Сводка готова.",

                FunctionCall: null,

                FinishReason: "stop",

                PromptTokens: 80,

                CandidatesTokens: 25,

                TotalTokens: 105,

                AttemptsMade: 1,

                ErrorMessage: null,

                Model: "nvidia/nemotron-3.5-bolt-e4b"));

        public GeminiCallStats GetStats() => new(2, 2, 60, 58, 3.3, null, 0);

    }

    private class RecordingLangfuseClient : ILangfuseClient

    {

        public List<(string Name, string Model)> Generations { get; } = new();

        public string GenerateTraceId() => Guid.NewGuid().ToString("N");

        public string GenerateObservationId() => Guid.NewGuid().ToString("N");

        public double CalculateCost(int promptTokens, int completionTokens, int cachedTokens = 0) => 0;

        public Task CreateTraceAsync(string traceId, string name, string? userId = null, string? sessionId = null, object? metadata = null, string[]? tags = null, CancellationToken ct = default) => Task.CompletedTask;

        public Task CreateSpanAsync(string traceId, string name, DateTime startTime, DateTime endTime, object? input = null, object? output = null, object? metadata = null, string? parentObservationId = null, string? spanId = null, CancellationToken ct = default) => Task.CompletedTask;

        public Task CreateGenerationAsync(string traceId, string name, string model, DateTime startTime, DateTime endTime, int promptTokens, int completionTokens, int totalTokens, int cachedTokens = 0, object? input = null, object? output = null, object? metadata = null, double? calculatedCost = null, string? parentObservationId = null, string? generationId = null, CancellationToken ct = default)

        {

            Generations.Add((name, model));

            return Task.CompletedTask;

        }

        public Task LogEventAsync(string traceId, string name, string level = "DEFAULT", string? message = null, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;

        public Task LogSecurityAlertAsync(string traceId, long telegramId, string toolOrAction, string requiredPermission, string reason, CancellationToken ct = default) => Task.CompletedTask;

        public Task LogScoreAsync(string traceId, string name, double value, string? comment = null, string? observationId = null, CancellationToken ct = default) => Task.CompletedTask;

    }

    [Fact]

    public async Task Agent_UpdatesNotificationScheduleFromChat_WithoutRequiringConfirmation()

    {

        var gemini = new UpdateNotificationsFakeGeminiClient();

        var repo = new FakeFotoprintRepository();

        var httpFactory = new FakeHttpClientFactory();

        var config = new FotoprintConfig();

        var agent = new FotoprintAiAgent(gemini, repo, httpFactory, config, null, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(

            telegramId: 100001,

            chatId: 55555,

            messageText: "Перенеси мою утреннюю сводку на 08:30 и напоминай о встречах за 15 минут");

        Assert.True(res.IsSuccess);

        Assert.Contains("update_notification_settings", res.ToolsInvoked);

        Assert.Contains("08:30", res.AnswerText);

        var serializedContents = JsonSerializer.Serialize(gemini.LastContents);

        Assert.Contains("updated", serializedContents);

        Assert.Contains("08:30", serializedContents);

        Assert.Contains("15", serializedContents);

    }

    [Fact]

    public void Every_Agent_Tool_Method_Has_RequireConfirmation_Attribute()

    {

        var toolMethods = typeof(FotoprintAiAgent)

            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)

            .Where(m => m.Name.StartsWith("Tool_", StringComparison.Ordinal))

            .ToList();

        Assert.NotEmpty(toolMethods);

        foreach (var method in toolMethods)

        {

            var attr = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<RequireConfirmationAttribute>(method);

            Assert.NotNull(attr);

        }

        Assert.False(FotoprintAiAgent.RequiresConfirmation("search_typography_regulations"));

        Assert.False(FotoprintAiAgent.RequiresConfirmation("search_meeting_rag"));

        Assert.False(FotoprintAiAgent.RequiresConfirmation("get_notification_settings"));

        Assert.False(FotoprintAiAgent.RequiresConfirmation("update_notification_settings"));

        Assert.True(FotoprintAiAgent.RequiresConfirmation("create_task"));

        Assert.True(FotoprintAiAgent.RequiresConfirmation("schedule_meeting"));

    }

    [Theory]

    [InlineData("search_typography_regulations", ":8086/tools/search_typography_regulations")]

    [InlineData("search_meeting_rag", ":8084/tools/search_meeting_rag")]

    public async Task Agent_Routes_Rag_Tools_To_Dedicated_Mcp_Servers(string toolName, string expectedUrlFragment)

    {

        var capturedUrls = new List<string>();

        var gemini = new SingleToolGeminiClient(toolName, JsonSerializer.SerializeToElement(new { query = "Heidelberg" }));

        var httpFactory = new UrlCapturingHttpClientFactory(capturedUrls, System.Net.HttpStatusCode.OK, new { status = "ok" });

        var agent = new FotoprintAiAgent(gemini, new FakeFotoprintRepository(), httpFactory, new FotoprintConfig(), null, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(100001, 55555, $"Найди информацию ({toolName})");

        Assert.True(res.IsSuccess);

        Assert.Contains(toolName, res.ToolsInvoked);

        Assert.Contains(capturedUrls, u => u.Contains(expectedUrlFragment, StringComparison.OrdinalIgnoreCase));

    }

    [Fact]

    public async Task Agent_Does_Not_Fallback_Locally_When_Mcp_Returns_403()

    {

        var capturedUrls = new List<string>();

        var gemini = new SingleToolGeminiClient("search_meeting_rag", JsonSerializer.SerializeToElement(new { query = "секретный созвон" }));

        var httpFactory = new UrlCapturingHttpClientFactory(capturedUrls, System.Net.HttpStatusCode.Forbidden, new { error = "Access Denied" });

        var agent = new FotoprintAiAgent(gemini, new FakeFotoprintRepository(), httpFactory, new FotoprintConfig(), null, null, NullLogger<FotoprintAiAgent>.Instance);

        var res = await agent.ProcessMessageAsync(100002, 55556, "Что обсуждали на секретном созвоне?");

        Assert.True(res.IsSuccess);

        Assert.Single(capturedUrls);

        Assert.Contains(":8084/tools/search_meeting_rag", capturedUrls[0]);

    }

    private class UpdateNotificationsFakeGeminiClient : IGeminiClient

    {

        public List<object>? LastContents { get; private set; }

        private int _turn;

        public Task<GeminiGenerateResponse> GenerateContentAsync(string prompt, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

            => Task.FromResult(new GeminiGenerateResponse(true, "ok", "STOP", 1, 1, 2, 1, null));

        public Task<GeminiTurnResponse> ChatTurnAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

        {

            _turn++;

            LastContents = contents.ToList();

            if (_turn == 1)

            {

                var argsJson = JsonSerializer.SerializeToElement(new

                {

                    digest_time_msk = "08:30",

                    pre_meeting_window_minutes = 15

                });

                var call = new GeminiFunctionCall("update_notification_settings", argsJson, "call_notif_1");

                return Task.FromResult(new GeminiTurnResponse(true, null, call, "tool_calls", 40, 15, 55, 1, null, new[] { call }));

            }

            return Task.FromResult(new GeminiTurnResponse(true, "Готово! Утренняя сводка перенесена на 08:30 МСК, а напоминания о встречах — за 15 минут.", null, "stop", 60, 20, 80, 1, null));

        }

        public Task<GeminiTurnResponse> ChatTurnNoThinkingAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

            => ChatTurnAsync(contents, tools, systemInstruction, temperature, maxOutputTokens, traceId, ct);

        public GeminiCallStats GetStats() => new(2, 2, 60, 58, 2.0, DateTime.UtcNow, 0);

    }

    private class SingleToolGeminiClient : IGeminiClient

    {

        private readonly string _toolName;

        private readonly JsonElement _args;

        private int _step;

        public SingleToolGeminiClient(string toolName, JsonElement args)

        {

            _toolName = toolName;

            _args = args;

        }

        public Task<GeminiGenerateResponse> GenerateContentAsync(string prompt, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

            => Task.FromResult(new GeminiGenerateResponse(true, "ok", "STOP", 1, 1, 2, 1, null));

        public Task<GeminiTurnResponse> ChatTurnAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

        {

            _step++;

            if (_step == 1)

            {

                return Task.FromResult(new GeminiTurnResponse(true, null, new GeminiFunctionCall(_toolName, _args), "tool_calls", 20, 10, 30, 1, null));

            }

            return Task.FromResult(new GeminiTurnResponse(true, "Готово.", null, "STOP", 30, 10, 40, 1, null));

        }

        public Task<GeminiTurnResponse> ChatTurnNoThinkingAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)

            => ChatTurnAsync(contents, tools, systemInstruction, temperature, maxOutputTokens, traceId, ct);

        public GeminiCallStats GetStats() => new(2, 2, 60, 58, 1.0, null, 0);

    }

    private class UrlCapturingHttpClientFactory : IHttpClientFactory

    {

        private readonly List<string> _urls;

        private readonly System.Net.HttpStatusCode _statusCode;

        private readonly object _payload;

        public UrlCapturingHttpClientFactory(List<string> urls, System.Net.HttpStatusCode statusCode, object payload)

        {

            _urls = urls;

            _statusCode = statusCode;

            _payload = payload;

        }

        public HttpClient CreateClient(string name) => new(new CapturingHandler(_urls, _statusCode, _payload));

        private class CapturingHandler : HttpMessageHandler

        {

            private readonly List<string> _urls;

            private readonly System.Net.HttpStatusCode _statusCode;

            private readonly object _payload;

            public CapturingHandler(List<string> urls, System.Net.HttpStatusCode statusCode, object payload)

            {

                _urls = urls;

                _statusCode = statusCode;

                _payload = payload;

            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)

            {

                _urls.Add(request.RequestUri?.ToString() ?? "");

                return Task.FromResult(new HttpResponseMessage(_statusCode)

                {

                    Content = new StringContent(JsonSerializer.Serialize(_payload), System.Text.Encoding.UTF8, "application/json")

                });

            }

        }

    }
}
