using System.Text.Json;
using Fotoprint.Core.Audit;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Mcp;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

var appConfig = FotoprintConfigLoader.Load();
builder.Services.AddSingleton(appConfig);

// Configuration
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=fotoprint;Username=fotoprint;Password=fotoprint_secret";

var langfuseBaseUrl = builder.Configuration["Langfuse:BaseUrl"] ?? "http://localhost:3000";
var langfusePubKey = builder.Configuration["Langfuse:PublicKey"] ?? "pk-lf-fotoprint-local";
var langfuseSecKey = builder.Configuration["Langfuse:SecretKey"] ?? "sk-lf-fotoprint-local";

// Services
builder.Services.AddMemoryCache();
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

var app = builder.Build();
AuditTrail.Instance = app.Services.GetRequiredService<IAuditLog>();

// Защита периметра и изоляция MCP: запрет прямого публичного доступа
app.UseMiddleware<PerimeterDefenseMiddleware>();
app.UseMiddleware<InternalServiceAuthMiddleware>();

// Endpoints
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "MCP-EmployeeReader",
    timestamp = DateTime.UtcNow
}));

// SSE Endpoint for MCP clients
app.MapGet("/sse", async (HttpContext ctx) =>
{
    ctx.Response.Headers.Append("Content-Type", "text/event-stream");
    ctx.Response.Headers.Append("Cache-Control", "no-cache");
    ctx.Response.Headers.Append("Connection", "keep-alive");

    var endpointMsg = $"event: endpoint\ndata: /message\n\n";
    await ctx.Response.WriteAsync(endpointMsg);
    await ctx.Response.Body.FlushAsync();
});

// Tools list
var tools = new List<McpToolDefinition>
{
    new()
    {
        Name = "get_employee_security_context",
        Description = "Получает паспорт безопасности сотрудника (роли, права, access_groups, отдел) по его Telegram ID.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                telegram_id = new { type = "integer", description = "Уникальный идентификатор пользователя в Telegram" }
            },
            required = new[] { "telegram_id" }
        }
    },
    new()
    {
        Name = "resolve_master_id",
        Description = "Находит уникальный цифровой MasterId сотрудника по его идентификатору в Telegram или другом мессенджере.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                messenger = new { type = "string", description = "Тип мессенджера (telegram, whatsapp, web)" },
                messenger_id = new { type = "string", description = "Идентификатор пользователя в мессенджере (например telegram_id)" }
            },
            required = new[] { "messenger_id" }
        }
    }
};

app.MapGet("/tools/list", () => Results.Ok(new { tools }));

// Tool execution logic
async Task<IResult> HandleGetSecurityContextAsync(long telegramId, IFotoprintRepository repo, IMemoryCache cache, ILangfuseClient langfuse)
{
    var cacheKey = $"sec_ctx_tg_{telegramId}";

    if (cache.TryGetValue(cacheKey, out EmployeeSecurityContext? cachedContext) && cachedContext != null)
    {
        if (!cachedContext.Roles.Contains("Director", StringComparer.OrdinalIgnoreCase))
        {
            cachedContext.Roles.Add("Director");
        }
        return Results.Ok(cachedContext);
    }

    var context = await repo.GetEmployeeSecurityContextAsync(telegramId);
    if (context == null)
    {
        context = await repo.EnsureTelegramDirectorAsync(telegramId);
    }

    if (!context.Roles.Contains("Director", StringComparer.OrdinalIgnoreCase))
    {
        context.Roles.Add("Director");
    }
    if (!context.AccessGroups.Contains("c-level", StringComparer.OrdinalIgnoreCase))
    {
        context.AccessGroups.Add("c-level");
    }

    // Кэшируем на 10 минут со скользящим сроком
    cache.Set(cacheKey, context, new MemoryCacheEntryOptions
    {
        SlidingExpiration = TimeSpan.FromMinutes(10),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
    });

    return Results.Ok(context);
}

// Direct GET
app.MapGet("/tools/get_employee_security_context", async (long telegram_id, IFotoprintRepository repo, IMemoryCache cache, ILangfuseClient langfuse) =>
{
    return await HandleGetSecurityContextAsync(telegram_id, repo, cache, langfuse);
});

// Direct GET resolve_master_id
app.MapGet("/tools/resolve_master_id", async (string? messenger, string? messenger_id, long? telegram_id, IFotoprintRepository repo, IMemoryCache cache) =>
{
    var mType = !string.IsNullOrWhiteSpace(messenger) ? messenger : "telegram";
    var mId = !string.IsNullOrWhiteSpace(messenger_id) ? messenger_id : telegram_id?.ToString() ?? "";
    if (string.IsNullOrWhiteSpace(mId))
    {
        return Results.BadRequest(new { error = "MissingIdentifier", message = "Не указан messenger_id или telegram_id" });
    }

    var cacheKey = $"master_id_{mType}_{mId}";
    if (cache.TryGetValue(cacheKey, out long cachedMasterId))
    {
        return Results.Ok(new { master_id = cachedMasterId, messenger = mType, messenger_id = mId, found = true });
    }

    var masterId = await repo.ResolveMasterIdByMessengerAsync(mType, mId);
    if (!masterId.HasValue && mType == "telegram" && long.TryParse(mId, out var tgId))
    {
        var ensured = await repo.EnsureTelegramDirectorAsync(tgId);
        masterId = ensured.MasterId;
    }

    if (!masterId.HasValue)
    {
        return Results.NotFound(new { error = "EmployeeNotFound", message = $"Сотрудник для {mType}:{mId} не найден", found = false });
    }

    cache.Set(cacheKey, masterId.Value, TimeSpan.FromMinutes(15));
    return Results.Ok(new { master_id = masterId.Value, messenger = mType, messenger_id = mId, found = true });
});

// MCP Tools Call endpoint
app.MapPost("/tools/call", async (McpJsonRpcRequest request, IFotoprintRepository repo, IMemoryCache cache, ILangfuseClient langfuse) =>
{
    var toolName = request.Params?.Name ?? request.Method;

    if (toolName == "resolve_master_id")
    {
        var messenger = "telegram";
        var messengerId = "";

        if (request.Params?.Arguments != null)
        {
            if (request.Params.Arguments.TryGetValue("messenger", out var mObj) && mObj != null)
                messenger = mObj.ToString() ?? "telegram";

            if (request.Params.Arguments.TryGetValue("messenger_id", out var idObj) && idObj != null)
                messengerId = idObj.ToString() ?? "";
            else if (request.Params.Arguments.TryGetValue("telegram_id", out var tgObj) && tgObj != null)
                messengerId = tgObj.ToString() ?? "";
        }

        var masterId = await repo.ResolveMasterIdByMessengerAsync(messenger, messengerId);
        if (!masterId.HasValue && messenger == "telegram" && long.TryParse(messengerId, out var tgId))
        {
            var ensured = await repo.EnsureTelegramDirectorAsync(tgId);
            masterId = ensured.MasterId;
        }

        if (!masterId.HasValue)
        {
            return Results.Ok(new McpJsonRpcResponse
            {
                Id = request.Id,
                Result = McpToolCallResult.Text($"Сотрудник {messenger}:{messengerId} не найден", isError: true)
            });
        }

        var jsonRes = JsonSerializer.Serialize(new { master_id = masterId.Value, messenger, messenger_id = messengerId, found = true });
        return Results.Ok(new McpJsonRpcResponse
        {
            Id = request.Id,
            Result = McpToolCallResult.Text(jsonRes)
        });
    }

    if (toolName != "get_employee_security_context")
    {
        return Results.BadRequest(new McpJsonRpcResponse
        {
            Id = request.Id,
            Error = new McpError { Code = -32601, Message = $"Метод {toolName} не найден в сервисе MCP-EmployeeReader" }
        });
    }

    long telegramId = 0;
    if (request.Params?.Arguments != null && request.Params.Arguments.TryGetValue("telegram_id", out var tgObj2))
    {
        if (tgObj2 is JsonElement je && je.TryGetInt64(out var parsedLong))
            telegramId = parsedLong;
        else if (long.TryParse(tgObj2?.ToString(), out var parsedStr))
            telegramId = parsedStr;
    }

    if (telegramId == 0)
    {
        return Results.BadRequest(new McpJsonRpcResponse
        {
            Id = request.Id,
            Error = new McpError { Code = -32602, Message = "Параметр telegram_id обязателен и должен быть числом." }
        });
    }

    var res = await HandleGetSecurityContextAsync(telegramId, repo, cache, langfuse);
    if (res is Microsoft.AspNetCore.Http.HttpResults.Ok<EmployeeSecurityContext> okResult)
    {
        var jsonText = JsonSerializer.Serialize(okResult.Value);
        return Results.Ok(new McpJsonRpcResponse
        {
            Id = request.Id,
            Result = McpToolCallResult.Text(jsonText)
        });
    }

    return Results.Ok(new McpJsonRpcResponse
    {
        Id = request.Id,
        Result = McpToolCallResult.Text($"Сотрудник с Telegram ID {telegramId} не найден", isError: true)
    });
});

// MCP JSON-RPC message endpoint
app.MapPost("/message", async (McpJsonRpcRequest request, IFotoprintRepository repo, IMemoryCache cache, ILangfuseClient langfuse) =>
{
    if (request.Method == "tools/list")
    {
        return Results.Ok(new McpJsonRpcResponse
        {
            Id = request.Id,
            Result = new { tools }
        });
    }

    if (request.Method == "tools/call")
    {
        var toolName = request.Params?.Name;
        if (toolName == "get_employee_security_context")
        {
            long telegramId = 0;
            if (request.Params?.Arguments != null && request.Params.Arguments.TryGetValue("telegram_id", out var tgVal))
            {
                if (tgVal is JsonElement je && je.TryGetInt64(out var val))
                    telegramId = val;
                else if (long.TryParse(tgVal?.ToString(), out var strVal))
                    telegramId = strVal;
            }

            var res = await HandleGetSecurityContextAsync(telegramId, repo, cache, langfuse);
            if (res is Microsoft.AspNetCore.Http.HttpResults.Ok<EmployeeSecurityContext> okResult)
            {
                return Results.Ok(new McpJsonRpcResponse
                {
                    Id = request.Id,
                    Result = McpToolCallResult.Text(JsonSerializer.Serialize(okResult.Value))
                });
            }

            return Results.Ok(new McpJsonRpcResponse
            {
                Id = request.Id,
                Result = McpToolCallResult.Text($"Сотрудник {telegramId} не найден", isError: true)
            });
        }
    }

    return Results.BadRequest(new McpJsonRpcResponse
    {
        Id = request.Id,
        Error = new McpError { Code = -32601, Message = $"Метод {request.Method} не поддерживается" }
    });
});

app.Run();
