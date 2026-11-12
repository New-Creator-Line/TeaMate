using System.Text.Json.Serialization;
using Fotoprint.Core.Telegram;

namespace Fotoprint.Core.Notifications;

public static class NotificationStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string DuplicateSkipped = "duplicate_skipped";
    public const string NotificationsDisabled = "notifications_disabled";
    public const string SkippedEmpty = "skipped_empty";
    public const string DryRun = "dry_run";
    public const string NotConfigured = "not_configured";
    public const string InvalidRecipient = "invalid_recipient";
    public const string ValidationError = "validation_error";
    public const string SkippedScheduleTime = "skipped_schedule_time";
    public const string SkippedQuietHours = "skipped_quiet_hours";
}

public static class NotificationTypes
{
    public const string Manual = "manual";
    public const string PreMeeting = "pre_meeting";
    public const string MorningDigest = "morning_digest";
    public const string TaskDeadline = "task_deadline";
}

public enum TaskDeadlineTier
{
    None = 0,
    DueSoon = 1,
    DueUrgent = 2,
    Overdue = 3
}

public sealed record SendNotificationCommand(
    long? TelegramId = null,
    int? EmployeeId = null,
    string Text = "",
    string NotificationType = NotificationTypes.Manual,
    string? DedupKey = null,
    Guid? EntityId = null,
    bool SaveToSession = true,
    bool DryRun = false,
    string? ParseMode = null,
    IReadOnlyList<IReadOnlyList<TelegramInlineButton>>? InlineKeyboard = null);

public sealed record DispatchPreMeetingCommand(
    int? WindowMinutes = null,
    int? EmployeeId = null,
    bool DryRun = false);

public sealed record DispatchMorningDigestCommand(
    int? EmployeeId = null,
    bool? SkipIfEmpty = null,
    bool DryRun = false,
    bool EnforceScheduleTime = false);

public sealed record DispatchTaskDeadlinesCommand(
    int? EmployeeId = null,
    bool DryRun = false);

public sealed record NotificationDeliveryItemResult(
    int? EmployeeId,
    long TelegramId,
    string NotificationType,
    string? DedupKey,
    Guid? EntityId,
    string Status,
    string? Error = null,
    string? PreviewText = null);

public sealed record NotificationBatchResult(
    bool Ok,
    string NotificationType,
    string? TargetDateMsk,
    int TotalMatched,
    int SentCount,
    int SkippedDuplicateCount,
    int SkippedDisabledCount,
    int SkippedEmptyCount,
    int FailedCount,
    int DryRunCount,
    IReadOnlyList<NotificationDeliveryItemResult> Results);

public sealed record NotificationDeliveryItemResponse(
    [property: JsonPropertyName("employee_id")] int? EmployeeId,
    [property: JsonPropertyName("telegram_id")] long TelegramId,
    [property: JsonPropertyName("notification_type")] string NotificationType,
    [property: JsonPropertyName("dedup_key")] string? DedupKey,
    [property: JsonPropertyName("entity_id")] Guid? EntityId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("preview_text")] string? PreviewText = null);

public sealed record NotificationBatchResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("notification_type")] string NotificationType,
    [property: JsonPropertyName("target_date_msk")] string? TargetDateMsk,
    [property: JsonPropertyName("total_matched")] int TotalMatched,
    [property: JsonPropertyName("sent_count")] int SentCount,
    [property: JsonPropertyName("skipped_duplicate_count")] int SkippedDuplicateCount,
    [property: JsonPropertyName("skipped_disabled_count")] int SkippedDisabledCount,
    [property: JsonPropertyName("skipped_empty_count")] int SkippedEmptyCount,
    [property: JsonPropertyName("failed_count")] int FailedCount,
    [property: JsonPropertyName("dry_run_count")] int DryRunCount,
    [property: JsonPropertyName("results")] IReadOnlyList<NotificationDeliveryItemResponse> Results);
