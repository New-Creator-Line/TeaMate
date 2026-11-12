using System.Data;
using System.Text.Json;
using Dapper;
using Fotoprint.Core.Models;
using Fotoprint.Core.Security;
using Npgsql;
using Pgvector;
using Pgvector.Dapper;

namespace Fotoprint.Core.Data;

public record TimeSlot
{
    public DateTime StartTimeUtc { get; init; }
    public DateTime EndTimeUtc { get; init; }
    public string Label { get; init; } = string.Empty;
}

public class User : Employee
{
    public static User FromEmployee(Employee emp)
    {
        return new User
        {
            Id = emp.Id,
            MasterId = emp.MasterId,
            FirstName = emp.FirstName,
            LastName = emp.LastName,
            MiddleName = emp.MiddleName,
            Email = emp.Email,
            TelegramId = emp.TelegramId,
            TelegramUsername = emp.TelegramUsername,
            WhatsappId = emp.WhatsappId,
            Department = emp.Department,
            Position = emp.Position,
            AccessGroups = emp.AccessGroups,
            NotificationsEnabled = emp.NotificationsEnabled,
            IsActive = emp.IsActive,
            CreatedAt = emp.CreatedAt,
            UpdatedAt = emp.UpdatedAt
        };
    }

    public static string FormatShortName(string? lastName, string? firstName, string? middleName)
    {
        var l = (lastName ?? string.Empty).Trim();
        var f = string.IsNullOrWhiteSpace(firstName) ? string.Empty : $"{char.ToUpper(firstName.Trim()[0])}.";
        var m = string.IsNullOrWhiteSpace(middleName) ? string.Empty : $"{char.ToUpper(middleName.Trim()[0])}.";
        var initials = $"{f}{m}".Trim();

        if (string.IsNullOrWhiteSpace(l))
        {
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                var fName = firstName.Trim();
                return !string.IsNullOrWhiteSpace(m) ? $"{fName} {m}" : fName;
            }
            return !string.IsNullOrWhiteSpace(initials) ? initials : string.Empty;
        }

        return !string.IsNullOrWhiteSpace(initials) ? $"{l} {initials}" : l;
    }

    public static string NormalizeYo(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s
            .Replace('ё', 'е')
            .Replace('Ё', 'е')
            .Replace('\u00eb', 'е')
            .Replace('\u00cb', 'е')
            .Replace("\u0308", string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    public string ShortName => FormatShortName(LastName, FirstName, MiddleName);
}

public interface IFotoprintRepository
{
    // Employee & RBAC
    Task<EmployeeSecurityContext> EnsureTelegramDirectorAsync(long telegramId, string? username = null, string? firstName = null, string? lastName = null, CancellationToken ct = default)
        => Task.FromResult(new EmployeeSecurityContext
        {
            EmployeeId = (int)(Math.Abs(telegramId) % 100000),
            MasterId = Math.Abs(telegramId),
            TelegramId = telegramId,
            TelegramUsername = username,
            FullName = $"{firstName} {lastName}".Trim(),
            Department = "Руководство",
            Position = "Генеральный директор",
            Roles = new() { "Director" },
            AccessGroups = new() { "all", "c-level", "production", "sales", "accounting" }
        });
    Task<EmployeeSecurityContext?> GetEmployeeSecurityContextAsync(long telegramId, CancellationToken ct = default);
    Task<EmployeeSecurityContext?> GetEmployeeSecurityContextByMasterIdAsync(long masterId, CancellationToken ct = default);
    Task<long?> ResolveMasterIdByMessengerAsync(string messengerType, string messengerUserId, CancellationToken ct = default);
    Task<Employee?> GetEmployeeByIdAsync(int id, CancellationToken ct = default);
    Task<List<Employee>> GetAllEmployeesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<User>> SearchEmployeesAsync(string query, int limit = 10, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());

    // Chat Sessions & Messages (MasterId & UUID SessionId)
    Task<ChatSession> GetOrCreateActiveSessionByMasterIdAsync(long masterId, long messengerChatId, string messengerType = "telegram", CancellationToken ct = default);
    Task<ChatSession> SwitchToNewSessionAsync(Guid currentSessionId, long masterId, long messengerChatId, string? newBranch = null, string messengerType = "telegram", CancellationToken ct = default);
    Task<List<ChatMessage>> GetMessagesBySessionIdAsync(Guid sessionId, int limit = 20, CancellationToken ct = default);
    Task SaveMessageToSessionAsync(Guid sessionId, long masterId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default);
    Task<ChatSession> GetOrCreateSessionAsync(long telegramChatId, long? telegramUserId = null, CancellationToken ct = default);
    Task SaveMessageAsync(Guid sessionId, string contextId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default);
    Task<List<ChatMessage>> GetRecentMessagesAsync(Guid sessionId, int limit = 20, CancellationToken ct = default);
    Task FreezeContextAsync(Guid sessionId, string contextId, CancellationToken ct = default);

    // Corporate Documents & Chunks (RAG)
    Task<Guid> SaveDocumentAsync(string title, string sourceType, string? filePath, string? department, List<string> allowedAccessGroups, object? metadata = null, CancellationToken ct = default);
    Task SaveDocumentChunksAsync(Guid documentId, List<string> chunkTexts, List<float[]?> embeddings, List<string> allowedAccessGroups, CancellationToken ct = default);
    Task<List<CorporateDocumentChunk>> GetChunksWithoutEmbeddingAsync(int limit = 200, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());
    Task UpdateChunkEmbeddingAsync(Guid chunkId, float[] embedding, CancellationToken ct = default) => Task.CompletedTask;
    Task<List<CorporateDocumentChunk>> SearchSimilarChunksAsync(float[] queryEmbedding, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default);
    Task<List<CorporateDocumentChunk>> SearchTextChunksAsync(string queryText, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default);

    // Meeting Chunks (Isolated Meeting RAG)
    Task SaveMeetingChunksAsync(Guid meetingId, Guid? protocolId, List<string> chunkTexts, List<float[]?> embeddings, List<string> allowedAccessGroups, object? metadata = null, CancellationToken ct = default) => Task.CompletedTask;
    Task<List<CorporateDocumentChunk>> GetMeetingChunksWithoutEmbeddingAsync(int limit = 200, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());
    Task UpdateMeetingChunkEmbeddingAsync(Guid chunkId, float[] embedding, CancellationToken ct = default) => Task.CompletedTask;
    Task<List<CorporateDocumentChunk>> SearchMeetingSimilarChunksAsync(float[] queryEmbedding, List<string> meetingAccessGroups, int topK = 5, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());
    Task<List<CorporateDocumentChunk>> SearchMeetingSimilarChunksAsync(float[] queryEmbedding, List<string> meetingAccessGroups, DateOnly? targetDate, int topK = 5, CancellationToken ct = default)
        => SearchMeetingSimilarChunksAsync(queryEmbedding, meetingAccessGroups, topK, ct);
    Task<List<CorporateDocumentChunk>> SearchMeetingTextChunksAsync(string queryText, List<string> meetingAccessGroups, int topK = 5, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());
    Task<List<CorporateDocumentChunk>> SearchMeetingTextChunksAsync(string queryText, List<string> meetingAccessGroups, DateOnly? targetDate, int topK = 5, CancellationToken ct = default)
        => SearchMeetingTextChunksAsync(queryText, meetingAccessGroups, topK, ct);
    Task<List<CorporateDocumentChunk>> SearchMeetingsByFilterAsync(List<string> meetingAccessGroups, DateOnly? targetDate = null, int topK = 5, CancellationToken ct = default) => Task.FromResult(new List<CorporateDocumentChunk>());

    // Meetings, Notes, Protocols, Action Items & Calendar
    Task<Guid> CreateMeetingAsync(
        string title,
        string? description,
        DateTime startTime,
        DateTime? endTime = null,
        string status = "completed",
        int? organizerId = null,
        List<int>? participantIds = null,
        object? metadata = null,
        CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
    Task<List<Meeting>> GetUpcomingMeetingsAsync(int timeWindowMinutes = 30, CancellationToken ct = default);
    Task<List<Meeting>> GetMeetingsForRangeAsync(DateTime fromUtc, DateTime toUtc, int? employeeId = null, CancellationToken ct = default) => Task.FromResult(new List<Meeting>());
    Task<List<ActionItem>> GetActionItemsForRangeAsync(DateTime fromUtc, DateTime toUtc, int? employeeId = null, CancellationToken ct = default) => Task.FromResult(new List<ActionItem>());
    Task<List<TimeSlot>> FindFreeSlotsAsync(
        int organizerId,
        List<int> participantIds,
        DateOnly targetDate,
        int durationMinutes,
        TimeOnly? workingStart = null,
        TimeOnly? workingEnd = null,
        int bufferMinutes = 10,
        CancellationToken ct = default) => Task.FromResult(new List<TimeSlot>());
    Task<Meeting?> GetMeetingByIdAsync(Guid meetingId, CancellationToken ct = default);
    Task<List<MeetingNote>> GetMeetingNotesAsync(Guid meetingId, CancellationToken ct = default);
    Task SaveMeetingNoteAsync(Guid meetingId, int? authorId, string noteText, string category = "general", CancellationToken ct = default);
    Task<Guid> SaveMeetingProtocolAsync(Guid meetingId, string title, string summary, List<string> decisions, string? rawTranscript = null, string? docxPath = null, string? pdfPath = null, CancellationToken ct = default);
    Task<Guid> SaveMeetingProtocolFullAsync(
        Guid meetingId,
        string title,
        string summary,
        List<string> decisions,
        List<string>? proposed,
        List<string>? discussed,
        List<string>? risks,
        List<string>? requiresClarification,
        string status = "draft",
        string? rawTranscript = null,
        string? docxPath = null,
        string? pdfPath = null,
        CancellationToken ct = default)
        => SaveMeetingProtocolAsync(meetingId, title, summary, decisions, rawTranscript, docxPath, pdfPath, ct);
    Task<MeetingProtocol?> GetMeetingProtocolAsync(Guid meetingId, CancellationToken ct = default);
    Task UpdateMeetingProtocolDraftAsync(
        Guid meetingId,
        string title,
        string summary,
        List<string> decisions,
        List<string> proposed,
        List<string> discussed,
        List<string> risks,
        List<string> requiresClarification,
        CancellationToken ct = default)
        => Task.CompletedTask;
    Task FinalizeMeetingProtocolAsync(Guid meetingId, CancellationToken ct = default)
        => Task.CompletedTask;
    Task<Guid> CreateActionItemAsync(Guid meetingId, Guid? protocolId, string title, string? description, int? responsibleId, DateTime? deadline, CancellationToken ct = default);
    Task<Guid> CreateActionItemWithQuoteAsync(Guid meetingId, Guid? protocolId, string title, string? description, int? responsibleId, DateTime? deadline, string? sourceQuote, string status = "pending", CancellationToken ct = default)
        => CreateActionItemAsync(meetingId, protocolId, title, description, responsibleId, deadline, ct);
    Task<List<ActionItem>> GetOrphanActionItemsAsync(CancellationToken ct = default)
        => Task.FromResult(new List<ActionItem>());
    Task<int> PurgeTestDataAsync(long? telegramId = null, CancellationToken ct = default)
        => Task.FromResult(0);
    Task<List<ActionItem>> GetActionItemsByMeetingAsync(Guid meetingId, CancellationToken ct = default);
    Task<List<ActionItem>> GetActionItemsByResponsibleAsync(int employeeId, string? statusFilter = null, CancellationToken ct = default);
    Task UpdateActionItemStatusAsync(Guid actionItemId, string status, int? bitrix24TaskId = null, CancellationToken ct = default);

    // Проактивные уведомления и настройки каналов (Multi-Channel Notifications)
    Task EnsureNotificationSchemaAsync(CancellationToken ct = default) => Task.CompletedTask;
    Task<Employee?> GetEmployeeByTelegramIdAsync(long telegramId, CancellationToken ct = default) => Task.FromResult<Employee?>(null);
    Task<bool> SetEmployeeNotificationsEnabledAsync(long masterId, bool enabled, CancellationToken ct = default) => Task.FromResult(true);
    Task<List<Meeting>> GetMeetingsInRangeUtcAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default) => Task.FromResult(new List<Meeting>());
    Task<List<ActionItem>> GetOpenActionItemsAsync(int? employeeId = null, CancellationToken ct = default) => Task.FromResult(new List<ActionItem>());
    Task<Guid?> TryClaimNotificationDeliveryAsync(
        string dedupKey,
        string notificationType,
        int? recipientEmployeeId,
        long recipientTelegramId,
        Guid? entityId = null,
        CancellationToken ct = default) => Task.FromResult<Guid?>(Guid.NewGuid());

    Task<Guid?> TryClaimNotificationDeliveryAsync(
        string dedupKey,
        string notificationType,
        int? recipientEmployeeId,
        long recipientTelegramId,
        Guid? entityId,
        CancellationToken ct,
        string channelType,
        string? recipientAddress) =>
        TryClaimNotificationDeliveryAsync(dedupKey, notificationType, recipientEmployeeId, recipientTelegramId, entityId, ct);
    Task CompleteNotificationDeliveryAsync(
        Guid deliveryId,
        string status,
        string? errorMessage = null,
        CancellationToken ct = default) => Task.CompletedTask;

    Task<EmployeeNotificationPreferences> GetNotificationPreferencesAsync(
        int employeeId,
        long masterId = 0,
        CancellationToken ct = default) =>
        Task.FromResult(EmployeeNotificationPreferences.CreateDefault(employeeId, masterId));

    Task<IReadOnlyDictionary<int, EmployeeNotificationPreferences>> GetAllNotificationPreferencesAsync(
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<int, EmployeeNotificationPreferences>>(
            new Dictionary<int, EmployeeNotificationPreferences>());

    Task<EmployeeNotificationPreferences> UpsertNotificationPreferencesAsync(
        int employeeId,
        long masterId,
        UpdateNotificationPreferencesPatch patch,
        CancellationToken ct = default)
    {
        var defaults = EmployeeNotificationPreferences.CreateDefault(employeeId, masterId);
        return Task.FromResult(defaults with
        {
            DigestEnabled = patch.DigestEnabled ?? defaults.DigestEnabled,
            DigestTimeMsk = !string.IsNullOrWhiteSpace(patch.DigestTimeMsk)
                ? Notifications.NotificationTimeHelper.NormalizeTimeMsk(patch.DigestTimeMsk, defaults.DigestTimeMsk)
                : defaults.DigestTimeMsk,
            DigestWeekdaysOnly = patch.DigestWeekdaysOnly ?? defaults.DigestWeekdaysOnly,
            PreMeetingEnabled = patch.PreMeetingEnabled ?? defaults.PreMeetingEnabled,
            PreMeetingWindowMinutes = patch.PreMeetingWindowMinutes ?? defaults.PreMeetingWindowMinutes,
            TaskDeadlinesEnabled = patch.TaskDeadlinesEnabled ?? defaults.TaskDeadlinesEnabled,
            TaskDueSoonHours = patch.TaskDueSoonHours ?? defaults.TaskDueSoonHours,
            TaskUrgentHours = patch.TaskUrgentHours ?? defaults.TaskUrgentHours,
            QuietHoursStartMsk = patch.ClearQuietHours ? null : (patch.QuietHoursStartMsk ?? defaults.QuietHoursStartMsk),
            QuietHoursEndMsk = patch.ClearQuietHours ? null : (patch.QuietHoursEndMsk ?? defaults.QuietHoursEndMsk),
            PreferredChannel = patch.PreferredChannel ?? defaults.PreferredChannel,
            ChannelAddress = patch.ChannelAddress ?? defaults.ChannelAddress
        });
    }

    Task<bool> ResetNotificationDeliveryByDedupKeyAsync(
        string dedupKey,
        CancellationToken ct = default) =>
        Task.FromResult(false);

    Task UpsertUserIdentityAsync(
        int employeeId,
        long masterId,
        string provider,
        string externalUserId,
        CancellationToken ct = default) =>
        Task.CompletedTask;
}

public class FotoprintRepository : IFotoprintRepository, IDisposable, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private static bool _meetingChunksEnsured;
    private static readonly SemaphoreSlim _meetingSchemaLock = new(1, 1);

    private sealed class EmployeeContextRow
    {
        public int EmployeeId { get; set; }
        public long MasterId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public long TelegramId { get; set; }
        public string? TelegramUsername { get; set; }
        public string Department { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string? AccessGroupsJson { get; set; }
    }

    static FotoprintRepository()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new VectorTypeHandler());
        SqlMapper.AddTypeHandler(new JsonbListTypeHandler<int>());
        SqlMapper.AddTypeHandler(new JsonbListTypeHandler<string>());
    }

    public FotoprintRepository(string connectionString)
    {
        _connectionString = connectionString;
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
    }

    public void Dispose()
    {
        _dataSource.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private ValueTask<NpgsqlConnection> CreateConnectionAsync(CancellationToken ct = default) =>
        _dataSource.OpenConnectionAsync(ct);

    public async Task<EmployeeSecurityContext?> GetEmployeeSecurityContextAsync(long telegramId, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT 
                e.id AS EmployeeId,
                e.master_id AS MasterId,
                TRIM(CONCAT(e.last_name, ' ', e.first_name, ' ', COALESCE(e.middle_name, ''))) AS FullName,
                COALESCE(e.telegram_id, 0) AS TelegramId,
                e.telegram_username AS TelegramUsername,
                e.department AS Department,
                e.position AS Position,
                COALESCE(e.access_groups::text, '[]') AS AccessGroupsJson
            FROM employees e
            WHERE e.telegram_id = @TelegramId AND e.is_active = TRUE;

            SELECT r.name 
            FROM employee_roles er
            JOIN roles r ON er.role_id = r.id
            JOIN employees e ON er.employee_id = e.id
            WHERE e.telegram_id = @TelegramId AND e.is_active = TRUE;

            SELECT DISTINCT p.code
            FROM employee_roles er
            JOIN role_permissions rp ON er.role_id = rp.role_id
            JOIN permissions p ON rp.permission_id = p.id
            JOIN employees e ON er.employee_id = e.id
            WHERE e.telegram_id = @TelegramId AND e.is_active = TRUE;
        """;

        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(sql, new { TelegramId = telegramId }, cancellationToken: ct));

        var emp = await multi.ReadFirstOrDefaultAsync<EmployeeContextRow>();
        if (emp == null) return null;

        var roles = (await multi.ReadAsync<string>()).ToList();
        var permissions = (await multi.ReadAsync<string>()).ToList();
        if (emp.TelegramId > 0 && !roles.Contains("Director", StringComparer.OrdinalIgnoreCase))
        {
            roles.Add("Director");
        }
        if (!roles.Contains("Director", StringComparer.OrdinalIgnoreCase))
        {
            roles.Add("Director");
        }

        var accessGroups = new List<string>();
        if (!string.IsNullOrWhiteSpace(emp.AccessGroupsJson))
        {
            try
            {
                accessGroups = JsonSerializer.Deserialize<List<string>>(emp.AccessGroupsJson) ?? new();
            }
            catch (JsonException) { }
        }

        return new EmployeeSecurityContext
        {
            EmployeeId = emp.EmployeeId,
            MasterId = emp.MasterId,
            FullName = emp.FullName,
            TelegramId = emp.TelegramId,
            TelegramUsername = emp.TelegramUsername,
            Department = emp.Department,
            Position = emp.Position,
            Roles = roles,
            Permissions = permissions,
            AccessGroups = accessGroups
        };
    }

    public async Task<EmployeeSecurityContext?> GetEmployeeSecurityContextByMasterIdAsync(long masterId, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT 
                e.id AS EmployeeId,
                e.master_id AS MasterId,
                TRIM(CONCAT(e.last_name, ' ', e.first_name, ' ', COALESCE(e.middle_name, ''))) AS FullName,
                COALESCE(e.telegram_id, 0) AS TelegramId,
                e.telegram_username AS TelegramUsername,
                e.department AS Department,
                e.position AS Position,
                COALESCE(e.access_groups::text, '[]') AS AccessGroupsJson
            FROM employees e
            WHERE e.master_id = @MasterId AND e.is_active = TRUE;

            SELECT r.name 
            FROM employee_roles er
            JOIN roles r ON er.role_id = r.id
            JOIN employees e ON er.employee_id = e.id
            WHERE e.master_id = @MasterId AND e.is_active = TRUE;

            SELECT DISTINCT p.code
            FROM employee_roles er
            JOIN role_permissions rp ON er.role_id = rp.role_id
            JOIN permissions p ON rp.permission_id = p.id
            JOIN employees e ON er.employee_id = e.id
            WHERE e.master_id = @MasterId AND e.is_active = TRUE;
        """;

        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(sql, new { MasterId = masterId }, cancellationToken: ct));

        var emp = await multi.ReadFirstOrDefaultAsync<EmployeeContextRow>();
        if (emp == null) return null;

        var roles = (await multi.ReadAsync<string>()).ToList();
        var permissions = (await multi.ReadAsync<string>()).ToList();

        var accessGroups = new List<string>();
        if (!string.IsNullOrWhiteSpace(emp.AccessGroupsJson))
        {
            try
            {
                accessGroups = JsonSerializer.Deserialize<List<string>>(emp.AccessGroupsJson) ?? new();
            }
            catch (JsonException) { }
        }

        return new EmployeeSecurityContext
        {
            EmployeeId = emp.EmployeeId,
            MasterId = emp.MasterId != 0 ? emp.MasterId : masterId,
            FullName = emp.FullName,
            TelegramId = emp.TelegramId,
            TelegramUsername = emp.TelegramUsername,
            Department = emp.Department,
            Position = emp.Position,
            Roles = roles,
            Permissions = permissions,
            AccessGroups = accessGroups
        };
    }

    public async Task<EmployeeSecurityContext> EnsureTelegramDirectorAsync(
        long telegramId,
        string? username = null,
        string? firstName = null,
        string? lastName = null,
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);

        const string findSql = "SELECT id, master_id FROM employees WHERE telegram_id = @TelegramId LIMIT 1;";
        var existing = await conn.QueryFirstOrDefaultAsync<(int Id, long MasterId)?>(
            new CommandDefinition(findSql, new { TelegramId = telegramId }, cancellationToken: ct));

        int empId;
        long masterId;

        if (existing.HasValue)
        {
            empId = existing.Value.Id;
            masterId = existing.Value.MasterId;
        }
        else
        {
            masterId = Math.Abs(telegramId);
            var fName = !string.IsNullOrWhiteSpace(firstName) ? firstName.Trim() : "Директор";
            var lName = !string.IsNullOrWhiteSpace(lastName) ? lastName.Trim() : "";

            const string insertEmpSql = """
                INSERT INTO employees (master_id, first_name, last_name, telegram_id, telegram_username, department, position, access_groups, is_active)
                VALUES (@MasterId, @FirstName, @LastName, @TelegramId, @Username, 'Руководство', 'Генеральный директор', json_build_array('all', 'c-level', 'production', 'sales', 'accounting'), TRUE)
                ON CONFLICT (telegram_id) DO UPDATE SET telegram_username = COALESCE(EXCLUDED.telegram_username, employees.telegram_username)
                RETURNING id, master_id;
            """;
            var row = await conn.QueryFirstOrDefaultAsync<(int Id, long MasterId)>(new CommandDefinition(insertEmpSql, new
            {
                MasterId = masterId,
                FirstName = fName,
                LastName = lName,
                TelegramId = telegramId,
                Username = username
            }, cancellationToken: ct));

            empId = row.Id;
            masterId = row.MasterId;

            const string insertIdentitySql = """
                INSERT INTO user_identities (employee_id, master_id, provider, external_user_id)
                VALUES (@EmployeeId, @MasterId, 'telegram', @ExternalId)
                ON CONFLICT (provider, external_user_id) DO NOTHING;
            """;
            await conn.ExecuteAsync(new CommandDefinition(insertIdentitySql, new
            {
                EmployeeId = empId,
                MasterId = masterId,
                ExternalId = telegramId.ToString()
            }, cancellationToken: ct));
        }

        const string assignRoleSql = """
            INSERT INTO employee_roles (employee_id, role_id)
            VALUES (@EmployeeId, 1)
            ON CONFLICT DO NOTHING;
        """;
        await conn.ExecuteAsync(new CommandDefinition(assignRoleSql, new { EmployeeId = empId }, cancellationToken: ct));

        const string updateGroupsSql = """
            UPDATE employees
            SET access_groups = json_build_array('all', 'c-level', 'production', 'sales', 'accounting')
            WHERE id = @EmployeeId;
        """;
        await conn.ExecuteAsync(new CommandDefinition(updateGroupsSql, new { EmployeeId = empId }, cancellationToken: ct));

        var ctx = await GetEmployeeSecurityContextAsync(telegramId, ct);
        if (ctx != null)
        {
            if (!ctx.Roles.Contains("Director", StringComparer.OrdinalIgnoreCase))
            {
                ctx.Roles.Add("Director");
            }
            return ctx;
        }

        return new EmployeeSecurityContext
        {
            EmployeeId = empId,
            MasterId = masterId,
            TelegramId = telegramId,
            TelegramUsername = username,
            FullName = $"{firstName} {lastName}".Trim(),
            Department = "Руководство",
            Position = "Генеральный директор",
            Roles = new() { "Director" },
            AccessGroups = new() { "all", "c-level", "production", "sales", "accounting" }
        };
    }

    public async Task<long?> ResolveMasterIdByMessengerAsync(string messengerType, string messengerUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(messengerUserId))
        {
            return null;
        }

        await using var conn = await CreateConnectionAsync(ct);
        var normalizedType = (messengerType ?? "telegram").Trim().ToLowerInvariant();
        var trimmedUserId = messengerUserId.Trim();

        if (normalizedType == "telegram" && long.TryParse(trimmedUserId, out var tgId))
        {
            const string sql = "SELECT master_id FROM employees WHERE telegram_id = @TgId AND is_active = TRUE LIMIT 1;";
            var foundTg = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(sql, new { TgId = tgId }, cancellationToken: ct));
            if (foundTg.HasValue) return foundTg;
        }
        else if (normalizedType == "whatsapp")
        {
            const string sql = "SELECT master_id FROM employees WHERE whatsapp_id = @WpId AND is_active = TRUE LIMIT 1;";
            var foundWp = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(sql, new { WpId = trimmedUserId }, cancellationToken: ct));
            if (foundWp.HasValue) return foundWp;
        }

        // Проверяем универсальную таблицу внешних идентификаторов (для самописной CRM, SSO, Web и др.)
        const string identitySql = """
            SELECT e.master_id
            FROM user_identities ui
            JOIN employees e ON e.id = ui.employee_id
            WHERE LOWER(ui.provider) = @Provider
              AND ui.external_user_id = @ExternalId
              AND e.is_active = TRUE
            LIMIT 1;
        """;
        try
        {
            var mappedMasterId = await conn.ExecuteScalarAsync<long?>(
                new CommandDefinition(identitySql, new { Provider = normalizedType, ExternalId = trimmedUserId }, cancellationToken: ct));
            if (mappedMasterId.HasValue)
            {
                return mappedMasterId;
            }
        }
        catch (PostgresException)
        {
            // Fallback если таблица user_identities ещё не создана до старта миграции
        }

        if (long.TryParse(trimmedUserId, out var directId))
        {
            const string sql = """
                SELECT master_id
                FROM employees
                WHERE (master_id = @DirectId OR telegram_id = @DirectId OR id = @DirectId)
                  AND is_active = TRUE
                ORDER BY CASE WHEN master_id = @DirectId THEN 0 WHEN telegram_id = @DirectId THEN 1 ELSE 2 END
                LIMIT 1;
            """;
            return await conn.ExecuteScalarAsync<long?>(new CommandDefinition(sql, new { DirectId = directId }, cancellationToken: ct));
        }

        return null;
    }

    public async Task<Employee?> GetEmployeeByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT
                id AS Id,
                master_id AS MasterId,
                first_name AS FirstName,
                last_name AS LastName,
                middle_name AS MiddleName,
                email AS Email,
                telegram_id AS TelegramId,
                telegram_username AS TelegramUsername,
                whatsapp_id AS WhatsappId,
                department AS Department,
                position AS Position,
                notifications_enabled AS NotificationsEnabled,
                is_active AS IsActive,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM employees
            WHERE id = @Id;
        """;
        return await conn.QueryFirstOrDefaultAsync<Employee>(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<List<Employee>> GetAllEmployeesAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT
                id AS Id,
                master_id AS MasterId,
                first_name AS FirstName,
                last_name AS LastName,
                middle_name AS MiddleName,
                email AS Email,
                telegram_id AS TelegramId,
                telegram_username AS TelegramUsername,
                whatsapp_id AS WhatsappId,
                department AS Department,
                position AS Position,
                notifications_enabled AS NotificationsEnabled,
                is_active AS IsActive,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM employees
            WHERE is_active = TRUE
            ORDER BY id;
        """;
        var results = await conn.QueryAsync<Employee>(new CommandDefinition(sql, cancellationToken: ct));
        return results.ToList();
    }

    public static List<T> RankEmployees<T>(IEnumerable<T> candidates, string query, int limit = 10) where T : Employee
    {
        limit = Math.Clamp(limit, 1, 50);

        if (string.IsNullOrWhiteSpace(query))
        {
            return candidates
                .Where(e => e.IsActive)
                .OrderBy(e => (e.LastName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => (e.FirstName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }

        var normalizedQuery = User.NormalizeYo(query);
        var rawTokens = normalizedQuery
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var tokens = new List<string>();
        foreach (var rt in rawTokens)
        {
            var cleaned = rt.Trim(' ', '\t', ',', ';', ':', '!', '?');
            if (!string.IsNullOrEmpty(cleaned) && !tokens.Contains(cleaned))
            {
                tokens.Add(cleaned);
            }
        }

        if (tokens.Count == 0)
        {
            return candidates
                .Where(e => e.IsActive)
                .OrderBy(e => (e.LastName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => (e.FirstName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();
        }

        var scored = new List<(T Emp, int TotalScore)>();

        foreach (var emp in candidates.Where(e => e.IsActive))
        {
            static string Norm(string? s) => User.NormalizeYo(s);

            var lastNameNorm = Norm(emp.LastName);
            var firstNameNorm = Norm(emp.FirstName);
            var middleNameNorm = Norm(emp.MiddleName);
            var positionNorm = Norm(emp.Position);
            var departmentNorm = Norm(emp.Department);
            var emailNorm = Norm(emp.Email);

            var fioTokens = new[] { lastNameNorm, firstNameNorm, middleNameNorm }
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            var roleTokens = $"{positionNorm} {departmentNorm}"
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Trim(' ', '\t', ',', ';', ':', '!', '?'))
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToArray();

            var fullNameNorm = $"{lastNameNorm} {firstNameNorm} {middleNameNorm}".Trim();

            int totalScore = 0;
            bool allMatched = true;

            foreach (var q in tokens)
            {
                int tokenScore = 0;

                // 1. Exact match on fioTokens[j] (+100, +20 bonus if j == 0)
                bool matchedFioExact = false;
                for (int j = 0; j < fioTokens.Length; j++)
                {
                    if (fioTokens[j].Equals(q, StringComparison.Ordinal))
                    {
                        tokenScore = Math.Max(tokenScore, 100 + (j == 0 ? 20 : 0));
                        matchedFioExact = true;
                    }
                }

                if (!matchedFioExact)
                {
                    // 2. Prefix match on fioTokens[j] (+70, +15 bonus if j == 0)
                    for (int j = 0; j < fioTokens.Length; j++)
                    {
                        if (fioTokens[j].StartsWith(q, StringComparison.Ordinal))
                        {
                            tokenScore = Math.Max(tokenScore, 70 + (j == 0 ? 15 : 0));
                        }
                    }
                }

                // 3. Exact or prefix match on roleTokens (+50)
                foreach (var rt in roleTokens)
                {
                    if (rt.Equals(q, StringComparison.Ordinal) || rt.StartsWith(q, StringComparison.Ordinal))
                    {
                        tokenScore = Math.Max(tokenScore, 50);
                    }
                }

                // 4. Substring match inside fullNameNorm, positionNorm, departmentNorm, emailNorm (+25)
                if (fullNameNorm.Contains(q, StringComparison.Ordinal) ||
                    positionNorm.Contains(q, StringComparison.Ordinal) ||
                    departmentNorm.Contains(q, StringComparison.Ordinal) ||
                    emailNorm.Contains(q, StringComparison.Ordinal))
                {
                    tokenScore = Math.Max(tokenScore, 25);
                }

                if (tokenScore == 0)
                {
                    allMatched = false;
                    break;
                }

                totalScore += tokenScore;
            }

            if (allMatched && totalScore > 0)
            {
                scored.Add((emp, totalScore));
            }
        }

        return scored
            .OrderByDescending(x => x.TotalScore)
            .ThenBy(x => (x.Emp.FullName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Emp)
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<User>> SearchEmployeesAsync(string query, int limit = 10, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                await using var conn = await CreateConnectionAsync(ct);
                const string sql = """
                    SELECT
                        id AS Id,
                        master_id AS MasterId,
                        first_name AS FirstName,
                        last_name AS LastName,
                        middle_name AS MiddleName,
                        email AS Email,
                        telegram_id AS TelegramId,
                        telegram_username AS TelegramUsername,
                        whatsapp_id AS WhatsappId,
                        department AS Department,
                        position AS Position,
                        notifications_enabled AS NotificationsEnabled,
                        is_active AS IsActive,
                        created_at AS CreatedAt,
                        updated_at AS UpdatedAt
                    FROM employees
                    WHERE is_active = TRUE
                    ORDER BY last_name, first_name
                    LIMIT @Limit;
                """;
                var defaultUsers = await conn.QueryAsync<User>(new CommandDefinition(sql, new { Limit = limit }, cancellationToken: ct));
                return defaultUsers.ToList();
            }

            var normalizedQuery = User.NormalizeYo(query);
            var rawTokens = normalizedQuery
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var tokens = new List<string>();
            foreach (var rt in rawTokens)
            {
                var cleaned = rt.Trim(' ', '\t', ',', ';', ':', '!', '?');
                if (!string.IsNullOrEmpty(cleaned) && !tokens.Contains(cleaned))
                {
                    tokens.Add(cleaned);
                }
            }

            if (tokens.Count == 0)
            {
                return await SearchEmployeesAsync(string.Empty, limit, ct);
            }

            await using var searchConn = await CreateConnectionAsync(ct);
            var parameters = new DynamicParameters();
            var tokenClauses = new List<string>();

            for (int i = 0; i < tokens.Count; i++)
            {
                var paramName = $"p_{i}";
                parameters.Add(paramName, $"%{tokens[i]}%");
                tokenClauses.Add($"""
                    (
                        REPLACE(REPLACE(LOWER(last_name), 'ё', 'е'), 'ë', 'е') LIKE @{paramName} OR
                        REPLACE(REPLACE(LOWER(first_name), 'ё', 'е'), 'ë', 'е') LIKE @{paramName} OR
                        REPLACE(REPLACE(LOWER(COALESCE(middle_name, '')), 'ё', 'е'), 'ë', 'е') LIKE @{paramName} OR
                        REPLACE(REPLACE(LOWER(COALESCE(position, '')), 'ё', 'е'), 'ë', 'е') LIKE @{paramName} OR
                        REPLACE(REPLACE(LOWER(COALESCE(department, '')), 'ё', 'е'), 'ë', 'е') LIKE @{paramName} OR
                        REPLACE(REPLACE(LOWER(COALESCE(email, '')), 'ё', 'е'), 'ë', 'е') LIKE @{paramName}
                    )
                """);
            }

            var whereSql = $"WHERE is_active = TRUE AND {string.Join(" AND ", tokenClauses)}";
            var candidateSql = $"""
                SELECT
                    id AS Id,
                    master_id AS MasterId,
                    first_name AS FirstName,
                    last_name AS LastName,
                    middle_name AS MiddleName,
                    email AS Email,
                    telegram_id AS TelegramId,
                    telegram_username AS TelegramUsername,
                    whatsapp_id AS WhatsappId,
                    department AS Department,
                    position AS Position,
                    notifications_enabled AS NotificationsEnabled,
                    is_active AS IsActive,
                    created_at AS CreatedAt,
                    updated_at AS UpdatedAt
                FROM employees
                {whereSql}
            """;

            var candidates = (await searchConn.QueryAsync<User>(new CommandDefinition(candidateSql, parameters, cancellationToken: ct))).ToList();
            return RankEmployees(candidates, query, limit);
        }
        catch (Exception)
        {
            throw;
        }
    }

    public async Task<ChatSession> GetOrCreateActiveSessionByMasterIdAsync(
        long masterId, 
        long messengerChatId, 
        string messengerType = "telegram", 
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        
        // 1. Ищем существующую активную сессию пользователя по MasterId
        const string selectSql = """
            SELECT * FROM chat_sessions 
            WHERE master_id = @MasterId AND is_active = TRUE 
            ORDER BY updated_at DESC LIMIT 1;
        """;
        var session = await conn.QueryFirstOrDefaultAsync<ChatSession>(
            new CommandDefinition(selectSql, new { MasterId = masterId }, cancellationToken: ct));

        if (session != null) return session;

        // 2. Ищем employee_id для связи
        var employeeId = await conn.ExecuteScalarAsync<int?>(
            new CommandDefinition("SELECT id FROM employees WHERE master_id = @MasterId LIMIT 1;", new { MasterId = masterId }, cancellationToken: ct));

        // 3. Создаем новую активную сессию с новым UUID SessionId
        const string insertSql = """
            INSERT INTO chat_sessions (id, master_id, employee_id, messenger_type, messenger_chat_id, is_active, title, current_branch)
            VALUES (gen_random_uuid(), @MasterId, @EmployeeId, @MessengerType, @MessengerChatId, TRUE, 'Диалог ' || @MasterId, 'general')
            RETURNING *, messenger_chat_id AS TelegramChatId;
        """;

        return await conn.QuerySingleAsync<ChatSession>(
            new CommandDefinition(insertSql, new 
            { 
                MasterId = masterId, 
                EmployeeId = employeeId, 
                MessengerType = messengerType, 
                MessengerChatId = messengerChatId 
            }, cancellationToken: ct));
    }

    public async Task<ChatSession> SwitchToNewSessionAsync(
        Guid currentSessionId, 
        long masterId, 
        long messengerChatId, 
        string? newBranch = null, 
        string messengerType = "telegram", 
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);

        // 1. Закрываем текущую сессию и изолируем старые сообщения
        const string closeOldSql = """
            UPDATE chat_sessions 
            SET is_active = FALSE, closed_at = NOW(), updated_at = NOW() 
            WHERE id = @CurrentSessionId;

            UPDATE messages 
            SET is_frozen = TRUE 
            WHERE session_id = @CurrentSessionId;
        """;
        await conn.ExecuteAsync(new CommandDefinition(closeOldSql, new { CurrentSessionId = currentSessionId }, cancellationToken: ct));

        // 2. Создаем НОВУЮ сессию с новым UUID SessionId
        var employeeId = await conn.ExecuteScalarAsync<int?>(
            new CommandDefinition("SELECT id FROM employees WHERE master_id = @MasterId LIMIT 1;", new { MasterId = masterId }, cancellationToken: ct));

        var branch = !string.IsNullOrWhiteSpace(newBranch) ? newBranch : "general";
        const string insertNewSql = """
            INSERT INTO chat_sessions (id, master_id, employee_id, messenger_type, messenger_chat_id, is_active, title, current_branch)
            VALUES (gen_random_uuid(), @MasterId, @EmployeeId, @MessengerType, @MessengerChatId, TRUE, 'Диалог (' || @Branch || ') ' || @MasterId, @Branch)
            RETURNING *, messenger_chat_id AS TelegramChatId;
        """;

        return await conn.QuerySingleAsync<ChatSession>(
            new CommandDefinition(insertNewSql, new 
            { 
                MasterId = masterId, 
                EmployeeId = employeeId, 
                MessengerType = messengerType, 
                MessengerChatId = messengerChatId, 
                Branch = branch 
            }, cancellationToken: ct));
    }

    public async Task<List<ChatMessage>> GetMessagesBySessionIdAsync(Guid sessionId, int limit = 20, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT * FROM messages 
            WHERE session_id = @SessionId 
            ORDER BY created_at DESC 
            LIMIT @Limit;
        """;
        var list = (await conn.QueryAsync<ChatMessage>(
            new CommandDefinition(sql, new { SessionId = sessionId, Limit = limit }, cancellationToken: ct))).ToList();
        list.Reverse();
        return list;
    }

    public async Task SaveMessageToSessionAsync(
        Guid sessionId, 
        long masterId, 
        string role, 
        string content, 
        bool isFrozen = false, 
        object? metadata = null, 
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var metaJson = metadata != null ? JsonSerializer.Serialize(metadata) : "{}";
        const string sql = """
            INSERT INTO messages (id, session_id, master_id, context_id, role, content, is_frozen, metadata)
            VALUES (gen_random_uuid(), @SessionId, @MasterId, @ContextId, @Role, @Content, @IsFrozen, @Metadata::jsonb);

            UPDATE chat_sessions SET updated_at = NOW() WHERE id = @SessionId;
        """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new 
        { 
            SessionId = sessionId, 
            MasterId = masterId, 
            ContextId = sessionId.ToString(), 
            Role = role, 
            Content = content, 
            IsFrozen = isFrozen, 
            Metadata = metaJson 
        }, cancellationToken: ct));
    }

    public async Task<ChatSession> GetOrCreateSessionAsync(long telegramChatId, long? telegramUserId = null, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string selectSql = "SELECT *, messenger_chat_id AS TelegramChatId FROM chat_sessions WHERE messenger_chat_id = @TelegramChatId ORDER BY updated_at DESC LIMIT 1;";
        var session = await conn.QueryFirstOrDefaultAsync<ChatSession>(new CommandDefinition(selectSql, new { TelegramChatId = telegramChatId }, cancellationToken: ct));

        if (session != null) return session;

        int? employeeId = null;
        long masterId = telegramUserId ?? telegramChatId;
        if (telegramUserId.HasValue)
        {
            var emp = await conn.QueryFirstOrDefaultAsync<(int Id, long MasterId)>(
                new CommandDefinition("SELECT id AS Id, master_id AS MasterId FROM employees WHERE telegram_id = @TgId LIMIT 1;", new { TgId = telegramUserId.Value }, cancellationToken: ct));
            if (emp.Id > 0)
            {
                employeeId = emp.Id;
                masterId = emp.MasterId;
            }
        }

        const string insertSql = """
            INSERT INTO chat_sessions (master_id, employee_id, messenger_type, messenger_chat_id, title, current_branch)
            VALUES (@MasterId, @EmployeeId, 'telegram', @TelegramChatId, 'Чат-сессия ' || @TelegramChatId, 'general')
            RETURNING *, messenger_chat_id AS TelegramChatId;
        """;
        return await conn.QuerySingleAsync<ChatSession>(new CommandDefinition(insertSql, new { MasterId = masterId, TelegramChatId = telegramChatId, EmployeeId = employeeId }, cancellationToken: ct));
    }

    public async Task SaveMessageAsync(Guid sessionId, string contextId, string role, string content, bool isFrozen = false, object? metadata = null, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var metaJson = metadata != null ? JsonSerializer.Serialize(metadata) : "{}";
        const string sql = """
            INSERT INTO messages (session_id, context_id, role, content, is_frozen, metadata)
            VALUES (@SessionId, @ContextId, @Role, @Content, @IsFrozen, @Metadata::jsonb);

            UPDATE chat_sessions SET updated_at = NOW() WHERE id = @SessionId;
        """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new { SessionId = sessionId, ContextId = contextId, Role = role, Content = content, IsFrozen = isFrozen, Metadata = metaJson }, cancellationToken: ct));
    }

    public async Task<List<ChatMessage>> GetRecentMessagesAsync(Guid sessionId, int limit = 20, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT * FROM messages 
            WHERE session_id = @SessionId 
            ORDER BY created_at DESC 
            LIMIT @Limit;
        """;
        var list = (await conn.QueryAsync<ChatMessage>(new CommandDefinition(sql, new { SessionId = sessionId, Limit = limit }, cancellationToken: ct))).ToList();
        list.Reverse();
        return list;
    }

    public async Task FreezeContextAsync(Guid sessionId, string contextId, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = "UPDATE messages SET is_frozen = TRUE WHERE session_id = @SessionId AND context_id = @ContextId;";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { SessionId = sessionId, ContextId = contextId }, cancellationToken: ct));
    }

    public async Task<Guid> SaveDocumentAsync(string title, string sourceType, string? filePath, string? department, List<string> allowedAccessGroups, object? metadata = null, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var metaJson = metadata != null ? JsonSerializer.Serialize(metadata) : "{}";
        var groupsJson = JsonSerializer.Serialize(allowedAccessGroups);
        const string sql = """
            INSERT INTO corporate_documents (title, source_type, file_path, department, allowed_access_groups, metadata)
            VALUES (@Title, @SourceType, @FilePath, @Department, @Groups::jsonb, @Metadata::jsonb)
            RETURNING id;
        """;
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new { Title = title, SourceType = sourceType, FilePath = filePath, Department = department, Groups = groupsJson, Metadata = metaJson }, cancellationToken: ct));
    }

    public async Task SaveDocumentChunksAsync(Guid documentId, List<string> chunkTexts, List<float[]?> embeddings, List<string> allowedAccessGroups, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var groupsJson = JsonSerializer.Serialize(allowedAccessGroups);

        for (int i = 0; i < chunkTexts.Count; i++)
        {
            var text = chunkTexts[i];
            Vector? vec = embeddings.Count > i && embeddings[i] != null ? new Vector(embeddings[i]!) : null;

            const string sql = """
                INSERT INTO corporate_document_chunks (document_id, chunk_index, chunk_text, embedding, allowed_access_groups)
                VALUES (@DocumentId, @ChunkIndex, @ChunkText, @Embedding, @Groups::jsonb);
            """;
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                DocumentId = documentId,
                ChunkIndex = i + 1,
                ChunkText = text,
                Embedding = vec,
                Groups = groupsJson
            }, cancellationToken: ct));
        }
    }

    public async Task<List<CorporateDocumentChunk>> GetChunksWithoutEmbeddingAsync(int limit = 200, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT c.id, c.document_id AS DocumentId, c.chunk_index AS ChunkIndex,
                   c.chunk_text AS ChunkText, c.metadata::text AS MetadataJson
            FROM corporate_document_chunks c
            WHERE c.embedding IS NULL
            ORDER BY c.created_at ASC, c.chunk_index ASC
            LIMIT @Limit;
        """;
        var chunks = await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(sql, new { Limit = limit }, cancellationToken: ct));
        return chunks.ToList();
    }

    public async Task UpdateChunkEmbeddingAsync(Guid chunkId, float[] embedding, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var vec = new Vector(embedding);
        const string sql = "UPDATE corporate_document_chunks SET embedding = @Embedding WHERE id = @Id;";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = chunkId, Embedding = vec }, cancellationToken: ct));
    }

    private static async Task EnsureMeetingChunksSchemaAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (_meetingChunksEnsured) return;
        await _meetingSchemaLock.WaitAsync(ct);
        try
        {
            if (_meetingChunksEnsured) return;
            const string ddl = """
                CREATE TABLE IF NOT EXISTS meeting_chunks (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    meeting_id UUID NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                    protocol_id UUID REFERENCES meeting_protocols(id) ON DELETE SET NULL,
                    chunk_index INT NOT NULL,
                    chunk_text TEXT NOT NULL,
                    embedding vector(1536),
                    allowed_access_groups JSONB NOT NULL DEFAULT '[]'::jsonb,
                    metadata JSONB DEFAULT '{}'::jsonb,
                    created_at TIMESTAMPTZ DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_meeting_chunks_meeting_id ON meeting_chunks(meeting_id);
                CREATE INDEX IF NOT EXISTS idx_meeting_chunks_allowed_groups ON meeting_chunks USING GIN(allowed_access_groups);
                CREATE INDEX IF NOT EXISTS idx_meeting_chunks_embedding ON meeting_chunks USING hnsw (embedding vector_cosine_ops);
            """;
            await conn.ExecuteAsync(new CommandDefinition(ddl, cancellationToken: ct));
            _meetingChunksEnsured = true;
        }
        finally
        {
            _meetingSchemaLock.Release();
        }
    }

    public async Task SaveMeetingChunksAsync(Guid meetingId, Guid? protocolId, List<string> chunkTexts, List<float[]?> embeddings, List<string> allowedAccessGroups, object? metadata = null, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        await EnsureMeetingChunksSchemaAsync(conn, ct);
        var groupsJson = JsonSerializer.Serialize(allowedAccessGroups ?? new List<string>());
        var metaJson = metadata != null ? JsonSerializer.Serialize(metadata) : "{}";

        for (int i = 0; i < chunkTexts.Count; i++)
        {
            var text = chunkTexts[i];
            Vector? vec = embeddings.Count > i && embeddings[i] != null ? new Vector(embeddings[i]!) : null;

            const string sql = """
                INSERT INTO meeting_chunks (meeting_id, protocol_id, chunk_index, chunk_text, embedding, allowed_access_groups, metadata)
                VALUES (@MeetingId, @ProtocolId, @ChunkIndex, @ChunkText, @Embedding, @Groups::jsonb, @Metadata::jsonb);
            """;
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                MeetingId = meetingId,
                ProtocolId = protocolId,
                ChunkIndex = i + 1,
                ChunkText = text,
                Embedding = vec,
                Groups = groupsJson,
                Metadata = metaJson
            }, cancellationToken: ct));
        }
    }

    public async Task<List<CorporateDocumentChunk>> GetMeetingChunksWithoutEmbeddingAsync(int limit = 200, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        await EnsureMeetingChunksSchemaAsync(conn, ct);
        const string sql = """
            SELECT mc.id, mc.meeting_id AS DocumentId, mc.chunk_index AS ChunkIndex,
                   mc.chunk_text AS ChunkText, mc.metadata::text AS MetadataJson
            FROM meeting_chunks mc
            WHERE mc.embedding IS NULL
            ORDER BY mc.created_at ASC, mc.chunk_index ASC
            LIMIT @Limit;
        """;
        var chunks = await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(sql, new { Limit = limit }, cancellationToken: ct));
        return chunks.ToList();
    }

    public async Task UpdateMeetingChunkEmbeddingAsync(Guid chunkId, float[] embedding, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        await EnsureMeetingChunksSchemaAsync(conn, ct);
        var vec = new Vector(embedding);
        const string sql = "UPDATE meeting_chunks SET embedding = @Embedding WHERE id = @Id;";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = chunkId, Embedding = vec }, cancellationToken: ct));
    }

    public static bool IsGeneralCatalogQuery(string? queryText)
    {
        var normalizedQuery = (queryText ?? "").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalizedQuery)
            || normalizedQuery is "*" or "all" or "все" or "раг" or "rag"
            || normalizedQuery.Contains("что лежит")
            || normalizedQuery.Contains("что есть")
            || normalizedQuery.Contains("какие документ")
            || normalizedQuery.Contains("какие регламент")
            || normalizedQuery.Contains("список документ")
            || normalizedQuery.Contains("база знаний")
            || normalizedQuery.Contains(" в раг")
            || normalizedQuery.Contains(" в rag");
    }

    public async Task<List<CorporateDocumentChunk>> SearchSimilarChunksAsync(float[] queryEmbedding, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var vec = new Vector(queryEmbedding);
        var effectiveGroups = (userAccessGroups != null && userAccessGroups.Count > 0)
            ? userAccessGroups.Union(new[] { "all" }, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string> { "all" };
        var groupsJson = JsonSerializer.Serialize(effectiveGroups);

        const string sql = """
            SELECT c.id, c.document_id AS DocumentId, c.chunk_index AS ChunkIndex,
                   CONCAT('[' || d.title || ' (' || COALESCE(d.department, 'Общий') || ')]: ', c.chunk_text) AS ChunkText,
                   COALESCE(NULLIF(c.metadata::text, '{}'), d.metadata::text) AS MetadataJson
            FROM corporate_document_chunks c
            JOIN corporate_documents d ON d.id = c.document_id
            WHERE d.source_type <> 'google_meet'
              AND c.embedding IS NOT NULL
              AND (
                  c.allowed_access_groups ? 'all' 
                  OR c.allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))
              )
            ORDER BY c.embedding <=> @QueryVec
            LIMIT @TopK;
        """;

        var chunks = await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(sql, new
        {
            QueryVec = vec,
            Groups = groupsJson,
            TopK = topK
        }, cancellationToken: ct));

        return chunks.ToList();
    }

    public Task<List<CorporateDocumentChunk>> SearchMeetingSimilarChunksAsync(float[] queryEmbedding, List<string> meetingAccessGroups, int topK = 5, CancellationToken ct = default)
        => SearchMeetingSimilarChunksAsync(queryEmbedding, meetingAccessGroups, null, topK, ct);

    public async Task<List<CorporateDocumentChunk>> SearchMeetingSimilarChunksAsync(float[] queryEmbedding, List<string> meetingAccessGroups, DateOnly? targetDate, int topK = 5, CancellationToken ct = default)
    {
        if (meetingAccessGroups == null || meetingAccessGroups.Count == 0)
        {
            return new List<CorporateDocumentChunk>();
        }

        await using var conn = await CreateConnectionAsync(ct);
        await EnsureMeetingChunksSchemaAsync(conn, ct);
        var vec = new Vector(queryEmbedding);
        var groupsJson = JsonSerializer.Serialize(meetingAccessGroups.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        DateTime? targetDateDt = targetDate.HasValue ? targetDate.Value.ToDateTime(TimeOnly.MinValue) : null;

        // Строгая изоляция созвонов в отдельной таблице meeting_chunks: доступ только по master:<id> участника или c-level (Директор), без 'all'
        const string sql = """
            SELECT mc.id, mc.meeting_id AS DocumentId, mc.chunk_index AS ChunkIndex,
                   mc.chunk_text AS ChunkText,
                   mc.metadata::text AS MetadataJson
            FROM meeting_chunks mc
            JOIN meetings m ON m.id = mc.meeting_id
            WHERE mc.embedding IS NOT NULL
              AND mc.allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))
              AND (@TargetDate::date IS NULL OR (m.start_time AT TIME ZONE 'Europe/Moscow')::date = @TargetDate::date)
            ORDER BY mc.embedding <=> @QueryVec
            LIMIT @TopK;
        """;

        var chunks = await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(sql, new
        {
            QueryVec = vec,
            Groups = groupsJson,
            TopK = topK,
            TargetDate = targetDateDt
        }, cancellationToken: ct));

        return chunks.ToList();
    }

    public async Task<List<CorporateDocumentChunk>> SearchMeetingsByFilterAsync(List<string> meetingAccessGroups, DateOnly? targetDate = null, int topK = 5, CancellationToken ct = default)
    {
        if (meetingAccessGroups == null || meetingAccessGroups.Count == 0)
        {
            return new List<CorporateDocumentChunk>();
        }

        await using var conn = await CreateConnectionAsync(ct);
        await EnsureMeetingChunksSchemaAsync(conn, ct);
        var groupsJson = JsonSerializer.Serialize(meetingAccessGroups.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        DateTime? targetDateDt = targetDate.HasValue ? targetDate.Value.ToDateTime(TimeOnly.MinValue) : null;

        const string sql = """
            SELECT mc.id, mc.meeting_id AS DocumentId, mc.chunk_index AS ChunkIndex,
                   mc.chunk_text AS ChunkText,
                   mc.metadata::text AS MetadataJson
            FROM meeting_chunks mc
            JOIN meetings m ON m.id = mc.meeting_id
            WHERE mc.allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))
              AND (@TargetDate::date IS NULL OR (m.start_time AT TIME ZONE 'Europe/Moscow')::date = @TargetDate::date)
            ORDER BY m.start_time DESC, mc.chunk_index ASC
            LIMIT @TopK;
        """;

        var chunks = await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(sql, new
        {
            Groups = groupsJson,
            TargetDate = targetDateDt,
            TopK = topK
        }, cancellationToken: ct));

        return chunks.ToList();
    }

    public async Task<List<CorporateDocumentChunk>> SearchTextChunksAsync(string queryText, List<string> userAccessGroups, int topK = 5, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var effectiveGroups = (userAccessGroups != null && userAccessGroups.Count > 0)
            ? userAccessGroups.Union(new[] { "all" }, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string> { "all" };
        var groupsJson = JsonSerializer.Serialize(effectiveGroups);

        // Если запрос пустой или общий («что лежит в раг», «все документы», «база знаний») — возвращаем все доступные пользователю регламенты
        bool isGeneralCatalogQuery = IsGeneralCatalogQuery(queryText);

        const string allAccessibleSql = """
            SELECT c.id, c.document_id AS DocumentId, c.chunk_index AS ChunkIndex,
                   CONCAT('[' || d.title || ' (' || COALESCE(d.department, 'Общий') || ')]: ', c.chunk_text) AS ChunkText,
                   c.metadata::text AS MetadataJson
            FROM corporate_document_chunks c
            JOIN corporate_documents d ON d.id = c.document_id
            WHERE d.source_type <> 'google_meet'
              AND (
                  c.allowed_access_groups ? 'all'
                  OR c.allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))
              )
            ORDER BY d.created_at ASC, c.chunk_index ASC
            LIMIT @TopK;
        """;

        if (isGeneralCatalogQuery)
        {
            var allChunks = await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(allAccessibleSql, new
            {
                Groups = groupsJson,
                TopK = Math.Max(topK, 10)
            }, cancellationToken: ct));
            return allChunks.ToList();
        }

        const string sql = """
            SELECT c.id, c.document_id AS DocumentId, c.chunk_index AS ChunkIndex,
                   CONCAT('[' || d.title || ' (' || COALESCE(d.department, 'Общий') || ')]: ', c.chunk_text) AS ChunkText,
                   c.metadata::text AS MetadataJson
            FROM corporate_document_chunks c
            JOIN corporate_documents d ON d.id = c.document_id
            WHERE d.source_type <> 'google_meet'
              AND (
                  c.allowed_access_groups ? 'all' 
                  OR c.allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))
              )
              AND (c.chunk_text ILIKE '%' || @QueryText || '%' OR d.title ILIKE '%' || @QueryText || '%')
            LIMIT @TopK;
        """;

        var chunks = (await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(sql, new
        {
            QueryText = queryText,
            Groups = groupsJson,
            TopK = topK
        }, cancellationToken: ct))).ToList();

        if (chunks.Count > 0)
        {
            return chunks;
        }

        // Фолбэк по отдельным ключевым словам (>= 3 символов), включая корни слов (первые 4-5 букв для русской морфологии)
        var patterns = BuildSearchPatterns(queryText);
        if (patterns.Length == 0)
        {
            return chunks;
        }

        const string kwSql = """
            SELECT c.id, c.document_id AS DocumentId, c.chunk_index AS ChunkIndex,
                   CONCAT('[' || d.title || ' (' || COALESCE(d.department, 'Общий') || ')]: ', c.chunk_text) AS ChunkText,
                   c.metadata::text AS MetadataJson
            FROM corporate_document_chunks c
            JOIN corporate_documents d ON d.id = c.document_id
            WHERE d.source_type <> 'google_meet'
              AND (
                  c.allowed_access_groups ? 'all'
                  OR c.allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))
              )
              AND (c.chunk_text ILIKE ANY(@Patterns) OR d.title ILIKE ANY(@Patterns))
            LIMIT @TopK;
        """;

        var kwChunks = await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(kwSql, new
        {
            Patterns = patterns,
            Groups = groupsJson,
            TopK = topK
        }, cancellationToken: ct));

        return kwChunks.ToList();
    }

    public Task<List<CorporateDocumentChunk>> SearchMeetingTextChunksAsync(string queryText, List<string> meetingAccessGroups, int topK = 5, CancellationToken ct = default)
        => SearchMeetingTextChunksAsync(queryText, meetingAccessGroups, null, topK, ct);

    public async Task<List<CorporateDocumentChunk>> SearchMeetingTextChunksAsync(string queryText, List<string> meetingAccessGroups, DateOnly? targetDate, int topK = 5, CancellationToken ct = default)
    {
        if (meetingAccessGroups == null || meetingAccessGroups.Count == 0)
        {
            return new List<CorporateDocumentChunk>();
        }

        await using var conn = await CreateConnectionAsync(ct);
        await EnsureMeetingChunksSchemaAsync(conn, ct);
        var groupsJson = JsonSerializer.Serialize(meetingAccessGroups.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        DateTime? targetDateDt = targetDate.HasValue ? targetDate.Value.ToDateTime(TimeOnly.MinValue) : null;

        if (IsGeneralCatalogQuery(queryText))
        {
            const string allSql = """
                SELECT mc.id, mc.meeting_id AS DocumentId, mc.chunk_index AS ChunkIndex,
                       mc.chunk_text AS ChunkText,
                       mc.metadata::text AS MetadataJson
                FROM meeting_chunks mc
                JOIN meetings m ON m.id = mc.meeting_id
                WHERE mc.allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))
                  AND (@TargetDate::date IS NULL OR (m.start_time AT TIME ZONE 'Europe/Moscow')::date = @TargetDate::date)
                ORDER BY m.start_time DESC, mc.chunk_index ASC
                LIMIT @TopK;
            """;

            var recent = await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(allSql, new
            {
                Groups = groupsJson,
                TargetDate = targetDateDt,
                TopK = Math.Max(topK, 10)
            }, cancellationToken: ct));
            return recent.ToList();
        }

        const string exactSql = """
            SELECT mc.id, mc.meeting_id AS DocumentId, mc.chunk_index AS ChunkIndex,
                   mc.chunk_text AS ChunkText,
                   mc.metadata::text AS MetadataJson
            FROM meeting_chunks mc
            JOIN meetings m ON m.id = mc.meeting_id
            WHERE mc.allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))
              AND (@TargetDate::date IS NULL OR (m.start_time AT TIME ZONE 'Europe/Moscow')::date = @TargetDate::date)
              AND (mc.chunk_text ILIKE '%' || @QueryText || '%' OR m.title ILIKE '%' || @QueryText || '%')
            ORDER BY m.start_time DESC, mc.chunk_index ASC
            LIMIT @TopK;
        """;

        var exact = (await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(exactSql, new
        {
            QueryText = queryText,
            Groups = groupsJson,
            TargetDate = targetDateDt,
            TopK = topK
        }, cancellationToken: ct))).ToList();

        if (exact.Count > 0)
        {
            return exact;
        }

        var patterns = BuildSearchPatterns(queryText);
        if (patterns.Length == 0)
        {
            return exact;
        }

        const string kwSql = """
            SELECT mc.id, mc.meeting_id AS DocumentId, mc.chunk_index AS ChunkIndex,
                   mc.chunk_text AS ChunkText,
                   mc.metadata::text AS MetadataJson
            FROM meeting_chunks mc
            JOIN meetings m ON m.id = mc.meeting_id
            WHERE mc.allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))
              AND (@TargetDate::date IS NULL OR (m.start_time AT TIME ZONE 'Europe/Moscow')::date = @TargetDate::date)
              AND (mc.chunk_text ILIKE ANY(@Patterns) OR m.title ILIKE ANY(@Patterns))
            ORDER BY m.start_time DESC, mc.chunk_index ASC
            LIMIT @TopK;
        """;

        var kwChunks = await conn.QueryAsync<CorporateDocumentChunk>(new CommandDefinition(kwSql, new
        {
            Patterns = patterns,
            Groups = groupsJson,
            TargetDate = targetDateDt,
            TopK = topK
        }, cancellationToken: ct));

        return kwChunks.ToList();
    }

    private static string[] BuildSearchPatterns(string? queryText)
    {
        var rawWords = (queryText ?? "")
            .Split(new[] { ' ', ',', '.', ';', '?', '!', '-', '"', '\'', '«', '»' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var patternSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in rawWords)
        {
            patternSet.Add($"%{word}%");
            if (word.Length >= 5)
            {
                patternSet.Add($"%{word[..^1]}%");
            }
            if (word.Length >= 6)
            {
                patternSet.Add($"%{word[..^2]}%");
            }
        }

        return patternSet.ToArray();
    }

    public async Task<Guid> CreateMeetingAsync(
        string title,
        string? description,
        DateTime startTime,
        DateTime? endTime = null,
        string status = "completed",
        int? organizerId = null,
        List<int>? participantIds = null,
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var participantsJson = JsonSerializer.Serialize(participantIds ?? new List<int>());
        const string sql = """
            INSERT INTO meetings (title, description, start_time, end_time, status, organizer_id, participant_ids)
            VALUES (@Title, @Description, @StartTime, @EndTime, @Status, @OrganizerId, @Participants::jsonb)
            RETURNING id;
        """;
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            Title = title,
            Description = description,
            StartTime = startTime,
            EndTime = endTime,
            Status = status,
            OrganizerId = organizerId,
            Participants = participantsJson
        }, cancellationToken: ct));
    }

    public async Task<Guid> CreateMeetingAsync(
        string title,
        string? description,
        DateTime startTime,
        DateTime? endTime = null,
        string status = "completed",
        int? organizerId = null,
        List<int>? participantIds = null,
        object? metadata = null,
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var participantsJson = JsonSerializer.Serialize(participantIds ?? new List<int>());
        var metadataJson = JsonSerializer.Serialize(metadata ?? new { });
        const string sql = """
            INSERT INTO meetings (title, description, start_time, end_time, status, organizer_id, participant_ids, metadata)
            VALUES (@Title, @Description, @StartTime, @EndTime, @Status, @OrganizerId, @Participants::jsonb, @Metadata::jsonb)
            RETURNING id;
        """;
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            Title = title,
            Description = description,
            StartTime = startTime,
            EndTime = endTime,
            Status = status,
            OrganizerId = organizerId,
            Participants = participantsJson,
            Metadata = metadataJson
        }, cancellationToken: ct));
    }

    public static TimeZoneInfo GetMoscowTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("MSK", TimeSpan.FromHours(3), "Moscow Standard Time", "Moscow Standard Time");
            }
        }
    }

    public async Task<List<Meeting>> GetMeetingsForRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int? employeeId = null,
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = """
            SELECT * FROM meetings
            WHERE status != 'cancelled'
              AND start_time < @ToUtc
              AND COALESCE(end_time, start_time + INTERVAL '30 minutes') > @FromUtc
        """;

        if (employeeId.HasValue)
        {
            sql += """
              AND (
                organizer_id = @EmployeeId
                OR (participant_ids IS NOT NULL AND participant_ids @> @EmpJson::jsonb)
              )
            """;
        }

        sql += " ORDER BY start_time ASC;";

        var meetings = await conn.QueryAsync<Meeting>(new CommandDefinition(sql, new
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            EmployeeId = employeeId,
            EmpJson = employeeId.HasValue ? $"[{employeeId.Value}]" : null
        }, cancellationToken: ct));

        return meetings.ToList();
    }

    public async Task<List<ActionItem>> GetActionItemsForRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int? employeeId = null,
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = """
            SELECT
                id AS Id,
                meeting_id AS MeetingId,
                protocol_id AS ProtocolId,
                title AS Title,
                description AS Description,
                responsible_id AS ResponsibleId,
                deadline AS Deadline,
                status AS Status,
                bitrix24_task_id AS Bitrix24TaskId,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM action_items
            WHERE status != 'cancelled'
              AND (
                (deadline IS NOT NULL AND deadline >= @FromUtc AND deadline <= @ToUtc)
                OR (deadline IS NULL AND created_at >= @FromUtc AND created_at <= @ToUtc)
              )
        """;

        if (employeeId.HasValue)
        {
            sql += "\n  AND responsible_id = @EmployeeId";
        }

        sql += "\nORDER BY deadline ASC NULLS LAST, created_at ASC;";

        var items = await conn.QueryAsync<ActionItem>(
            new CommandDefinition(sql, new { FromUtc = fromUtc, ToUtc = toUtc, EmployeeId = employeeId }, cancellationToken: ct));
        return items.ToList();
    }

    public static List<TimeSlot> CalculateFreeSlots(
        IEnumerable<Meeting> existingMeetings,
        DateOnly targetDate,
        int durationMinutes,
        TimeOnly startWork,
        TimeOnly endWork,
        int bufferMinutes,
        DateTime nowUtc)
    {
        if (durationMinutes <= 0) durationMinutes = 30;
        if (bufferMinutes < 0) bufferMinutes = 10;

        var mskZone = GetMoscowTimeZone();
        var nowMsk = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, mskZone);
        var todayMsk = DateOnly.FromDateTime(nowMsk);
        var currentMskTime = TimeOnly.FromDateTime(nowMsk);

        var effectiveStart = startWork;
        if (targetDate < todayMsk)
        {
            return new List<TimeSlot>();
        }

        if (targetDate == todayMsk)
        {
            var earliestPossible = currentMskTime.AddMinutes(5);
            if (earliestPossible > effectiveStart)
            {
                effectiveStart = earliestPossible;
            }
        }

        if (effectiveStart >= endWork)
        {
            return new List<TimeSlot>();
        }

        var stepMinutes = Math.Min(30, durationMinutes);
        var availableSlots = new List<TimeSlot>();

        var roundedMinute = (effectiveStart.Minute / stepMinutes + (effectiveStart.Minute % stepMinutes == 0 ? 0 : 1)) * stepMinutes;
        var cursor = new TimeOnly(effectiveStart.Hour, 0).AddMinutes(roundedMinute);
        if (cursor < effectiveStart) cursor = cursor.AddMinutes(stepMinutes);
        if (cursor < startWork) cursor = startWork;

        var meetingsList = existingMeetings.ToList();

        while (cursor >= startWork && cursor.AddMinutes(durationMinutes) <= endWork && cursor.AddMinutes(durationMinutes) > cursor)
        {
            var slotStartMsk = targetDate.ToDateTime(cursor);
            var slotEndMsk = slotStartMsk.AddMinutes(durationMinutes);

            var slotStartUtc = TimeZoneInfo.ConvertTimeToUtc(slotStartMsk, mskZone);
            var slotEndUtc = TimeZoneInfo.ConvertTimeToUtc(slotEndMsk, mskZone);

            var hasConflict = meetingsList.Any(m =>
            {
                var meetingStart = m.StartTime;
                var meetingEnd = m.EndTime ?? m.StartTime.AddMinutes(30);
                return slotStartUtc < meetingEnd.AddMinutes(bufferMinutes) &&
                       slotEndUtc > meetingStart.AddMinutes(-bufferMinutes);
            });

            if (!hasConflict)
            {
                availableSlots.Add(new TimeSlot
                {
                    StartTimeUtc = slotStartUtc,
                    EndTimeUtc = slotEndUtc,
                    Label = $"{cursor:HH\\:mm} - {cursor.AddMinutes(durationMinutes):HH\\:mm}"
                });
            }

            var nextCursor = cursor.AddMinutes(stepMinutes);
            if (nextCursor <= cursor) break;
            cursor = nextCursor;
        }

        return availableSlots;
    }

    public async Task<List<TimeSlot>> FindFreeSlotsAsync(
        int organizerId,
        List<int> participantIds,
        DateOnly targetDate,
        int durationMinutes,
        TimeOnly? workingStart = null,
        TimeOnly? workingEnd = null,
        int bufferMinutes = 10,
        CancellationToken ct = default)
    {
        var startWork = workingStart ?? new TimeOnly(9, 0);
        var endWork = workingEnd ?? new TimeOnly(18, 0);

        var mskZone = GetMoscowTimeZone();
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(targetDate.ToDateTime(startWork), mskZone);
        var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(targetDate.ToDateTime(endWork), mskZone);

        var allEmployeeIds = participantIds.Concat(new[] { organizerId }).Distinct().ToArray();

        await using var conn = await CreateConnectionAsync(ct);
        const string querySql = """
            SELECT * FROM meetings
            WHERE status != 'cancelled'
              AND start_time < @DayEndUtc
              AND COALESCE(end_time, start_time + INTERVAL '30 minutes') > @DayStartUtc
              AND (
                organizer_id = ANY(@EmployeeIds)
                OR EXISTS (
                  SELECT 1 FROM jsonb_array_elements_text(COALESCE(participant_ids, '[]'::jsonb)) p
                  WHERE p ~ '^[0-9]+$' AND p::int = ANY(@EmployeeIds)
                )
              );
        """;

        var existingMeetings = (await conn.QueryAsync<Meeting>(new CommandDefinition(querySql, new
        {
            DayStartUtc = dayStartUtc,
            DayEndUtc = dayEndUtc,
            EmployeeIds = allEmployeeIds
        }, cancellationToken: ct))).ToList();

        return CalculateFreeSlots(existingMeetings, targetDate, durationMinutes, startWork, endWork, bufferMinutes, DateTime.UtcNow);
    }

    public async Task<List<Meeting>> GetUpcomingMeetingsAsync(int timeWindowMinutes = 30, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT * FROM meetings
            WHERE status = 'scheduled'
              AND start_time >= NOW() - INTERVAL '5 minutes'
              AND start_time <= NOW() + (@WindowMinutes || ' minutes')::interval
            ORDER BY start_time ASC;
        """;
        var meetings = await conn.QueryAsync<Meeting>(new CommandDefinition(sql, new { WindowMinutes = timeWindowMinutes }, cancellationToken: ct));
        return meetings.ToList();
    }

    public async Task<Meeting?> GetMeetingByIdAsync(Guid meetingId, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM meetings WHERE id = @Id;";
        return await conn.QueryFirstOrDefaultAsync<Meeting>(new CommandDefinition(sql, new { Id = meetingId }, cancellationToken: ct));
    }

    public async Task<List<MeetingNote>> GetMeetingNotesAsync(Guid meetingId, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM meeting_notes WHERE meeting_id = @MeetingId ORDER BY created_at ASC;";
        var notes = await conn.QueryAsync<MeetingNote>(new CommandDefinition(sql, new { MeetingId = meetingId }, cancellationToken: ct));
        return notes.ToList();
    }

    public async Task SaveMeetingNoteAsync(Guid meetingId, int? authorId, string noteText, string category = "general", CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            INSERT INTO meeting_notes (meeting_id, author_id, note_text, category)
            VALUES (@MeetingId, @AuthorId, @NoteText, @Category);
        """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new { MeetingId = meetingId, AuthorId = authorId, NoteText = noteText, Category = category }, cancellationToken: ct));
    }

    public Task<Guid> SaveMeetingProtocolAsync(Guid meetingId, string title, string summary, List<string> decisions, string? rawTranscript = null, string? docxPath = null, string? pdfPath = null, CancellationToken ct = default)
        => SaveMeetingProtocolFullAsync(meetingId, title, summary, decisions, null, null, null, null, "final", rawTranscript, docxPath, pdfPath, ct);

    public async Task<Guid> SaveMeetingProtocolFullAsync(
        Guid meetingId,
        string title,
        string summary,
        List<string> decisions,
        List<string>? proposed,
        List<string>? discussed,
        List<string>? risks,
        List<string>? requiresClarification,
        string status = "draft",
        string? rawTranscript = null,
        string? docxPath = null,
        string? pdfPath = null,
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            INSERT INTO meeting_protocols (
                meeting_id, title, summary, decisions, proposed, discussed, risks, requires_clarification,
                status, raw_transcript, file_docx_path, file_pdf_path
            )
            VALUES (
                @MeetingId, @Title, @Summary, @Decisions::jsonb, @Proposed::jsonb, @Discussed::jsonb, @Risks::jsonb, @RequiresClarification::jsonb,
                @Status, @RawTranscript, @DocxPath, @PdfPath
            )
            RETURNING id;
        """;
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            MeetingId = meetingId,
            Title = title,
            Summary = summary,
            Decisions = JsonSerializer.Serialize(decisions ?? new List<string>()),
            Proposed = JsonSerializer.Serialize(proposed ?? new List<string>()),
            Discussed = JsonSerializer.Serialize(discussed ?? new List<string>()),
            Risks = JsonSerializer.Serialize(risks ?? new List<string>()),
            RequiresClarification = JsonSerializer.Serialize(requiresClarification ?? new List<string>()),
            Status = string.IsNullOrWhiteSpace(status) ? "draft" : status.Trim().ToLowerInvariant(),
            RawTranscript = rawTranscript,
            DocxPath = docxPath,
            PdfPath = pdfPath
        }, cancellationToken: ct));
    }

    public async Task UpdateMeetingProtocolDraftAsync(
        Guid meetingId,
        string title,
        string summary,
        List<string> decisions,
        List<string> proposed,
        List<string> discussed,
        List<string> risks,
        List<string> requiresClarification,
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            UPDATE meeting_protocols
            SET title = @Title,
                summary = @Summary,
                decisions = @Decisions::jsonb,
                proposed = @Proposed::jsonb,
                discussed = @Discussed::jsonb,
                risks = @Risks::jsonb,
                requires_clarification = @RequiresClarification::jsonb,
                updated_at = NOW()
            WHERE id = (
                SELECT id FROM meeting_protocols WHERE meeting_id = @MeetingId ORDER BY created_at DESC LIMIT 1
            );
        """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            MeetingId = meetingId,
            Title = title,
            Summary = summary,
            Decisions = JsonSerializer.Serialize(decisions ?? new List<string>()),
            Proposed = JsonSerializer.Serialize(proposed ?? new List<string>()),
            Discussed = JsonSerializer.Serialize(discussed ?? new List<string>()),
            Risks = JsonSerializer.Serialize(risks ?? new List<string>()),
            RequiresClarification = JsonSerializer.Serialize(requiresClarification ?? new List<string>()),
        }, cancellationToken: ct));
    }

    public async Task FinalizeMeetingProtocolAsync(Guid meetingId, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            UPDATE meeting_protocols
            SET status = 'final', updated_at = NOW()
            WHERE id = (
                SELECT id FROM meeting_protocols WHERE meeting_id = @MeetingId ORDER BY created_at DESC LIMIT 1
            );
        """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new { MeetingId = meetingId }, cancellationToken: ct));
    }

    public async Task<MeetingProtocol?> GetMeetingProtocolAsync(Guid meetingId, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM meeting_protocols WHERE meeting_id = @MeetingId ORDER BY created_at DESC LIMIT 1;";
        return await conn.QueryFirstOrDefaultAsync<MeetingProtocol>(new CommandDefinition(sql, new { MeetingId = meetingId }, cancellationToken: ct));
    }

    public Task<Guid> CreateActionItemAsync(Guid meetingId, Guid? protocolId, string title, string? description, int? responsibleId, DateTime? deadline, CancellationToken ct = default)
        => CreateActionItemWithQuoteAsync(meetingId, protocolId, title, description, responsibleId, deadline, null, "pending", ct);

    public async Task<Guid> CreateActionItemWithQuoteAsync(Guid meetingId, Guid? protocolId, string title, string? description, int? responsibleId, DateTime? deadline, string? sourceQuote, string status = "pending", CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var effectiveStatus = string.IsNullOrWhiteSpace(status) ? "pending" : status;
        const string sql = """
            INSERT INTO action_items (meeting_id, protocol_id, title, description, responsible_id, deadline, status, source_quote)
            VALUES (@MeetingId, @ProtocolId, @Title, @Description, @ResponsibleId, @Deadline, @Status, @SourceQuote)
            RETURNING id;
        """;
        return await conn.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            MeetingId = meetingId,
            ProtocolId = protocolId,
            Title = title,
            Description = description,
            ResponsibleId = responsibleId,
            Deadline = deadline,
            Status = effectiveStatus,
            SourceQuote = sourceQuote
        }, cancellationToken: ct));
    }

    public async Task<List<ActionItem>> GetOrphanActionItemsAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT * FROM action_items
            WHERE (responsible_id IS NULL OR deadline IS NULL)
              AND LOWER(status) NOT IN ('cancelled', 'completed', 'done', 'closed')
            ORDER BY created_at DESC;
        """;
        var items = await conn.QueryAsync<ActionItem>(new CommandDefinition(sql, cancellationToken: ct));
        return items.ToList();
    }

    public async Task<int> PurgeTestDataAsync(long? telegramId = null, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        if (telegramId.HasValue)
        {
            const string sqlByUser = """
                DELETE FROM chat_history
                WHERE session_id IN (SELECT id FROM chat_sessions WHERE telegram_id = @TelegramId);
                DELETE FROM chat_sessions WHERE telegram_id = @TelegramId;
                DELETE FROM audit_events WHERE actor_telegram_id = @TelegramId;
            """;
            return await conn.ExecuteAsync(new CommandDefinition(sqlByUser, new { TelegramId = telegramId.Value }, cancellationToken: ct));
        }

        const string sqlAllTemp = """
            DELETE FROM chat_history;
            DELETE FROM chat_sessions;
            DELETE FROM meeting_notes WHERE content ILIKE '%test%' OR content ILIKE '%тест%';
        """;
        return await conn.ExecuteAsync(new CommandDefinition(sqlAllTemp, cancellationToken: ct));
    }

    public async Task<List<ActionItem>> GetActionItemsByMeetingAsync(Guid meetingId, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = "SELECT * FROM action_items WHERE meeting_id = @MeetingId ORDER BY created_at ASC;";
        var items = await conn.QueryAsync<ActionItem>(new CommandDefinition(sql, new { MeetingId = meetingId }, cancellationToken: ct));
        return items.ToList();
    }

    public async Task<List<ActionItem>> GetActionItemsByResponsibleAsync(int employeeId, string? statusFilter = null, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = "SELECT * FROM action_items WHERE responsible_id = @ResponsibleId";
        if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            sql += " AND status = @StatusFilter";
        }
        sql += " ORDER BY deadline ASC NULLS LAST;";

        var items = await conn.QueryAsync<ActionItem>(new CommandDefinition(sql, new { ResponsibleId = employeeId, StatusFilter = statusFilter }, cancellationToken: ct));
        return items.ToList();
    }

    private sealed record MeetingRow(
        Guid Id,
        string Title,
        string? Description,
        DateTime StartTime,
        DateTime? EndTime,
        string Status,
        int? OrganizerId,
        string? ParticipantIdsJson,
        string? CalendarEventId,
        string? MetadataJson,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public async Task EnsureNotificationSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            ALTER TABLE employees
                ADD COLUMN IF NOT EXISTS notifications_enabled BOOLEAN NOT NULL DEFAULT TRUE;

            CREATE TABLE IF NOT EXISTS notification_deliveries (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                dedup_key VARCHAR(255) NOT NULL UNIQUE,
                notification_type VARCHAR(100) NOT NULL,
                recipient_employee_id INT REFERENCES employees(id) ON DELETE SET NULL,
                recipient_telegram_id BIGINT NOT NULL DEFAULT 0,
                channel_type VARCHAR(50) NOT NULL DEFAULT 'telegram',
                recipient_address VARCHAR(255),
                entity_id UUID,
                status VARCHAR(30) NOT NULL DEFAULT 'pending',
                error_message TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                sent_at TIMESTAMPTZ
            );

            ALTER TABLE notification_deliveries
                ADD COLUMN IF NOT EXISTS channel_type VARCHAR(50) NOT NULL DEFAULT 'telegram';
            ALTER TABLE notification_deliveries
                ADD COLUMN IF NOT EXISTS recipient_address VARCHAR(255);

            CREATE INDEX IF NOT EXISTS idx_notification_deliveries_type_created
                ON notification_deliveries(notification_type, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_notification_deliveries_telegram_id
                ON notification_deliveries(recipient_telegram_id);

            CREATE TABLE IF NOT EXISTS employee_notification_preferences (
                employee_id INT PRIMARY KEY REFERENCES employees(id) ON DELETE CASCADE,
                master_id BIGINT UNIQUE NOT NULL,
                digest_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                digest_time_msk VARCHAR(5) NOT NULL DEFAULT '09:00',
                digest_weekdays_only BOOLEAN NOT NULL DEFAULT TRUE,
                pre_meeting_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                pre_meeting_window_minutes INT NOT NULL DEFAULT 30,
                task_deadlines_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                task_due_soon_hours INT NOT NULL DEFAULT 24,
                task_urgent_hours INT NOT NULL DEFAULT 2,
                quiet_hours_start_msk VARCHAR(5),
                quiet_hours_end_msk VARCHAR(5),
                preferred_channel VARCHAR(50) NOT NULL DEFAULT 'telegram',
                channel_address VARCHAR(255),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_emp_notif_prefs_master_id
                ON employee_notification_preferences(master_id);

            CREATE TABLE IF NOT EXISTS user_identities (
                id SERIAL PRIMARY KEY,
                employee_id INT NOT NULL REFERENCES employees(id) ON DELETE CASCADE,
                master_id BIGINT NOT NULL,
                provider VARCHAR(50) NOT NULL,
                external_user_id VARCHAR(255) NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE(provider, external_user_id)
            );

            CREATE INDEX IF NOT EXISTS idx_user_identities_master_id
                ON user_identities(master_id);
        """;
        await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<EmployeeNotificationPreferences> GetNotificationPreferencesAsync(
        int employeeId,
        long masterId = 0,
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT employee_id AS EmployeeId,
                   master_id AS MasterId,
                   digest_enabled AS DigestEnabled,
                   digest_time_msk AS DigestTimeMsk,
                   digest_weekdays_only AS DigestWeekdaysOnly,
                   pre_meeting_enabled AS PreMeetingEnabled,
                   pre_meeting_window_minutes AS PreMeetingWindowMinutes,
                   task_deadlines_enabled AS TaskDeadlinesEnabled,
                   task_due_soon_hours AS TaskDueSoonHours,
                   task_urgent_hours AS TaskUrgentHours,
                   quiet_hours_start_msk AS QuietHoursStartMsk,
                   quiet_hours_end_msk AS QuietHoursEndMsk,
                   preferred_channel AS PreferredChannel,
                   channel_address AS ChannelAddress,
                   updated_at AS UpdatedAt
            FROM employee_notification_preferences
            WHERE employee_id = @EmployeeId
            LIMIT 1;
        """;

        var row = await conn.QueryFirstOrDefaultAsync<EmployeeNotificationPreferences>(
            new CommandDefinition(sql, new { EmployeeId = employeeId }, cancellationToken: ct));

        if (row != null)
            return row;

        var resolvedMasterId = masterId;
        if (resolvedMasterId <= 0)
        {
            const string masterSql = "SELECT master_id FROM employees WHERE id = @EmployeeId LIMIT 1;";
            resolvedMasterId = await conn.QueryFirstOrDefaultAsync<long?>(
                new CommandDefinition(masterSql, new { EmployeeId = employeeId }, cancellationToken: ct)) ?? 0;
        }

        return EmployeeNotificationPreferences.CreateDefault(employeeId, resolvedMasterId);
    }

    public async Task<IReadOnlyDictionary<int, EmployeeNotificationPreferences>> GetAllNotificationPreferencesAsync(
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT employee_id AS EmployeeId,
                   master_id AS MasterId,
                   digest_enabled AS DigestEnabled,
                   digest_time_msk AS DigestTimeMsk,
                   digest_weekdays_only AS DigestWeekdaysOnly,
                   pre_meeting_enabled AS PreMeetingEnabled,
                   pre_meeting_window_minutes AS PreMeetingWindowMinutes,
                   task_deadlines_enabled AS TaskDeadlinesEnabled,
                   task_due_soon_hours AS TaskDueSoonHours,
                   task_urgent_hours AS TaskUrgentHours,
                   quiet_hours_start_msk AS QuietHoursStartMsk,
                   quiet_hours_end_msk AS QuietHoursEndMsk,
                   preferred_channel AS PreferredChannel,
                   channel_address AS ChannelAddress,
                   updated_at AS UpdatedAt
            FROM employee_notification_preferences
            ORDER BY employee_id;
        """;

        var rows = await conn.QueryAsync<EmployeeNotificationPreferences>(
            new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToDictionary(r => r.EmployeeId);
    }

    public async Task<EmployeeNotificationPreferences> UpsertNotificationPreferencesAsync(
        int employeeId,
        long masterId,
        UpdateNotificationPreferencesPatch patch,
        CancellationToken ct = default)
    {
        var current = await GetNotificationPreferencesAsync(employeeId, masterId, ct);
        var effectiveMasterId = masterId > 0 ? masterId : current.MasterId;

        if (effectiveMasterId <= 0)
        {
            await using var masterConn = await CreateConnectionAsync(ct);
            const string masterSql = "SELECT master_id FROM employees WHERE id = @EmployeeId LIMIT 1;";
            effectiveMasterId = await masterConn.QueryFirstOrDefaultAsync<long?>(
                new CommandDefinition(masterSql, new { EmployeeId = employeeId }, cancellationToken: ct)) ?? 0;
        }

        string? nextQuietStart = current.QuietHoursStartMsk;
        string? nextQuietEnd = current.QuietHoursEndMsk;
        if (patch.ClearQuietHours)
        {
            nextQuietStart = null;
            nextQuietEnd = null;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(patch.QuietHoursStartMsk))
                nextQuietStart = Notifications.NotificationTimeHelper.NormalizeTimeMsk(patch.QuietHoursStartMsk, current.QuietHoursStartMsk ?? "22:00");
            if (!string.IsNullOrWhiteSpace(patch.QuietHoursEndMsk))
                nextQuietEnd = Notifications.NotificationTimeHelper.NormalizeTimeMsk(patch.QuietHoursEndMsk, current.QuietHoursEndMsk ?? "08:00");
        }

        var nextDigestTime = !string.IsNullOrWhiteSpace(patch.DigestTimeMsk)
            ? Notifications.NotificationTimeHelper.NormalizeTimeMsk(patch.DigestTimeMsk, current.DigestTimeMsk)
            : current.DigestTimeMsk;

        var preMeetingWindow = Math.Clamp(patch.PreMeetingWindowMinutes ?? current.PreMeetingWindowMinutes, 5, 240);
        var dueSoonHours = Math.Clamp(patch.TaskDueSoonHours ?? current.TaskDueSoonHours, 1, 168);
        var urgentHours = Math.Clamp(patch.TaskUrgentHours ?? current.TaskUrgentHours, 1, 72);
        if (urgentHours > dueSoonHours)
            urgentHours = dueSoonHours;

        var nextPreferredChannel = !string.IsNullOrWhiteSpace(patch.PreferredChannel)
            ? patch.PreferredChannel.Trim().ToLowerInvariant()
            : current.PreferredChannel;

        var updated = new EmployeeNotificationPreferences
        {
            EmployeeId = employeeId,
            MasterId = effectiveMasterId,
            DigestEnabled = patch.DigestEnabled ?? current.DigestEnabled,
            DigestTimeMsk = nextDigestTime,
            DigestWeekdaysOnly = patch.DigestWeekdaysOnly ?? current.DigestWeekdaysOnly,
            PreMeetingEnabled = patch.PreMeetingEnabled ?? current.PreMeetingEnabled,
            PreMeetingWindowMinutes = preMeetingWindow,
            TaskDeadlinesEnabled = patch.TaskDeadlinesEnabled ?? current.TaskDeadlinesEnabled,
            TaskDueSoonHours = dueSoonHours,
            TaskUrgentHours = urgentHours,
            QuietHoursStartMsk = nextQuietStart,
            QuietHoursEndMsk = nextQuietEnd,
            PreferredChannel = nextPreferredChannel,
            ChannelAddress = patch.ChannelAddress ?? current.ChannelAddress,
            UpdatedAt = DateTime.UtcNow
        };

        await using var conn = await CreateConnectionAsync(ct);

        if (patch.NotificationsEnabled.HasValue)
        {
            const string toggleSql = """
                UPDATE employees
                SET notifications_enabled = @Enabled,
                    updated_at = NOW()
                WHERE id = @EmployeeId;
            """;
            await conn.ExecuteAsync(
                new CommandDefinition(toggleSql, new { EmployeeId = employeeId, Enabled = patch.NotificationsEnabled.Value }, cancellationToken: ct));
        }

        if (effectiveMasterId > 0)
        {
            const string upsertSql = """
                INSERT INTO employee_notification_preferences (
                    employee_id, master_id, digest_enabled, digest_time_msk, digest_weekdays_only,
                    pre_meeting_enabled, pre_meeting_window_minutes,
                    task_deadlines_enabled, task_due_soon_hours, task_urgent_hours,
                    quiet_hours_start_msk, quiet_hours_end_msk,
                    preferred_channel, channel_address, updated_at
                )
                VALUES (
                    @EmployeeId, @MasterId, @DigestEnabled, @DigestTimeMsk, @DigestWeekdaysOnly,
                    @PreMeetingEnabled, @PreMeetingWindowMinutes,
                    @TaskDeadlinesEnabled, @TaskDueSoonHours, @TaskUrgentHours,
                    @QuietHoursStartMsk, @QuietHoursEndMsk,
                    @PreferredChannel, @ChannelAddress, NOW()
                )
                ON CONFLICT (employee_id) DO UPDATE SET
                    master_id = EXCLUDED.master_id,
                    digest_enabled = EXCLUDED.digest_enabled,
                    digest_time_msk = EXCLUDED.digest_time_msk,
                    digest_weekdays_only = EXCLUDED.digest_weekdays_only,
                    pre_meeting_enabled = EXCLUDED.pre_meeting_enabled,
                    pre_meeting_window_minutes = EXCLUDED.pre_meeting_window_minutes,
                    task_deadlines_enabled = EXCLUDED.task_deadlines_enabled,
                    task_due_soon_hours = EXCLUDED.task_due_soon_hours,
                    task_urgent_hours = EXCLUDED.task_urgent_hours,
                    quiet_hours_start_msk = EXCLUDED.quiet_hours_start_msk,
                    quiet_hours_end_msk = EXCLUDED.quiet_hours_end_msk,
                    preferred_channel = EXCLUDED.preferred_channel,
                    channel_address = EXCLUDED.channel_address,
                    updated_at = NOW();
            """;
            await conn.ExecuteAsync(new CommandDefinition(upsertSql, updated, cancellationToken: ct));
        }

        return updated;
    }

    public async Task<bool> ResetNotificationDeliveryByDedupKeyAsync(string dedupKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dedupKey))
            return false;

        await using var conn = await CreateConnectionAsync(ct);
        const string sql = "DELETE FROM notification_deliveries WHERE dedup_key = @DedupKey;";
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { DedupKey = dedupKey.Trim() }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task UpsertUserIdentityAsync(
        int employeeId,
        long masterId,
        string provider,
        string externalUserId,
        CancellationToken ct = default)
    {
        if (masterId <= 0 || string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(externalUserId))
            return;

        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            INSERT INTO user_identities (employee_id, master_id, provider, external_user_id)
            VALUES (@EmployeeId, @MasterId, @Provider, @ExternalUserId)
            ON CONFLICT (provider, external_user_id) DO UPDATE SET
                employee_id = EXCLUDED.employee_id,
                master_id = EXCLUDED.master_id;
        """;
        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    EmployeeId = employeeId,
                    MasterId = masterId,
                    Provider = provider.Trim().ToLowerInvariant(),
                    ExternalUserId = externalUserId.Trim()
                },
                cancellationToken: ct));
    }

    public async Task<Employee?> GetEmployeeByTelegramIdAsync(long telegramId, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT
                id AS Id,
                master_id AS MasterId,
                first_name AS FirstName,
                last_name AS LastName,
                middle_name AS MiddleName,
                email AS Email,
                telegram_id AS TelegramId,
                telegram_username AS TelegramUsername,
                whatsapp_id AS WhatsappId,
                department AS Department,
                position AS Position,
                notifications_enabled AS NotificationsEnabled,
                is_active AS IsActive,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM employees
            WHERE telegram_id = @TelegramId
            ORDER BY is_active DESC, id ASC
            LIMIT 1;
        """;
        return await conn.QueryFirstOrDefaultAsync<Employee>(
            new CommandDefinition(sql, new { TelegramId = telegramId }, cancellationToken: ct));
    }

    public async Task<bool> SetEmployeeNotificationsEnabledAsync(long masterId, bool enabled, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            UPDATE employees
            SET notifications_enabled = @Enabled,
                updated_at = NOW()
            WHERE master_id = @MasterId;
        """;
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { MasterId = masterId, Enabled = enabled }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task<List<Meeting>> GetMeetingsInRangeUtcAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            SELECT
                id AS Id,
                title AS Title,
                description AS Description,
                start_time AS StartTime,
                end_time AS EndTime,
                status AS Status,
                organizer_id AS OrganizerId,
                participant_ids::text AS ParticipantIdsJson,
                calendar_event_id AS CalendarEventId,
                metadata::text AS MetadataJson,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM meetings
            WHERE status = 'scheduled'
              AND start_time >= @StartUtc
              AND start_time <= @EndUtc
            ORDER BY start_time ASC;
        """;

        var rows = await conn.QueryAsync<MeetingRow>(new CommandDefinition(sql, new
        {
            StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            EndUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)
        }, cancellationToken: ct));

        return rows.Select(row => new Meeting
        {
            Id = row.Id,
            Title = row.Title,
            Description = row.Description,
            StartTime = row.StartTime,
            EndTime = row.EndTime,
            Status = row.Status,
            OrganizerId = row.OrganizerId,
            ParticipantIds = ParseParticipantIds(row.ParticipantIdsJson),
            CalendarEventId = row.CalendarEventId,
            MetadataJson = row.MetadataJson ?? "{}",
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        }).ToList();
    }

    public async Task<List<ActionItem>> GetOpenActionItemsAsync(int? employeeId = null, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        var sql = """
            SELECT
                id AS Id,
                meeting_id AS MeetingId,
                protocol_id AS ProtocolId,
                title AS Title,
                description AS Description,
                responsible_id AS ResponsibleId,
                deadline AS Deadline,
                status AS Status,
                bitrix24_task_id AS Bitrix24TaskId,
                created_at AS CreatedAt,
                updated_at AS UpdatedAt
            FROM action_items
            WHERE responsible_id IS NOT NULL
              AND LOWER(status) NOT IN ('cancelled', 'completed', 'done', 'closed')
        """;

        if (employeeId.HasValue)
        {
            sql += "\n  AND responsible_id = @EmployeeId";
        }

        sql += "\nORDER BY deadline ASC NULLS LAST, created_at ASC;";

        var items = await conn.QueryAsync<ActionItem>(
            new CommandDefinition(sql, new { EmployeeId = employeeId }, cancellationToken: ct));
        return items.ToList();
    }

    public Task<Guid?> TryClaimNotificationDeliveryAsync(
        string dedupKey,
        string notificationType,
        int? recipientEmployeeId,
        long recipientTelegramId,
        Guid? entityId = null,
        CancellationToken ct = default) =>
        TryClaimNotificationDeliveryAsync(dedupKey, notificationType, recipientEmployeeId, recipientTelegramId, entityId, ct, "telegram", null);

    public async Task<Guid?> TryClaimNotificationDeliveryAsync(
        string dedupKey,
        string notificationType,
        int? recipientEmployeeId,
        long recipientTelegramId,
        Guid? entityId,
        CancellationToken ct,
        string channelType,
        string? recipientAddress)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            INSERT INTO notification_deliveries (
                dedup_key,
                notification_type,
                recipient_employee_id,
                recipient_telegram_id,
                channel_type,
                recipient_address,
                entity_id,
                status
            )
            VALUES (
                @DedupKey,
                @NotificationType,
                @RecipientEmployeeId,
                @RecipientTelegramId,
                @ChannelType,
                @RecipientAddress,
                @EntityId,
                'pending'
            )
            ON CONFLICT (dedup_key) DO UPDATE
            SET status = 'pending',
                created_at = NOW(),
                error_message = NULL
            WHERE notification_deliveries.status = 'pending'
              AND notification_deliveries.created_at < NOW() - INTERVAL '10 minutes'
            RETURNING id;
        """;

        return await conn.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(sql, new
        {
            DedupKey = dedupKey,
            NotificationType = notificationType,
            RecipientEmployeeId = recipientEmployeeId,
            RecipientTelegramId = recipientTelegramId,
            ChannelType = string.IsNullOrWhiteSpace(channelType) ? "telegram" : channelType.Trim().ToLowerInvariant(),
            RecipientAddress = recipientAddress ?? (recipientTelegramId > 0 ? recipientTelegramId.ToString() : null),
            EntityId = entityId
        }, cancellationToken: ct));
    }

    public async Task CompleteNotificationDeliveryAsync(
        Guid deliveryId,
        string status,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            UPDATE notification_deliveries
            SET status = @Status,
                error_message = @ErrorMessage,
                sent_at = CASE WHEN @Status = 'sent' THEN NOW() ELSE sent_at END
            WHERE id = @Id;
        """;

        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = deliveryId,
            Status = status,
            ErrorMessage = errorMessage
        }, cancellationToken: ct));
    }

    private static List<int> ParseParticipantIds(string? participantIdsJson)
    {
        if (string.IsNullOrWhiteSpace(participantIdsJson))
        {
            return new List<int>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<int>>(participantIdsJson) ?? new List<int>();
        }
        catch (JsonException)
        {
            return new List<int>();
        }
    }

    public async Task UpdateActionItemStatusAsync(Guid actionItemId, string status, int? bitrix24TaskId = null, CancellationToken ct = default)
    {
        await using var conn = await CreateConnectionAsync(ct);
        const string sql = """
            UPDATE action_items 
            SET status = @Status,
                bitrix24_task_id = COALESCE(@BitrixTaskId, bitrix24_task_id),
                updated_at = NOW()
            WHERE id = @Id;
        """;
        await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = actionItemId, Status = status, BitrixTaskId = bitrix24TaskId }, cancellationToken: ct));
    }
}

internal sealed class JsonbListTypeHandler<T> : SqlMapper.TypeHandler<List<T>>
{
    public override void SetValue(System.Data.IDbDataParameter parameter, List<T>? value)
    {
        parameter.Value = value == null ? "[]" : JsonSerializer.Serialize(value);
    }

    public override List<T> Parse(object value)
    {
        if (value is null or DBNull) return new List<T>();
        if (value is List<T> typed) return typed;
        var json = value.ToString();
        if (string.IsNullOrWhiteSpace(json)) return new List<T>();
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }
}

