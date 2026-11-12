using System.Text.Json;
using Fotoprint.Core.Telegram;

namespace Fotoprint.AiGateway.Api.Telegram;

/// <summary>
/// Thread-safe snapshot of Cloudflare Quick Tunnel webhook synchronization state.
/// </summary>
public sealed class CloudflareWebhookState
{
    private readonly object _sync = new();
    private string? _tunnelWebhookUrl;
    private DateTimeOffset? _lastSyncUtc;
    private string? _lastError;

    public void RecordSync(string webhookUrl, DateTimeOffset utcNow)
    {
        lock (_sync)
        {
            _tunnelWebhookUrl = webhookUrl;
            _lastSyncUtc = utcNow;
            _lastError = null;
        }
    }

    public void RecordError(string errorMessage, string? knownTunnelUrl = null)
    {
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(knownTunnelUrl))
            {
                _tunnelWebhookUrl = knownTunnelUrl;
            }

            _lastError = errorMessage;
        }
    }

    public (string? TunnelWebhookUrl, DateTimeOffset? LastSyncUtc, string? LastError) Snapshot()
    {
        lock (_sync)
        {
            return (_tunnelWebhookUrl, _lastSyncUtc, _lastError);
        }
    }
}

/// <summary>
/// Polls the cloudflared metrics container on the Docker network and automatically registers
/// the assigned HTTPS Quick Tunnel URL with Telegram's setWebhook API.
/// </summary>
public sealed class CloudflareWebhookRegistrarService : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ITelegramBotClient _telegramBot;
    private readonly TelegramBotOptions _options;
    private readonly CloudflareWebhookState _state;
    private readonly ILogger<CloudflareWebhookRegistrarService> _logger;
    private string? _registeredInThisProcessUrl;

    public CloudflareWebhookRegistrarService(
        IHttpClientFactory httpFactory,
        ITelegramBotClient telegramBot,
        TelegramBotOptions options,
        CloudflareWebhookState state,
        ILogger<CloudflareWebhookRegistrarService> logger)
    {
        _httpFactory = httpFactory;
        _telegramBot = telegramBot;
        _options = options;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured || string.IsNullOrWhiteSpace(_options.CloudflaredMetricsUrl))
        {
            _logger.LogInformation(
                "Cloudflare auto-webhook registration skipped (BotConfigured={Configured}, MetricsUrl='{Url}').",
                _options.IsConfigured,
                _options.CloudflaredMetricsUrl ?? string.Empty);
            return;
        }

        // Allow Kestrel and Nginx/cloudflared containers a brief window to finish startup
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var synced = await TrySyncOnceAsync(stoppingToken);
            var delay = synced ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(3);
            await Task.Delay(delay, stoppingToken);
        }
    }

    public async Task<string?> TryFetchTunnelWebhookUrlAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.CloudflaredMetricsUrl))
        {
            return null;
        }

        var client = _httpFactory.CreateClient("CloudflaredMetrics");
        using var response = await client.GetAsync(_options.CloudflaredMetricsUrl, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return CloudflareTunnelParser.TryExtractWebhookUrl(json, out var webhookUrl)
            ? webhookUrl
            : null;
    }

    private async Task<bool> TrySyncOnceAsync(CancellationToken ct)
    {
        try
        {
            var desiredUrl = await TryFetchTunnelWebhookUrlAsync(ct);
            if (string.IsNullOrWhiteSpace(desiredUrl))
            {
                _state.RecordError("Ожидание назначения публичного URL от cloudflared quicktunnel...");
                return false;
            }

            var currentInfo = await _telegramBot.GetWebhookInfoAsync(ct);
            string? currentUrl = null;
            if (currentInfo.Ok &&
                currentInfo.Result.HasValue &&
                currentInfo.Result.Value.TryGetProperty("url", out var urlProp) &&
                urlProp.ValueKind == JsonValueKind.String)
            {
                currentUrl = urlProp.GetString();
            }

            var needsRegistration =
                !string.Equals(currentUrl, desiredUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_registeredInThisProcessUrl, desiredUrl, StringComparison.OrdinalIgnoreCase);

            if (needsRegistration)
            {
                var setResp = await _telegramBot.SetWebhookAsync(
                    webhookUrl: desiredUrl,
                    secretToken: _options.WebhookSecret,
                    dropPendingUpdates: false,
                    ct: ct);

                if (!setResp.Ok)
                {
                    var err = $"Telegram setWebhook error {setResp.ErrorCode}: {setResp.Description}";
                    _logger.LogWarning(err);
                    _state.RecordError(err, desiredUrl);
                    return false;
                }

                _logger.LogInformation("Telegram webhook successfully registered at {WebhookUrl}", desiredUrl);
            }

            _registeredInThisProcessUrl = desiredUrl;
            _state.RecordSync(desiredUrl, DateTimeOffset.UtcNow);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cloudflare webhook sync attempt pending");
            _state.RecordError(ex.Message);
            return false;
        }
    }
}
