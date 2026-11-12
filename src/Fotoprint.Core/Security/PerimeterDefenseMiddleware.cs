using System.Net;
using System.Text.Json;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Telegram;
using Fotoprint.Core.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Security;

/// <summary>
/// Middleware защиты периметра от сканирования портов, перебора уязвимостей и бот-сканеров.
/// </summary>
public class PerimeterDefenseMiddleware
{
    private readonly RequestDelegate _next;
    private readonly FotoprintConfig _config;
    private readonly ILogger<PerimeterDefenseMiddleware> _logger;

    public PerimeterDefenseMiddleware(
        RequestDelegate next,
        FotoprintConfig config,
        ILogger<PerimeterDefenseMiddleware> logger)
    {
        _next = next;
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ILangfuseClient langfuse)
    {
        var secConfig = _config.Security;
        var path = context.Request.Path.Value ?? string.Empty;
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // 1. Проверка на сканеры портов и ботов по User-Agent (Nmap, Masscan, Nikto, Sqlmap и др.)
        if (secConfig.BlockKnownScannerUserAgents && !string.IsNullOrWhiteSpace(userAgent))
        {
            var matchedScanner = secConfig.KnownScannerUserAgents
                .FirstOrDefault(scanner => userAgent.Contains(scanner, StringComparison.OrdinalIgnoreCase));

            if (matchedScanner != null)
            {
                _logger.LogWarning("🚨 [PerimeterDefense] Заблокирован сканер портов '{Scanner}' с IP {RemoteIp}. User-Agent: {UserAgent}",
                    matchedScanner, remoteIp, userAgent);

                var traceId = langfuse.GenerateTraceId();
                _ = langfuse.LogSecurityAlertAsync(
                    traceId,
                    0,
                    "PerimeterDefense:ScannerBlocked",
                    "anti_port_scan",
                    $"Блокировка сканера {matchedScanner} с IP {remoteIp}",
                    context.RequestAborted);

                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
        }

        // 2. Детекция зондирования закрытых файлов и путей (Honeypot probe paths: .env, .git, phpmyadmin, actuator и т.д.)
        if (secConfig.AntiPortScanEnabled)
        {
            var matchedPattern = secConfig.ScannerProbePathPatterns
                .FirstOrDefault(pattern => path.Contains(pattern, StringComparison.OrdinalIgnoreCase));

            if (matchedPattern != null)
            {
                _logger.LogWarning("🚨 [PerimeterDefense] Заблокирована попытка поиска уязвимого пути '{Path}' с IP {RemoteIp}",
                    path, remoteIp);

                var traceId = langfuse.GenerateTraceId();
                _ = langfuse.LogSecurityAlertAsync(
                    traceId,
                    0,
                    "PerimeterDefense:ProbePathBlocked",
                    "anti_port_scan",
                    $"Попытка зондирования пути {path} (паттерн {matchedPattern}) с IP {remoteIp}",
                    context.RequestAborted);

                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
        }

        await _next(context);
    }
}

/// <summary>
/// Middleware изоляции MCP-серверов: запрещает любые прямые вызовы к MCP в обход n8n и API Gateway.
/// Требует валидный межсервисный ключ X-Internal-Service-Key.
/// </summary>
public class InternalServiceAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly FotoprintConfig _config;
    private readonly ILogger<InternalServiceAuthMiddleware> _logger;

    public const string InternalHeaderName = "X-Internal-Service-Key";

    public static string ResolveExpectedKey(FotoprintConfig config)
    {
        var envKey = Environment.GetEnvironmentVariable("INTERNAL_SERVICE_KEY");
        return !string.IsNullOrWhiteSpace(envKey) ? envKey : config.Security.InternalServiceSecretKey;
    }

    public static bool IsAuthorized(HttpRequest request, FotoprintConfig config)
    {
        if (!config.Security.EnforceInternalServiceAuth)
        {
            return true;
        }

        var expectedKey = ResolveExpectedKey(config);
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            return false;
        }

        var providedKey = request.Headers[InternalHeaderName].FirstOrDefault();
        return TelegramWebhookSecurity.IsSecretValid(expectedKey, providedKey);
    }

    public InternalServiceAuthMiddleware(
        RequestDelegate next,
        FotoprintConfig config,
        ILogger<InternalServiceAuthMiddleware> logger)
    {
        _next = next;
        _config = config;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ILangfuseClient langfuse)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Health-check разрешен для внутреннего мониторинга Docker
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/healthz", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var secConfig = _config.Security;
        if (!secConfig.EnforceInternalServiceAuth)
        {
            await _next(context);
            return;
        }

        // Ключ разрешается через ResolveExpectedKey (ENV INTERNAL_SERVICE_KEY или config.yaml)
        // и проверяется в constant-time в IsAuthorized.

        // Проверяем наличие и валидность межсервисного секретного ключа
        // Проверка через единый IsAuthorized helper (constant-time SHA-256)
        if (!IsAuthorized(context.Request, _config))
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            _logger.LogWarning("🚫 [InternalAuth] Попытка неавторизованного прямого вызова MCP-сервера по пути '{Path}' с IP {RemoteIp}. Доступ заблокирован.",
                path, remoteIp);

            var traceId = langfuse.GenerateTraceId();
            _ = langfuse.LogSecurityAlertAsync(
                traceId,
                0,
                "InternalServiceAuth:DirectMcpAccessBlocked",
                "internal_service_key",
                $"Попытка прямого вызова MCP сервиса {path} с IP {remoteIp} без валидного X-Internal-Service-Key",
                context.RequestAborted);

            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Forbidden: Direct MCP Access Prohibited",
                status = 403,
                message = "Прямой доступ к внутренним MCP-серверам типографии запрещен. Все запросы должны поступать исключительно через защищенный периметр оркестратора n8n или API Gateway.",
                timestamp = DateTime.UtcNow
            }));
            return;
        }

        await _next(context);
    }
}

