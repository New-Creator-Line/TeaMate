using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Notifications;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telegram;

namespace Fotoprint.AiGateway.Api.Notifications;

public static class NotificationEndpoints
{
    private const int MaxRequestBodyBytes = 64 * 1024;

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static IServiceCollection AddNotificationIntegration(this IServiceCollection services)
    {
        services.AddSingleton<INotificationDataProvider, RepositoryNotificationDataProvider>();
        services.AddSingleton<INotificationChannel, TelegramNotificationChannel>();
        services.AddHttpClient("CrmNotificationWebhookClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<INotificationChannel>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var cfg = sp.GetRequiredService<FotoprintConfig>();
            var webhookUrl = Environment.GetEnvironmentVariable("CRM_NOTIFICATION_WEBHOOK_URL");
            var webhookKey = Environment.GetEnvironmentVariable("CRM_NOTIFICATION_WEBHOOK_KEY");
            return new WebhookNotificationChannel(factory.CreateClient("CrmNotificationWebhookClient"), webhookUrl, webhookKey);
        });
        services.AddSingleton<INotificationService>(sp => new NotificationService(
            sp.GetRequiredService<Fotoprint.Core.Data.IFotoprintRepository>(),
            sp.GetRequiredService<INotificationDataProvider>(),
            sp.GetServices<INotificationChannel>(),
            sp.GetRequiredService<Fotoprint.Core.Telemetry.ILangfuseClient>(),
            sp.GetRequiredService<FotoprintConfig>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<ILogger<NotificationService>>(),
            sp.GetService<Fotoprint.Core.Telegram.ITelegramBotClient>()));
        services.AddHostedService<NotificationSchemaInitializer>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications")
            .AddEndpointFilter(async (invocationContext, next) =>
            {
                var http = invocationContext.HttpContext;
                var config = http.RequestServices.GetRequiredService<FotoprintConfig>();
                if (!InternalServiceAuthMiddleware.IsAuthorized(http.Request, config))
                {
                    return Results.Unauthorized();
                }

                return await next(invocationContext);
            });

        group.MapPost("/send", async (
            HttpContext httpContext,
            INotificationService notificationService,
            CancellationToken ct) =>
        {
            var (req, parseError) = await TryReadOptionalJsonAsync<SendNotificationRequest>(httpContext, ct);
            if (parseError is not null)
            {
                return Results.BadRequest(new { ok = false, error = parseError });
            }

            if (req is null || string.IsNullOrWhiteSpace(req.Text))
            {
                return Results.BadRequest(new { ok = false, error = "Field 'text' is required." });
            }

            var keyboard = req.InlineKeyboard?
                .Select(row => (IReadOnlyList<TelegramInlineButton>)row
                    .Select(btn => new TelegramInlineButton(btn.Text, btn.CallbackData))
                    .ToList())
                .ToList();

            var batch = await notificationService.SendTargetedNotificationAsync(
                new SendNotificationCommand(
                    TelegramId: req.TelegramId,
                    EmployeeId: req.EmployeeId,
                    Text: req.Text,
                    NotificationType: string.IsNullOrWhiteSpace(req.NotificationType)
                        ? NotificationTypes.Manual
                        : req.NotificationType,
                    DedupKey: req.DedupKey,
                    EntityId: req.EntityId,
                    SaveToSession: req.SaveToSession ?? true,
                    DryRun: req.DryRun ?? false,
                    ParseMode: req.ParseMode,
                    InlineKeyboard: keyboard),
                ct);

            var response = ToResponseDto(batch);
            var firstStatus = batch.Results.FirstOrDefault()?.Status;
            return firstStatus is NotificationStatuses.InvalidRecipient or NotificationStatuses.ValidationError
                ? Results.BadRequest(response)
                : Results.Ok(response);
        });

        group.MapPost("/dispatch/morning-digest", async (
            HttpContext httpContext,
            INotificationService notificationService,
            CancellationToken ct) =>
        {
            var (req, parseError) = await TryReadOptionalJsonAsync<DispatchMorningDigestRequest>(httpContext, ct);
            if (parseError is not null)
            {
                return Results.BadRequest(new { ok = false, error = parseError });
            }

            req ??= new DispatchMorningDigestRequest();
            var batch = await notificationService.DispatchMorningDigestAsync(
                new DispatchMorningDigestCommand(
                    EmployeeId: req.EmployeeId,
                    SkipIfEmpty: req.SkipIfEmpty,
                    DryRun: req.DryRun ?? false,
                    EnforceScheduleTime: req.EnforceScheduleTime ?? false),
                ct);

            return Results.Ok(ToResponseDto(batch));
        });

        group.MapPost("/dispatch/pre-meeting", async (
            HttpContext httpContext,
            INotificationService notificationService,
            CancellationToken ct) =>
        {
            var (req, parseError) = await TryReadOptionalJsonAsync<DispatchPreMeetingRequest>(httpContext, ct);
            if (parseError is not null)
            {
                return Results.BadRequest(new { ok = false, error = parseError });
            }

            req ??= new DispatchPreMeetingRequest();
            var batch = await notificationService.DispatchPreMeetingRemindersAsync(
                new DispatchPreMeetingCommand(
                    WindowMinutes: req.WindowMinutes,
                    EmployeeId: req.EmployeeId,
                    DryRun: req.DryRun ?? false),
                ct);

            return Results.Ok(ToResponseDto(batch));
        });

        group.MapPost("/dispatch/task-deadlines", async (
            HttpContext httpContext,
            INotificationService notificationService,
            CancellationToken ct) =>
        {
            var (req, parseError) = await TryReadOptionalJsonAsync<DispatchTaskDeadlinesRequest>(httpContext, ct);
            if (parseError is not null)
            {
                return Results.BadRequest(new { ok = false, error = parseError });
            }

            req ??= new DispatchTaskDeadlinesRequest();
            var batch = await notificationService.DispatchTaskDeadlineRemindersAsync(
                new DispatchTaskDeadlinesCommand(
                    EmployeeId: req.EmployeeId,
                    DryRun: req.DryRun ?? false),
                ct);

            return Results.Ok(ToResponseDto(batch));
        });

        group.MapGet("/preferences/{employeeId:int}", async (
            int employeeId,
            Fotoprint.Core.Data.IFotoprintRepository repo,
            CancellationToken ct) =>
        {
            var employee = await repo.GetEmployeeByIdAsync(employeeId, ct);
            if (employee is null)
            {
                return Results.NotFound(new { ok = false, error = $"Employee {employeeId} not found." });
            }

            var prefs = await repo.GetNotificationPreferencesAsync(employeeId, employee.MasterId, ct);
            return Results.Ok(new
            {
                ok = true,
                preferences = new
                {
                    employee_id = prefs.EmployeeId,
                    master_id = prefs.MasterId,
                    notifications_enabled = employee.NotificationsEnabled,
                    digest_enabled = prefs.DigestEnabled,
                    digest_time_msk = prefs.DigestTimeMsk,
                    digest_weekdays_only = prefs.DigestWeekdaysOnly,
                    pre_meeting_enabled = prefs.PreMeetingEnabled,
                    pre_meeting_window_minutes = prefs.PreMeetingWindowMinutes,
                    task_deadlines_enabled = prefs.TaskDeadlinesEnabled,
                    task_due_soon_hours = prefs.TaskDueSoonHours,
                    task_urgent_hours = prefs.TaskUrgentHours,
                    quiet_hours_start_msk = prefs.QuietHoursStartMsk,
                    quiet_hours_end_msk = prefs.QuietHoursEndMsk,
                    preferred_channel = prefs.PreferredChannel,
                    channel_address = prefs.ChannelAddress,
                    updated_at = prefs.UpdatedAt
                }
            });
        });

        group.MapPut("/preferences/{employeeId:int}", async (
            int employeeId,
            HttpContext httpContext,
            Fotoprint.Core.Data.IFotoprintRepository repo,
            CancellationToken ct) =>
        {
            var employee = await repo.GetEmployeeByIdAsync(employeeId, ct);
            if (employee is null)
            {
                return Results.NotFound(new { ok = false, error = $"Employee {employeeId} not found." });
            }

            var (patch, parseError) = await TryReadOptionalJsonAsync<Fotoprint.Core.Models.UpdateNotificationPreferencesPatch>(httpContext, ct);
            if (parseError is not null || patch is null)
            {
                return Results.BadRequest(new { ok = false, error = parseError ?? "JSON body is required." });
            }

            var updated = await repo.UpsertNotificationPreferencesAsync(employeeId, employee.MasterId, patch, ct);
            return Results.Ok(new
            {
                ok = true,
                preferences = new
                {
                    employee_id = updated.EmployeeId,
                    master_id = updated.MasterId,
                    digest_enabled = updated.DigestEnabled,
                    digest_time_msk = updated.DigestTimeMsk,
                    digest_weekdays_only = updated.DigestWeekdaysOnly,
                    pre_meeting_enabled = updated.PreMeetingEnabled,
                    pre_meeting_window_minutes = updated.PreMeetingWindowMinutes,
                    task_deadlines_enabled = updated.TaskDeadlinesEnabled,
                    task_due_soon_hours = updated.TaskDueSoonHours,
                    task_urgent_hours = updated.TaskUrgentHours,
                    quiet_hours_start_msk = updated.QuietHoursStartMsk,
                    quiet_hours_end_msk = updated.QuietHoursEndMsk,
                    preferred_channel = updated.PreferredChannel,
                    channel_address = updated.ChannelAddress,
                    updated_at = updated.UpdatedAt
                }
            });
        });

        return endpoints;
    }

    private static async Task<(T? Value, string? Error)> TryReadOptionalJsonAsync<T>(
        HttpContext httpContext,
        CancellationToken ct)
        where T : class
    {
        if (httpContext.Request.ContentLength is 0)
        {
            return (null, null);
        }

        if (httpContext.Request.ContentLength > MaxRequestBodyBytes)
        {
            return (null, $"Payload exceeds maximum allowed size ({MaxRequestBodyBytes / 1024} KB).");
        }

        var buffer = new byte[4096];
        using var ms = new MemoryStream();
        int read;
        while ((read = await httpContext.Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > MaxRequestBodyBytes)
            {
                return (null, $"Payload exceeds maximum allowed size ({MaxRequestBodyBytes / 1024} KB).");
            }

            ms.Write(buffer, 0, read);
        }

        if (ms.Length == 0)
        {
            return (null, null);
        }

        var raw = Encoding.UTF8.GetString(ms.ToArray());
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        try
        {
            return (JsonSerializer.Deserialize<T>(raw, RequestJsonOptions), null);
        }
        catch (JsonException ex)
        {
            return (null, $"Malformed JSON payload: {ex.Message}");
        }
    }

    private static NotificationBatchResponse ToResponseDto(NotificationBatchResult batch) => new(
        Ok: batch.Ok,
        NotificationType: batch.NotificationType,
        TargetDateMsk: batch.TargetDateMsk,
        TotalMatched: batch.TotalMatched,
        SentCount: batch.SentCount,
        SkippedDuplicateCount: batch.SkippedDuplicateCount,
        SkippedDisabledCount: batch.SkippedDisabledCount,
        SkippedEmptyCount: batch.SkippedEmptyCount,
        FailedCount: batch.FailedCount,
        DryRunCount: batch.DryRunCount,
        Results: batch.Results.Select(r => new NotificationDeliveryItemResponse(
            EmployeeId: r.EmployeeId,
            TelegramId: r.TelegramId,
            NotificationType: r.NotificationType,
            DedupKey: r.DedupKey,
            EntityId: r.EntityId,
            Status: r.Status,
            Error: r.Error,
            PreviewText: r.PreviewText)).ToList());
}

internal sealed class NotificationSchemaInitializer : IHostedService
{
    private readonly IFotoprintRepository _repo;
    private readonly ILogger<NotificationSchemaInitializer> _logger;

    public NotificationSchemaInitializer(
        IFotoprintRepository repo,
        ILogger<NotificationSchemaInitializer> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repo.EnsureNotificationSchemaAsync(cancellationToken);
            _logger.LogInformation("Notification database schema verified.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not ensure notification database schema during startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record InlineButtonRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string CallbackData);

public sealed record SendNotificationRequest(
    [property: JsonPropertyName("telegram_id")] long? TelegramId = null,
    [property: JsonPropertyName("employee_id")] int? EmployeeId = null,
    [property: JsonPropertyName("text")] string Text = "",
    [property: JsonPropertyName("notification_type")] string? NotificationType = null,
    [property: JsonPropertyName("dedup_key")] string? DedupKey = null,
    [property: JsonPropertyName("entity_id")] Guid? EntityId = null,
    [property: JsonPropertyName("save_to_session")] bool? SaveToSession = null,
    [property: JsonPropertyName("dry_run")] bool? DryRun = null,
    [property: JsonPropertyName("parse_mode")] string? ParseMode = null,
    [property: JsonPropertyName("inline_keyboard")] List<List<InlineButtonRequest>>? InlineKeyboard = null);

public sealed record DispatchMorningDigestRequest(
    [property: JsonPropertyName("employee_id")] int? EmployeeId = null,
    [property: JsonPropertyName("skip_if_empty")] bool? SkipIfEmpty = null,
    [property: JsonPropertyName("dry_run")] bool? DryRun = null,
    [property: JsonPropertyName("enforce_schedule_time")] bool? EnforceScheduleTime = null);

public sealed record DispatchPreMeetingRequest(
    [property: JsonPropertyName("window_minutes")] int? WindowMinutes = null,
    [property: JsonPropertyName("employee_id")] int? EmployeeId = null,
    [property: JsonPropertyName("dry_run")] bool? DryRun = null);

public sealed record DispatchTaskDeadlinesRequest(
    [property: JsonPropertyName("employee_id")] int? EmployeeId = null,
    [property: JsonPropertyName("dry_run")] bool? DryRun = null);
