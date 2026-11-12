using System.Text.Json;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Mcp;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;

var builder = WebApplication.CreateBuilder(args);

var appConfig = FotoprintConfigLoader.Load();
builder.Services.AddSingleton(appConfig);

var employeeReaderUrl = builder.Configuration["Security:EmployeeReaderUrl"] ?? "http://localhost:8081";
var langfuseBaseUrl = builder.Configuration["Langfuse:BaseUrl"] ?? "http://localhost:3000";
var langfusePubKey = builder.Configuration["Langfuse:PublicKey"] ?? "pk-lf-fotoprint-local";
var langfuseSecKey = builder.Configuration["Langfuse:SecretKey"] ?? "sk-lf-fotoprint-local";

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

// Защита периметра и изоляция MCP: запрет прямого публичного доступа
app.UseMiddleware<PerimeterDefenseMiddleware>();
app.UseMiddleware<InternalServiceAuthMiddleware>();
app.UseMiddleware<SecurityInterceptorMiddleware>();

// Health
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "MCP-1C",
    timestamp = DateTime.UtcNow
}));

// Tools list
var tools = new List<McpToolDefinition>
{
    new()
    {
        Name = "get_order_status",
        Description = "Возвращает актуальный статус заказа из 1С:Управление нашей типографией (этап производства, готовность в %, планируемая отгрузка).",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                order_id = new { type = "string", description = "Номер заказа, например 44102 или ЗК-0044102" }
            },
            required = new[] { "order_id" }
        }
    },
    new()
    {
        Name = "get_production_kpis",
        Description = "Возвращает сводные производственные метрики цехов из 1С: выработка, процент брака, загрузка офсетных машин и постпресса.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                date_from = new { type = "string", description = "Дата начала периода (ГГГГ-ММ-ДД)" },
                date_to = new { type = "string", description = "Дата окончания периода (ГГГГ-ММ-ДД)" },
                department = new { type = "string", description = "Цех/отдел (Офсет, Цифровая печать, Постпресс, Склад)" }
            }
        }
    }
};

app.MapGet("/tools/list", () => Results.Ok(new { tools }));

// Tool 1: get_order_status
app.MapGet("/tools/get_order_status", [RequirePermission("onec:read_orders")] [RequireConfirmation(false)] (string order_id) =>
{
    // Реалистичные моковые производственные данные 1С:УНТ
    var cleanId = order_id.Replace("ЗК-", "").Trim();
    object orderData = cleanId switch
    {
        "44102" => new
        {
            order_id = "ЗК-44102",
            client = "Альфа-Маркет",
            item_name = "Рекламный каталог А4, 48 полос, тираж 50 000 экз.",
            current_stage = "Офсетная печать (Heidelberg Speedmaster XL 75)",
            readiness_percent = 65,
            status = "in_progress",
            materials_status = "Мелованная бумага 130г доставлена, краска в наличии",
            planned_delivery = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd"),
            alerts = new[] { "Внимание: Зафиксировано отставание от графика на 6 часов из-за юстировки валов." },
            source = "1c_mock"
        },
        "44103" => new
        {
            order_id = "ЗК-44103",
            client = "ТехноМир",
            item_name = "Брошюры А5 на скрепке, тираж 25 000 экз.",
            current_stage = "Постпечатная сборка и ВШРА (Muller Martini)",
            readiness_percent = 88,
            status = "in_progress",
            materials_status = "Все материалы в цехе, обложка с УФ-лаком отпечатана",
            planned_delivery = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"),
            alerts = Array.Empty<string>(),
            source = "1c_mock"
        },
        _ => new
        {
            order_id = $"ЗК-{cleanId}",
            client = "ГринВей Корпорейшн",
            item_name = "Буклеты евро, 4+4, тираж 10 000 экз.",
            current_stage = "Склад готовой продукции (ожидает отгрузки)",
            readiness_percent = 100,
            status = "ready_for_dispatch",
            materials_status = "Все материалы списаны, тираж упакован на паллеты",
            planned_delivery = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            alerts = Array.Empty<string>(),
            source = "1c_mock"
        }
    };

    return Results.Ok(orderData);
});

// Tool 2: get_production_kpis
app.MapGet("/tools/get_production_kpis", [RequirePermission("onec:read_production_kpis")] [RequireConfirmation(false)] (string? date_from, string? date_to, string? department) =>
{
    var dept = department ?? "Офсетный цех";
    var kpiData = new
    {
        period = new
        {
            date_from = date_from ?? DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd"),
            date_to = date_to ?? DateTime.UtcNow.ToString("yyyy-MM-dd")
        },
        department = dept,
        metrics = new
        {
            total_impressions = 485000, // оттисков
            completed_orders_count = 28,
            active_orders_count = 6,
            scrap_rate_percent = 1.9, // норма до 2.5%
            scrap_rate_status = "NORMAL (в пределах норматива < 2.5%)",
            equipment_utilization_percent = 87.4,
            planned_maintenance = "Плановая замена красочных валов Heidelberg назначена на субботу",
            downtime_hours_total = 4.5
        }
    };

    return Results.Ok(kpiData);
});

// Unified tool execution endpoint for MCP / n8n
app.MapPost("/tools/call", async (McpJsonRpcRequest request, ISecurityValidator validator, ILangfuseClient langfuse, HttpContext ctx) =>
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

    string requiredPerm = toolName == "get_production_kpis" ? "onec:read_production_kpis" : "onec:read_orders";
    var (isAllowed, empContext, reason) = await validator.ValidateAccessAsync(telegramId, requiredPerm, toolName, traceId, ctx.RequestAborted);

    if (!isAllowed)
    {
        return Results.Json(new McpJsonRpcResponse
        {
            Id = request.Id,
            Error = new McpError { Code = 403, Message = reason ?? "Access Denied" }
        }, statusCode: 403);
    }

    if (toolName == "get_order_status")
    {
        var orderId = request.Params?.Arguments?.GetValueOrDefault("order_id")?.ToString() ?? "44102";
        var isUrgent = orderId.Contains("44102");
        var res = new
        {
            order_id = orderId,
            client = isUrgent ? "Альфа-Маркет" : "ГринВей",
            current_stage = isUrgent ? "Офсетная печать (Heidelberg)" : "Склад",
            readiness_percent = isUrgent ? 65 : 100,
            planned_delivery = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd"),
            alerts = isUrgent ? new[] { "Задержка на 6 часов из-за юстировки валов." } : Array.Empty<string>()
        };
        return Results.Ok(new McpJsonRpcResponse
        {
            Id = request.Id,
            Result = McpToolCallResult.Text(JsonSerializer.Serialize(res))
        });
    }

    if (toolName == "get_production_kpis")
    {
        var res = new
        {
            department = "Офсетный цех",
            total_impressions = 485000,
            scrap_rate_percent = 1.9,
            equipment_utilization_percent = 87.4,
            status = "В норме"
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
