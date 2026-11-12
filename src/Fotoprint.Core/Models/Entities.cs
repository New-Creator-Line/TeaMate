using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fotoprint.Core.Models;

public class Employee
{
    public int Id { get; set; }
    public long MasterId { get; set; } // Уникальный числовой идентификатор (только цифры)
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string? Email { get; set; }
    public long? TelegramId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? WhatsappId { get; set; }
    public string Department { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public List<string> AccessGroups { get; set; } = new();
    public bool NotificationsEnabled { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(MiddleName)
        ? $"{LastName} {FirstName}"
        : $"{LastName} {FirstName} {MiddleName}";
}

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "general";
}

public class ChatSession
{
    public Guid Id { get; set; } // Уникальный UUID SessionId текущего диалога
    public long MasterId { get; set; } // Привязка к MasterId пользователя
    public int? EmployeeId { get; set; }
    public string MessengerType { get; set; } = "telegram";
    public long MessengerChatId { get; set; }
    public long TelegramChatId { get; set; } // Legacy fallback
    public bool IsActive { get; set; } = true;
    public DateTime? ClosedAt { get; set; }
    public string Title { get; set; } = "Новая сессия";
    public string CurrentBranch { get; set; } = "general";
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; } // UUID сессии (при смене диалога меняется)
    public long MasterId { get; set; } // Цифровой MasterId автора
    public string ContextId { get; set; } = string.Empty;
    public string Role { get; set; } = "user"; // user, assistant, system
    public string Content { get; set; } = string.Empty;
    public bool IsFrozen { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class ContextBatch
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string? Summary { get; set; }
    public int MessageCount { get; set; }
    public bool IsContextSwitched { get; set; }
    public string? Branch { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class CorporateDocument
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty; // pdf, docx, xlsx, txt
    public string? FilePath { get; set; }
    public string? Department { get; set; }
    public List<string> AllowedAccessGroups { get; set; } = new();
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CorporateDocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public float[]? Embedding { get; set; }
    public List<string> AllowedAccessGroups { get; set; } = new();
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class Meeting
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = "scheduled"; // scheduled, in_progress, completed, cancelled
    public int? OrganizerId { get; set; }
    public List<int> ParticipantIds { get; set; } = new();
    public string? CalendarEventId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public string? Metadata { get => MetadataJson; set => MetadataJson = value ?? "{}"; }
    public string ItemType { get; set; } = "meeting";
    public string Category { get; set; } = "production";
    public bool IsAllDay { get; set; }
    public bool IsRecurring { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CalendarEventItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("meeting_id")]
    public Guid MeetingId { get; set; }

    [JsonPropertyName("item_type")]
    public string ItemType { get; set; } = "meeting"; // meeting | task | routine

    [JsonPropertyName("category")]
    public string Category { get; set; } = "production"; // production | sales | accounting | management | tasks | routine

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("start_time")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("end_time")]
    public string? EndTime { get; set; }

    [JsonPropertyName("is_all_day")]
    public bool IsAllDay { get; set; }

    [JsonPropertyName("is_recurring")]
    public bool IsRecurring { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "scheduled";

    [JsonPropertyName("organizer_id")]
    public int? OrganizerId { get; set; }

    [JsonPropertyName("organizer_name")]
    public string OrganizerName { get; set; } = string.Empty;

    [JsonPropertyName("participant_ids")]
    public List<int> ParticipantIds { get; set; } = new();

    [JsonPropertyName("participants")]
    public List<string> Participants { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public class MeetingNote
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public int? AuthorId { get; set; }
    public string NoteText { get; set; } = string.Empty;
    public string Category { get; set; } = "general"; // discussion, decision, question, action_point
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class MeetingProtocol
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Decisions { get; set; } = new();
    public List<string> Proposed { get; set; } = new();
    public List<string> Discussed { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public List<string> RequiresClarification { get; set; } = new();
    public string Status { get; set; } = "draft"; // draft | final
    public string? RawTranscript { get; set; }
    public string? FileDocxPath { get; set; }
    public string? FilePdfPath { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ActionItem
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public Guid? ProtocolId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? ResponsibleId { get; set; }
    public DateTime? Deadline { get; set; }
    public string Status { get; set; } = "pending"; // pending, confirmed, exported_to_bitrix, cancelled, требует уточнения
    public int? Bitrix24TaskId { get; set; }
    public string? SourceQuote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed record EmployeeNotificationPreferences
{
    public int EmployeeId { get; init; }
    public long MasterId { get; init; }
    public bool DigestEnabled { get; init; } = true;
    public string DigestTimeMsk { get; init; } = "09:00";
    public bool DigestWeekdaysOnly { get; init; } = true;
    public bool PreMeetingEnabled { get; init; } = true;
    public int PreMeetingWindowMinutes { get; init; } = 30;
    public bool TaskDeadlinesEnabled { get; init; } = true;
    public int TaskDueSoonHours { get; init; } = 24;
    public int TaskUrgentHours { get; init; } = 2;
    public string? QuietHoursStartMsk { get; init; }
    public string? QuietHoursEndMsk { get; init; }
    public string PreferredChannel { get; init; } = "telegram";
    public string? ChannelAddress { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    public static EmployeeNotificationPreferences CreateDefault(int employeeId, long masterId, int defaultDigestHourMsk = 9, int defaultPreMeetingMinutes = 30, int defaultDueSoonHours = 24, int defaultUrgentHours = 2) => new()
    {
        EmployeeId = employeeId,
        MasterId = masterId,
        DigestEnabled = true,
        DigestTimeMsk = $"{Math.Clamp(defaultDigestHourMsk, 0, 23):D2}:00",
        DigestWeekdaysOnly = true,
        PreMeetingEnabled = true,
        PreMeetingWindowMinutes = Math.Clamp(defaultPreMeetingMinutes, 5, 240),
        TaskDeadlinesEnabled = true,
        TaskDueSoonHours = Math.Clamp(defaultDueSoonHours, 1, 168),
        TaskUrgentHours = Math.Clamp(defaultUrgentHours, 1, 48),
        PreferredChannel = "telegram"
    };
}

public sealed record UpdateNotificationPreferencesPatch(
    [property: System.Text.Json.Serialization.JsonPropertyName("notifications_enabled")] bool? NotificationsEnabled = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("digest_enabled")] bool? DigestEnabled = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("digest_time_msk")] string? DigestTimeMsk = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("digest_weekdays_only")] bool? DigestWeekdaysOnly = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("pre_meeting_enabled")] bool? PreMeetingEnabled = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("pre_meeting_window_minutes")] int? PreMeetingWindowMinutes = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("task_deadlines_enabled")] bool? TaskDeadlinesEnabled = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("task_due_soon_hours")] int? TaskDueSoonHours = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("task_urgent_hours")] int? TaskUrgentHours = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("quiet_hours_start_msk")] string? QuietHoursStartMsk = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("quiet_hours_end_msk")] string? QuietHoursEndMsk = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("clear_quiet_hours")] bool ClearQuietHours = false,
    [property: System.Text.Json.Serialization.JsonPropertyName("preferred_channel")] string? PreferredChannel = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("channel_address")] string? ChannelAddress = null);

public sealed record UserIdentityMapping(
    int Id,
    int EmployeeId,
    long MasterId,
    string Provider,
    string ExternalUserId,
    DateTime CreatedAt);

public static class CalendarHelper
{
    public static string FormatShortName(string? lastName, string? firstName, string? middleName)
    {
        if (string.IsNullOrWhiteSpace(lastName))
            return firstName ?? "";
        var f = !string.IsNullOrWhiteSpace(firstName) ? $"{firstName[0]}." : "";
        var m = !string.IsNullOrWhiteSpace(middleName) ? $"{middleName[0]}." : "";
        var initials = $"{f}{m}".Trim();
        return string.IsNullOrEmpty(initials) ? lastName : $"{lastName} {initials}";
    }

    public static CalendarEventItem MapMeetingToCalendarItem(Meeting m, TimeZoneInfo mskZone, Func<int, string> formatEmpShort)
    {
        string itemType = "meeting";
        string category = "production";
        bool isAllDay = false;
        bool isRecurring = false;
        List<string> tags = new();

        if (!string.IsNullOrWhiteSpace(m.MetadataJson) && m.MetadataJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(m.MetadataJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("item_type", out var itProp))
                    itemType = itProp.GetString() ?? itemType;
                if (root.TryGetProperty("category", out var catProp))
                    category = catProp.GetString() ?? category;
                if (root.TryGetProperty("is_all_day", out var alldayProp))
                    isAllDay = alldayProp.GetBoolean();
                if (root.TryGetProperty("is_recurring", out var recProp))
                    isRecurring = recProp.GetBoolean();
                if (root.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                {
                    tags = tagsProp.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => !string.IsNullOrEmpty(t)).ToList();
                }
            }
            catch { }
        }

        var text = $"{m.Title} {m.Description}".ToLowerInvariant();

        if (tags.Count == 0)
        {
            if (text.Contains("heidelberg")) tags.Add("heidelberg");
            if (text.Contains("44102")) tags.Add("заказ-44102");
            if (text.Contains("альфа-маркет")) tags.Add("альфа-маркет");
            if (text.Contains("валы") || text.Contains("валов")) tags.Add("валы");
            if (text.Contains("бумаг")) tags.Add("бумага");
            if (text.Contains("счет") || text.Contains("оплат")) tags.Add("оплаты");
            if (tags.Count == 0) tags.Add(category);
        }

        if (string.IsNullOrEmpty(category) || category == "production")
        {
            if (text.Contains("продаж") || text.Contains("альфа-маркет") || text.Contains("клиент") || text.Contains("crm"))
                category = "sales";
            else if (text.Contains("бухгалтер") || text.Contains("счет") || text.Contains("дебитор") || text.Contains("себестоимост") || text.Contains("финанс"))
                category = "accounting";
            else if (text.Contains("руководств") || text.Contains("langfuse") || text.Contains("sla") || text.Contains("n8n") || text.Contains("стратег"))
                category = "management";
            else if (text.Contains("сводка") || text.Contains("выработка") || text.Contains("брака"))
                category = "routine";
        }

        var organizerName = m.OrganizerId.HasValue ? formatEmpShort(m.OrganizerId.Value) : "Не указан";
        var participants = (m.ParticipantIds ?? new List<int>()).Select(formatEmpShort).ToList();

        var startTimeMsk = TimeZoneInfo.ConvertTimeFromUtc(m.StartTime, mskZone);
        var endTimeMsk = m.EndTime.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(m.EndTime.Value, mskZone) : (DateTime?)null;

        return new CalendarEventItem
        {
            Id = m.Id.ToString(),
            MeetingId = m.Id,
            ItemType = itemType,
            Category = category,
            Title = m.Title,
            Description = m.Description,
            StartTime = isAllDay ? startTimeMsk.ToString("yyyy-MM-ddT00:00:00+03:00") : startTimeMsk.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            EndTime = isAllDay ? startTimeMsk.ToString("yyyy-MM-ddT23:59:59+03:00") : endTimeMsk?.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            IsAllDay = isAllDay,
            IsRecurring = isRecurring,
            Status = m.Status,
            OrganizerId = m.OrganizerId,
            OrganizerName = organizerName,
            ParticipantIds = m.ParticipantIds ?? new List<int>(),
            Participants = participants,
            Tags = tags
        };
    }

    public static CalendarEventItem MapActionItemToCalendarItem(ActionItem a, TimeZoneInfo mskZone, Func<int, string> formatEmpShort)
    {
        var responsibleName = a.ResponsibleId.HasValue ? formatEmpShort(a.ResponsibleId.Value) : "Не указан";
        var pIds = a.ResponsibleId.HasValue ? new List<int> { a.ResponsibleId.Value } : new List<int>();
        var pNames = a.ResponsibleId.HasValue ? new List<string> { responsibleName } : new List<string>();

        DateTime startMsk;
        DateTime endMsk;
        bool isAllDay = false;

        if (a.Deadline.HasValue)
        {
            var dMsk = TimeZoneInfo.ConvertTimeFromUtc(a.Deadline.Value, mskZone);
            if (dMsk.TimeOfDay == TimeSpan.Zero)
            {
                isAllDay = true;
                startMsk = dMsk.Date;
                endMsk = dMsk.Date.AddDays(1).AddTicks(-1);
            }
            else
            {
                startMsk = dMsk.AddMinutes(-30);
                endMsk = dMsk;
            }
        }
        else
        {
            var cMsk = TimeZoneInfo.ConvertTimeFromUtc(a.CreatedAt, mskZone);
            isAllDay = true;
            startMsk = cMsk.Date;
            endMsk = cMsk.Date.AddDays(1).AddTicks(-1);
        }

        var tags = new List<string> { "битрикс24", "задача" };
        var text = $"{a.Title} {a.Description}".ToLowerInvariant();
        if (text.Contains("heidelberg") || text.Contains("валы")) tags.Add("heidelberg");
        if (text.Contains("альфа-маркет")) tags.Add("альфа-маркет");

        return new CalendarEventItem
        {
            Id = a.Id.ToString(),
            MeetingId = a.MeetingId != Guid.Empty ? a.MeetingId : a.Id,
            ItemType = "task",
            Category = "tasks",
            Title = a.Title,
            Description = a.Description ?? "Поручение / Задача Битрикс24",
            StartTime = isAllDay ? startMsk.ToString("yyyy-MM-ddT00:00:00+03:00") : startMsk.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            EndTime = isAllDay ? endMsk.ToString("yyyy-MM-ddT23:59:59+03:00") : endMsk.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            IsAllDay = isAllDay,
            IsRecurring = false,
            Status = a.Status,
            OrganizerId = a.ResponsibleId,
            OrganizerName = responsibleName,
            ParticipantIds = pIds,
            Participants = pNames,
            Tags = tags
        };
    }

    public static List<CalendarEventItem> GetReferenceWeekDemoItems(TimeZoneInfo mskZone, Func<int, string> formatEmpShort)
    {
        var items = new List<CalendarEventItem>();

        // All-day routines across Nov 12 - Nov 18, 2026
        for (int day = 12; day <= 18; day++)
        {
            var dateStr = $"2026-11-{day:D2}";
            // 1. Утренняя сводка выработки цеха
            items.Add(new CalendarEventItem
            {
                Id = $"b0000001-11{day:D2}-2026-0000-000000000001",
                MeetingId = Guid.Parse($"b0000001-11{day:D2}-2026-0000-000000000001"),
                ItemType = "routine",
                Category = "routine",
                Title = "Утренняя сводка выработки цеха",
                Description = "Ежедневная утренняя пятиминутка начальника производства и директора, фиксация приоритетов дня",
                StartTime = $"{dateStr}T00:00:00+03:00",
                EndTime = $"{dateStr}T23:59:59+03:00",
                IsAllDay = true,
                IsRecurring = true,
                Status = "scheduled",
                OrganizerId = 1,
                OrganizerName = formatEmpShort(1),
                ParticipantIds = new List<int> { 1, 2 },
                Participants = new List<string> { formatEmpShort(1), formatEmpShort(2) },
                Tags = new List<string> { "рутина", "цех", "приоритеты" }
            });

            // 2. Контроль очереди Битрикс24
            items.Add(new CalendarEventItem
            {
                Id = $"b0000002-11{day:D2}-2026-0000-000000000002",
                MeetingId = Guid.Parse($"b0000002-11{day:D2}-2026-0000-000000000002"),
                ItemType = "routine",
                Category = "tasks",
                Title = "Контроль очереди Битрикс24",
                Description = "Мониторинг зависших тикетов и задач производства и отдела продаж",
                StartTime = $"{dateStr}T00:00:00+03:00",
                EndTime = $"{dateStr}T23:59:59+03:00",
                IsAllDay = true,
                IsRecurring = true,
                Status = "scheduled",
                OrganizerId = 2,
                OrganizerName = formatEmpShort(2),
                ParticipantIds = new List<int> { 2, 3 },
                Participants = new List<string> { formatEmpShort(2), formatEmpShort(3) },
                Tags = new List<string> { "битрикс24", "задачи", "контроль" }
            });

            // 3. Проверка норм брака Heidelberg
            items.Add(new CalendarEventItem
            {
                Id = $"b0000003-11{day:D2}-2026-0000-000000000003",
                MeetingId = Guid.Parse($"b0000003-11{day:D2}-2026-0000-000000000003"),
                ItemType = "routine",
                Category = "production",
                Title = "Проверка норм брака Heidelberg",
                Description = "Сверка технологического брака листового офсета с регламентом 2.5%",
                StartTime = $"{dateStr}T00:00:00+03:00",
                EndTime = $"{dateStr}T23:59:59+03:00",
                IsAllDay = true,
                IsRecurring = true,
                Status = "scheduled",
                OrganizerId = 2,
                OrganizerName = formatEmpShort(2),
                ParticipantIds = new List<int> { 2 },
                Participants = new List<string> { formatEmpShort(2) },
                Tags = new List<string> { "heidelberg", "нормы-брака", "цех" }
            });
        }

        // Thursday Nov 12, 2026
        items.Add(new CalendarEventItem
        {
            Id = "bbbbbbb1-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            MeetingId = Guid.Parse("bbbbbbb1-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ItemType = "meeting",
            Category = "production",
            Title = "Оперативка производства: тираж №44102 (ЛР АМД)",
            Description = "Разбор юстировки валов Heidelberg XL 75 и графика смен для каталогов Альфа-Маркет",
            StartTime = "2026-11-12T10:05:00+03:00",
            EndTime = "2026-11-12T11:30:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 2, 3 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(2), formatEmpShort(3) },
            Tags = new List<string> { "heidelberg", "заказ-44102", "производство", "планерка" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000112-1200-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000112-1200-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "production",
            Title = "Планерка ОМО / Снабжение бумаги",
            Description = "Сверка поставок мелованной бумаги 150г/м2, резервирование партий под тираж 50 000 экз.",
            StartTime = "2026-11-12T12:00:00+03:00",
            EndTime = "2026-11-12T13:25:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 2 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(2) },
            Tags = new List<string> { "снабжение", "бумага", "омо", "производство" }
        });

        // Overlapping task 12:00 - 12:30
        items.Add(new CalendarEventItem
        {
            Id = "eeeeeee1-eeee-eeee-eeee-eeeeeeeeeeee",
            MeetingId = Guid.Parse("bbbbbbb2-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ItemType = "task",
            Category = "tasks",
            Title = "Отправить спецификацию валов в Битрикс24",
            Description = "Сформировать и отправить поставщику спецификацию на валы Heidelberg в Битрикс24 (задача #1045)",
            StartTime = "2026-11-12T12:00:00+03:00",
            EndTime = "2026-11-12T12:30:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "pending",
            OrganizerId = 2,
            OrganizerName = formatEmpShort(2),
            ParticipantIds = new List<int> { 2 },
            Participants = new List<string> { formatEmpShort(2) },
            Tags = new List<string> { "битрикс24", "heidelberg", "валы", "задача" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000112-1335-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000112-1335-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "production",
            Title = "Техконтроль печатного цеха Heidelberg (ПЗ ФизК)",
            Description = "Инструментальная проверка состояния красочных аппаратов и биения валов печатного цеха",
            StartTime = "2026-11-12T13:35:00+03:00",
            EndTime = "2026-11-12T15:00:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 2,
            OrganizerName = formatEmpShort(2),
            ParticipantIds = new List<int> { 2 },
            Participants = new List<string> { formatEmpShort(2) },
            Tags = new List<string> { "техконтроль", "heidelberg", "цех", "производство" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000112-1530-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000112-1530-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "sales",
            Title = "Согласование коммерческих условий Альфа-Маркет (ЛК АМД)",
            Description = "Переговоры с коммерческим директором Альфа-Маркет по предоплате 100% и скидке 15%",
            StartTime = "2026-11-12T15:30:00+03:00",
            EndTime = "2026-11-12T16:55:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 3 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(3) },
            Tags = new List<string> { "альфа-маркет", "клиенты", "скидки", "продажи" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000112-1900-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000112-1900-2026-0000-000000000001"),
            ItemType = "task",
            Category = "tasks",
            Title = "Вечерний чек-ин смены",
            Description = "Контрольная сдача смены печатников и передача ночного задания на тираж №44102",
            StartTime = "2026-11-12T19:00:00+03:00",
            EndTime = "2026-11-12T19:30:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "pending",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 2 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(2) },
            Tags = new List<string> { "чек-ин", "смена", "выработка", "задача" }
        });

        // Friday Nov 13, 2026
        items.Add(new CalendarEventItem
        {
            Id = "b0000113-1900-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000113-1900-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "sales",
            Title = "Итоги недели по отгрузкам (ЛК МПДиС)",
            Description = "Сводка выполненных отгрузок за неделю, контроль дебиторской задолженности и закрытие актов",
            StartTime = "2026-11-13T19:00:00+03:00",
            EndTime = "2026-11-13T20:25:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 3 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(3) },
            Tags = new List<string> { "отгрузки", "продажи", "итоги-недели" }
        });

        // Saturday Nov 14, 2026
        items.Add(new CalendarEventItem
        {
            Id = "b0000114-1200-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000114-1200-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "production",
            Title = "Аудит профилактики валов (ЛР АМД)",
            Description = "Плановое техническое обслуживание и смывка красочных валиков офсетных секций",
            StartTime = "2026-11-14T12:00:00+03:00",
            EndTime = "2026-11-14T13:25:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 2,
            OrganizerName = formatEmpShort(2),
            ParticipantIds = new List<int> { 2 },
            Participants = new List<string> { formatEmpShort(2) },
            Tags = new List<string> { "аудит", "валы", "heidelberg", "производство" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000114-1335-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000114-1335-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "accounting",
            Title = "Сверка складских остатков и БД (ЛР СУБД)",
            Description = "Сверка остатков бумаги, красок и лаков в 1С с фактическими инвентаризационными описями",
            StartTime = "2026-11-14T13:35:00+03:00",
            EndTime = "2026-11-14T15:00:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 4,
            OrganizerName = formatEmpShort(4),
            ParticipantIds = new List<int> { 4 },
            Participants = new List<string> { formatEmpShort(4) },
            Tags = new List<string> { "1с", "инвентаризация", "склад", "бухгалтерия" }
        });

        // Monday Nov 16, 2026
        items.Add(new CalendarEventItem
        {
            Id = "b0000116-1705-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000116-1705-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "accounting",
            Title = "Финансовый комитет и закрытие счетов (ЛР СУБД)",
            Description = "Утверждение реестра оплат недели: аванс поставщику валов и оплата сырья",
            StartTime = "2026-11-16T17:05:00+03:00",
            EndTime = "2026-11-16T18:30:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 4 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(4) },
            Tags = new List<string> { "финансы", "платежи", "бухгалтерия", "счета" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000116-1900-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000116-1900-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "sales",
            Title = "План продаж корпоративным клиентам (ЛК ОМО)",
            Description = "Анализ воронки ключевых корпоративных клиентов, новогодний каталог и загрузка цехов",
            StartTime = "2026-11-16T19:00:00+03:00",
            EndTime = "2026-11-16T20:25:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 3 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(3) },
            Tags = new List<string> { "продажи", "b2b", "планирование", "клиенты" }
        });

        // Tuesday Nov 17, 2026
        items.Add(new CalendarEventItem
        {
            Id = "b0000117-1530-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000117-1530-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "production",
            Title = "Калибровка цветопробы цеха (ПЗ ФизК)",
            Description = "Проверка спектрофотометра и линеаризация выводного устройства CTP",
            StartTime = "2026-11-17T15:30:00+03:00",
            EndTime = "2026-11-17T16:55:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 2,
            OrganizerName = formatEmpShort(2),
            ParticipantIds = new List<int> { 2 },
            Participants = new List<string> { formatEmpShort(2) },
            Tags = new List<string> { "цветопроба", "калибровка", "цех", "производство" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000117-1705-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000117-1705-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "management",
            Title = "Разбор аналитики Langfuse и SLA (ЛК САиИО)",
            Description = "Оценка времени ответа Copilot, анализ логов трассировок и удовлетворенности пользователей",
            StartTime = "2026-11-17T17:05:00+03:00",
            EndTime = "2026-11-17T18:30:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 2 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(2) },
            Tags = new List<string> { "langfuse", "sla", "copilot", "руководство" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000117-1900-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000117-1900-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "sales",
            Title = "Синхронизация задач CRM (ПЗ ТиМАУ)",
            Description = "Разбор стадий сделок в CRM, проверка заполнения контактных лиц и задач менеджеров",
            StartTime = "2026-11-17T19:00:00+03:00",
            EndTime = "2026-11-17T20:25:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 3,
            OrganizerName = formatEmpShort(3),
            ParticipantIds = new List<int> { 3 },
            Participants = new List<string> { formatEmpShort(3) },
            Tags = new List<string> { "битрикс24", "crm", "лиды", "продажи" }
        });

        // Wednesday Nov 18, 2026
        items.Add(new CalendarEventItem
        {
            Id = "b0000118-1530-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000118-1530-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "management",
            Title = "Совещание по автоматизации n8n (ЛК САиИО)",
            Description = "Тестирование вебхука смены статуса заказов 1С и триггера авто-создания встреч",
            StartTime = "2026-11-18T15:30:00+03:00",
            EndTime = "2026-11-18T16:55:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 2, 3 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(2), formatEmpShort(3) },
            Tags = new List<string> { "n8n", "автоматизация", "вебхуки", "руководство" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000118-1705-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000118-1705-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "accounting",
            Title = "Ревизия себестоимости тиражей (ЛК СУБД)",
            Description = "Анализ плановой и фактической маржинальности ноябрьских заказов с учетом ночных смен",
            StartTime = "2026-11-18T17:05:00+03:00",
            EndTime = "2026-11-18T18:30:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 1,
            OrganizerName = formatEmpShort(1),
            ParticipantIds = new List<int> { 1, 4 },
            Participants = new List<string> { formatEmpShort(1), formatEmpShort(4) },
            Tags = new List<string> { "себестоимость", "тираж-44102", "бухгалтерия" }
        });

        items.Add(new CalendarEventItem
        {
            Id = "b0000118-1900-2026-0000-000000000001",
            MeetingId = Guid.Parse("b0000118-1900-2026-0000-000000000001"),
            ItemType = "meeting",
            Category = "accounting",
            Title = "Контроль дебиторской задолженности (ПЗ СУБД)",
            Description = "Сверка актов сверок и статус погашения задолженности за предыдущий отчетный месяц",
            StartTime = "2026-11-18T19:00:00+03:00",
            EndTime = "2026-11-18T20:25:00+03:00",
            IsAllDay = false,
            IsRecurring = false,
            Status = "scheduled",
            OrganizerId = 4,
            OrganizerName = formatEmpShort(4),
            ParticipantIds = new List<int> { 4 },
            Participants = new List<string> { formatEmpShort(4) },
            Tags = new List<string> { "дебиторка", "платежи", "отчетность", "бухгалтерия" }
        });

        return items;
    }
}


