using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Fotoprint.Core.Data;
using Fotoprint.Core.Models;
using Fotoprint.Core.Telegram;

namespace Fotoprint.Core.Notifications;

/// <summary>
/// Channel-agnostic outbound notification port.
/// Supports Telegram today and allows plugging in a custom CRM webhook/WebSocket/email channel
/// without modifying NotificationService business logic.
/// </summary>
public interface INotificationChannel
{
    string ChannelType { get; }
    bool IsConfigured { get; }

    Task SendAsync(
        NotificationDeliveryTarget target,
        string messageText,
        string? parseMode = null,
        CancellationToken ct = default);
}

public sealed record NotificationDeliveryTarget(
    int EmployeeId,
    long MasterId,
    long? TelegramId,
    string? ChannelAddress,
    string NotificationType,
    Guid? EntityId = null);

/// <summary>
/// Default Telegram adapter implementing INotificationChannel.
/// </summary>
public sealed class TelegramNotificationChannel : INotificationChannel
{
    private readonly ITelegramBotClient _telegramClient;

    public TelegramNotificationChannel(ITelegramBotClient telegramClient)
    {
        _telegramClient = telegramClient ?? throw new ArgumentNullException(nameof(telegramClient));
    }

    public string ChannelType => "telegram";

    public bool IsConfigured => _telegramClient.IsConfigured;

    public async Task SendAsync(
        NotificationDeliveryTarget target,
        string messageText,
        string? parseMode = null,
        CancellationToken ct = default)
    {
        long chatId = target.TelegramId ?? 0;
        if (chatId <= 0 && !string.IsNullOrWhiteSpace(target.ChannelAddress))
        {
            _ = long.TryParse(target.ChannelAddress.Trim(), out chatId);
        }

        if (chatId <= 0)
            throw new InvalidOperationException($"Employee #{target.EmployeeId} does not have a valid Telegram chat ID.");

        await _telegramClient.SendMessageAsync(chatId, messageText, parseMode: parseMode, ct: ct);
    }
}

/// <summary>
/// Generic HTTP Webhook notification channel for future custom CRM integration (bell notifications / internal chat).
/// Configured via CRM_NOTIFICATION_WEBHOOK_URL or per-user ChannelAddress URL.
/// </summary>
public sealed class WebhookNotificationChannel : INotificationChannel
{
    private readonly HttpClient _httpClient;
    private readonly string? _defaultWebhookUrl;
    private readonly string? _apiKey;

    public WebhookNotificationChannel(HttpClient httpClient, string? defaultWebhookUrl = null, string? apiKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _defaultWebhookUrl = string.IsNullOrWhiteSpace(defaultWebhookUrl) ? null : defaultWebhookUrl.Trim();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public string ChannelType => "crm_webhook";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_defaultWebhookUrl);

    public async Task SendAsync(
        NotificationDeliveryTarget target,
        string messageText,
        string? parseMode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_defaultWebhookUrl))
            throw new InvalidOperationException("CRM webhook notification channel URL is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, _defaultWebhookUrl);
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);

        request.Content = JsonContent.Create(new
        {
            employee_id = target.EmployeeId,
            master_id = target.MasterId,
            recipient_address = target.ChannelAddress,
            notification_type = target.NotificationType,
            entity_id = target.EntityId,
            text = messageText,
            parse_mode = parseMode,
            sent_at = DateTimeOffset.UtcNow
        });

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Port for fetching schedule, task, and preference data for notifications.
/// Decouples NotificationService from direct SQL storage so a future custom CRM API
/// can supply meetings/tasks or sync alongside PostgreSQL.
/// </summary>
public interface INotificationDataProvider
{
    Task<List<Employee>> GetAllEmployeesAsync(CancellationToken ct = default);
    Task<Employee?> GetEmployeeByIdAsync(int id, CancellationToken ct = default);
    Task<EmployeeNotificationPreferences> GetNotificationPreferencesAsync(int employeeId, long masterId = 0, CancellationToken ct = default);
    Task<IReadOnlyDictionary<int, EmployeeNotificationPreferences>> GetAllNotificationPreferencesAsync(CancellationToken ct = default);
    Task<List<Meeting>> GetMeetingsInRangeUtcAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default);
    Task<List<ActionItem>> GetOpenActionItemsAsync(int? employeeId = null, CancellationToken ct = default);
}

/// <summary>
/// Default PostgreSQL implementation of INotificationDataProvider backed by IFotoprintRepository.
/// </summary>
public sealed class RepositoryNotificationDataProvider : INotificationDataProvider
{
    private readonly IFotoprintRepository _repo;

    public RepositoryNotificationDataProvider(IFotoprintRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public Task<List<Employee>> GetAllEmployeesAsync(CancellationToken ct = default)
        => _repo.GetAllEmployeesAsync(ct);

    public Task<Employee?> GetEmployeeByIdAsync(int id, CancellationToken ct = default)
        => _repo.GetEmployeeByIdAsync(id, ct);

    public Task<EmployeeNotificationPreferences> GetNotificationPreferencesAsync(int employeeId, long masterId = 0, CancellationToken ct = default)
        => _repo.GetNotificationPreferencesAsync(employeeId, masterId, ct);

    public Task<IReadOnlyDictionary<int, EmployeeNotificationPreferences>> GetAllNotificationPreferencesAsync(CancellationToken ct = default)
        => _repo.GetAllNotificationPreferencesAsync(ct);

    public Task<List<Meeting>> GetMeetingsInRangeUtcAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
        => _repo.GetMeetingsInRangeUtcAsync(startUtc, endUtc, ct);

    public Task<List<ActionItem>> GetOpenActionItemsAsync(int? employeeId = null, CancellationToken ct = default)
        => _repo.GetOpenActionItemsAsync(employeeId, ct);
}
