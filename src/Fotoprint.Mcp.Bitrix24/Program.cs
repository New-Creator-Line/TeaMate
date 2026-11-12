using System.Text.Json;
using Fotoprint.Core.Audit;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Mcp;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;

var builder = WebApplication.CreateBuilder(args);

var appConfig = FotoprintConfigLoader.Load();
builder.Services.AddSingleton(appConfig);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=fotoprint;Username=fotoprint;Password=fotoprint_secret";
var employeeReaderUrl = builder.Configuration["Security:EmployeeReaderUrl"] ?? "http://localhost:8081";
var langfuseBaseUrl = builder.Configuration["Langfuse:BaseUrl"] ?? "http://localhost:3000";
var langfusePubKey = builder.Configuration["Langfuse:PublicKey"] ?? "pk-lf-fotoprint-local";
var langfuseSecKey = builder.Configuration["Langfuse:SecretKey"] ?? "sk-lf-fotoprint-local";

builder.Services.AddSingleton<IFotoprintRepository>(_ => new FotoprintRepository(connectionString));
builder.Services.AddSingleton<IAuditLog>(sp => new PostgresAuditLog(connectionString, sp.GetService<ILogger<PostgresAuditLog>>()));

builder.Services.AddHttpClient<ILangfuseClient, LangfuseClient>();
builder.Services.AddSingleton(new LangfuseOptions
{
    BaseUrl = langfuseBaseUrl,
    PublicKey = langfusePubKey,
    SecretKey = langfuseSecKey,
    Enabled = true
});

builder.Services.AddHttpClient<ISecurityValidator, SecurityValidator>(client =>
{
    client.BaseAddress = new Uri(employeeReaderUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("X-Internal-Service-Key", appConfig.Security.InternalServiceSecretKey);
});

var app = builder.Build();
AuditTrail.Instance = app.Services.GetRequiredService<IAuditLog>();

// Защита периметра и изоляция MCP: запрет прямого публичного доступа
app.UseMiddleware<PerimeterDefenseMiddleware>();
app.UseMiddleware<InternalServiceAuthMiddleware>();
app.UseMiddleware<SecurityInterceptorMiddleware>();

// Health
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "MCP-Bitrix24",
    timestamp = DateTime.UtcNow
}));

// Tools list
var tools = new List<McpToolDefinition>
{
    new()
    {
        Name = "get_user_tasks",
        Description = "Получает список активных и просроченных задач сотрудника из Битрикс24.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                employee_id = new { type = "integer", description = "ID сотрудника" },
                status_filter = new { type = "string", description = "Фильтр по статусу: pending, overdue, all" }
            },
            required = new[] { "employee_id" }
        }
    },
    new()
    {
        Name = "create_task",
        Description = "Создает задачу в Битрикс24 по итогам встречи и привязывает её к реестру поручений.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                title = new { type = "string", description = "Название задачи" },
                description = new { type = "string", description = "Подробное описание задачи" },
                responsible_id = new { type = "integer", description = "ID ответственного сотрудника" },
                deadline = new { type = "string", description = "Срок выполнения (ГГГГ-ММ-ДД)" },
                meeting_id = new { type = "string", description = "UUID встречи, в рамках которой создано поручение" }
            },
            required = new[] { "title", "responsible_id" }
        }
    },
    new()
    {
        Name = "get_crm_deal_info",
        Description = "Получает карточку CRM-сделки из Битрикс24 (клиент, сумма, маржа, стадия, ответственный менеджер).",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                deal_query = new { type = "string", description = "Номер сделки, номер заказа или название клиента (например '44102', 'Альфа-Маркет', 'D-8841')" }
            },
            required = new[] { "deal_query" }
        }
    }
};

app.MapGet("/tools/list", () => Results.Ok(new { tools }));

static object BuildMockBitrixTasks(int employeeId, string? statusFilter)
{
    var now = DateTime.UtcNow;
    var allMocks = new[]
    {
        new
        {
            id = $"mock-b24-{employeeId}-1",
            bitrix_task_id = 2041 + employeeId * 10,
            title = "Согласовать резерв мелованной бумаги 130г под заказ ЗК-44102",
            description = "Проверить поступление паллет со склада поставщика и подтвердить отгрузку в офсетный цех.",
            responsible_id = employeeId,
            deadline = now.AddDays(1).ToString("yyyy-MM-dd HH:mm"),
            is_overdue = false,
            status = "pending",
            priority = "high",
            source = "bitrix24_mock",
            url = $"https://fotoprint.bitrix24.by/company/personal/user/{employeeId}/tasks/task/view/{2041 + employeeId * 10}/"
        },
        new
        {
            id = $"mock-b24-{employeeId}-2",
            bitrix_task_id = 2042 + employeeId * 10,
            title = "Проверить отчет по юстировке валов Heidelberg Speedmaster XL 75",
            description = "Зафиксировать причины простоя (6 ч) и обновить плановую дату отгрузки клиенту Альфа-Маркет.",
            responsible_id = employeeId,
            deadline = now.AddHours(-5).ToString("yyyy-MM-dd HH:mm"),
            is_overdue = true,
            status = "overdue",
            priority = "high",
            source = "bitrix24_mock",
            url = $"https://fotoprint.bitrix24.by/company/personal/user/{employeeId}/tasks/task/view/{2042 + employeeId * 10}/"
        },
        new
        {
            id = $"mock-b24-{employeeId}-3",
            bitrix_task_id = 2043 + employeeId * 10,
            title = "Подготовить КП на допечатку буклетов для ГринВей Корпорейшн",
            description = "Рассчитать тираж 15 000 экз. на мелованной бумаге 150г с матовой ламинацией.",
            responsible_id = employeeId,
            deadline = now.AddDays(3).ToString("yyyy-MM-dd HH:mm"),
            is_overdue = false,
            status = "pending",
            priority = "normal",
            source = "bitrix24_mock",
            url = $"https://fotoprint.bitrix24.by/company/personal/user/{employeeId}/tasks/task/view/{2043 + employeeId * 10}/"
        }
    };

    var filter = statusFilter?.Trim().ToLowerInvariant();
    return filter switch
    {
        "overdue" => allMocks.Where(t => t.is_overdue).ToArray(),
        "pending" => allMocks.Where(t => !t.is_overdue).ToArray(),
        _ => allMocks
    };
}

static object BuildMockCrmDeal(string dealQuery)
{
    var q = dealQuery.Trim();
    if (q.Contains("Техно", StringComparison.OrdinalIgnoreCase) || q.Contains("44103"))
    {
        return new
        {
            deal_id = "D-8842",
            title = "ТехноМир — Брошюры А5, 25 000 экз. (ЗК-44103)",
            client = "ООО «ТехноМир»",
            stage = "Постпечатная обработка (FINAL_ASSEMBLY)",
            amount_byn = 21500.00m,
            currency = "BYN",
            margin_percent = 28.5,
            payment_status = "Оплачено 100%",
            manager = "Дмитрий Соколов",
            planned_close_date = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"),
            source = "bitrix24_mock"
        };
    }

    return new
    {
        deal_id = q.StartsWith("D-", StringComparison.OrdinalIgnoreCase) ? q.ToUpperInvariant() : "D-8841",
        title = "Альфа-Маркет — Рекламный каталог А4, 50 000 экз. (ЗК-44102)",
        client = "ООО «Альфа-Маркет»",
        stage = "В производстве / Офсетная печать (IN_PRODUCTION)",
        amount_byn = 51200.00m,
        currency = "BYN",
        margin_percent = 31.2,
        payment_status = "Аванс 50% получен, постоплата при отгрузке",
        manager = "Елена Волкова",
        planned_close_date = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd"),
        source = "bitrix24_mock"
    };
}

// Tool 1: get_user_tasks (строго свои задачи или любые для Директора)
app.MapGet("/tools/get_user_tasks", [RequirePermission("bitrix24:read_tasks")] [RequireConfirmation(false)] async (int employee_id, string? status_filter, IFotoprintRepository repo, ILangfuseClient langfuse, HttpContext ctx) =>
{
    var secContext = ctx.Items["SecurityContext"] as EmployeeSecurityContext;
    if (secContext != null && !secContext.IsExecutive && employee_id != secContext.EmployeeId)
    {
        var traceId = ctx.Request.Headers.TryGetValue("X-Trace-Id", out var tr) ? tr.ToString() : langfuse.GenerateTraceId();
        var reason = $"IDOR-блокировка: сотрудник #{secContext.EmployeeId} ({secContext.FullName}) попытался прочитать задачи сотрудника #{employee_id}.";
        await langfuse.LogSecurityAlertAsync(traceId, secContext.TelegramId, "get_user_tasks", "bitrix24:read_other_user_tasks", reason, ctx.RequestAborted);
        return Results.Json(new
        {
            error = "Access Denied",
            status = 403,
            message = "Доступ запрещен: вы можете просматривать только свои собственные задачи (чтение чужих задач доступно только Директору).",
            requested_employee_id = employee_id,
            your_employee_id = secContext.EmployeeId
        }, statusCode: 403);
    }

    var dbFilter = string.Equals(status_filter, "overdue", StringComparison.OrdinalIgnoreCase) ? null : status_filter;
    var tasks = await repo.GetActionItemsByResponsibleAsync(employee_id, dbFilter);

    if (string.Equals(status_filter, "overdue", StringComparison.OrdinalIgnoreCase))
    {
        tasks = tasks.Where(t => t.Deadline.HasValue && t.Deadline.Value < DateTime.UtcNow).ToList();
    }

    if (tasks.Count == 0)
    {
        return Results.Ok(BuildMockBitrixTasks(employee_id, status_filter));
    }

    // Дополняем информацией из Битрикс24
    var enriched = tasks.Select(t => new
    {
        id = t.Id,
        bitrix_task_id = t.Bitrix24TaskId ?? (1000 + Math.Abs(t.Id.GetHashCode() % 9000)),
        title = t.Title,
        description = t.Description,
        responsible_id = t.ResponsibleId,
        deadline = t.Deadline?.ToString("yyyy-MM-dd HH:mm"),
        is_overdue = t.Deadline.HasValue && t.Deadline.Value < DateTime.UtcNow,
        status = t.Status,
        source = "bitrix24_db_mock"
    });

    return Results.Ok(enriched);
});

// Tool 2: create_task
app.MapPost("/tools/create_task", [RequirePermission("bitrix24:create_tasks")] [RequireConfirmation(true)] async (CreateTaskRequest req, IFotoprintRepository repo, ILangfuseClient langfuse, HttpContext ctx) =>
{
    var bitrixId = Random.Shared.Next(1050, 9999);
    var actionItemId = Guid.NewGuid();

    if (req.MeetingId.HasValue && req.MeetingId.Value != Guid.Empty)
    {
        try
        {
            actionItemId = await repo.CreateActionItemAsync(
                meetingId: req.MeetingId.Value,
                protocolId: null,
                title: req.Title,
                description: req.Description,
                responsibleId: req.ResponsibleId,
                deadline: req.Deadline ?? DateTime.UtcNow.AddDays(3)
            );
            await repo.UpdateActionItemStatusAsync(actionItemId, "exported_to_bitrix", bitrixId);
        }
        catch
        {
            // При отсутствии привязанной встречи в БД возвращаем чистый мок ответа Битрикс24
        }
    }

    var traceId = ctx.Request.Headers.TryGetValue("X-Trace-Id", out var tr) ? tr.ToString() : langfuse.GenerateTraceId();
    await langfuse.LogEventAsync(traceId, "Bitrix24:TaskCreated", "INFO", $"Задача '{req.Title}' создана в Битрикс24 (ID: {bitrixId})");

    return Results.Ok(new
    {
        success = true,
        action_item_id = actionItemId,
        bitrix24_task_id = bitrixId,
        title = req.Title,
        description = req.Description,
        responsible_id = req.ResponsibleId,
        deadline = (req.Deadline ?? DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd"),
        status = "created",
        source = "bitrix24_mock",
        url = $"https://fotoprint.bitrix24.by/company/personal/user/{req.ResponsibleId}/tasks/task/view/{bitrixId}/"
    });
});

// Tool 3: get_crm_deal_info
app.MapGet("/tools/get_crm_deal_info", [RequirePermission("bitrix24:read_tasks")] [RequireConfirmation(false)] (string deal_query) =>
{
    return Results.Ok(BuildMockCrmDeal(deal_query));
});

// MCP tools call router
app.MapPost("/tools/call", async (McpJsonRpcRequest request, IFotoprintRepository repo, ISecurityValidator validator, ILangfuseClient langfuse, HttpContext ctx) =>
{
    var toolName = request.Params?.Name ?? request.Method;

    long telegramId = 0;
    if (ctx.Request.Headers.TryGetValue("X-Telegram-Id", out var h) && long.TryParse(h, out var hid))
        telegramId = hid;
    else if (request.Params?.Arguments != null && request.Params.Arguments.TryGetValue("telegram_id", out var tgObj))
    {
        if (tgObj is JsonElement je && je.TryGetInt64(out var pLong)) telegramId = pLong;
        else if (long.TryParse(tgObj?.ToString(), out var pStr)) telegramId = pStr;
    }

    var traceId = ctx.Request.Headers.TryGetValue("X-Trace-Id", out var tr) ? tr.ToString() : langfuse.GenerateTraceId();

    string requiredPerm = toolName == "create_task" ? "bitrix24:create_tasks" : "bitrix24:read_tasks";
    var (isAllowed, empContext, reason) = await validator.ValidateAccessAsync(telegramId, requiredPerm, toolName, traceId, ctx.RequestAborted);

    if (!isAllowed)
    {
        return Results.Json(new McpJsonRpcResponse
        {
            Id = request.Id,
            Error = new McpError { Code = 403, Message = reason ?? "Access Denied" }
        }, statusCode: 403);
    }

    if (toolName == "get_user_tasks")
    {
        int employeeId = empContext?.EmployeeId ?? 1;
        if (request.Params?.Arguments != null && request.Params.Arguments.TryGetValue("employee_id", out var empObj))
        {
            if (empObj is JsonElement je && je.TryGetInt32(out var eid)) employeeId = eid;
            else if (int.TryParse(empObj?.ToString(), out var parsedEid)) employeeId = parsedEid;
        }

        if (empContext != null && !empContext.IsExecutive && employeeId != empContext.EmployeeId)
        {
            var denyReason = $"IDOR-блокировка (/tools/call): сотрудник #{empContext.EmployeeId} попытался прочитать задачи #{employeeId}.";
            await langfuse.LogSecurityAlertAsync(traceId, telegramId, toolName, "bitrix24:read_other_user_tasks", denyReason, ctx.RequestAborted);
            return Results.Json(new McpJsonRpcResponse
            {
                Id = request.Id,
                Error = new McpError { Code = 403, Message = "Доступ запрещен: можно читать только свои задачи." }
            }, statusCode: 403);
        }

        var tasks = await repo.GetActionItemsByResponsibleAsync(employeeId);
        return Results.Ok(new McpJsonRpcResponse
        {
            Id = request.Id,
            Result = McpToolCallResult.Text(JsonSerializer.Serialize(tasks))
        });
    }

    if (toolName == "create_task")
    {
        var confirmedHeader = ctx.Request.Headers["X-User-Confirmed"].ToString();
        if (!string.Equals(confirmedHeader, "true", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new McpJsonRpcResponse
            {
                Id = request.Id,
                Result = McpToolCallResult.Text(JsonSerializer.Serialize(new
                {
                    status = "confirmation_required",
                    requires_confirmation = true,
                    message = "Требуется подтверждение пользователя (X-User-Confirmed: true) для создания задачи."
                }))
            });
        }

        var title = request.Params?.Arguments?.GetValueOrDefault("title")?.ToString() ?? "Поручение по встрече";
        var bitrixId = Random.Shared.Next(1050, 9999);

        var res = new
        {
            bitrix24_task_id = bitrixId,
            title = title,
            status = "created",
            url = $"https://fotoprint.bitrix24.by/tasks/view/{bitrixId}/"
        };

        return Results.Ok(new McpJsonRpcResponse
        {
            Id = request.Id,
            Result = McpToolCallResult.Text(JsonSerializer.Serialize(res))
        });
    }

    return Results.BadRequest(new McpJsonRpcResponse
    {
        Id = request.Id,
        Error = new McpError { Code = -32601, Message = $"Метод {toolName} не найден" }
    });
});

app.Run();

public record CreateTaskRequest(string Title, string? Description, int ResponsibleId, DateTime? Deadline, Guid? MeetingId);

