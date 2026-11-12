using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Models;
using Fotoprint.Core.Telegram;
using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fotoprint.Core.Notifications;

public interface INotificationService
{
    /// <summary>
    /// Sends a single targeted notification to a Telegram chat or employee with optional deduplication.
    /// </summary>
    Task<NotificationBatchResult> SendTargetedNotificationAsync(
        SendNotificationCommand command,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a personalized MSK morning digest of today's meetings and open tasks to eligible employees.
    /// </summary>
    Task<NotificationBatchResult> DispatchMorningDigestAsync(
        DispatchMorningDigestCommand command,
        CancellationToken ct = default);

    /// <summary>
    /// Sends pre-meeting briefings to organizers and participants of meetings starting within the configured window.
    /// </summary>
    Task<NotificationBatchResult> DispatchPreMeetingRemindersAsync(
        DispatchPreMeetingCommand command,
        CancellationToken ct = default);

    /// <summary>
    /// Sends approaching-deadline and daily overdue task reminders to responsible employees with per-run flood caps.
    /// </summary>
    Task<NotificationBatchResult> DispatchTaskDeadlineRemindersAsync(
        DispatchTaskDeadlinesCommand command,
        CancellationToken ct = default);
}

/// <summary>
/// Orchestrates deduplicated, timezone-aware proactive Telegram notifications with session persistence and telemetry.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly IFotoprintRepository _repo;
    private readonly INotificationDataProvider _dataProvider;
    private readonly ITelegramBotClient? _telegramBot;
    private readonly IReadOnlyDictionary<string, INotificationChannel> _channels;
    private readonly ILangfuseClient _langfuse;
    private readonly FotoprintConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IFotoprintRepository repo,
        ITelegramBotClient telegramBot,
        ILangfuseClient langfuse,
        FotoprintConfig config,
        TimeProvider? timeProvider = null,
        ILogger<NotificationService>? logger = null)
        : this(
            repo,
            new RepositoryNotificationDataProvider(repo),
            new INotificationChannel[] { new TelegramNotificationChannel(telegramBot) },
            langfuse,
            config,
            timeProvider,
            logger,
            telegramBot)
    {
    }

    public NotificationService(
        IFotoprintRepository repo,
        INotificationDataProvider dataProvider,
        IEnumerable<INotificationChannel> channels,
        ILangfuseClient langfuse,
        FotoprintConfig config,
        TimeProvider? timeProvider = null,
        ILogger<NotificationService>? logger = null,
        ITelegramBotClient? telegramBot = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        _telegramBot = telegramBot;
        _langfuse = langfuse ?? throw new ArgumentNullException(nameof(langfuse));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<NotificationService>.Instance;

        var channelMap = new Dictionary<string, INotificationChannel>(StringComparer.OrdinalIgnoreCase);
        if (channels != null)
        {
            foreach (var ch in channels)
            {
                if (ch != null && !string.IsNullOrWhiteSpace(ch.ChannelType))
                {
                    channelMap[ch.ChannelType.Trim()] = ch;
                }
            }
        }
        _channels = channelMap;
    }

    public async Task<NotificationBatchResult> SendTargetedNotificationAsync(
        SendNotificationCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            var err = new NotificationDeliveryItemResult(
                command.EmployeeId,
                command.TelegramId ?? 0,
                command.NotificationType,
                command.DedupKey,
                command.EntityId,
                NotificationStatuses.ValidationError,
                "Notification text is required.");
            return BuildBatchResult(command.NotificationType, null, [err]);
        }

        Employee? employee = null;
        if (command.EmployeeId.HasValue)
        {
            employee = await _repo.GetEmployeeByIdAsync(command.EmployeeId.Value, ct);
        }
        else if (command.TelegramId.HasValue)
        {
            employee = await _repo.GetEmployeeByTelegramIdAsync(command.TelegramId.Value, ct);
        }

        var targetTelegramId = command.TelegramId ?? employee?.TelegramId ?? 0;
        if (targetTelegramId <= 0)
        {
            var invalid = new NotificationDeliveryItemResult(
                employee?.Id ?? command.EmployeeId,
                0,
                command.NotificationType,
                command.DedupKey,
                command.EntityId,
                NotificationStatuses.InvalidRecipient,
                "Target employee has no linked Telegram ID.");
            return BuildBatchResult(command.NotificationType, null, [invalid]);
        }

        if (employee is { IsActive: false } or { NotificationsEnabled: false })
        {
            var disabled = new NotificationDeliveryItemResult(
                employee.Id,
                targetTelegramId,
                command.NotificationType,
                command.DedupKey,
                command.EntityId,
                NotificationStatuses.NotificationsDisabled);
            return BuildBatchResult(command.NotificationType, null, [disabled]);
        }

        var itemResult = await DeliverWithDeduplicationAsync(
            employee,
            targetTelegramId,
            command.Text,
            command.NotificationType,
            command.DedupKey,
            command.EntityId,
            command.SaveToSession && _config.Notifications.SaveNotificationsToSession,
            command.DryRun,
            command.ParseMode,
            command.InlineKeyboard,
            ct);

        return BuildBatchResult(command.NotificationType, null, [itemResult]);
    }

    public async Task<NotificationBatchResult> DispatchMorningDigestAsync(
        DispatchMorningDigestCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tz = NotificationTimeHelper.ResolveTimeZone(_config.Environment.Timezone);
        var nowUtc = _timeProvider.GetUtcNow();
        var (dayStartUtc, dayEndUtc, localDate) = NotificationTimeHelper.GetDayBoundsUtc(nowUtc, tz);
        var targetDateStr = localDate.ToString("yyyy-MM-dd");

        if (!_config.Notifications.Enabled)
        {
            return BuildBatchResult(NotificationTypes.MorningDigest, targetDateStr, []);
        }

        var employees = await _dataProvider.GetAllEmployeesAsync(ct);
        var prefsMap = await _dataProvider.GetAllNotificationPreferencesAsync(ct);
        var todaysMeetings = await _dataProvider.GetMeetingsInRangeUtcAsync(dayStartUtc, dayEndUtc.AddTicks(-1), ct);
        var openTasks = await _dataProvider.GetOpenActionItemsAsync(command.EmployeeId, ct);

        var skipIfEmpty = command.SkipIfEmpty ?? !_config.Notifications.IncludeEmptyMorningDigest;
        var results = new List<NotificationDeliveryItemResult>();

        foreach (var employee in FilterTargetEmployees(employees, command.EmployeeId))
        {
            var prefs = prefsMap.TryGetValue(employee.Id, out var p)
                ? p
                : await _dataProvider.GetNotificationPreferencesAsync(employee.Id, employee.MasterId, ct);

            if (!HasDeliverableTarget(employee, prefs))
            {
                continue;
            }

            var tgId = employee.TelegramId ?? 0;
            var dedupKey = NotificationTimeHelper.BuildMorningDigestDedupKey(localDate, employee.Id);
            if (!employee.NotificationsEnabled || (!prefs.DigestEnabled && (!command.EmployeeId.HasValue || command.EnforceScheduleTime)))
            {
                results.Add(new NotificationDeliveryItemResult(
                    employee.Id,
                    tgId,
                    NotificationTypes.MorningDigest,
                    dedupKey,
                    null,
                    NotificationStatuses.NotificationsDisabled));
                continue;
            }

            if (command.EnforceScheduleTime &&
                NotificationTimeHelper.IsQuietHoursActive(nowUtc, tz, prefs.QuietHoursStartMsk, prefs.QuietHoursEndMsk))
            {
                results.Add(new NotificationDeliveryItemResult(
                    employee.Id,
                    tgId,
                    NotificationTypes.MorningDigest,
                    dedupKey,
                    null,
                    NotificationStatuses.SkippedQuietHours));
                continue;
            }

            if (command.EnforceScheduleTime && !NotificationTimeHelper.IsMorningDigestDueNow(nowUtc, tz, prefs))
            {
                results.Add(new NotificationDeliveryItemResult(
                    employee.Id,
                    tgId,
                    NotificationTypes.MorningDigest,
                    dedupKey,
                    null,
                    NotificationStatuses.SkippedScheduleTime));
                continue;
            }

            var empMeetings = todaysMeetings
                .Where(m => m.OrganizerId == employee.Id || m.ParticipantIds.Contains(employee.Id))
                .ToList();
            var empTasks = openTasks
                .Where(t => t.ResponsibleId == employee.Id)
                .ToList();

            if (skipIfEmpty && empMeetings.Count == 0 && empTasks.Count == 0)
            {
                results.Add(new NotificationDeliveryItemResult(
                    employee.Id,
                    tgId,
                    NotificationTypes.MorningDigest,
                    dedupKey,
                    null,
                    NotificationStatuses.SkippedEmpty));
                continue;
            }

            var text = NotificationFormatter.FormatMorningDigest(
                employee,
                localDate,
                empMeetings,
                empTasks,
                nowUtc,
                tz);

            var delivered = await DeliverWithDeduplicationAsync(
                employee,
                tgId,
                text,
                NotificationTypes.MorningDigest,
                dedupKey,
                null,
                _config.Notifications.SaveNotificationsToSession,
                command.DryRun,
                null,
                null,
                ct,
                prefs.PreferredChannel,
                prefs.ChannelAddress);

            results.Add(delivered);
        }

        return BuildBatchResult(NotificationTypes.MorningDigest, targetDateStr, results);
    }

    public async Task<NotificationBatchResult> DispatchPreMeetingRemindersAsync(
        DispatchPreMeetingCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tz = NotificationTimeHelper.ResolveTimeZone(_config.Environment.Timezone);
        var nowUtc = _timeProvider.GetUtcNow();
        var localDate = NotificationTimeHelper.GetDayBoundsUtc(nowUtc, tz).LocalDate.ToString("yyyy-MM-dd");

        if (!_config.Notifications.Enabled)
        {
            return BuildBatchResult(NotificationTypes.PreMeeting, localDate, []);
        }

        var defaultWindowMinutes = _config.Notifications.PreMeetingWindowMinutes > 0
            ? _config.Notifications.PreMeetingWindowMinutes
            : _config.Meetings.PreMeetingWindowMinutes;
        defaultWindowMinutes = Math.Clamp(defaultWindowMinutes, 1, 1440);

        var employees = await _dataProvider.GetAllEmployeesAsync(ct);
        var prefsMap = await _dataProvider.GetAllNotificationPreferencesAsync(ct);

        var maxQueryWindowMinutes = command.WindowMinutes.HasValue
            ? Math.Clamp(command.WindowMinutes.Value, 1, 1440)
            : prefsMap.Values.Select(p => Math.Clamp(p.PreMeetingWindowMinutes, 1, 1440))
                .Append(defaultWindowMinutes)
                .Max();

        var rangeStartUtc = nowUtc.UtcDateTime.AddMinutes(-2);
        var rangeEndUtc = nowUtc.UtcDateTime.AddMinutes(maxQueryWindowMinutes);

        var meetings = await _dataProvider.GetMeetingsInRangeUtcAsync(rangeStartUtc, rangeEndUtc, ct);
        if (meetings.Count == 0)
        {
            return BuildBatchResult(NotificationTypes.PreMeeting, localDate, []);
        }

        var employeeMap = employees.ToDictionary(e => e.Id);
        var openTasks = await _dataProvider.GetOpenActionItemsAsync(command.EmployeeId, ct);
        var results = new List<NotificationDeliveryItemResult>();

        foreach (var meeting in meetings)
        {
            var recipientIds = new HashSet<int>(meeting.ParticipantIds);
            if (meeting.OrganizerId.HasValue)
            {
                recipientIds.Add(meeting.OrganizerId.Value);
            }

            foreach (var recipientId in recipientIds.OrderBy(id => id))
            {
                if (command.EmployeeId.HasValue && recipientId != command.EmployeeId.Value)
                {
                    continue;
                }

                if (!employeeMap.TryGetValue(recipientId, out var employee))
                {
                    continue;
                }

                var prefs = prefsMap.TryGetValue(employee.Id, out var p)
                    ? p
                    : await _dataProvider.GetNotificationPreferencesAsync(employee.Id, employee.MasterId, ct);

                if (!HasDeliverableTarget(employee, prefs))
                {
                    continue;
                }

                var tgId = employee.TelegramId ?? 0;
                var empWindowMinutes = command.WindowMinutes.HasValue
                    ? Math.Clamp(command.WindowMinutes.Value, 1, 1440)
                    : (prefsMap.ContainsKey(employee.Id) || prefs.PreMeetingWindowMinutes != 30
                        ? Math.Clamp(prefs.PreMeetingWindowMinutes, 1, 1440)
                        : defaultWindowMinutes);

                var meetingStartUtc = NotificationTimeHelper.EnsureUtc(meeting.StartTime);
                if (meetingStartUtc > nowUtc.UtcDateTime.AddMinutes(empWindowMinutes))
                {
                    continue;
                }

                var dedupKey = NotificationTimeHelper.BuildPreMeetingDedupKey(meeting.Id, meeting.StartTime, employee.Id);
                if (!employee.NotificationsEnabled || (!prefs.PreMeetingEnabled && !command.EmployeeId.HasValue))
                {
                    results.Add(new NotificationDeliveryItemResult(
                        employee.Id,
                        tgId,
                        NotificationTypes.PreMeeting,
                        dedupKey,
                        meeting.Id,
                        NotificationStatuses.NotificationsDisabled));
                    continue;
                }

                if (!command.EmployeeId.HasValue &&
                    NotificationTimeHelper.IsQuietHoursActive(nowUtc, tz, prefs.QuietHoursStartMsk, prefs.QuietHoursEndMsk))
                {
                    results.Add(new NotificationDeliveryItemResult(
                        employee.Id,
                        tgId,
                        NotificationTypes.PreMeeting,
                        dedupKey,
                        meeting.Id,
                        NotificationStatuses.SkippedQuietHours));
                    continue;
                }

                var empTasks = openTasks.Where(t => t.ResponsibleId == employee.Id).ToList();
                var text = NotificationFormatter.FormatPreMeetingReminder(
                    employee,
                    meeting,
                    empTasks,
                    employees,
                    nowUtc,
                    tz);

                var delivered = await DeliverWithDeduplicationAsync(
                    employee,
                    tgId,
                    text,
                    NotificationTypes.PreMeeting,
                    dedupKey,
                    meeting.Id,
                    _config.Notifications.SaveNotificationsToSession,
                    command.DryRun,
                    null,
                    null,
                    ct,
                    prefs.PreferredChannel,
                    prefs.ChannelAddress);

                results.Add(delivered);
            }
        }

        return BuildBatchResult(NotificationTypes.PreMeeting, localDate, results);
    }

    public async Task<NotificationBatchResult> DispatchTaskDeadlineRemindersAsync(
        DispatchTaskDeadlinesCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tz = NotificationTimeHelper.ResolveTimeZone(_config.Environment.Timezone);
        var nowUtc = _timeProvider.GetUtcNow();
        var localDate = NotificationTimeHelper.GetDayBoundsUtc(nowUtc, tz).LocalDate.ToString("yyyy-MM-dd");

        if (!_config.Notifications.Enabled)
        {
            return BuildBatchResult(NotificationTypes.TaskDeadline, localDate, []);
        }

        var employees = await _dataProvider.GetAllEmployeesAsync(ct);
        var prefsMap = await _dataProvider.GetAllNotificationPreferencesAsync(ct);
        var employeeMap = employees.ToDictionary(e => e.Id);
        var openTasks = await _dataProvider.GetOpenActionItemsAsync(command.EmployeeId, ct);
        var maxPerEmployee = Math.Max(1, _config.Notifications.MaxTaskAlertsPerRunPerEmployee);
        var results = new List<NotificationDeliveryItemResult>();

        var evaluatedByEmployee = openTasks
            .Where(t => t.ResponsibleId.HasValue && employeeMap.ContainsKey(t.ResponsibleId.Value))
            .Select(task =>
            {
                var emp = employeeMap[task.ResponsibleId!.Value];
                prefsMap.TryGetValue(emp.Id, out var empPrefs);
                var urgentHours = empPrefs is not null ? empPrefs.TaskUrgentHours : _config.Notifications.TaskUrgentHours;
                var dueSoonHours = empPrefs is not null ? empPrefs.TaskDueSoonHours : _config.Notifications.TaskDueSoonHours;

                var (tier, dedupKey) = NotificationTimeHelper.EvaluateTaskDeadline(
                    task,
                    emp.Id,
                    nowUtc,
                    tz,
                    urgentHours,
                    dueSoonHours,
                    _config.Notifications.TaskMaxOverdueDays);
                return (Employee: emp, Prefs: empPrefs, Task: task, Tier: tier, DedupKey: dedupKey);
            })
            .Where(x => x.Tier != TaskDeadlineTier.None && !string.IsNullOrWhiteSpace(x.DedupKey))
            .OrderByDescending(x => x.Tier == TaskDeadlineTier.DueUrgent ? 3 : x.Tier == TaskDeadlineTier.DueSoon ? 2 : 1)
            .ThenByDescending(x => x.Tier == TaskDeadlineTier.Overdue ? NotificationTimeHelper.EnsureUtc(x.Task.Deadline!.Value) : DateTime.MinValue)
            .ThenBy(x => NotificationTimeHelper.EnsureUtc(x.Task.Deadline!.Value))
            .GroupBy(x => x.Employee.Id);

        foreach (var group in evaluatedByEmployee)
        {
            var perEmployeeSentOrDryRunCount = 0;

            foreach (var item in group)
            {
                var employee = item.Employee;
                var prefs = item.Prefs ?? await _dataProvider.GetNotificationPreferencesAsync(employee.Id, employee.MasterId, ct);

                if (!HasDeliverableTarget(employee, prefs))
                {
                    continue;
                }

                var tgId = employee.TelegramId ?? 0;
                if (!employee.NotificationsEnabled || (!prefs.TaskDeadlinesEnabled && !command.EmployeeId.HasValue))
                {
                    results.Add(new NotificationDeliveryItemResult(
                        employee.Id,
                        tgId,
                        NotificationTypes.TaskDeadline,
                        item.DedupKey,
                        item.Task.Id,
                        NotificationStatuses.NotificationsDisabled));
                    continue;
                }

                if (!command.EmployeeId.HasValue &&
                    NotificationTimeHelper.IsQuietHoursActive(nowUtc, tz, prefs.QuietHoursStartMsk, prefs.QuietHoursEndMsk))
                {
                    results.Add(new NotificationDeliveryItemResult(
                        employee.Id,
                        tgId,
                        NotificationTypes.TaskDeadline,
                        item.DedupKey,
                        item.Task.Id,
                        NotificationStatuses.SkippedQuietHours));
                    continue;
                }

                if (perEmployeeSentOrDryRunCount >= maxPerEmployee)
                {
                    break;
                }

                var text = NotificationFormatter.FormatTaskDeadlineReminder(
                    employee,
                    item.Task,
                    item.Tier,
                    nowUtc,
                    tz);

                var delivered = await DeliverWithDeduplicationAsync(
                    employee,
                    tgId,
                    text,
                    NotificationTypes.TaskDeadline,
                    item.DedupKey,
                    item.Task.Id,
                    _config.Notifications.SaveNotificationsToSession,
                    command.DryRun,
                    null,
                    null,
                    ct,
                    prefs.PreferredChannel,
                    prefs.ChannelAddress);

                results.Add(delivered);

                if (delivered.Status is NotificationStatuses.Sent or NotificationStatuses.DryRun)
                {
                    perEmployeeSentOrDryRunCount++;
                }
            }
        }

        return BuildBatchResult(NotificationTypes.TaskDeadline, localDate, results);
    }

    private async Task<NotificationDeliveryItemResult> DeliverWithDeduplicationAsync(
        Employee? employee,
        long telegramId,
        string text,
        string notificationType,
        string? dedupKey,
        Guid? entityId,
        bool saveToSession,
        bool dryRun,
        string? parseMode,
        IReadOnlyList<IReadOnlyList<TelegramInlineButton>>? inlineKeyboard,
        CancellationToken ct,
        string? preferredChannel = null,
        string? channelAddress = null)
    {
        var effectiveDedupKey = string.IsNullOrWhiteSpace(dedupKey)
            ? $"manual:{Guid.NewGuid():N}"
            : dedupKey.Trim();

        if (dryRun)
        {
            return new NotificationDeliveryItemResult(
                employee?.Id,
                telegramId,
                notificationType,
                effectiveDedupKey,
                entityId,
                NotificationStatuses.DryRun,
                PreviewText: text);
        }

        var desiredChannelType = string.IsNullOrWhiteSpace(preferredChannel) ? "telegram" : preferredChannel.Trim().ToLowerInvariant();
        var channel = ResolveChannel(desiredChannelType);
        var useDirectTelegramBot = string.Equals(desiredChannelType, "telegram", StringComparison.OrdinalIgnoreCase) && _telegramBot != null;
        var isConfigured = useDirectTelegramBot ? _telegramBot!.IsConfigured : (channel?.IsConfigured ?? false);
        if (!isConfigured)
        {
            return new NotificationDeliveryItemResult(
                employee?.Id,
                telegramId,
                notificationType,
                effectiveDedupKey,
                entityId,
                NotificationStatuses.NotConfigured,
                $"Notification channel '{desiredChannelType}' is not configured.");
        }

        var effectiveChannelType = useDirectTelegramBot ? "telegram" : (channel?.ChannelType ?? desiredChannelType);
        var effectiveAddress = !string.IsNullOrWhiteSpace(channelAddress)
            ? channelAddress
            : (telegramId > 0 ? telegramId.ToString() : null);

        var deliveryId = await _repo.TryClaimNotificationDeliveryAsync(
            effectiveDedupKey,
            notificationType,
            employee?.Id,
            telegramId,
            entityId,
            ct,
            effectiveChannelType,
            effectiveAddress);

        if (!deliveryId.HasValue)
        {
            return new NotificationDeliveryItemResult(
                employee?.Id,
                telegramId,
                notificationType,
                effectiveDedupKey,
                entityId,
                NotificationStatuses.DuplicateSkipped);
        }

        TelegramApiResponse sendResult;
        try
        {
            if (useDirectTelegramBot)
            {
                sendResult = await _telegramBot!.SendMessageAsync(
                    telegramId,
                    text,
                    inlineKeyboard,
                    parseMode,
                    ct);
            }
            else if (channel != null)
            {
                var target = new NotificationDeliveryTarget(
                    EmployeeId: employee?.Id ?? 0,
                    MasterId: employee?.MasterId ?? 0,
                    TelegramId: telegramId,
                    ChannelAddress: effectiveAddress,
                    NotificationType: notificationType,
                    EntityId: entityId);
                await channel.SendAsync(target, text, parseMode, ct);
                sendResult = new TelegramApiResponse(true);
            }
            else
            {
                sendResult = new TelegramApiResponse(false, ErrorCode: 503, Description: "No notification channel configured.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var errMsg = TruncateError(ex.Message);
            await SafeCompleteDeliveryAsync(deliveryId.Value, NotificationStatuses.Failed, errMsg, ct);
            _logger.LogWarning(ex,
                "Failed sending Telegram notification type={NotificationType} to chatId={TelegramId}",
                notificationType, telegramId);
            return new NotificationDeliveryItemResult(
                employee?.Id,
                telegramId,
                notificationType,
                effectiveDedupKey,
                entityId,
                NotificationStatuses.Failed,
                errMsg);
        }

        if (!sendResult.Ok)
        {
            var errMsg = TruncateError(
                sendResult.ErrorCode.HasValue
                    ? $"Telegram {sendResult.ErrorCode}: {sendResult.Description}"
                    : (sendResult.Description ?? "Telegram delivery failed"));

            _logger.LogWarning(
                "Telegram notification rejected by API: type={NotificationType}, chatId={ChatId}, code={Code}, description={Desc}",
                notificationType, telegramId, sendResult.ErrorCode, sendResult.Description);

            await SafeCompleteDeliveryAsync(deliveryId.Value, NotificationStatuses.Failed, errMsg, ct);

            await SafeLogTelemetryEventAsync(
                "Notification:DeliveryFailed",
                "WARNING",
                errMsg,
                new { notification_type = notificationType, telegram_id = telegramId, dedup_key = effectiveDedupKey },
                ct);

            return new NotificationDeliveryItemResult(
                employee?.Id,
                telegramId,
                notificationType,
                effectiveDedupKey,
                entityId,
                NotificationStatuses.Failed,
                errMsg);
        }

        await SafeCompleteDeliveryAsync(deliveryId.Value, NotificationStatuses.Sent, null, ct);

        if (saveToSession && employee is { MasterId: > 0 })
        {
            await TrySaveToActiveSessionAsync(employee.MasterId, telegramId, text, notificationType, effectiveDedupKey, entityId, ct);
        }

        await SafeLogTelemetryEventAsync(
            "Notification:Delivered",
            "DEFAULT",
            $"Delivered {notificationType} to {telegramId}",
            new { notification_type = notificationType, telegram_id = telegramId, employee_id = employee?.Id, dedup_key = effectiveDedupKey },
            ct);

        return new NotificationDeliveryItemResult(
            employee?.Id,
            telegramId,
            notificationType,
            effectiveDedupKey,
            entityId,
            NotificationStatuses.Sent);
    }

    private async Task SafeCompleteDeliveryAsync(Guid deliveryId, string status, string? error, CancellationToken ct)
    {
        try
        {
            await _repo.CompleteNotificationDeliveryAsync(deliveryId, status, error, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not update notification_deliveries status for id={DeliveryId}", deliveryId);
        }
    }

    private async Task SafeLogTelemetryEventAsync(
        string name,
        string level,
        string message,
        object metadata,
        CancellationToken ct)
    {
        try
        {
            var traceId = _langfuse.GenerateTraceId();
            await _langfuse.LogEventAsync(traceId, name, level, message, metadata, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed logging notification telemetry event {EventName}", name);
        }
    }

    private async Task TrySaveToActiveSessionAsync(
        long masterId,
        long telegramId,
        string text,
        string notificationType,
        string dedupKey,
        Guid? entityId,
        CancellationToken ct)
    {
        try
        {
            var session = await _repo.GetOrCreateActiveSessionByMasterIdAsync(masterId, telegramId, "telegram", ct);
            await _repo.SaveMessageToSessionAsync(
                session.Id,
                masterId,
                "assistant",
                text,
                isFrozen: false,
                metadata: new
                {
                    proactive_notification = true,
                    notification_type = notificationType,
                    dedup_key = dedupKey,
                    entity_id = entityId
                },
                ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed persisting proactive notification to session for masterId={MasterId}", masterId);
        }
    }

    private bool HasDeliverableTarget(Employee employee, EmployeeNotificationPreferences prefs)
    {
        if (!string.IsNullOrWhiteSpace(prefs.PreferredChannel) &&
            !string.Equals(prefs.PreferredChannel, "telegram", StringComparison.OrdinalIgnoreCase) &&
            _channels.TryGetValue(prefs.PreferredChannel.Trim(), out var ch) &&
            ch.IsConfigured)
        {
            return true;
        }

        return employee.TelegramId.HasValue && employee.TelegramId.Value > 0;
    }

    private INotificationChannel? ResolveChannel(string? preferredChannel)
    {
        if (!string.IsNullOrWhiteSpace(preferredChannel) &&
            _channels.TryGetValue(preferredChannel.Trim(), out var matched) &&
            matched.IsConfigured)
        {
            return matched;
        }

        if (_channels.TryGetValue("telegram", out var tgChannel))
        {
            return tgChannel;
        }

        return _channels.Values.FirstOrDefault();
    }

    private static IEnumerable<Employee> FilterTargetEmployees(IReadOnlyList<Employee> employees, int? employeeId) =>
        employeeId.HasValue
            ? employees.Where(e => e.Id == employeeId.Value)
            : employees;

    private static NotificationBatchResult BuildBatchResult(
        string notificationType,
        string? targetDateMsk,
        IReadOnlyList<NotificationDeliveryItemResult> results)
    {
        var sent = results.Count(r => r.Status == NotificationStatuses.Sent);
        var dup = results.Count(r => r.Status == NotificationStatuses.DuplicateSkipped);
        var disabled = results.Count(r =>
            r.Status is NotificationStatuses.NotificationsDisabled
                or NotificationStatuses.SkippedQuietHours
                or NotificationStatuses.SkippedScheduleTime);
        var empty = results.Count(r => r.Status == NotificationStatuses.SkippedEmpty);
        var failed = results.Count(r =>
            r.Status is NotificationStatuses.Failed
                or NotificationStatuses.NotConfigured
                or NotificationStatuses.InvalidRecipient
                or NotificationStatuses.ValidationError);
        var dryRun = results.Count(r => r.Status == NotificationStatuses.DryRun);

        return new NotificationBatchResult(
            Ok: failed == 0,
            NotificationType: notificationType,
            TargetDateMsk: targetDateMsk,
            TotalMatched: results.Count,
            SentCount: sent,
            SkippedDuplicateCount: dup,
            SkippedDisabledCount: disabled,
            SkippedEmptyCount: empty,
            FailedCount: failed,
            DryRunCount: dryRun,
            Results: results);
    }

    private static string TruncateError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error";
        var trimmed = message.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
