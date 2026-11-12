using Fotoprint.Core.Agents;
using Fotoprint.Core.Audit;
using Fotoprint.Core.Data;
using Fotoprint.Core.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace Fotoprint.AiGateway.Api.Actions;

public sealed record ActionDecisionRequest(long TelegramId, long ChatId = 0, string? TraceId = null);

/// <summary>
/// Канально-независимые эндпоинты подтверждения/отмены плана действий и просмотра журнала аудита.
/// Любой клиент (Telegram, Web UI, CRM) рисует кнопки по <c>pending_action_id</c> и вызывает эти методы.
/// </summary>
public static class ActionEndpoints
{
    public static void MapActionEndpoints(this WebApplication app)
    {
        // POST /api/actions/{actionId}/confirm|cancel — делегирует в агент (единая точка исполнения сохранённого плана).
        app.MapPost("/api/actions/{actionId}/{verb}", async (
            string actionId,
            string verb,
            [FromBody] ActionDecisionRequest req,
            IFotoprintRepository repo,
            IFotoprintAgent agent,
            ILangfuseClient langfuse,
            CancellationToken ct) =>
        {
            var normalizedVerb = verb.ToLowerInvariant();
            if (normalizedVerb is not ("confirm" or "cancel"))
            {
                return Results.BadRequest(new { error = "verb must be 'confirm' or 'cancel'" });
            }

            var traceId = req.TraceId ?? langfuse.GenerateTraceId();
            var secContext = await repo.GetEmployeeSecurityContextAsync(req.TelegramId, ct);
            if (secContext == null)
            {
                await langfuse.LogSecurityAlertAsync(traceId, req.TelegramId, $"/api/actions/{normalizedVerb}", "employee_registered",
                    "Неизвестный сотрудник попытался подтвердить/отменить план действий.", ct);
                return Results.Json(new { is_success = false, error = "Access Denied", trace_id = traceId }, statusCode: 403);
            }

            var response = await agent.ProcessMessageAsync(req.TelegramId, req.ChatId, $"{normalizedVerb}:{actionId}", traceId, ct);
            return Results.Ok(new
            {
                answer = response.AnswerText,
                tools_invoked = response.ToolsInvoked,
                is_success = response.IsSuccess,
                trace_id = traceId
            });
        });

        // GET /api/audit/events — журнал аудита (ОТ-04); доступен только руководителю (право executive).
        app.MapGet("/api/audit/events", async (
            [FromQuery(Name = "telegram_id")] long telegramId,
            [FromQuery(Name = "actor_id")] long? actorId,
            [FromQuery(Name = "action")] string? action,
            [FromQuery(Name = "limit")] int? limit,
            IFotoprintRepository repo,
            IAuditLog audit,
            ILangfuseClient langfuse,
            CancellationToken ct) =>
        {
            var traceId = langfuse.GenerateTraceId();
            var secContext = await repo.GetEmployeeSecurityContextAsync(telegramId, ct);
            if (secContext is not { IsExecutive: true })
            {
                await langfuse.LogSecurityAlertAsync(traceId, telegramId, "/api/audit/events", "executive:view_summary",
                    "Попытка чтения журнала аудита без прав руководителя.", ct);
                return Results.Json(new { error = "Access Denied", trace_id = traceId }, statusCode: 403);
            }

            var events = await audit.QueryAsync(actorId, action, limit ?? 100, ct);
            return Results.Ok(new { count = events.Count, events });
        });
    }
}
