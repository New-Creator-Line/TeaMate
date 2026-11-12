using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fotoprint.Core.Audit;
using Fotoprint.Core.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Security;

public interface ISecurityValidator
{
    Task<(bool IsAllowed, EmployeeSecurityContext? Context, string? Reason)> ValidateAccessAsync(long telegramId, string requiredPermission, string actionName, string? traceId = null, CancellationToken ct = default);
}

public class SecurityValidator : ISecurityValidator
{
    private readonly HttpClient _employeeReaderClient;
    private readonly ILangfuseClient _langfuseClient;
    private readonly ILogger<SecurityValidator> _logger;

    public SecurityValidator(HttpClient employeeReaderClient, ILangfuseClient langfuseClient, ILogger<SecurityValidator> logger)
    {
        _employeeReaderClient = employeeReaderClient;
        _langfuseClient = langfuseClient;
        _logger = logger;
    }

    public async Task<(bool IsAllowed, EmployeeSecurityContext? Context, string? Reason)> ValidateAccessAsync(long telegramId, string requiredPermission, string actionName, string? traceId = null, CancellationToken ct = default)
    {
        traceId ??= _langfuseClient.GenerateTraceId();

        try
        {
            var response = await _employeeReaderClient.GetAsync($"tools/get_employee_security_context?telegram_id={telegramId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                var reason = $"Сотрудник с Telegram ID {telegramId} не найден в реестре или неактивен.";
                await _langfuseClient.LogSecurityAlertAsync(traceId, telegramId, actionName, requiredPermission, reason, ct);
                return (false, null, reason);
            }

            var context = await response.Content.ReadFromJsonAsync<EmployeeSecurityContext>(cancellationToken: ct);
            if (context == null)
            {
                var reason = "Не удалось десериализовать контекст безопасности сотрудника.";
                await _langfuseClient.LogSecurityAlertAsync(traceId, telegramId, actionName, requiredPermission, reason, ct);
                return (false, null, reason);
            }

            if (!context.HasPermission(requiredPermission))
            {
                var reason = $"Доступ запрещен. Требуется право: '{requiredPermission}'. Имеющиеся роли: [{string.Join(", ", context.Roles)}].";
                await _langfuseClient.LogSecurityAlertAsync(traceId, telegramId, actionName, requiredPermission, reason, ct);
                return (false, context, reason);
            }

            return (true, context, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Security validation failed due to error contacting MCP-EmployeeReader");
            var reason = $"Ошибка проверки безопасности: {ex.Message}";
            await _langfuseClient.LogSecurityAlertAsync(traceId, telegramId, actionName, requiredPermission, reason, ct);
            return (false, null, reason);
        }
    }
}

public class SecurityInterceptorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityInterceptorMiddleware> _logger;

    public SecurityInterceptorMiddleware(RequestDelegate next, ILogger<SecurityInterceptorMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ISecurityValidator validator, ILangfuseClient langfuseClient)
    {
        var endpoint = context.GetEndpoint();
        var requirePermAttr = endpoint?.Metadata.GetMetadata<RequirePermissionAttribute>();

        if (requirePermAttr != null)
        {
            long telegramId = 0;
            if (context.Request.Headers.TryGetValue("X-Telegram-Id", out var tgHeader) && long.TryParse(tgHeader, out var parsedId))
            {
                telegramId = parsedId;
            }
            else if (context.Request.Headers.TryGetValue("X-User-Id", out var userHeader) && long.TryParse(userHeader, out var parsedUserHeader))
            {
                telegramId = parsedUserHeader;
            }
            else if (context.Request.Query.TryGetValue("telegram_id", out var queryTg) && long.TryParse(queryTg, out var parsedQueryId))
            {
                telegramId = parsedQueryId;
            }
            else if (context.Request.Query.TryGetValue("user_id", out var queryUser) && long.TryParse(queryUser, out var parsedQueryUser))
            {
                telegramId = parsedQueryUser;
            }

            var traceId = context.Request.Headers.TryGetValue("X-Trace-Id", out var traceHeader)
                ? traceHeader.ToString()
                : langfuseClient.GenerateTraceId();

            if (telegramId == 0)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Access Denied",
                    status = 403,
                    message = "Отсутствует заголовок X-Telegram-Id или параметр telegram_id.",
                    trace_id = traceId
                }));
                return;
            }

            var (isAllowed, empContext, reason) = await validator.ValidateAccessAsync(
                telegramId, 
                requirePermAttr.Permission, 
                context.Request.Path, 
                traceId, 
                context.RequestAborted);

            if (!isAllowed)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Access Denied",
                    status = 403,
                    message = reason,
                    required_permission = requirePermAttr.Permission,
                    telegram_id = telegramId,
                    trace_id = traceId,
                    timestamp = DateTime.UtcNow
                }));
                return;
            }

            // Добавляем контекст в HttpContext.Items для последующих хэндлеров
            context.Items["SecurityContext"] = empContext;
            _ = AuditTrail.Instance.RecordAsync(new AuditEvent(
                ActorId: empContext?.MasterId ?? telegramId,
                Channel: "mcp",
                Action: "mcp.tool_access",
                ObjectType: "endpoint",
                ObjectId: context.Request.Path.Value,
                Result: AuditResult.Success,
                TraceId: traceId,
                Details: new { permission = requirePermAttr.Permission }), context.RequestAborted);
        }

        var confirmAttr = endpoint?.Metadata.GetMetadata<RequireConfirmationAttribute>();
        if (confirmAttr is { Required: true })
        {
            var confirmedHeader = context.Request.Headers["X-User-Confirmed"].ToString();
            if (!string.Equals(confirmedHeader, "true", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = "confirmation_required",
                    requires_confirmation = true,
                    message = "Для выполнения этого действия требуется подтверждение пользователя. Напиши точный план действий со всеми параметрами и попроси подтверждение ('Да, подтверждаю' / 'Выполняй')."
                }));
                return;
            }
        }

        await _next(context);
    }
}
