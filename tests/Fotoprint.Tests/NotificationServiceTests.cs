using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Models;
using Fotoprint.Core.Notifications;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telegram;
using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fotoprint.Tests;

public sealed class NotificationServiceTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class FakeTelegramBotClient : ITelegramBotClient
    {
        public bool IsConfigured { get; set; } = true;
        public List<(long ChatId, string Text, IReadOnlyList<IReadOnlyList<TelegramInlineButton>>? Keyboard, string? ParseMode)> SentMessages { get; } = [];
        public Func<long, string, TelegramApiResponse>? Responder { get; set; }

        public Task<TelegramApiResponse> SendMessageAsync(
            long chatId,
            string text,
            IReadOnlyList<IReadOnlyList<TelegramInlineButton>>? inlineKeyboard = null,
            string? parseMode = null,
            CancellationToken ct = default)
        {
            SentMessages.Add((chatId, text, inlineKeyboard, parseMode));
            if (Responder is not null)
            {
                return Task.FromResult(Responder(chatId, text));
            }

            return Task.FromResult(new TelegramApiResponse(true));
        }

        public Task<TelegramApiResponse> SendChatActionAsync(long chatId, string action = "typing", CancellationToken ct = default) =>
            Task.FromResult(new TelegramApiResponse(true));

        public Task<TelegramApiResponse> AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken ct = default) =>
            Task.FromResult(new TelegramApiResponse(true));

        public Task<TelegramApiResponse> SetWebhookAsync(string webhookUrl, string? secretToken = null, bool dropPendingUpdates = false, CancellationToken ct = default) =>
            Task.FromResult(new TelegramApiResponse(true));

        public Task<TelegramApiResponse> GetWebhookInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new TelegramApiResponse(true));
    }

    private sealed class FakeLangfuseClient : ILangfuseClient
    {
        public List<(string TraceId, string Name, string Level, string? Message)> LoggedEvents { get; } = [];

        public string GenerateTraceId() => "trace-notif-test";
        public Task CreateTraceAsync(string traceId, string name, string? userId = null, string? sessionId = null, object? metadata = null, string[]? tags = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateTraceOutputAsync(string traceId, string output, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogGenerationAsync(string traceId, string name, string model, string input, string output, int promptTokens, int completionTokens, double durationSeconds, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogSpanAsync(string traceId, string name, string? input = null, string? output = null, double durationSeconds = 0, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogEventAsync(string traceId, string name, string level = "DEFAULT", string? statusMessage = null, object? metadata = null, CancellationToken ct = default)
        {
            LoggedEvents.Add((traceId, name, level, statusMessage));
            return Task.CompletedTask;
        }
        public Task LogSecurityAlertAsync(string traceId, long telegramId, string ruleName, string category, string description, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogScoreAsync(string traceId, string name, double value, string? comment = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryNotificationRepository : IFotoprintRepository
    {
        public List<Employee> Employees { get; } = [];
        public List<Meeting> Meetings { get; } = [];
        public List<ActionItem> ActionItems { get; } = [];
        public Dictionary<string, (Guid Id, string Status, string? Error)> Deliveries { get; } = new(StringComparer.Ordinal);
        public List<(Guid SessionId, long MasterId, string Role, string Content)> SavedMessages { get; } = [];
        public Dictionary<int, EmployeeNotificationPreferences> Preferences { get; } = [];
        private readonly Dictionary<long, Guid> _activeSessions = new();

        public Task EnsureNotificationSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<EmployeeNotificationPreferences> GetNotificationPreferencesAsync(int employeeId, long masterId = 0, CancellationToken ct = default)
        {
            if (Preferences.TryGetValue(employeeId, out var p))
                return Task.FromResult(p);
            return Task.FromResult(EmployeeNotificationPreferences.CreateDefault(employeeId, masterId));
        }

        public Task<IReadOnlyDictionary<int, EmployeeNotificationPreferences>> GetAllNotificationPreferencesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<int, EmployeeNotificationPreferences>>(Preferences);

        public Task<List<Employee>> GetAllEmployeesAsync(CancellationToken ct = default) =>
            Task.FromResult(Employees.Where(e => e.IsActive).ToList());

        public Task<Employee?> GetEmployeeByIdAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Employees.FirstOrDefault(e => e.Id == id));

        public Task<Employee?> GetEmployeeByTelegramIdAsync(long telegramId, CancellationToken ct = default) =>
            Task.FromResult(Employees.FirstOrDefault(e => e.TelegramId == telegramId));

        public Task<bool> SetEmployeeNotificationsEnabledAsync(long masterId, bool enabled, CancellationToken ct = default)
        {
            var emp = Employees.FirstOrDefault(e => e.MasterId == masterId);
            if (emp is null) return Task.FromResult(false);
            emp.NotificationsEnabled = enabled;
            return Task.FromResult(true);
        }

        public Task<List<Meeting>> GetMeetingsInRangeUtcAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default) =>
            Task.FromResult(Meetings
                .Where(m => m.Status == "scheduled" && m.StartTime >= startUtc && m.StartTime <= endUtc)
                .OrderBy(m => m.StartTime)
                .ToList());

        public Task<List<ActionItem>> GetOpenActionItemsAsync(int? employeeId = null, CancellationToken ct = default) =>
            Task.FromResult(ActionItems
                .Where(a => a.ResponsibleId.HasValue
                    && (!employeeId.HasValue || a.ResponsibleId == employeeId.Value)
                    && !string.Equals(a.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(a.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(a.Status, "done", StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Deadline ?? DateTime.MaxValue)
                .ToList());

        public Task<Guid?> TryClaimNotificationDeliveryAsync(
            string dedupKey,
            string notificationType,
            int? recipientEmployeeId,
            long recipientTelegramId,
            Guid? entityId = null,
            CancellationToken ct = default)
        {
            if (Deliveries.ContainsKey(dedupKey))
            {
                return Task.FromResult<Guid?>(null);
            }

            var id = Guid.NewGuid();
            Deliveries[dedupKey] = (id, NotificationStatuses.Pending, null);
            return Task.FromResult<Guid?>(id);
        }

        public Task CompleteNotificationDeliveryAsync(Guid deliveryId, string status, string? errorMessage = null, CancellationToken ct = default)
        {
            var match = Deliveries.FirstOrDefault(kv => kv.Value.Id == deliveryId);
            if (!string.IsNullOrEmpty(match.Key))
            {
                Deliveries[match.Key] = (deliveryId, status, errorMessage);
            }
            return Task.CompletedTask;
        }

        public Task<ChatSession> GetOrCreateActiveSessionByMasterIdAsync(long masterId, long messengerChatId, string messengerType = "telegram", CancellationToken ct = default)
        {
            if (!_activeSessions.TryGetValue(masterId, out var sid))
            {
                sid = Guid.NewGuid();
                _activeSessions[masterId] = sid;
            }
            return Task.FromResult(new ChatSession { Id = sid, MasterId = masterId, MessengerChatId = messengerChatId, IsActive = true });
        }

        public Task SaveMessageToSessionAsync(Guid sessionId, long masterId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default)
        {
            SavedMessages.Add((sessionId, masterId, role, content));
            return Task.CompletedTask;
        }

        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextAsync(long telegramId, CancellationToken ct = default) => Task.FromResult<EmployeeSecurityContext?>(null);
        public Task<EmployeeSecurityContext?> GetEmployeeSecurityContextByMasterIdAsync(long masterId, CancellationToken ct = default) => Task.FromResult<EmployeeSecurityContext?>(null);
        public Task<long?> ResolveMasterIdByMessengerAsync(string messengerType, string messengerUserId, CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task<ChatSession> SwitchToNewSessionAsync(Guid currentSessionId, long masterId, long messengerChatId, string? newBranch = null, string messengerType = "telegram", CancellationToken ct = default) => Task.FromResult(new ChatSession());
        public Task<List<ChatMessage>> GetMessagesBySessionIdAsync(Guid sessionId, int limit = 20, CancellationToken ct = default) => Task.FromResult(new List<ChatMessage>());
        public Task<ChatSession> GetOrCreateSessionAsync(long telegramChatId, long? telegramUserId = null, CancellationToken ct = default) => Task.FromResult(new ChatSession());
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
    }

    private static NotificationService CreateService(
        InMemoryNotificationRepository repo,
        FakeTelegramBotClient bot,
        FakeTimeProvider clock,
        FakeLangfuseClient? langfuse = null,
        FotoprintConfig? config = null)
    {
        return new NotificationService(
            repo,
            bot,
            langfuse ?? new FakeLangfuseClient(),
            config ?? new FotoprintConfig(),
            clock,
            NullLogger<NotificationService>.Instance);
    }

    [Fact]
    public void GetDayBoundsUtc_RespectsMoscowMidnightBoundary()
    {
        var tz = NotificationTimeHelper.ResolveTimeZone("Europe/Moscow");
        // 2026-11-11 22:30 UTC == 2026-11-12 01:30 MSK
        var nowUtc = new DateTimeOffset(2026, 11, 11, 22, 30, 0, TimeSpan.Zero);

        var (startUtc, endUtc, mskDate) = NotificationTimeHelper.GetDayBoundsUtc(nowUtc, tz);

        Assert.Equal(new DateOnly(2026, 11, 12), mskDate);
        Assert.Equal(new DateTime(2026, 11, 11, 21, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 11, 12, 21, 0, 0, DateTimeKind.Utc), endUtc);
    }

    [Fact]
    public async Task DispatchPreMeetingRemindersAsync_DeduplicatesAndPersistsToSession()
    {
        // 08:40 MSK == 05:40 UTC
        var nowUtc = new DateTimeOffset(2026, 11, 12, 5, 40, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(nowUtc);
        var repo = new InMemoryNotificationRepository();
        var emp1 = new Employee { Id = 1, MasterId = 100001, FirstName = "Алексей", LastName = "Смирнов", TelegramId = 100001, IsActive = true, NotificationsEnabled = true };
        var emp5 = new Employee { Id = 5, MasterId = 100005, FirstName = "Денис", LastName = "Александренков", TelegramId = 1980443994, IsActive = true, NotificationsEnabled = true };
        repo.Employees.AddRange([emp1, emp5]);

        var meetingId = Guid.NewGuid();
        repo.Meetings.Add(new Meeting
        {
            Id = meetingId,
            Title = "Оперативка по печати каталогов",
            Description = "Проверка бумаги и сроков",
            StartTime = nowUtc.UtcDateTime.AddMinutes(20),
            EndTime = nowUtc.UtcDateTime.AddMinutes(50),
            Status = "scheduled",
            OrganizerId = 1,
            ParticipantIds = [1, 5]
        });

        repo.ActionItems.Add(new ActionItem
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            Title = "Проверить остаток краски CMYK",
            ResponsibleId = 5,
            Deadline = nowUtc.UtcDateTime.AddHours(3),
            Status = "pending"
        });

        var bot = new FakeTelegramBotClient();
        var service = CreateService(repo, bot, clock);

        var firstRun = await service.DispatchPreMeetingRemindersAsync(new DispatchPreMeetingCommand());
        Assert.Equal(2, firstRun.SentCount);
        Assert.Equal(0, firstRun.SkippedDuplicateCount);
        Assert.Equal(2, bot.SentMessages.Count);

        var denisMsg = bot.SentMessages.Single(m => m.ChatId == 1980443994).Text;
        Assert.Contains("09:00", denisMsg); // 06:00 UTC == 09:00 MSK
        Assert.Contains("Проверить остаток краски CMYK", denisMsg);
        Assert.Contains(repo.SavedMessages, m => m.MasterId == 100005 && m.Role == "assistant" && m.Content.Contains("Оперативка"));

        // Second cron tick 5 minutes later must deduplicate cleanly
        clock.Advance(TimeSpan.FromMinutes(5));
        var secondRun = await service.DispatchPreMeetingRemindersAsync(new DispatchPreMeetingCommand());
        Assert.Equal(0, secondRun.SentCount);
        Assert.Equal(2, secondRun.SkippedDuplicateCount);
        Assert.Equal(2, bot.SentMessages.Count);
    }

    [Fact]
    public async Task DispatchTaskDeadlineRemindersAsync_ClassifiesTiersAndRespectsDailyOverdueDedup()
    {
        var nowUtc = new DateTimeOffset(2026, 11, 12, 7, 0, 0, TimeSpan.Zero); // 10:00 MSK
        var clock = new FakeTimeProvider(nowUtc);
        var repo = new InMemoryNotificationRepository();
        repo.Employees.Add(new Employee { Id = 5, MasterId = 100005, FirstName = "Денис", LastName = "Александренков", TelegramId = 1980443994, IsActive = true, NotificationsEnabled = true });

        var urgentTask = new ActionItem { Id = Guid.NewGuid(), Title = "Сдать макет в цех", ResponsibleId = 5, Deadline = nowUtc.UtcDateTime.AddMinutes(90), Status = "pending" };
        var soonTask = new ActionItem { Id = Guid.NewGuid(), Title = "Подписать акт сверки", ResponsibleId = 5, Deadline = nowUtc.UtcDateTime.AddHours(12), Status = "confirmed" };
        var overdueTask = new ActionItem { Id = Guid.NewGuid(), Title = "Оплатить валы Heidelberg", ResponsibleId = 5, Deadline = nowUtc.UtcDateTime.AddHours(-3), Status = "pending" };
        var cancelledTask = new ActionItem { Id = Guid.NewGuid(), Title = "Отмененная задача", ResponsibleId = 5, Deadline = nowUtc.UtcDateTime.AddHours(-1), Status = "cancelled" };
        var farTask = new ActionItem { Id = Guid.NewGuid(), Title = "Квартальный план", ResponsibleId = 5, Deadline = nowUtc.UtcDateTime.AddDays(5), Status = "pending" };
        repo.ActionItems.AddRange([urgentTask, soonTask, overdueTask, cancelledTask, farTask]);

        var bot = new FakeTelegramBotClient();
        var service = CreateService(repo, bot, clock);

        var run1 = await service.DispatchTaskDeadlineRemindersAsync(new DispatchTaskDeadlinesCommand());
        Assert.Equal(3, run1.SentCount);
        Assert.Equal(3, bot.SentMessages.Count);
        Assert.DoesNotContain(bot.SentMessages, m => m.Text.Contains("Отмененная") || m.Text.Contains("Квартальный"));

        // Same day 1 hour later: all 3 tasks remain in the same tiers and stay deduplicated
        clock.Advance(TimeSpan.FromHours(1));
        var run2 = await service.DispatchTaskDeadlineRemindersAsync(new DispatchTaskDeadlinesCommand());
        Assert.Equal(0, run2.SentCount);
        Assert.Equal(3, run2.SkippedDuplicateCount);

        // Once urgentTask crosses its deadline (+1h more => -30m past deadline), it escalates once to Overdue
        clock.Advance(TimeSpan.FromHours(1));
        var runEscalated = await service.DispatchTaskDeadlineRemindersAsync(new DispatchTaskDeadlinesCommand());
        Assert.Equal(1, runEscalated.SentCount);
        Assert.Contains(runEscalated.Results, r => r.EntityId == urgentTask.Id && r.Status == NotificationStatuses.Sent);

        // Next MSK day: overdue reminder fires once again
        clock.Advance(TimeSpan.FromHours(24));
        var run3 = await service.DispatchTaskDeadlineRemindersAsync(new DispatchTaskDeadlinesCommand());
        Assert.Contains(run3.Results, r => r.EntityId == overdueTask.Id && r.Status == NotificationStatuses.Sent);
    }

    [Fact]
    public async Task DispatchMorningDigestAsync_RespectsOptOutAndHandlesTelegram403WithoutBreakingBatch()
    {
        var nowUtc = new DateTimeOffset(2026, 11, 12, 6, 0, 0, TimeSpan.Zero); // 09:00 MSK
        var clock = new FakeTimeProvider(nowUtc);
        var repo = new InMemoryNotificationRepository();
        repo.Employees.AddRange([
            new Employee { Id = 1, MasterId = 100001, FirstName = "Алексей", LastName = "Смирнов", TelegramId = 100001, IsActive = true, NotificationsEnabled = true }, // will return 403
            new Employee { Id = 2, MasterId = 100002, FirstName = "Иван", LastName = "Кузнецов", TelegramId = 100002, IsActive = true, NotificationsEnabled = false }, // opted out
            new Employee { Id = 5, MasterId = 100005, FirstName = "Денис", LastName = "Александренков", TelegramId = 1980443994, IsActive = true, NotificationsEnabled = true } // succeeds
        ]);

        repo.Meetings.Add(new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "Утренний разбор тиражей",
            StartTime = nowUtc.UtcDateTime.AddHours(2),
            EndTime = nowUtc.UtcDateTime.AddHours(3),
            Status = "scheduled",
            OrganizerId = 5,
            ParticipantIds = [1, 5]
        });

        var bot = new FakeTelegramBotClient
        {
            Responder = (chatId, _) => chatId == 100001
                ? new TelegramApiResponse(false, ErrorCode: 403, Description: "Forbidden: bot was blocked by the user")
                : new TelegramApiResponse(true)
        };

        var service = CreateService(repo, bot, clock);
        var result = await service.DispatchMorningDigestAsync(new DispatchMorningDigestCommand());

        Assert.Equal(1, result.SentCount);
        Assert.Equal(1, result.SkippedDisabledCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(bot.SentMessages, m => m.ChatId == 1980443994);
        Assert.DoesNotContain(bot.SentMessages, m => m.ChatId == 100002);

        var failedKey = NotificationTimeHelper.BuildMorningDigestDedupKey(new DateOnly(2026, 11, 12), 1);
        Assert.Equal(NotificationStatuses.Failed, repo.Deliveries[failedKey].Status);
    }

    [Fact]
    public async Task SendTargetedNotificationAsync_SupportsDryRunWithoutClaimingOrSending()
    {
        var nowUtc = new DateTimeOffset(2026, 11, 12, 6, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(nowUtc);
        var repo = new InMemoryNotificationRepository();
        repo.Employees.Add(new Employee { Id = 5, MasterId = 100005, FirstName = "Денис", LastName = "Александренков", TelegramId = 1980443994, IsActive = true, NotificationsEnabled = true });

        var bot = new FakeTelegramBotClient();
        var service = CreateService(repo, bot, clock);

        var batch = await service.SendTargetedNotificationAsync(new SendNotificationCommand(
            EmployeeId: 5,
            Text: "Тестовое напоминание",
            NotificationType: "manual",
            DedupKey: "manual-dry-run-1",
            DryRun: true));

        Assert.True(batch.Ok);
        Assert.Equal(1, batch.DryRunCount);
        Assert.Equal(0, batch.SentCount);
        Assert.Empty(bot.SentMessages);
        Assert.Empty(repo.Deliveries);
    }

    [Fact]
    public async Task DispatchPreMeetingRemindersAsync_RenotifiesWhenMeetingIsRescheduled()
    {
        var nowUtc = new DateTimeOffset(2026, 11, 12, 5, 40, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(nowUtc);
        var repo = new InMemoryNotificationRepository();
        repo.Employees.Add(new Employee { Id = 5, MasterId = 100005, FirstName = "Денис", LastName = "Александренков", TelegramId = 1980443994, IsActive = true, NotificationsEnabled = true });

        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "Синхронизация по заказам",
            StartTime = nowUtc.UtcDateTime.AddMinutes(20),
            Status = "scheduled",
            OrganizerId = 5,
            ParticipantIds = [5]
        };
        repo.Meetings.Add(meeting);

        var bot = new FakeTelegramBotClient();
        var service = CreateService(repo, bot, clock);

        var first = await service.DispatchPreMeetingRemindersAsync(new DispatchPreMeetingCommand());
        Assert.Equal(1, first.SentCount);

        // Reschedule meeting 2 hours later
        meeting.StartTime = nowUtc.UtcDateTime.AddHours(2).AddMinutes(20);
        clock.Advance(TimeSpan.FromHours(2));

        var second = await service.DispatchPreMeetingRemindersAsync(new DispatchPreMeetingCommand());
        Assert.Equal(1, second.SentCount);
        Assert.Equal(2, bot.SentMessages.Count);
    }

    [Fact]
    public async Task DispatchTaskDeadlineRemindersAsync_SkipsStaleOverdueAndCapsFloodPerRun()
    {
        var nowUtc = new DateTimeOffset(2026, 11, 12, 7, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(nowUtc);
        var repo = new InMemoryNotificationRepository();
        repo.Employees.Add(new Employee { Id = 5, MasterId = 100005, FirstName = "Денис", LastName = "Александренков", TelegramId = 1980443994, IsActive = true, NotificationsEnabled = true });

        // Stale ancient overdue (> 30 days) -> must be ignored
        repo.ActionItems.Add(new ActionItem
        {
            Id = Guid.NewGuid(),
            Title = "Древняя задача из прошлого квартала",
            ResponsibleId = 5,
            Deadline = nowUtc.UtcDateTime.AddDays(-45),
            Status = "pending"
        });

        // 7 recent overdue/urgent tasks -> capped at 3 per employee by config
        for (int i = 0; i < 7; i++)
        {
            repo.ActionItems.Add(new ActionItem
            {
                Id = Guid.NewGuid(),
                Title = $"Актуальная задача #{i + 1}",
                ResponsibleId = 5,
                Deadline = nowUtc.UtcDateTime.AddMinutes(-(i + 1) * 30),
                Status = "pending"
            });
        }

        var urgentTask = new ActionItem
        {
            Id = Guid.NewGuid(),
            Title = "Срочный макет",
            ResponsibleId = 5,
            Deadline = nowUtc.UtcDateTime.AddMinutes(45),
            Status = "pending"
        };
        repo.ActionItems.Add(urgentTask);

        var config = new FotoprintConfig
        {
            Notifications = new NotificationsConfig
            {
                TaskMaxOverdueDays = 30,
                MaxTaskAlertsPerRunPerEmployee = 3
            }
        };

        var bot = new FakeTelegramBotClient();
        var service = CreateService(repo, bot, clock, config: config);

        var run = await service.DispatchTaskDeadlineRemindersAsync(new DispatchTaskDeadlinesCommand());
        Assert.Equal(3, run.SentCount);
        Assert.Equal(3, bot.SentMessages.Count);
        Assert.DoesNotContain(bot.SentMessages, m => m.Text.Contains("Древняя задача"));
        Assert.Contains(bot.SentMessages, m => m.Text.Contains("Срочный макет"));
    }

    [Fact]
    public async Task SendTargetedNotificationAsync_EmptyTextReturnsValidationError()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var repo = new InMemoryNotificationRepository();
        var bot = new FakeTelegramBotClient();
        var service = CreateService(repo, bot, clock);

        var res = await service.SendTargetedNotificationAsync(new SendNotificationCommand(TelegramId: 1980443994, Text: "   "));
        Assert.False(res.Ok);
        Assert.Equal(NotificationStatuses.ValidationError, res.Results[0].Status);
    }

    [Fact]
    public void TimeHelper_ParsesTimes_And_EvaluatesOvernightQuietHours()
    {
        Assert.True(NotificationTimeHelper.TryParseTimeMsk("8:30", out var t1));
        Assert.Equal(new TimeOnly(8, 30), t1);
        Assert.Equal("08:30", NotificationTimeHelper.NormalizeTimeMsk("8.30"));

        var tz = NotificationTimeHelper.ResolveTimeZone("Europe/Moscow");
        // 23:15 MSK (20:15 UTC) is inside 22:00-08:00 quiet hours
        var nightUtc = new DateTimeOffset(2026, 11, 12, 20, 15, 0, TimeSpan.Zero);
        Assert.True(NotificationTimeHelper.IsQuietHoursActive(nightUtc, tz, "22:00", "08:00"));

        // 07:45 MSK (04:45 UTC) is still inside 22:00-08:00 quiet hours
        var earlyMorningUtc = new DateTimeOffset(2026, 11, 12, 4, 45, 0, TimeSpan.Zero);
        Assert.True(NotificationTimeHelper.IsQuietHoursActive(earlyMorningUtc, tz, "22:00", "08:00"));

        // 08:30 MSK (05:30 UTC) is outside 22:00-08:00 quiet hours
        var workMorningUtc = new DateTimeOffset(2026, 11, 12, 5, 30, 0, TimeSpan.Zero);
        Assert.False(NotificationTimeHelper.IsQuietHoursActive(workMorningUtc, tz, "22:00", "08:00"));
    }

    [Fact]
    public async Task DispatchMorningDigestAsync_EnforceScheduleTime_SendsOnlyToEmployeesWhoseCustomTimeIsDue()
    {
        // Thursday 2026-11-12 05:32 UTC == 08:32 MSK
        var nowUtc = new DateTimeOffset(2026, 11, 12, 5, 32, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(nowUtc);
        var repo = new InMemoryNotificationRepository();

        repo.Employees.AddRange([
            new Employee { Id = 5, MasterId = 100005, FirstName = "Денис", LastName = "Александренков", TelegramId = 1980443994, IsActive = true, NotificationsEnabled = true },
            new Employee { Id = 6, MasterId = 100006, FirstName = "Кирилл", LastName = "Инженер", TelegramId = 2135203645, IsActive = true, NotificationsEnabled = true }
        ]);

        // Employee 5 shifted digest to 08:30 MSK; Employee 6 shifted digest to 10:30 MSK
        repo.Preferences[5] = EmployeeNotificationPreferences.CreateDefault(5, 100005) with { DigestTimeMsk = "08:30" };
        repo.Preferences[6] = EmployeeNotificationPreferences.CreateDefault(6, 100006) with { DigestTimeMsk = "10:30" };

        repo.Meetings.Add(new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "Планерка цеха",
            StartTime = nowUtc.UtcDateTime.AddHours(3),
            EndTime = nowUtc.UtcDateTime.AddHours(4),
            Status = "scheduled",
            OrganizerId = 5,
            ParticipantIds = [5, 6]
        });

        var bot = new FakeTelegramBotClient();
        var service = CreateService(repo, bot, clock);

        // At 08:32 MSK: Employee 5 receives digest, Employee 6 is skipped (not due yet)
        var batchAt0832 = await service.DispatchMorningDigestAsync(new DispatchMorningDigestCommand(EnforceScheduleTime: true));
        Assert.Equal(1, batchAt0832.SentCount);
        Assert.Single(bot.SentMessages, m => m.ChatId == 1980443994);
        Assert.Contains(batchAt0832.Results, r => r.EmployeeId == 6 && r.Status == NotificationStatuses.SkippedScheduleTime);

        // Advance 2 hours to 10:32 MSK (07:32 UTC): Employee 6 receives digest, Employee 5 is deduplicated
        clock.Advance(TimeSpan.FromHours(2));
        var batchAt1032 = await service.DispatchMorningDigestAsync(new DispatchMorningDigestCommand(EnforceScheduleTime: true));
        Assert.Equal(1, batchAt1032.SentCount);
        Assert.Single(bot.SentMessages, m => m.ChatId == 2135203645);
    }

    [Fact]
    public async Task DispatchPreMeetingRemindersAsync_RespectsPerEmployeeCustomWindowAndQuietHours()
    {
        var nowUtc = new DateTimeOffset(2026, 11, 12, 7, 0, 0, TimeSpan.Zero); // 10:00 MSK
        var clock = new FakeTimeProvider(nowUtc);
        var repo = new InMemoryNotificationRepository();

        repo.Employees.AddRange([
            new Employee { Id = 5, MasterId = 100005, FirstName = "Денис", LastName = "Александренков", TelegramId = 1980443994, IsActive = true, NotificationsEnabled = true },
            new Employee { Id = 6, MasterId = 100006, FirstName = "Кирилл", LastName = "Инженер", TelegramId = 2135203645, IsActive = true, NotificationsEnabled = true }
        ]);

        // Employee 5 wants reminders 15 min before meeting; Employee 6 wants 45 min before meeting
        repo.Preferences[5] = EmployeeNotificationPreferences.CreateDefault(5, 100005) with { PreMeetingWindowMinutes = 15 };
        repo.Preferences[6] = EmployeeNotificationPreferences.CreateDefault(6, 100006) with { PreMeetingWindowMinutes = 45 };

        // Meeting starts in 30 minutes
        repo.Meetings.Add(new Meeting
        {
            Id = Guid.NewGuid(),
            Title = "Техсовет по Heidelberg",
            StartTime = nowUtc.UtcDateTime.AddMinutes(30),
            EndTime = nowUtc.UtcDateTime.AddMinutes(60),
            Status = "scheduled",
            OrganizerId = 5,
            ParticipantIds = [5, 6]
        });

        var bot = new FakeTelegramBotClient();
        var service = CreateService(repo, bot, clock);

        // At T-30m: only Employee 6 (45m window) gets notified; Employee 5 (15m window) waits
        var runAtMinus30 = await service.DispatchPreMeetingRemindersAsync(new DispatchPreMeetingCommand());
        Assert.Equal(1, runAtMinus30.SentCount);
        Assert.Single(bot.SentMessages, m => m.ChatId == 2135203645);

        // Advance 20 minutes to T-10m: now Employee 5 (15m window) gets notified, Employee 6 is deduplicated
        clock.Advance(TimeSpan.FromMinutes(20));
        var runAtMinus10 = await service.DispatchPreMeetingRemindersAsync(new DispatchPreMeetingCommand());
        Assert.Equal(1, runAtMinus10.SentCount);
        Assert.Single(bot.SentMessages, m => m.ChatId == 1980443994);
    }
}
