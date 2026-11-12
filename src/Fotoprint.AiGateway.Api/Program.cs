using System.Text;
using System.Text.Json;
using Fotoprint.Core.Agents;
using Fotoprint.Core.Audio;
using Fotoprint.Core.Audit;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Documents;
using Fotoprint.Core.Formatting;
using Fotoprint.Core.Llm;
using Fotoprint.Core.Models;
using Fotoprint.Core.Protocols;
using Fotoprint.Core.Resilience;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;
using Fotoprint.AiGateway.Api.Dashboard;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Загрузка единого конфигурационного манифеста config.yaml
var appConfig = FotoprintConfigLoader.Load();
builder.Services.AddSingleton(appConfig);

// Configuration
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=fotoprint;Username=fotoprint;Password=fotoprint_secret";
var n8nWebhookBaseUrl = builder.Configuration["N8n:WebhookBaseUrl"] ?? "http://localhost:5678/webhook";
var langfuseBaseUrl = builder.Configuration["Langfuse:BaseUrl"] ?? "http://localhost:3000";
var langfusePubKey = builder.Configuration["Langfuse:PublicKey"] ?? "pk-lf-fotoprint-local";
var langfuseSecKey = builder.Configuration["Langfuse:SecretKey"] ?? "sk-lf-fotoprint-local";
var bitrixMcpUrl = builder.Configuration["McpUrls:Bitrix24"] ?? "http://localhost:8083";
var employeeReaderUrl = builder.Configuration["McpUrls:EmployeeReader"] ?? "http://localhost:8081";

// Core Services
var calendarMcpUrl = builder.Configuration["McpUrls:Calendar"] ?? appConfig.McpUrls?.Calendar ?? "http://localhost:8084";
builder.Services.AddHttpClient("CalendarMcpClient", c =>
{
    c.BaseAddress = new Uri(calendarMcpUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
    var key = appConfig.Security?.InternalServiceSecretKey ?? "fotoprint-internal-service-secret-token-2026";
    c.DefaultRequestHeaders.TryAddWithoutValidation("X-Internal-Service-Key", key);
});
builder.Services.AddSingleton<IFotoprintRepository>(_ => new FotoprintRepository(connectionString));
builder.Services.AddSingleton<IAuditLog>(sp => new PostgresAuditLog(connectionString, sp.GetService<ILogger<PostgresAuditLog>>()));
builder.Services.AddSingleton<IPendingActionStore, InMemoryPendingActionStore>();
builder.Services.AddSingleton<IDocumentParser, DocumentParser>();
builder.Services.AddSingleton<IMeetingProtocolGenerator, MeetingProtocolGenerator>();

// Resilience & Load Balancing (Параметры сглаживания и лимитов берутся из config.yaml, 15 RPM для Gemini)
var lbOptions = new LoadBalancerOptions
{
    MaxConcurrentRequests = appConfig.Llm.RateLimits.MaxConcurrentRequests,
    MaxRequestsPerMinute = appConfig.Llm.RateLimits.Rpm,
    MinIntervalBetweenRequestsMs = appConfig.LoadBalancer.MinIntervalBetweenRequestsMs,
    QueueCapacity = appConfig.LoadBalancer.QueueCapacity
};
builder.Services.AddSingleton<ILlmLoadBalancer>(sp => new LlmLoadBalancer(
    lbOptions,
    sp.GetService<ILogger<LlmLoadBalancer>>(),
    sp.GetService<ILangfuseClient>()));
builder.Services.AddSingleton<IRollbackManager, RollbackManager>();

// NVIDIA NIM LLM Client с контролем нагрузки, повторами, защитой от обрывов пула сокетов и трейсингом (единый Singleton для общего счетчика RPM и Circuit Breaker)
var llmTimeout = TimeSpan.FromSeconds(Math.Max(90, appConfig.Llm.Parameters.TimeoutSeconds));
static SocketsHttpHandler CreateLlmSocketsHandler() => new()
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(25),
    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
    KeepAlivePingDelay = TimeSpan.FromSeconds(15),
    KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
    ConnectTimeout = TimeSpan.FromSeconds(15)
};

builder.Services.AddHttpClient("NvidiaHttpClient", c => c.Timeout = llmTimeout)
    .ConfigurePrimaryHttpMessageHandler(CreateLlmSocketsHandler);

builder.Services.AddSingleton<NvidiaClient>(sp => new NvidiaClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("NvidiaHttpClient"),
    sp.GetRequiredService<FotoprintConfig>(),
    sp.GetRequiredService<ILlmLoadBalancer>(),
    sp.GetRequiredService<IRollbackManager>(),
    sp.GetService<ILangfuseClient>(),
    sp.GetService<ILogger<NvidiaClient>>()));
builder.Services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<NvidiaClient>());
builder.Services.AddSingleton<INvidiaClient>(sp => sp.GetRequiredService<NvidiaClient>());
builder.Services.AddSingleton<IGeminiClient>(sp => sp.GetRequiredService<NvidiaClient>());

builder.Services.AddHttpClient("GeminiEmbeddingHttpClient", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<IEmbeddingClient>(sp => new GeminiEmbeddingClient(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeminiEmbeddingHttpClient"),
    sp.GetRequiredService<FotoprintConfig>(),
    sp.GetService<ILogger<GeminiEmbeddingClient>>()));

// 2-й этап: Детекция смены контекста через LLM перед передачей в n8n
builder.Services.AddSingleton<IContextSwitchDetector, ContextSwitchDetector>();

// Langfuse & FinOps Telemetry
builder.Services.AddHttpClient<ILangfuseClient, LangfuseClient>();
builder.Services.AddSingleton(new LangfuseOptions
{
    BaseUrl = langfuseBaseUrl,
    PublicKey = langfusePubKey,
    SecretKey = langfuseSecKey,
    Enabled = true,
    PromptTokenCostPer1k = appConfig.Telemetry.FinOps.PromptTokenCostPer1k,
    CompletionTokenCostPer1k = appConfig.Telemetry.FinOps.CompletionTokenCostPer1k,
    CachedTokenCostPer1k = appConfig.Telemetry.FinOps.CachedTokenCostPer1k
});

// Автоматический оценщик качества LLM-as-a-Judge (Faithfulness, Context Switch, Formatting)
builder.Services.AddSingleton<ILlmJudgeService, LlmJudgeService>();

// Автономный AI-агент с Tool Calling (автоматический выбор нужного MCP)
builder.Services.AddSingleton<IFotoprintAgent, FotoprintAiAgent>();

// Аудиотранскрибация (локальный C++ Whisper/Vulkan GPU + облачный Gemini Fallback) и MoM-анализатор
builder.Services.AddSingleton<IVocabularyProvider, YamlVocabularyProvider>();
builder.Services.AddSingleton<IVocabularyPostProcessor, VocabularyPostProcessor>();
builder.Services.AddSingleton<IAudioEngineResolver, AudioEngineResolver>();
builder.Services.AddSingleton<LocalEngineAudioTranscriber>();
builder.Services.AddHttpClient<CloudGeminiAudioTranscriber>();
builder.Services.AddSingleton<IAudioTranscriber>(sp =>
    new CompositeAudioTranscriber(
        sp.GetRequiredService<LocalEngineAudioTranscriber>(),
        sp.GetRequiredService<CloudGeminiAudioTranscriber>(),
        sp.GetRequiredService<FotoprintConfig>(),
        sp.GetService<ILangfuseClient>(),
        sp.GetRequiredService<ILogger<CompositeAudioTranscriber>>()));
builder.Services.AddSingleton<IMeetingTranscriptAnalyzer, MeetingTranscriptAnalyzer>();

builder.Services.AddHttpClient("N8nClient", client =>
{
    client.BaseAddress = new Uri(n8nWebhookBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(180);
});

builder.Services.AddHttpClient("BitrixMcpClient", client =>
{
    client.BaseAddress = new Uri(bitrixMcpUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("X-Internal-Service-Key", appConfig.Security.InternalServiceSecretKey);
});

builder.Services.AddHttpClient("EmployeeReaderClient", client =>
{
    client.BaseAddress = new Uri(employeeReaderUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("X-Internal-Service-Key", appConfig.Security.InternalServiceSecretKey);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

Fotoprint.AiGateway.Api.Telegram.TelegramEndpoints.AddTelegramIntegration(builder.Services, builder.Configuration);
Fotoprint.AiGateway.Api.Notifications.NotificationEndpoints.AddNotificationIntegration(builder.Services);

var app = builder.Build();
AuditTrail.Instance = app.Services.GetRequiredService<IAuditLog>();

// Защита периметра от сканеров портов, ботов и поиска уязвимых файлов
app.UseMiddleware<PerimeterDefenseMiddleware>();

// Раздача статических файлов фронтенда из infra/nginx/html
string? htmlStaticDir = null;
var probeDir = new DirectoryInfo(AppContext.BaseDirectory);
while (probeDir != null)
{
    var candidate = Path.Combine(probeDir.FullName, "infra", "nginx", "html");
    if (Directory.Exists(candidate))
    {
        htmlStaticDir = candidate;
        break;
    }
    probeDir = probeDir.Parent;
}

if (htmlStaticDir == null)
{
    probeDir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (probeDir != null)
    {
        var candidate = Path.Combine(probeDir.FullName, "infra", "nginx", "html");
        if (Directory.Exists(candidate))
        {
            htmlStaticDir = candidate;
            break;
        }
        probeDir = probeDir.Parent;
    }
}

if (htmlStaticDir != null && Directory.Exists(htmlStaticDir))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(htmlStaticDir),
        RequestPath = ""
    });
}


// Веб-интерфейс (при локальном запуске без Nginx отдает infra/nginx/html/index.html)
app.MapGet("/", () =>
{
    var roots = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
    foreach (var root in roots)
    {
        var dir = root;
        for (var i = 0; i <= 6 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "infra", "nginx", "html", "index.html");
            if (File.Exists(candidate))
            {
                return Results.File(candidate, "text/html; charset=utf-8");
            }
            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }
    }

    return Results.NotFound("Web UI (infra/nginx/html/index.html) not found.");
});

// Аудиотранскрибация речи (Speech-to-Text: локальный Whisper C++ Vulkan/CPU + Gemini Fallback)
app.MapPost("/api/audio/transcribe", async (
    IFormFile file,
    [FromQuery] string? language,
    IAudioTranscriber transcriber,
    ILangfuseClient langfuse,
    FotoprintConfig config,
    CancellationToken ct) =>
{
    var traceId = langfuse.GenerateTraceId();
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new
        {
            success = false,
            text = string.Empty,
            segments = new List<AudioSegment>(),
            duration_ms = 0L,
            provider = "none",
            error = "Аудиофайл пуст или отсутствует."
        });
    }

    var maxBytes = Math.Max(1, config.Audio.MaxFileSizeMb) * 1024L * 1024L;
    if (file.Length > maxBytes)
    {
        return Results.BadRequest(new
        {
            success = false,
            text = string.Empty,
            segments = new List<AudioSegment>(),
            duration_ms = 0L,
            provider = "none",
            error = $"Размер файла превышает лимит {config.Audio.MaxFileSizeMb} МБ."
        });
    }

    var startSpan = DateTime.UtcNow;
    var safeAudioFileName = string.IsNullOrWhiteSpace(file.FileName) ? "audio.wav" : Path.GetFileName(file.FileName);
    if (string.IsNullOrWhiteSpace(safeAudioFileName)) safeAudioFileName = "audio.wav";
    await using var stream = file.OpenReadStream();
    var result = await transcriber.TranscribeAsync(
        stream,
        safeAudioFileName,
        string.IsNullOrWhiteSpace(language) ? "ru" : language,
        ct);
    var endSpan = DateTime.UtcNow;

    try
    {
        var traceName = "meeting-audio-transcribe";
        await langfuse.CreateTraceAsync(
            traceId: traceId,
            name: traceName,
            userId: null,
            sessionId: null,
            metadata: new { file_name = safeAudioFileName, size_bytes = file.Length, language = language ?? "ru", provider = result.Provider, duration_ms = result.DurationMs },
            tags: new[] { "channel:api", "module:meetings", $"stt:{result.Provider}", result.Success ? "status:ok" : "status:error" },
            input: new { file_name = safeAudioFileName, size_bytes = file.Length, language = language ?? "ru" },
            output: result.Success ? (object)new { text = result.Text, segments_count = result.Segments?.Count ?? 0, provider = result.Provider } : new { error = result.ErrorMessage },
            ct: ct);

        await langfuse.CreateSpanAsync(
            traceId: traceId,
            name: "Span:Audio_Transcribe",
            startTime: startSpan,
            endTime: endSpan,
            input: new { file_name = safeAudioFileName, size_bytes = file.Length, language = language ?? "ru" },
            output: new { success = result.Success, provider = result.Provider, text_length = result.Text?.Length ?? 0 },
            metadata: new { duration_ms = result.DurationMs, error = result.ErrorMessage },
            ct: ct);
    }
    catch { }

    return Results.Ok(new
    {
        success = result.Success,
        trace_id = traceId,
        text = result.Text,
        segments = result.Segments ?? new List<AudioSegment>(),
        duration_ms = result.DurationMs,
        provider = result.Provider,
        error = result.ErrorMessage
    });
}).DisableAntiforgery();

// Health
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "Fotoprint.AiGateway.Api",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
}));

// Единый управленческий дашборд Трека 1 (/dashboard/, /api/system/stats, /api/decisions) + телеметрия LLM
app.MapDashboardEndpoints();

// Прямой вызов NVIDIA LLM с контролем нагрузки и повторами
app.MapPost("/api/llm/generate", async (
    [FromBody] GeminiPromptRequest req,
    INvidiaClient llm,
    ILangfuseClient langfuse,
    FotoprintConfig config) =>
{
    var traceId = !string.IsNullOrWhiteSpace(req.TraceId) ? req.TraceId : langfuse.GenerateTraceId();
    var traceName = !string.IsNullOrWhiteSpace(req.TraceName) ? req.TraceName : "api-llm-generate";
    var startGen = DateTime.UtcNow;

    var response = await llm.GenerateContentAsync(
        prompt: req.Prompt,
        systemInstruction: req.SystemInstruction,
        temperature: req.Temperature,
        maxOutputTokens: req.MaxOutputTokens,
        traceId: traceId);
    var endGen = DateTime.UtcNow;

    await langfuse.CreateTraceAsync(
        traceId: traceId,
        name: traceName,
        userId: req.UserId,
        sessionId: req.SessionId,
        metadata: new { prompt_length = req.Prompt?.Length ?? 0, model = response.Model ?? config.Llm.Models.ChatModel, total_tokens = response.TotalTokens },
        tags: new[] { "channel:api", $"provider:{config.Llm.Provider}", response.IsSuccess ? "status:ok" : "status:error" },
        input: req.Prompt,
        output: response.IsSuccess ? response.Text : response.ErrorMessage);

    await langfuse.CreateGenerationAsync(
        traceId: traceId,
        name: traceName.Contains("summarize", StringComparison.OrdinalIgnoreCase) ? "Gen:Meeting_LLM_Summarize" : "Gen:LLM_Generate",
        model: response.Model ?? config.Llm.Models.ChatModel,
        startTime: startGen,
        endTime: endGen,
        promptTokens: response.PromptTokens,
        completionTokens: response.CandidatesTokens,
        totalTokens: response.TotalTokens,
        input: req.Prompt,
        output: response.IsSuccess ? response.Text : response.ErrorMessage);

    return Results.Ok(new
    {
        response,
        llm_call_stats = llm.GetStats(),
        trace_id = traceId
    });
});

// Прямой вызов Gemini / NVIDIA (обратная совместимость)
app.MapPost("/api/gemini/generate", async (
    [FromBody] GeminiPromptRequest req,
    IGeminiClient gemini,
    ILangfuseClient langfuse,
    FotoprintConfig config) =>
{
    var traceId = langfuse.GenerateTraceId();
    await langfuse.CreateTraceAsync(
        traceId: traceId,
        name: "api-gemini-generate",
        metadata: new { prompt_length = req.Prompt?.Length ?? 0, model = config.Llm.Models.ChatModel },
        tags: new[] { "channel:api", $"provider:{config.Llm.Provider}" });

    var response = await gemini.GenerateContentAsync(
        prompt: req.Prompt,
        systemInstruction: req.SystemInstruction,
        temperature: req.Temperature,
        maxOutputTokens: req.MaxOutputTokens,
        traceId: traceId);

    return Results.Ok(new
    {
        response,
        gemini_call_stats = gemini.GetStats(),
        trace_id = traceId
    });
});

// Список доступных пользователей и их RBAC-ролей для переключателя в Web UI
app.MapGet("/api/agent/users", async (IFotoprintRepository repo, CancellationToken ct) =>
{
    var employees = await repo.GetAllEmployeesAsync(ct);
    var users = new List<object>();

    foreach (var emp in employees.Where(e => e.TelegramId.HasValue))
    {
        var secCtx = await repo.GetEmployeeSecurityContextAsync(emp.TelegramId!.Value, ct);
        users.Add(new
        {
            employee_id = emp.Id,
            telegram_id = emp.TelegramId.Value,
            full_name = secCtx?.FullName ?? $"{emp.LastName} {emp.FirstName}".Trim(),
            department = emp.Department,
            position = emp.Position,
            roles = secCtx?.Roles ?? new List<string>(),
            permissions = secCtx?.Permissions ?? new List<string>()
        });
    }

    return Results.Ok(new { users });
});

// Масштабируемый умный поиск сотрудников по ФИО, должности и отделу для календаря и модалок
app.MapGet("/api/employees/search", async (
    [FromQuery] string? q,
    [FromQuery] int? limit,
    IFotoprintRepository repo,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var logger = loggerFactory.CreateLogger("EmployeeSearchApi");
    try
    {
        var effectiveLimit = Math.Clamp(limit ?? 10, 1, 50);
        var matchedUsers = await repo.SearchEmployeesAsync(q ?? string.Empty, effectiveLimit, ct);

        var employees = new List<object>(matchedUsers.Count);
        foreach (var emp in matchedUsers)
        {
            EmployeeSecurityContext? secCtx = null;
            try
            {
                if (emp.MasterId != 0)
                {
                    secCtx = await repo.GetEmployeeSecurityContextByMasterIdAsync(emp.MasterId, ct);
                }
                else if (emp.TelegramId.HasValue && emp.TelegramId.Value != 0)
                {
                    secCtx = await repo.GetEmployeeSecurityContextAsync(emp.TelegramId.Value, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve security context for employee {EmployeeId}", emp.Id);
            }

            var shortName = User.FormatShortName(emp.LastName, emp.FirstName, emp.MiddleName);
            var fullName = !string.IsNullOrWhiteSpace(emp.FullName)
                ? emp.FullName
                : (secCtx?.FullName ?? $"{emp.LastName} {emp.FirstName}".Trim());

            employees.Add(new
            {
                employee_id = emp.Id,
                telegram_id = emp.TelegramId,
                full_name = fullName,
                short_name = shortName,
                department = emp.Department,
                position = emp.Position,
                roles = secCtx?.Roles ?? new List<string>()
            });
        }

        return Results.Ok(new
        {
            employees,
            total_matched = employees.Count
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error searching employees with query '{Query}'", q);
        return Results.Problem(
            detail: "Internal error searching employees",
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

// Внутренний вызов AI-агента из оркестратора n8n (после проверки прав и роутинга в n8n)
app.MapPost("/api/agent/execute", async (
    [FromBody] AgentChatRequest req,
    HttpContext httpContext,
    IFotoprintRepository repo,
    FotoprintConfig config,
    IFotoprintAgent agent,
    ILangfuseClient langfuse,
    CancellationToken ct) =>
{
    var traceId = !string.IsNullOrWhiteSpace(req.TraceId) ? req.TraceId : langfuse.GenerateTraceId();

    if (!HasValidInternalServiceKey(httpContext, config))
    {
        var secContext = await repo.GetEmployeeSecurityContextAsync(req.TelegramId, ct);
        if (secContext == null)
        {
            await langfuse.LogSecurityAlertAsync(traceId, req.TelegramId, "/api/agent/execute", "employee_registered",
                $"Неизвестный или неактивный TelegramId {req.TelegramId} попытался вызвать /api/agent/execute напрямую.", ct);
            return Results.Json(new
            {
                is_success = false,
                error = "Access Denied",
                message = "Доступ запрещен: сотрудник не найден или деактивирован.",
                trace_id = traceId
            }, statusCode: 403);
        }
    }

    var response = await agent.ProcessMessageAsync(
        telegramId: req.TelegramId,
        chatId: req.ChatId,
        messageText: req.Message,
        traceId: traceId,
        ct: ct);

    return Results.Ok(new
    {
        answer = response.AnswerText,
        tools_invoked = response.ToolsInvoked,
        total_llm_calls = response.TotalLlmCalls,
        total_tokens = response.TotalTokensUsed,
        is_success = response.IsSuccess,
        pending_action_id = response.PendingActionId,
        trace_id = traceId
    });
});

// Диалог из Web UI: трейс + контекст -> оркестратор n8n (fotoprint-chat) -> AI-агент -> ответ
app.MapPost("/api/agent/chat", async (
    [FromBody] AgentChatRequest req,
    IFotoprintRepository repo,
    IHttpClientFactory httpFactory,
    IContextSwitchDetector switchDetector,
    ILlmLoadBalancer loadBalancer,
    IRollbackManager rollbackManager,
    ILangfuseClient langfuse,
    FotoprintConfig config,
    CancellationToken ct) =>
{
    var traceId = langfuse.GenerateTraceId();
    var masterId = await repo.ResolveMasterIdByMessengerAsync("telegram", req.TelegramId.ToString(), ct);

    string effectiveSessionId = $"web-chat-{req.ChatId}";
    bool isContextSwitched = false;
    string branch = "general_chat";
    string switchReason = "Новый веб-диалог";

    if (masterId.HasValue)
    {
        var activeSession = await repo.GetOrCreateActiveSessionByMasterIdAsync(masterId.Value, req.ChatId, "web", ct);
        var recentMsgs = await repo.GetMessagesBySessionIdAsync(activeSession.Id, limit: config.ContextSwitch.MaxHistoryMessages, ct: ct);
        var switchResult = await switchDetector.DetectSwitchAsync(recentMsgs, req.Message, traceId, ct);

        isContextSwitched = switchResult.IsSwitched;
        branch = switchResult.SuggestedBranch;
        switchReason = switchResult.Reason;

        if (switchResult.IsSwitched)
        {
            activeSession = await repo.SwitchToNewSessionAsync(activeSession.Id, masterId.Value, req.ChatId, branch, "web", ct);
        }

        effectiveSessionId = activeSession.Id.ToString();

        await repo.SaveMessageToSessionAsync(
            sessionId: activeSession.Id,
            masterId: masterId.Value,
            role: "user",
            content: req.Message,
            isFrozen: false,
            metadata: new { trace_id = traceId, branch, is_context_switched = isContextSwitched },
            ct: ct);
    }

    var requestStartTime = DateTime.UtcNow;
    await langfuse.CreateTraceAsync(
        traceId: traceId,
        name: "web-agent-chat",
        userId: req.TelegramId.ToString(),
        sessionId: effectiveSessionId,
        metadata: new { telegram_id = req.TelegramId, chat_id = req.ChatId, branch, is_context_switched = isContextSwitched, message = req.Message },
        tags: new[] { "channel:web-chat", "orchestrator:n8n", $"branch:{branch}", $"provider:{config.Llm.Provider}" },
        input: req.Message,
        ct: ct);

    var n8nPayload = new
    {
        trace_id = traceId,
        master_id = masterId,
        session_id = effectiveSessionId,
        is_context_switched = isContextSwitched,
        branch,
        switch_reason = switchReason,
        chat_id = req.ChatId,
        user_id = req.TelegramId,
        channel = "web-chat",
        text = req.Message
    };

    var n8nStart = DateTime.UtcNow;
    var n8nCallResult = await rollbackManager.ExecuteWithRollbackAsync<(bool Forbidden, JsonElement Body)>(
        nodeName: "N8n:WebChat",
        action: async (attempt, retryToken) =>
        {
            var n8nClient = httpFactory.CreateClient("N8nClient");
            using var n8nReq = new HttpRequestMessage(HttpMethod.Post, "fotoprint-chat")
            {
                Content = JsonContent.Create(n8nPayload)
            };
            n8nReq.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);
            n8nReq.Headers.TryAddWithoutValidation("X-Telegram-Id", req.TelegramId.ToString());
            n8nReq.Headers.TryAddWithoutValidation("X-Internal-Service-Key", config.Security.InternalServiceSecretKey);

            using var resp = await n8nClient.SendAsync(n8nReq, retryToken);
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var errJson = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: retryToken);
                return (true, errJson);
            }

            if (!resp.IsSuccessStatusCode)
            {
                throw new NodeTransientException($"n8n вернул код {(int)resp.StatusCode}: {resp.ReasonPhrase}", (int)resp.StatusCode);
            }

            var okJson = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: retryToken);
            return (false, okJson);
        },
        compensatingRollbackAction: (ex, token) => Task.CompletedTask,
        traceId: traceId,
        ct: ct);
    var n8nEnd = DateTime.UtcNow;

    if (!n8nCallResult.IsSuccess)
    {
        var errAnswer = $"⚠️ Ошибка оркестратора n8n: {n8nCallResult.ErrorMessage}";
        await langfuse.CreateSpanAsync(traceId, "Span:N8n_Orchestrator", n8nStart, n8nEnd, input: n8nPayload, output: new { error = n8nCallResult.ErrorMessage }, ct: ct);
        await langfuse.CreateTraceAsync(
            traceId: traceId,
            name: "web-agent-chat",
            userId: req.TelegramId.ToString(),
            sessionId: effectiveSessionId,
            metadata: new { telegram_id = req.TelegramId, chat_id = req.ChatId, branch, is_context_switched = isContextSwitched, is_success = false },
            tags: new[] { "channel:web-chat", "orchestrator:n8n", $"branch:{branch}", "status:error" },
            input: req.Message,
            output: errAnswer,
            ct: ct);
        await langfuse.LogScoreAsync(traceId, "agent_success", 0.0, n8nCallResult.ErrorMessage, ct: ct);

        return Results.Ok(new
        {
            answer = errAnswer,
            tools_invoked = Array.Empty<string>(),
            total_llm_calls = 0,
            total_tokens = 0,
            is_success = false,
            trace_id = traceId,
            orchestrator = "n8n",
            branch
        });
    }

    var (forbidden, body) = n8nCallResult.Value;
    await langfuse.CreateSpanAsync(traceId, "Span:N8n_Orchestrator", n8nStart, n8nEnd, input: n8nPayload, output: body, metadata: new { forbidden, branch }, ct: ct);

    if (forbidden)
    {
        var denyMsg = body.TryGetProperty("message", out var msgProp)
            ? msgProp.GetString()
            : "🚫 Доступ запрещен оркестратором n8n.";

        await langfuse.CreateTraceAsync(
            traceId: traceId,
            name: "web-agent-chat",
            userId: req.TelegramId.ToString(),
            sessionId: effectiveSessionId,
            metadata: new { telegram_id = req.TelegramId, chat_id = req.ChatId, branch = "auth_rejected", is_success = false },
            tags: new[] { "channel:web-chat", "orchestrator:n8n", "branch:auth_rejected", "status:forbidden" },
            input: req.Message,
            output: denyMsg,
            ct: ct);
        await langfuse.LogScoreAsync(traceId, "agent_success", 0.0, "RBAC 403 Forbidden", ct: ct);

        return Results.Ok(new
        {
            answer = denyMsg,
            tools_invoked = Array.Empty<string>(),
            total_llm_calls = 0,
            total_tokens = 0,
            is_success = false,
            trace_id = traceId,
            orchestrator = "n8n",
            branch = "auth_rejected"
        });
    }

    var answerText = PlainTextFormatter.Sanitize(body.TryGetProperty("answer", out var ansProp) ? ansProp.GetString() : null);
    var isAgentSuccess = !body.TryGetProperty("is_success", out var succProp) || succProp.ValueKind != JsonValueKind.False;
    var toolsCount = body.TryGetProperty("tools_invoked", out var toolsProp) && toolsProp.ValueKind == JsonValueKind.Array
        ? toolsProp.GetArrayLength()
        : 0;
    var totalTokensUsed = body.TryGetProperty("total_tokens", out var tokProp) && tokProp.TryGetInt32(out var tv) ? tv : 0;
    var totalLatencySec = Math.Round((DateTime.UtcNow - requestStartTime).TotalSeconds, 2);

    if (masterId.HasValue && Guid.TryParse(effectiveSessionId, out var sid) && !string.IsNullOrEmpty(answerText))
    {
        await repo.SaveMessageToSessionAsync(
            sessionId: sid,
            masterId: masterId.Value,
            role: "assistant",
            content: answerText,
            isFrozen: false,
            metadata: new { trace_id = traceId, orchestrator = "n8n", branch },
            ct: ct);
    }

    // Обновляем корневой Trace (Input/Output + итоговые метрики) и записываем операционные Scores для дашборда Langfuse
    await langfuse.CreateTraceAsync(
        traceId: traceId,
        name: "web-agent-chat",
        userId: req.TelegramId.ToString(),
        sessionId: effectiveSessionId,
        metadata: new
        {
            telegram_id = req.TelegramId,
            chat_id = req.ChatId,
            branch,
            is_context_switched = isContextSwitched,
            tools_invoked_count = toolsCount,
            total_tokens = totalTokensUsed,
            latency_sec = totalLatencySec,
            is_success = isAgentSuccess
        },
        tags: new[] { "channel:web-chat", "orchestrator:n8n", $"branch:{branch}", $"provider:{config.Llm.Provider}", isAgentSuccess ? "status:ok" : "status:error" },
        input: req.Message,
        output: answerText,
        ct: ct);

    _ = Task.Run(async () =>
    {
        try
        {
            await langfuse.LogScoreAsync(traceId, "agent_success", isAgentSuccess ? 1.0 : 0.0, $"Branch: {branch}");
            await langfuse.LogScoreAsync(traceId, "tools_invoked_count", toolsCount, $"MCP tools used: {toolsCount}");
            await langfuse.LogScoreAsync(traceId, "context_switched", isContextSwitched ? 1.0 : 0.0, switchReason);
            await langfuse.LogScoreAsync(traceId, "response_latency_sec", totalLatencySec, $"End-to-end latency: {totalLatencySec}s");
        }
        catch { }
    });

    return Results.Ok(body);
});

// Универсальная канально-независимая точка входа чата (для будущей самописной CRM, Web-виджета, Telegram и др.)
app.MapPost("/api/chat/ingress", async (
    HttpContext httpContext,
    [FromBody] ChannelChatIngressRequest req,
    IFotoprintRepository repo,
    IFotoprintAgent agent,
    ILangfuseClient langfuse,
    FotoprintConfig config,
    CancellationToken ct) =>
{
    if (!InternalServiceAuthMiddleware.IsAuthorized(httpContext.Request, config))
    {
        return Results.Unauthorized();
    }

    var channel = string.IsNullOrWhiteSpace(req.Channel) ? "crm" : req.Channel.Trim().ToLowerInvariant();
    var externalUserId = req.ExternalUserId?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(externalUserId) || string.IsNullOrWhiteSpace(req.Message))
    {
        return Results.BadRequest(new { ok = false, error = "external_user_id and message are required." });
    }

    var traceId = !string.IsNullOrWhiteSpace(req.TraceId) ? req.TraceId! : langfuse.GenerateTraceId();
    var masterId = await repo.ResolveMasterIdByMessengerAsync(channel, externalUserId, ct);

    EmployeeSecurityContext? secCtx = null;
    if (masterId.HasValue)
    {
        secCtx = await repo.GetEmployeeSecurityContextByMasterIdAsync(masterId.Value, ct);
    }

    if (secCtx == null)
    {
        return Results.Json(new
        {
            ok = false,
            error = "User is not registered or inactive in Fotoprint RBAC.",
            channel,
            external_user_id = externalUserId,
            trace_id = traceId
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var chatIdNum = req.ChatId ?? secCtx.TelegramId;
    var activeSession = await repo.GetOrCreateActiveSessionByMasterIdAsync(secCtx.MasterId, chatIdNum, channel, ct);
    await repo.SaveMessageToSessionAsync(
        sessionId: activeSession.Id,
        masterId: secCtx.MasterId,
        role: "user",
        content: req.Message,
        isFrozen: false,
        metadata: new { trace_id = traceId, channel, external_user_id = externalUserId },
        ct: ct);

    var response = await agent.ProcessMessageAsync(
        telegramId: secCtx.TelegramId,
        chatId: chatIdNum,
        messageText: req.Message,
        traceId: traceId,
        ct: ct);

    if (!string.IsNullOrWhiteSpace(response.AnswerText))
    {
        await repo.SaveMessageToSessionAsync(
            sessionId: activeSession.Id,
            masterId: secCtx.MasterId,
            role: "assistant",
            content: response.AnswerText,
            isFrozen: false,
            metadata: new { trace_id = traceId, channel, tools = response.ToolsInvoked },
            ct: ct);
    }

    return Results.Ok(new
    {
        ok = response.IsSuccess,
        channel,
        external_user_id = externalUserId,
        employee_id = secCtx.EmployeeId,
        master_id = secCtx.MasterId,
        session_id = activeSession.Id,
        answer = response.AnswerText,
        tools_invoked = response.ToolsInvoked,
        total_llm_calls = response.TotalLlmCalls,
        total_tokens = response.TotalTokensUsed,
        pending_action_id = response.PendingActionId,
        actions = BuildPendingActionButtons(response.PendingActionId),
        trace_id = traceId
    });
});

// ============================================================================
// 1. TELEGRAM WEBHOOK & CHAT INGESTION
// ============================================================================
Fotoprint.AiGateway.Api.Telegram.TelegramEndpoints.MapTelegramEndpoints(app);
Fotoprint.AiGateway.Api.Notifications.NotificationEndpoints.MapNotificationEndpoints(app);
Fotoprint.AiGateway.Api.Actions.ActionEndpoints.MapActionEndpoints(app);

// ============================================================================
// 2. RAG ИНДЕКСАЦИЯ И ПОИСК ЗНАНИЙ
// ============================================================================
app.MapPost("/api/rag/index-document", async (
    IFormFile file,
    [FromForm] string? department,
    [FromForm] string? allowed_groups,
    HttpContext httpContext,
    FotoprintConfig config,
    IFotoprintRepository repo,
    IDocumentParser docParser,
    IEmbeddingClient embeddingClient,
    ILangfuseClient langfuse,
    CancellationToken ct) =>
{
    var traceId = langfuse.GenerateTraceId();
    if (!HasValidInternalServiceKey(httpContext, config))
    {
        var caller = await ResolveCallerSecurityContextAsync(httpContext, repo, null, null, ct);
        if (caller == null || !caller.HasPermission("rag:search_all"))
        {
            var actorId = caller?.TelegramId ?? 0;
            await langfuse.LogSecurityAlertAsync(traceId, actorId, "/api/rag/index-document", "rag:search_all",
                $"Попытка несанкционированной индексации документа в RAG (TelegramId={actorId}).", ct);
            return Results.Json(new
            {
                error = "Access Denied",
                message = "Доступ запрещен: индексация регламентов доступна только с правом rag:search_all (Директор)."
            }, statusCode: 403);
        }
    }

    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "Файл пуст или отсутствует." });

    var groups = new List<string> { "all" };
    if (!string.IsNullOrWhiteSpace(allowed_groups))
    {
        groups = allowed_groups.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(g => g.Trim())
            .ToList();
    }

    // Парсим документ (санитизируем имя файла против Path Traversal)
    var safeDocFileName = string.IsNullOrWhiteSpace(file.FileName) ? "document.txt" : Path.GetFileName(file.FileName);
    if (string.IsNullOrWhiteSpace(safeDocFileName)) safeDocFileName = "document.txt";
    await using var stream = file.OpenReadStream();
    var parsed = await docParser.ParseStreamAsync(stream, safeDocFileName);

    // Сохраняем в БД
    var docId = await repo.SaveDocumentAsync(
        title: parsed.Title,
        sourceType: parsed.SourceType,
        filePath: $"/storage/docs/{safeDocFileName}",
        department: department,
        allowedAccessGroups: groups,
        ct: ct
    );

    var chunkTexts = parsed.Chunks.Select(c => c.Text).ToList();
    var embeddings = await embeddingClient.EmbedBatchAsync(chunkTexts, "RETRIEVAL_DOCUMENT", ct);
    var embeddedCount = embeddings.Count(e => e != null);

    await repo.SaveDocumentChunksAsync(docId, chunkTexts, embeddings, groups, ct);

    await langfuse.LogEventAsync(traceId, "RAG:DocumentIndexed", "INFO", $"Документ '{parsed.Title}' ({parsed.Chunks.Count} чанков, векторизовано {embeddedCount}) проиндексирован моделью {embeddingClient.ModelName}.", ct: ct);

    return Results.Ok(new
    {
        success = true,
        document_id = docId,
        title = parsed.Title,
        chunks_count = parsed.Chunks.Count,
        embedded_chunks_count = embeddedCount,
        embedding_model = embeddingClient.ModelName,
        allowed_access_groups = groups
    });
}).DisableAntiforgery();

app.MapPost("/api/rag/reindex-embeddings", async (
    HttpContext httpContext,
    FotoprintConfig config,
    IFotoprintRepository repo,
    IEmbeddingClient embeddingClient,
    ILangfuseClient langfuse,
    CancellationToken ct) =>
{
    var traceId = langfuse.GenerateTraceId();
    if (!HasValidInternalServiceKey(httpContext, config))
    {
        var caller = await ResolveCallerSecurityContextAsync(httpContext, repo, null, null, ct);
        if (caller == null || !caller.HasPermission("rag:search_all"))
        {
            var actorId = caller?.TelegramId ?? 0;
            await langfuse.LogSecurityAlertAsync(traceId, actorId, "/api/rag/reindex-embeddings", "rag:search_all",
                $"Попытка запуска reindex-embeddings без прав rag:search_all (TelegramId={actorId}).", ct);
            return Results.Json(new
            {
                error = "Access Denied",
                message = "Доступ запрещен: переиндексация доступна только с правом rag:search_all (Директор)."
            }, statusCode: 403);
        }
    }

    if (!embeddingClient.IsConfigured)
    {
        return Results.BadRequest(new { success = false, error = "GEMINI_API_KEY не настроен." });
    }

    var updatedCount = await BackfillMissingEmbeddingsAsync(repo, embeddingClient, ct);
    await langfuse.LogEventAsync(traceId, "RAG:EmbeddingsReindexed", "INFO", $"Доиндексировано чанков: {updatedCount} (модель: {embeddingClient.ModelName}).", ct: ct);

    return Results.Ok(new
    {
        success = true,
        updated_chunks_count = updatedCount,
        embedding_model = embeddingClient.ModelName,
        dimensions = embeddingClient.Dimensions
    });
});

app.MapPost("/api/rag/search", async (
    [FromBody] RagSearchRequest req,
    HttpContext httpContext,
    FotoprintConfig config,
    IFotoprintRepository repo,
    IEmbeddingClient embeddingClient,
    ILangfuseClient langfuse,
    CancellationToken ct) =>
{
    var traceId = langfuse.GenerateTraceId();
    List<string> userGroups;
    if (HasValidInternalServiceKey(httpContext, config))
    {
        userGroups = req.UserAccessGroups is { Count: > 0 } ? req.UserAccessGroups : new List<string> { "all" };
    }
    else
    {
        var caller = await ResolveCallerSecurityContextAsync(httpContext, repo, req.MasterId, req.TelegramId, ct);
        if (caller == null)
        {
            await langfuse.LogSecurityAlertAsync(traceId, req.MasterId ?? req.TelegramId ?? 0, "/api/rag/search", "rag:search_department",
                "Внешний вызов /api/rag/search без авторизованного MasterId/TelegramId (защита от подмены UserAccessGroups).", ct);
            return Results.Json(new
            {
                error = "Access Denied",
                message = "Доступ запрещен: передача произвольных UserAccessGroups без авторизации не допускается."
            }, statusCode: 403);
        }
        userGroups = caller.AccessGroups is { Count: > 0 } ? caller.AccessGroups : new List<string> { "all" };
    }

    var startSpan = DateTime.UtcNow;
    var searchMode = "text";
    List<CorporateDocumentChunk> results;

    if (embeddingClient.IsConfigured && !FotoprintRepository.IsGeneralCatalogQuery(req.Query))
    {
        var queryEmbedding = await embeddingClient.EmbedTextAsync(req.Query, "RETRIEVAL_QUERY", ct);
        if (queryEmbedding != null)
        {
            results = await repo.SearchSimilarChunksAsync(queryEmbedding, userGroups, req.TopK, ct);
            if (results.Count > 0)
            {
                searchMode = "vector";
            }
            else
            {
                results = await repo.SearchTextChunksAsync(req.Query, userGroups, req.TopK, ct);
                searchMode = "text_fallback";
            }
        }
        else
        {
            results = await repo.SearchTextChunksAsync(req.Query, userGroups, req.TopK, ct);
            searchMode = "text_fallback";
        }
    }
    else
    {
        results = await repo.SearchTextChunksAsync(req.Query, userGroups, req.TopK, ct);
    }

    var endSpan = DateTime.UtcNow;

    // Техническая метрика: замер времени выполнения поиска по pgvector
    await langfuse.CreateSpanAsync(
        traceId: traceId,
        name: "Span:PgVector_Search",
        startTime: startSpan,
        endTime: endSpan,
        input: req.Query,
        output: new { count = results.Count, mode = searchMode },
        metadata: new { top_k = req.TopK, user_groups = userGroups, search_mode = searchMode },
        ct: ct);

    // Поколение токенов Gen:Embedding_RAG (FinOps учет векторизации и поиска)
    await langfuse.CreateGenerationAsync(
        traceId: traceId,
        name: "Gen:Embedding_RAG",
        model: embeddingClient.ModelName,
        startTime: startSpan,
        endTime: endSpan,
        promptTokens: Math.Max(1, req.Query.Length / 4),
        completionTokens: 0,
        totalTokens: Math.Max(1, req.Query.Length / 4),
        input: req.Query,
        output: $"Returned {results.Count} chunks ({searchMode})",
        ct: ct);

    await langfuse.LogEventAsync(traceId, "RAG:SearchExecuted", "INFO", $"Поиск '{req.Query}' ({searchMode}, {embeddingClient.ModelName}) вернул {results.Count} фрагментов.", ct: ct);

    return Results.Ok(new
    {
        query = req.Query,
        search_mode = searchMode,
        embedding_model = embeddingClient.ModelName,
        results_count = results.Count,
        results = results.Select(r => new
        {
            chunk_id = r.Id,
            document_id = r.DocumentId,
            chunk_index = r.ChunkIndex,
            text = r.ChunkText
        })
    });
});

// ============================================================================
// 3. ГЕНЕРАЦИЯ И ЭКСПОРТ ПРОТОКОЛОВ ВСТРЕЧ (.DOCX / .PDF)
// ============================================================================
app.MapPost("/api/meetings/{id:guid}/generate-protocol", async (
    Guid id,
    IFotoprintRepository repo,
    IMeetingProtocolGenerator generator,
    ILangfuseClient langfuse,
    FotoprintConfig config) =>
{
    var traceId = langfuse.GenerateTraceId();
    var meeting = await repo.GetMeetingByIdAsync(id);
    if (meeting == null)
        return Results.NotFound(new { error = $"Встреча {id} не найдена" });

    var notes = await repo.GetMeetingNotesAsync(id);
    var actionItems = await repo.GetActionItemsByMeetingAsync(id);
    var allEmps = await repo.GetAllEmployeesAsync();
    var empDict = allEmps.ToDictionary(e => e.Id, e => e.FullName);

    var organizerName = meeting.OrganizerId.HasValue && empDict.TryGetValue(meeting.OrganizerId.Value, out var org) ? org : "Руководитель встречи";
    var participants = meeting.ParticipantIds.Select(pid => empDict.TryGetValue(pid, out var p) ? p : $"Сотрудник #{pid}").ToList();

    var summary = notes.Count > 0
        ? string.Join(" ", notes.Select(n => n.NoteText))
        : "Обсуждение производственных задач и сроков отгрузки заказов.";

    var decisions = notes.Where(n => n.Category == "decision").Select(n => n.NoteText).ToList();
    if (decisions.Count == 0)
    {
        decisions.Add("Утвердить план перераспределения смен в печатном цехе.");
        decisions.Add("Заказать оригинальные запасные части Heidelberg.");
    }

    var actionExportData = actionItems.Select(a => new ActionItemExportData(
        Title: a.Title,
        ResponsibleName: a.ResponsibleId.HasValue && empDict.TryGetValue(a.ResponsibleId.Value, out var r) ? r : "Не назначен",
        Deadline: a.Deadline,
        Status: a.Status
    )).ToList();

    var exportData = new ProtocolExportData(
        Title: meeting.Title,
        MeetingDate: meeting.StartTime,
        OrganizerName: organizerName,
        Participants: participants,
        Summary: summary,
        Decisions: decisions,
        ActionItems: actionExportData
    );

    var startProto = DateTime.UtcNow;
    var docxBytes = generator.GenerateDocx(exportData);
    var htmlContent = generator.GenerateHtml(exportData);
    var endProto = DateTime.UtcNow;

    // Сохраняем протокол в БД
    var protocolId = await repo.SaveMeetingProtocolAsync(
        meetingId: id,
        title: $"Протокол: {meeting.Title}",
        summary: summary,
        decisions: decisions,
        rawTranscript: summary
    );

    await langfuse.CreateTraceAsync(
        traceId: traceId,
        name: "meeting-generate-protocol",
        userId: meeting.OrganizerId?.ToString(),
        sessionId: $"meeting-{id}",
        metadata: new
        {
            meeting_id = id,
            protocol_id = protocolId,
            title = meeting.Title,
            decisions_count = decisions.Count,
            action_items_count = actionExportData.Count
        },
        tags: new[] { "channel:api", "module:meetings", "status:ok" },
        input: new { meeting_id = id, title = meeting.Title, notes_count = notes.Count },
        output: new { protocol_id = protocolId, summary, decisions_count = decisions.Count, action_items_count = actionExportData.Count });

    // FinOps учет: Gen:Protocol_And_Executive_Summarizer
    await langfuse.CreateGenerationAsync(
        traceId: traceId,
        name: "Gen:Protocol_And_Executive_Summarizer",
        model: config.Llm.Models.ChatModel,
        startTime: startProto,
        endTime: endProto,
        promptTokens: Math.Max(1, summary.Length / 4),
        completionTokens: Math.Max(1, htmlContent.Length / 4),
        totalTokens: Math.Max(2, (summary.Length + htmlContent.Length) / 4),
        input: summary,
        output: htmlContent,
        metadata: new
        {
            meeting_id = id,
            protocol_id = protocolId,
            decisions_count = decisions.Count,
            action_items_count = actionExportData.Count
        });

    return Results.Ok(new
    {
        success = true,
        protocol_id = protocolId,
        meeting_id = id,
        trace_id = traceId,
        docx_base64 = Convert.ToBase64String(docxBytes),
        html_preview = htmlContent
    });
});

app.MapGet("/api/meetings/{id:guid}/export-docx", async (
    Guid id,
    [FromQuery] long? master_id,
    [FromQuery] long? telegram_id,
    HttpContext httpContext,
    FotoprintConfig config,
    IFotoprintRepository repo,
    IMeetingProtocolGenerator generator,
    ILangfuseClient langfuse,
    CancellationToken ct) =>
{
    var meeting = await repo.GetMeetingByIdAsync(id, ct);
    if (meeting == null) return Results.NotFound();

    if (!HasValidInternalServiceKey(httpContext, config))
    {
        var traceId = langfuse.GenerateTraceId();
        var secContext = await ResolveCallerSecurityContextAsync(httpContext, repo, master_id, telegram_id, ct);
        bool hasAccess = secContext != null &&
                         (secContext.IsExecutive ||
                          meeting.OrganizerId == secContext.EmployeeId ||
                          meeting.ParticipantIds.Contains(secContext.EmployeeId));
        if (!hasAccess)
        {
            var actorId = secContext?.MasterId ?? master_id ?? telegram_id ?? 0;
            await langfuse.LogSecurityAlertAsync(traceId, actorId, "/api/meetings/export-docx", "meeting_participant_or_director",
                $"Попытка несанкционированной выгрузки DOCX встречи {id} пользователем {actorId}.", ct);
            return Results.Json(new
            {
                error = "Access Denied",
                message = "Доступ к материалам созвона имеют только его участники и директор."
            }, statusCode: 403);
        }
    }

    var savedProtocol = await repo.GetMeetingProtocolAsync(id, ct);
    var notes = await repo.GetMeetingNotesAsync(id, ct);
    var actionItems = await repo.GetActionItemsByMeetingAsync(id, ct);
    var allEmps = await repo.GetAllEmployeesAsync(ct);
    var empDict = allEmps.ToDictionary(e => e.Id, e => e.FullName);

    var exportData = new ProtocolExportData(
        Title: savedProtocol?.Title ?? meeting.Title,
        MeetingDate: meeting.StartTime,
        OrganizerName: meeting.OrganizerId.HasValue && empDict.TryGetValue(meeting.OrganizerId.Value, out var org) ? org : "Организатор",
        Participants: meeting.ParticipantIds.Select(pid => empDict.TryGetValue(pid, out var p) ? p : $"Участник #{pid}").ToList(),
        Summary: !string.IsNullOrWhiteSpace(savedProtocol?.Summary) ? savedProtocol.Summary : (meeting.Description ?? "Сводка обсуждения и ключевые договоренности сторон."),
        Decisions: savedProtocol?.Decisions is { Count: > 0 } decs ? decs : notes.Where(n => n.Category == "decision").Select(n => n.NoteText).ToList(),
        ActionItems: actionItems.Select(a => new ActionItemExportData(
            a.Title,
            a.ResponsibleId.HasValue && empDict.TryGetValue(a.ResponsibleId.Value, out var r) ? r : "требует уточнения",
            a.Deadline,
            a.Status,
            a.SourceQuote)).ToList(),
        RawTranscript: savedProtocol?.RawTranscript,
        Proposed: savedProtocol?.Proposed,
        Discussed: savedProtocol?.Discussed,
        Risks: savedProtocol?.Risks,
        RequiresClarification: savedProtocol?.RequiresClarification
    );

    var bytes = generator.GenerateDocx(exportData);
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"Protocol_{id}.docx");
});

// Сквозной пайплайн аудиопротоколирования встречи (MoM):
// Аудиофайл -> Whisper/Gemini STT -> MoM-разбор -> БД -> .DOCX -> Кнопки Битрикс24
app.MapPost("/api/meetings/process-audio", async (
    IFormFile file,
    [FromForm] string? title,
    [FromForm] int? organizer_id,
    [FromForm] long? chat_id,
    [FromQuery] string? language,
    IAudioTranscriber transcriber,
    IMeetingTranscriptAnalyzer analyzer,
    IFotoprintRepository repo,
    IMeetingProtocolGenerator protocolGenerator,
    ILangfuseClient langfuse,
    FotoprintConfig config,
    CancellationToken ct) =>
{
    var traceId = langfuse.GenerateTraceId();
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new
        {
            success = false,
            trace_id = traceId,
            error = "Аудиофайл пуст или отсутствует."
        });
    }

    var maxBytes = Math.Max(1, config.Audio.MaxFileSizeMb) * 1024L * 1024L;
    if (file.Length > maxBytes)
    {
        return Results.BadRequest(new
        {
            success = false,
            trace_id = traceId,
            error = $"Размер файла превышает лимит {config.Audio.MaxFileSizeMb} МБ."
        });
    }

    var safeMeetingFileName = string.IsNullOrWhiteSpace(file.FileName) ? "meeting.m4a" : Path.GetFileName(file.FileName);
    if (string.IsNullOrWhiteSpace(safeMeetingFileName)) safeMeetingFileName = "meeting.m4a";
    await using var audioStream = file.OpenReadStream();
    var transcription = await transcriber.TranscribeAsync(
        audioStream,
        safeMeetingFileName,
        string.IsNullOrWhiteSpace(language) ? "ru" : language,
        ct);

    if (!transcription.Success || string.IsNullOrWhiteSpace(transcription.Text))
    {
        return Results.BadRequest(new
        {
            success = false,
            trace_id = traceId,
            provider = transcription.Provider,
            error = transcription.ErrorMessage ?? "Не удалось распознать речь из аудиофайла."
        });
    }

    var analysis = await analyzer.AnalyzeTranscriptAsync(transcription.Text, title, traceId, ct);
    var effectiveTitle = !string.IsNullOrWhiteSpace(title)
        ? title!
        : (!string.IsNullOrWhiteSpace(analysis.Title)
            ? analysis.Title
            : $"Рабочее совещание от {DateTime.UtcNow:dd.MM.yyyy HH:mm}");

    var employees = new List<Employee>();
    try
    {
        employees = await repo.GetAllEmployeesAsync(ct);
    }
    catch { /* Автономный fallback при недоступной БД */ }

    int? effectiveOrganizerId = organizer_id;
    if (!effectiveOrganizerId.HasValue && chat_id is > 0)
    {
        var callerCtx = await repo.GetEmployeeSecurityContextAsync(chat_id.Value, ct);
        effectiveOrganizerId = callerCtx?.EmployeeId;
    }

    var empDict = employees.ToDictionary(e => e.Id, e => e.FullName);
    var organizerName = effectiveOrganizerId.HasValue && empDict.TryGetValue(effectiveOrganizerId.Value, out var orgName)
        ? orgName
        : "Руководитель совещания";

    var normalizedItems = (analysis.ActionItems ?? new List<ExtractedActionItem>())
        .Select(MeetingTranscriptAnalyzer.NormalizeActionItem)
        .ToList();

    var participants = normalizedItems
        .Select(a => a.Responsible)
        .Where(r => !string.IsNullOrWhiteSpace(r) && !string.Equals(r, "требует уточнения", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (participants.Count == 0)
    {
        participants.Add("Участники рабочей группы");
    }

    var meetingDate = DateTime.UtcNow;
    Guid meetingId;
    try
    {
        meetingId = await repo.CreateMeetingAsync(
            title: effectiveTitle,
            description: analysis.Summary,
            startTime: meetingDate,
            endTime: meetingDate,
            status: "completed",
            organizerId: effectiveOrganizerId,
            participantIds: effectiveOrganizerId.HasValue ? new List<int> { effectiveOrganizerId.Value } : new List<int>(),
            ct: ct);
    }
    catch
    {
        meetingId = Guid.NewGuid();
    }

    var docxSummaryBuilder = new StringBuilder();
    docxSummaryBuilder.AppendLine(analysis.Summary);
    if (analysis.Discussed.Count > 0)
    {
        docxSummaryBuilder.AppendLine();
        docxSummaryBuilder.AppendLine("Обсуждали: " + string.Join("; ", analysis.Discussed));
    }
    if (analysis.Risks.Count > 0)
    {
        docxSummaryBuilder.AppendLine();
        docxSummaryBuilder.AppendLine("Риски и блокеры: " + string.Join("; ", analysis.Risks));
    }
    if (analysis.RequiresClarification.Count > 0)
    {
        docxSummaryBuilder.AppendLine();
        docxSummaryBuilder.AppendLine("Требует уточнения: " + string.Join("; ", analysis.RequiresClarification));
    }

    var exportActionItems = normalizedItems.Select(item => new ActionItemExportData(
        Title: item.Title,
        ResponsibleName: string.IsNullOrWhiteSpace(item.Responsible) ? "требует уточнения" : item.Responsible,
        Deadline: item.ParsedDeadline,
        Status: string.IsNullOrWhiteSpace(item.Responsible) || item.Responsible == "требует уточнения" || !item.ParsedDeadline.HasValue
            ? "требует уточнения"
            : "pending",
        SourceQuote: item.SourceQuote
    )).ToList();

    var exportData = new ProtocolExportData(
        Title: effectiveTitle,
        MeetingDate: meetingDate,
        OrganizerName: organizerName,
        Participants: participants,
        Summary: docxSummaryBuilder.ToString().Trim(),
        Decisions: analysis.Decisions,
        ActionItems: exportActionItems,
        RawTranscript: transcription.Text,
        Proposed: analysis.Proposed,
        Discussed: analysis.Discussed,
        Risks: analysis.Risks,
        RequiresClarification: analysis.RequiresClarification
    );

    var startProto = DateTime.UtcNow;
    var docxBytes = protocolGenerator.GenerateDocx(exportData);
    var htmlPreview = protocolGenerator.GenerateHtml(exportData);
    var endProto = DateTime.UtcNow;

    var protocolsDir = Path.Combine(Path.GetTempPath(), "fotoprint_protocols");
    Directory.CreateDirectory(protocolsDir);
    var docxFilePath = Path.Combine(protocolsDir, $"Protocol_{meetingId}.docx");
    await File.WriteAllBytesAsync(docxFilePath, docxBytes, ct);

    Guid protocolId;
    try
    {
        protocolId = await repo.SaveMeetingProtocolFullAsync(
            meetingId: meetingId,
            title: $"Протокол: {effectiveTitle}",
            summary: analysis.Summary,
            decisions: analysis.Decisions,
            proposed: analysis.Proposed,
            discussed: analysis.Discussed,
            risks: analysis.Risks,
            requiresClarification: analysis.RequiresClarification,
            status: "draft",
            rawTranscript: transcription.Text,
            docxPath: docxFilePath,
            ct: ct);
    }
    catch
    {
        protocolId = Guid.NewGuid();
    }

    var savedActionItems = new List<object>();
    var inlineButtons = new List<object[]>();

    foreach (var item in normalizedItems)
    {
        int? responsibleEmpId = null;
        if (!string.Equals(item.Responsible, "требует уточнения", StringComparison.OrdinalIgnoreCase) && employees.Count > 0)
        {
            var matchedEmp = employees.FirstOrDefault(e =>
                (!string.IsNullOrWhiteSpace(e.LastName) && item.Responsible.Contains(e.LastName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(e.FullName) && item.Responsible.Contains(e.FullName, StringComparison.OrdinalIgnoreCase)));
            if (matchedEmp != null)
            {
                responsibleEmpId = matchedEmp.Id;
            }
        }

        var itemStatus = (responsibleEmpId == null || !item.ParsedDeadline.HasValue) ? "требует уточнения" : "pending";
        Guid actionItemId;
        try
        {
            actionItemId = await repo.CreateActionItemWithQuoteAsync(
                meetingId: meetingId,
                protocolId: protocolId,
                title: item.Title,
                description: item.Description ?? $"Ответственный: {item.Responsible}; Срок: {item.DeadlineText}",
                responsibleId: responsibleEmpId,
                deadline: item.ParsedDeadline,
                sourceQuote: item.SourceQuote,
                status: itemStatus,
                ct: ct);
        }
        catch
        {
            actionItemId = Guid.NewGuid();
        }

        var callbackData = $"create_bitrix_task:{actionItemId}";
        var deadlineDisplay = item.ParsedDeadline.HasValue
            ? item.ParsedDeadline.Value.ToString("yyyy-MM-dd")
            : (string.IsNullOrWhiteSpace(item.DeadlineText) ? "требует уточнения" : item.DeadlineText);

        savedActionItems.Add(new
        {
            id = actionItemId,
            title = item.Title,
            description = item.Description,
            responsible = string.IsNullOrWhiteSpace(item.Responsible) ? "требует уточнения" : item.Responsible,
            responsible_id = responsibleEmpId,
            deadline = deadlineDisplay,
            deadline_text = string.IsNullOrWhiteSpace(item.DeadlineText) ? "требует уточнения" : item.DeadlineText,
            parsed_deadline = item.ParsedDeadline,
            source_quote = item.SourceQuote,
            status = itemStatus,
            callback_data = callbackData
        });

        inlineButtons.Add(new object[]
        {
            new
            {
                text = $"✅ Поставить в Битрикс: \"{item.Title}\"",
                callback_data = callbackData
            }
        });
    }

    var tgTextBuilder = new StringBuilder();
    tgTextBuilder.AppendLine($"📋 Протокол встречи: {effectiveTitle}");
    tgTextBuilder.AppendLine();
    tgTextBuilder.AppendLine($"📝 Резюме: {analysis.Summary}");

    if (analysis.Decisions.Count > 0)
    {
        tgTextBuilder.AppendLine();
        tgTextBuilder.AppendLine("✅ Принятые решения:");
        foreach (var dec in analysis.Decisions)
        {
            tgTextBuilder.AppendLine($"• {dec}");
        }
    }

    if (normalizedItems.Count > 0)
    {
        tgTextBuilder.AppendLine();
        tgTextBuilder.AppendLine("📌 Поручения (нажмите кнопку для постановки задачи в Битрикс24):");
        for (int i = 0; i < normalizedItems.Count; i++)
        {
            var ai = normalizedItems[i];
            tgTextBuilder.AppendLine($"{i + 1}. {ai.Title} — Отв.: {ai.Responsible} | Срок: {ai.DeadlineText}");
        }
    }

    var telegramMessage = new
    {
        chat_id = chat_id,
        text = PlainTextFormatter.Sanitize(tgTextBuilder.ToString()),
        reply_markup = new { inline_keyboard = inlineButtons }
    };

    try
    {
        await langfuse.CreateTraceAsync(
            traceId: traceId,
            name: "meeting-process-audio",
            userId: organizer_id?.ToString(),
            sessionId: $"meeting-{meetingId}",
            metadata: new
            {
                meeting_id = meetingId,
                protocol_id = protocolId,
                audio_provider = transcription.Provider,
                duration_ms = transcription.DurationMs,
                decisions_count = analysis.Decisions.Count,
                action_items_count = normalizedItems.Count
            },
            tags: new[] { "channel:api", "module:meetings", $"stt:{transcription.Provider}", "status:ok" },
            input: new { title = effectiveTitle, file_name = safeMeetingFileName, transcript_preview = transcription.Text.Length > 1000 ? transcription.Text[..1000] : transcription.Text },
            output: new { meeting_id = meetingId, protocol_id = protocolId, summary = analysis.Summary, decisions = analysis.Decisions, action_items_count = normalizedItems.Count },
            ct: ct);

        await langfuse.CreateGenerationAsync(
            traceId: traceId,
            name: "Gen:Audio_MoM_Protocol",
            model: config.Llm.Models.ChatModel,
            startTime: startProto,
            endTime: endProto,
            promptTokens: Math.Max(1, transcription.Text.Length / 4),
            completionTokens: Math.Max(1, htmlPreview.Length / 4),
            totalTokens: Math.Max(2, (transcription.Text.Length + htmlPreview.Length) / 4),
            input: transcription.Text,
            output: analysis.Summary,
            metadata: new
            {
                meeting_id = meetingId,
                protocol_id = protocolId,
                audio_provider = transcription.Provider,
                duration_ms = transcription.DurationMs,
                decisions_count = analysis.Decisions.Count,
                action_items_count = normalizedItems.Count
            },
            ct: ct);
    }
    catch { }

    return Results.Ok(new
    {
        success = true,
        meeting_id = meetingId,
        protocol_id = protocolId,
        trace_id = traceId,
        transcription = new
        {
            text = transcription.Text,
            provider = transcription.Provider,
            duration_ms = transcription.DurationMs,
            segments_count = transcription.Segments?.Count ?? 0,
            segments = transcription.Segments ?? new List<AudioSegment>()
        },
        protocol = new
        {
            title = effectiveTitle,
            status = "draft",
            summary = analysis.Summary,
            decisions = analysis.Decisions,
            proposed = analysis.Proposed ?? new List<string>(),
            discussed = analysis.Discussed,
            risks = analysis.Risks,
            requires_clarification = analysis.RequiresClarification,
            docx_file_path = docxFilePath,
            docx_base64 = Convert.ToBase64String(docxBytes),
            docx_size_bytes = docxBytes.Length,
            html_preview = htmlPreview
        },
        action_items = savedActionItems,
        telegram_message = telegramMessage
    });
}).DisableAntiforgery();

// Сохранение созвона (Google Meet / Calendar) после Whisper + LLM:
// БД (meetings, meeting_protocols, action_items) + .docx/.html + индексация в pgvector (gemini-embedding-2) со строгим RBAC (участники + Директор)
app.MapPost("/api/meetings/save-and-index", async (
    [FromBody] SaveAndIndexMeetingRequest req,
    IFotoprintRepository repo,
    IDocumentParser docParser,
    IMeetingProtocolGenerator protocolGenerator,
    IEmbeddingClient embeddingClient,
    ILangfuseClient langfuse,
    FotoprintConfig config,
    CancellationToken ct) =>
{
    var traceId = langfuse.GenerateTraceId();
    if (string.IsNullOrWhiteSpace(req.Summary) && string.IsNullOrWhiteSpace(req.RawTranscript))
    {
        return Results.BadRequest(new { success = false, trace_id = traceId, error = "Не переданы summary или raw_transcript созвона." });
    }

    var employees = await repo.GetAllEmployeesAsync(ct);
    var participantEmployees = new List<Employee>();

    if (req.OrganizerMasterId.HasValue)
    {
        var orgEmp = employees.FirstOrDefault(e => e.MasterId == req.OrganizerMasterId.Value);
        if (orgEmp != null) participantEmployees.Add(orgEmp);
    }
    if (!string.IsNullOrWhiteSpace(req.OrganizerEmail))
    {
        var orgEmp = employees.FirstOrDefault(e => string.Equals(e.Email, req.OrganizerEmail.Trim(), StringComparison.OrdinalIgnoreCase));
        if (orgEmp != null && !participantEmployees.Any(p => p.Id == orgEmp.Id)) participantEmployees.Add(orgEmp);
    }

    foreach (var mId in req.ParticipantMasterIds ?? new List<long>())
    {
        var emp = employees.FirstOrDefault(e => e.MasterId == mId);
        if (emp != null && !participantEmployees.Any(p => p.Id == emp.Id)) participantEmployees.Add(emp);
    }
    foreach (var email in req.ParticipantEmails ?? new List<string>())
    {
        var emp = employees.FirstOrDefault(e => string.Equals(e.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (emp != null && !participantEmployees.Any(p => p.Id == emp.Id)) participantEmployees.Add(emp);
    }

    var organizerId = participantEmployees.FirstOrDefault()?.Id;
    var participantIds = participantEmployees.Select(e => e.Id).Distinct().ToList();

    // Строгий RBAC для созвона: только Директор ("c-level") и конкретные участники ("master:<id>"), БЕЗ "all"
    var allowedAccessGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "c-level" };
    if (req.OrganizerMasterId.HasValue && req.OrganizerMasterId.Value > 0)
        allowedAccessGroups.Add($"master:{req.OrganizerMasterId.Value}");
    foreach (var mId in req.ParticipantMasterIds ?? new List<long>())
    {
        if (mId > 0) allowedAccessGroups.Add($"master:{mId}");
    }
    foreach (var emp in participantEmployees)
    {
        allowedAccessGroups.Add($"master:{emp.MasterId}");
    }

    var effectiveTitle = string.IsNullOrWhiteSpace(req.Title)
        ? $"Созвон от {DateTime.UtcNow.AddHours(3):dd.MM.yyyy HH:mm}"
        : req.Title.Trim();
    var startTime = req.StartTime ?? DateTime.UtcNow;
    var endTime = req.EndTime ?? DateTime.UtcNow;

    var meetingId = await repo.CreateMeetingAsync(
        title: effectiveTitle,
        description: req.Summary,
        startTime: startTime,
        endTime: endTime,
        status: "completed",
        organizerId: organizerId,
        participantIds: participantIds,
        ct: ct);

    var decisions = req.Decisions ?? new List<string>();
    var proposed = req.Proposed ?? new List<string>();
    var discussed = req.Discussed ?? new List<string>();
    var risks = req.Risks ?? new List<string>();
    var requiresClarification = req.RequiresClarification ?? new List<string>();
    var actionInputs = req.ActionItems ?? new List<MeetingActionItemInput>();
    var empById = employees.ToDictionary(e => e.Id, e => e.FullName);

    var exportData = new ProtocolExportData(
        Title: effectiveTitle,
        MeetingDate: startTime,
        OrganizerName: organizerId.HasValue && empById.TryGetValue(organizerId.Value, out var orgName) ? orgName : "Организатор созвона",
        Participants: participantEmployees.Count > 0
            ? participantEmployees.Select(e => e.FullName).ToList()
            : new List<string> { "Участники созвона" },
        Summary: req.Summary ?? string.Empty,
        Decisions: decisions,
        ActionItems: actionInputs.Select(a => new ActionItemExportData(
            Title: a.Title,
            ResponsibleName: string.IsNullOrWhiteSpace(a.Responsible) ? "требует уточнения" : a.Responsible,
            Deadline: a.Deadline,
            Status: string.IsNullOrWhiteSpace(a.Responsible) || !a.Deadline.HasValue ? "требует уточнения" : "pending",
            SourceQuote: a.SourceQuote
        )).ToList(),
        RawTranscript: req.RawTranscript,
        Proposed: proposed,
        Discussed: discussed,
        Risks: risks,
        RequiresClarification: requiresClarification
    );

    var protocolsDir = Path.Combine(Path.GetTempPath(), "fotoprint_protocols");
    Directory.CreateDirectory(protocolsDir);
    var docxPath = Path.Combine(protocolsDir, $"Meeting_{meetingId}.docx");
    var pdfPath = Path.Combine(protocolsDir, $"Meeting_{meetingId}.html");

    await File.WriteAllBytesAsync(docxPath, protocolGenerator.GenerateDocx(exportData), ct);
    await File.WriteAllBytesAsync(pdfPath, protocolGenerator.GeneratePdf(exportData), ct);

    var protocolId = await repo.SaveMeetingProtocolFullAsync(
        meetingId: meetingId,
        title: $"Протокол: {effectiveTitle}",
        summary: req.Summary ?? string.Empty,
        decisions: decisions,
        proposed: proposed,
        discussed: discussed,
        risks: risks,
        requiresClarification: requiresClarification,
        status: string.IsNullOrWhiteSpace(req.Status) ? "draft" : req.Status!,
        rawTranscript: req.RawTranscript,
        docxPath: docxPath,
        pdfPath: pdfPath,
        ct: ct);

    foreach (var item in actionInputs)
    {
        if (string.IsNullOrWhiteSpace(item.Title)) continue;

        int? respEmpId = null;
        if (!string.IsNullOrWhiteSpace(item.Responsible))
        {
            var matched = employees.FirstOrDefault(e =>
                (!string.IsNullOrWhiteSpace(e.LastName) && item.Responsible.Contains(e.LastName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(e.FullName) && item.Responsible.Contains(e.FullName, StringComparison.OrdinalIgnoreCase)));
            respEmpId = matched?.Id;
        }

        var itemStatus = (respEmpId == null || !item.Deadline.HasValue) ? "требует уточнения" : "pending";
        await repo.CreateActionItemWithQuoteAsync(
            meetingId: meetingId,
            protocolId: protocolId,
            title: item.Title,
            description: item.Description,
            responsibleId: respEmpId,
            deadline: item.Deadline,
            sourceQuote: item.SourceQuote,
            status: itemStatus,
            ct: ct);
    }

    var moscowStart = startTime.Kind == DateTimeKind.Utc ? startTime.AddHours(3) : startTime;
    var ragContent = new StringBuilder();
    ragContent.AppendLine($"Созвон: {effectiveTitle} ({moscowStart:dd.MM.yyyy HH:mm} MSK)");
    ragContent.AppendLine($"Дата созвона: {moscowStart:yyyy-MM-dd}");
    if (participantEmployees.Count > 0)
    {
        ragContent.AppendLine("Участники: " + string.Join(", ", participantEmployees.Select(e => e.FullName)));
    }
    ragContent.AppendLine($"Саммари: {req.Summary}");
    if (decisions.Count > 0)
    {
        ragContent.AppendLine("Принятые решения: " + string.Join("; ", decisions));
    }
    if (!string.IsNullOrWhiteSpace(req.RawTranscript))
    {
        ragContent.AppendLine("Стенограмма созвона:");
        ragContent.AppendLine(req.RawTranscript);
    }

    await using var textStream = new MemoryStream(Encoding.UTF8.GetBytes(ragContent.ToString()));
    var parsedRag = await docParser.ParseStreamAsync(
        textStream,
        $"meeting_{meetingId}.txt",
        config.Rag.Chunking.ChunkSize > 0 ? config.Rag.Chunking.ChunkSize : 800,
        config.Rag.Chunking.ChunkOverlap >= 0 ? config.Rag.Chunking.ChunkOverlap : 150,
        ct);

    var ragGroups = allowedAccessGroups.ToList();
    var docMetadata = new
    {
        meeting_id = meetingId,
        protocol_id = protocolId,
        calendar_event_id = req.CalendarEventId,
        title = effectiveTitle,
        meeting_date = startTime.ToString("yyyy-MM-dd HH:mm"),
        summary = req.Summary ?? string.Empty
    };

    var chunkTexts = parsedRag.Chunks.Select(c => c.Text).ToList();
    var embedStart = DateTime.UtcNow;
    var embeddings = await embeddingClient.EmbedBatchAsync(chunkTexts, "RETRIEVAL_DOCUMENT", ct);
    var embedEnd = DateTime.UtcNow;
    var embeddedCount = embeddings.Count(e => e != null);
    await repo.SaveMeetingChunksAsync(meetingId, protocolId, chunkTexts, embeddings, ragGroups, docMetadata, ct);

    var traceUserId = req.OrganizerMasterId?.ToString()
        ?? req.ParticipantMasterIds?.FirstOrDefault().ToString();
    var totalChunkChars = chunkTexts.Sum(t => t.Length);

    await langfuse.CreateTraceAsync(
        traceId: traceId,
        name: "google-meet-save-and-index",
        userId: traceUserId,
        sessionId: $"meeting-{meetingId}",
        metadata: new
        {
            meeting_id = meetingId,
            protocol_id = protocolId,
            storage_table = "meeting_chunks",
            calendar_event_id = req.CalendarEventId,
            chunks_count = chunkTexts.Count,
            embedded_chunks_count = embeddedCount,
            embedding_model = embeddingClient.ModelName,
            allowed_access_groups = ragGroups
        },
        tags: new[] { "channel:api", "module:meetings", "source:google_meet", "status:ok" },
        input: new
        {
            title = effectiveTitle,
            calendar_event_id = req.CalendarEventId,
            participant_master_ids = req.ParticipantMasterIds,
            summary_length = req.Summary?.Length ?? 0,
            transcript_length = req.RawTranscript?.Length ?? 0
        },
        output: new
        {
            meeting_id = meetingId,
            protocol_id = protocolId,
            storage_table = "meeting_chunks",
            chunks_count = chunkTexts.Count,
            embedded_chunks_count = embeddedCount,
            allowed_access_groups = ragGroups
        },
        ct: ct);

    await langfuse.CreateGenerationAsync(
        traceId: traceId,
        name: "Gen:Meeting_Embedding_Index",
        model: embeddingClient.ModelName,
        startTime: embedStart,
        endTime: embedEnd,
        promptTokens: Math.Max(1, totalChunkChars / 4),
        completionTokens: 0,
        totalTokens: Math.Max(1, totalChunkChars / 4),
        input: effectiveTitle,
        output: $"Indexed {embeddedCount}/{chunkTexts.Count} meeting chunks ({string.Join(", ", ragGroups)})",
        metadata: new { meeting_id = meetingId, protocol_id = protocolId, storage_table = "meeting_chunks", dimensions = embeddingClient.Dimensions },
        ct: ct);

    await langfuse.LogEventAsync(traceId, "Meeting:SavedAndIndexed", "INFO",
        $"Созвон '{effectiveTitle}' (ID: {meetingId}) сохранён в meeting_chunks и векторизован ({embeddedCount}/{chunkTexts.Count} чанков, доступ: {string.Join(", ", ragGroups)}).", ct: ct);

    return Results.Ok(new
    {
        success = true,
        trace_id = traceId,
        meeting_id = meetingId,
        protocol_id = protocolId,
        storage_table = "meeting_chunks",
        title = effectiveTitle,
        chunks_count = chunkTexts.Count,
        embedded_chunks_count = embeddedCount,
        embedding_model = embeddingClient.ModelName,
        allowed_access_groups = ragGroups,
        export_docx_url = $"/api/meetings/{meetingId}/export-full?format=docx",
        export_pdf_url = $"/api/meetings/{meetingId}/export-full?format=pdf"
    });
});

// Редактирование черновика протокола перед финальным подтверждением (AI-07)
app.MapPost("/api/meetings/{id:guid}/protocol/update", async (
    Guid id,
    [FromBody] ProtocolDraftUpdateRequest req,
    HttpContext httpContext,
    FotoprintConfig config,
    IFotoprintRepository repo,
    IMeetingProtocolGenerator generator,
    CancellationToken ct) =>
{
    var meeting = await repo.GetMeetingByIdAsync(id, ct);
    if (meeting == null) return Results.NotFound(new { error = "Meeting not found" });

    var secContext = await ResolveCallerSecurityContextAsync(httpContext, repo, req.MasterId, req.TelegramId, ct);
    if (!HasValidInternalServiceKey(httpContext, config))
    {
        bool allowed = secContext != null && (secContext.IsExecutive || meeting.OrganizerId == secContext.EmployeeId || meeting.ParticipantIds.Contains(secContext.EmployeeId));
        if (!allowed)
        {
            return Results.Json(new { error = "Access Denied", message = "Редактировать черновик могут только участники встречи или Директор." }, statusCode: 403);
        }
    }

    var current = await repo.GetMeetingProtocolAsync(id, ct);
    var title = string.IsNullOrWhiteSpace(req.Title) ? (current?.Title ?? meeting.Title) : req.Title!.Trim();
    var summary = req.Summary ?? current?.Summary ?? string.Empty;
    var decisions = req.Decisions ?? current?.Decisions ?? new List<string>();
    var proposed = req.Proposed ?? current?.Proposed ?? new List<string>();
    var discussed = req.Discussed ?? current?.Discussed ?? new List<string>();
    var risks = req.Risks ?? current?.Risks ?? new List<string>();
    var clarifications = req.RequiresClarification ?? current?.RequiresClarification ?? new List<string>();

    await repo.UpdateMeetingProtocolDraftAsync(id, title, summary, decisions, proposed, discussed, risks, clarifications, ct);

    var actionItems = await repo.GetActionItemsByMeetingAsync(id, ct);
    var empDict = (await repo.GetAllEmployeesAsync(ct)).ToDictionary(e => e.Id, e => e.FullName);
    var exportData = new ProtocolExportData(
        Title: title,
        MeetingDate: meeting.StartTime,
        OrganizerName: meeting.OrganizerId.HasValue && empDict.TryGetValue(meeting.OrganizerId.Value, out var org) ? org : "Организатор",
        Participants: meeting.ParticipantIds.Select(pid => empDict.TryGetValue(pid, out var p) ? p : $"Участник #{pid}").ToList(),
        Summary: summary,
        Decisions: decisions,
        ActionItems: actionItems.Select(a => new ActionItemExportData(
            a.Title,
            a.ResponsibleId.HasValue && empDict.TryGetValue(a.ResponsibleId.Value, out var r) ? r : "требует уточнения",
            a.Deadline,
            a.Status,
            a.SourceQuote)).ToList(),
        RawTranscript: current?.RawTranscript,
        Proposed: proposed,
        Discussed: discussed,
        Risks: risks,
        RequiresClarification: clarifications);

    var docxBytes = generator.GenerateDocx(exportData);
    var htmlPreview = generator.GenerateHtml(exportData);

    await AuditTrail.Instance.RecordAsync(new AuditEvent(
        secContext?.TelegramId ?? req.TelegramId ?? 0,
        "api",
        "protocol.draft_updated",
        "meeting_protocol",
        id.ToString(),
        AuditResult.Success,
        httpContext.TraceIdentifier), ct);

    return Results.Ok(new
    {
        success = true,
        meeting_id = id,
        status = "draft",
        title,
        summary,
        decisions,
        proposed,
        discussed,
        risks,
        requires_clarification = clarifications,
        docx_base64 = Convert.ToBase64String(docxBytes),
        html_preview = htmlPreview
    });
});

// Подтверждение и финализация протокола (draft -> final, AI-07)
app.MapPost("/api/meetings/{id:guid}/protocol/confirm", async (
    Guid id,
    [FromQuery] long? master_id,
    [FromQuery] long? telegram_id,
    HttpContext httpContext,
    FotoprintConfig config,
    IFotoprintRepository repo,
    CancellationToken ct) =>
{
    var meeting = await repo.GetMeetingByIdAsync(id, ct);
    if (meeting == null) return Results.NotFound(new { error = "Meeting not found" });

    var secContext = await ResolveCallerSecurityContextAsync(httpContext, repo, master_id, telegram_id, ct);
    if (!HasValidInternalServiceKey(httpContext, config))
    {
        bool allowed = secContext != null && (secContext.IsExecutive || meeting.OrganizerId == secContext.EmployeeId || meeting.ParticipantIds.Contains(secContext.EmployeeId));
        if (!allowed)
        {
            return Results.Json(new { error = "Access Denied", message = "Финализировать протокол могут только участники встречи или Директор." }, statusCode: 403);
        }
    }

    await repo.FinalizeMeetingProtocolAsync(id, ct);
    await AuditTrail.Instance.RecordAsync(new AuditEvent(
        secContext?.TelegramId ?? telegram_id ?? 0,
        "api",
        "protocol.confirmed",
        "meeting_protocol",
        id.ToString(),
        AuditResult.Success,
        httpContext.TraceIdentifier), ct);

    return Results.Ok(new { success = true, meeting_id = id, status = "final" });
});

// Задачи без ответственного или без срока («требует уточнения», Сценарий 4 ТЗ)
app.MapGet("/api/tasks/orphans", async (
    [FromQuery] long? master_id,
    [FromQuery] long? telegram_id,
    HttpContext httpContext,
    FotoprintConfig config,
    IFotoprintRepository repo,
    CancellationToken ct) =>
{
    if (!HasValidInternalServiceKey(httpContext, config))
    {
        var secContext = await ResolveCallerSecurityContextAsync(httpContext, repo, master_id, telegram_id, ct);
        if (secContext == null)
        {
            return Results.Json(new { error = "Access Denied" }, statusCode: 403);
        }
    }

    var orphans = await repo.GetOrphanActionItemsAsync(ct);
    return Results.Ok(new
    {
        count = orphans.Count,
        items = orphans.Select(a => new
        {
            id = a.Id,
            meeting_id = a.MeetingId,
            title = a.Title,
            responsible = a.ResponsibleId?.ToString() ?? "требует уточнения",
            deadline = a.Deadline?.ToString("yyyy-MM-dd") ?? "требует уточнения",
            source_quote = a.SourceQuote,
            status = a.Status
        })
    });
});

// Удаление тестовых/пользовательских данных (п. 7.4 ТЗ — управление хранением)
app.MapDelete("/api/data/purge-test", async (
    [FromQuery] long? telegram_id,
    [FromQuery] long? master_id,
    HttpContext httpContext,
    FotoprintConfig config,
    IFotoprintRepository repo,
    CancellationToken ct) =>
{
    var secContext = await ResolveCallerSecurityContextAsync(httpContext, repo, master_id, telegram_id, ct);
    if (!HasValidInternalServiceKey(httpContext, config) && !(secContext?.IsExecutive ?? false))
    {
        return Results.Json(new { error = "Access Denied", message = "Операция удаления данных доступна только Директору или с внутренним сервисным ключом." }, statusCode: 403);
    }

    var deletedRows = await repo.PurgeTestDataAsync(telegram_id, ct);
    await AuditTrail.Instance.RecordAsync(new AuditEvent(
        secContext?.TelegramId ?? telegram_id ?? 0,
        "api",
        "data.purged",
        "test_and_session_data",
        telegram_id?.ToString() ?? "all",
        AuditResult.Success,
        httpContext.TraceIdentifier,
        new { deleted_rows = deletedRows }), ct);

    return Results.Ok(new { success = true, deleted_rows = deletedRows, scope = telegram_id.HasValue ? $"telegram:{telegram_id.Value}" : "temporary_chat_and_test_notes" });
});

// Поиск подходящего созвона в RAG по MasterId пользователя и вопросу (строго участники + Директор)
app.MapPost("/api/meetings/rag-search", async (
    [FromBody] MeetingRagSearchRequest req,
    IFotoprintRepository repo,
    IEmbeddingClient embeddingClient,
    ILangfuseClient langfuse,
    CancellationToken ct) =>
{
    var traceId = langfuse.GenerateTraceId();
    var secContext = await repo.GetEmployeeSecurityContextByMasterIdAsync(req.MasterId, ct);
    if (secContext == null)
    {
        await langfuse.CreateTraceAsync(
            traceId: traceId,
            name: "google-meet-rag-search",
            userId: req.MasterId.ToString(),
            sessionId: null,
            metadata: new { master_id = req.MasterId, query = req.Query },
            tags: new[] { "channel:api", "module:meetings", "source:google_meet", "status:forbidden" },
            input: req.Query,
            output: $"Доступ запрещён: сотрудник MasterId {req.MasterId} не найден",
            ct: ct);
        await langfuse.LogSecurityAlertAsync(traceId, req.MasterId, "MeetingRagSearch", "master_id_exists",
            $"Неизвестный MasterId {req.MasterId} попытался выполнить поиск по созвонам.", ct);
        return Results.Json(new { found = false, trace_id = traceId, access_denied = true, message = $"Сотрудник с MasterId {req.MasterId} не найден." }, statusCode: 403);
    }

    var meetingAccessGroups = new List<string> { $"master:{secContext.MasterId}" };
    if (secContext.IsExecutive)
    {
        meetingAccessGroups.Add("c-level");
    }

    var startSpan = DateTime.UtcNow;
    var searchMode = "text_fallback";
    List<CorporateDocumentChunk> matchedChunks = new();
    var parsedQuery = MeetingSearchQueryParser.Parse(req.Query);

    if (parsedQuery.TargetDate.HasValue)
    {
        if (parsedQuery.HasTopicTokens && embeddingClient.IsConfigured && !FotoprintRepository.IsGeneralCatalogQuery(req.Query))
        {
            var queryVector = await embeddingClient.EmbedTextAsync(req.Query, "RETRIEVAL_QUERY", ct);
            if (queryVector != null)
            {
                matchedChunks = await repo.SearchMeetingSimilarChunksAsync(queryVector, meetingAccessGroups, parsedQuery.TargetDate, req.TopK, ct);
                if (matchedChunks.Count > 0)
                {
                    searchMode = "vector_date";
                }
            }
        }

        if (matchedChunks.Count == 0)
        {
            matchedChunks = await repo.SearchMeetingsByFilterAsync(meetingAccessGroups, parsedQuery.TargetDate, req.TopK, ct);
            if (matchedChunks.Count > 0)
            {
                searchMode = "date_filter";
            }
        }
    }
    else if (parsedQuery.PreferLatest && !parsedQuery.HasTopicTokens)
    {
        matchedChunks = await repo.SearchMeetingsByFilterAsync(meetingAccessGroups, null, req.TopK, ct);
        if (matchedChunks.Count > 0)
        {
            searchMode = "latest_meeting";
        }
    }

    if (matchedChunks.Count == 0 && embeddingClient.IsConfigured && !FotoprintRepository.IsGeneralCatalogQuery(req.Query))
    {
        var queryVector = await embeddingClient.EmbedTextAsync(req.Query, "RETRIEVAL_QUERY", ct);
        if (queryVector != null)
        {
            matchedChunks = await repo.SearchMeetingSimilarChunksAsync(queryVector, meetingAccessGroups, req.TopK, ct);
            if (matchedChunks.Count > 0)
            {
                searchMode = "vector";
            }
        }
    }

    if (matchedChunks.Count == 0)
    {
        matchedChunks = await repo.SearchMeetingTextChunksAsync(req.Query, meetingAccessGroups, parsedQuery.TargetDate, req.TopK, ct);
        if (matchedChunks.Count > 0)
        {
            searchMode = "text_fallback";
        }
    }
    var endSpan = DateTime.UtcNow;

    if (matchedChunks.Count == 0)
    {
        await langfuse.CreateTraceAsync(
            traceId: traceId,
            name: "google-meet-rag-search",
            userId: secContext.MasterId.ToString(),
            sessionId: null,
            metadata: new { master_id = secContext.MasterId, is_director = secContext.IsExecutive, access_groups = meetingAccessGroups, matches_count = 0 },
            tags: new[] { "channel:api", "module:meetings", "source:google_meet", "status:not_found" },
            input: req.Query,
            output: "Доступных созвонов не найдено (RBAC-фильтр).",
            ct: ct);
        await langfuse.CreateGenerationAsync(
            traceId: traceId,
            name: "Gen:Meeting_Embedding_Search",
            model: embeddingClient.ModelName,
            startTime: startSpan,
            endTime: endSpan,
            promptTokens: Math.Max(1, req.Query.Length / 4),
            completionTokens: 0,
            totalTokens: Math.Max(1, req.Query.Length / 4),
            input: req.Query,
            output: "0 matches (RBAC filtered)",
            ct: ct);
        await langfuse.CreateSpanAsync(traceId, "Span:Meeting_RAG_Search", startSpan, endSpan,
            input: new { master_id = secContext.MasterId, query = req.Query, access_groups = meetingAccessGroups },
            output: new { matches_count = 0 }, ct: ct);
        await langfuse.LogScoreAsync(traceId, "meeting_rag_found", 0.0, "Совпадений не найдено либо нет доступа", ct: ct);

        return Results.Ok(new
        {
            found = false,
            trace_id = traceId,
            master_id = secContext.MasterId,
            message = "По вашему запросу не найдено доступных созвонов (доступ имеют только участники встречи и директор)."
        });
    }

    Guid? bestMeetingId = matchedChunks[0].DocumentId != Guid.Empty ? matchedChunks[0].DocumentId : null;
    if (!bestMeetingId.HasValue)
    {
        foreach (var ch in matchedChunks)
        {
            if (!string.IsNullOrWhiteSpace(ch.MetadataJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(ch.MetadataJson);
                    if (doc.RootElement.TryGetProperty("meeting_id", out var mProp) && Guid.TryParse(mProp.GetString(), out var parsedMid))
                    {
                        bestMeetingId = parsedMid;
                        break;
                    }
                }
                catch { }
            }
        }
    }

    Meeting? meeting = bestMeetingId.HasValue ? await repo.GetMeetingByIdAsync(bestMeetingId.Value, ct) : null;
    MeetingProtocol? protocol = bestMeetingId.HasValue ? await repo.GetMeetingProtocolAsync(bestMeetingId.Value, ct) : null;
    var summaryText = protocol?.Summary ?? meeting?.Description ?? matchedChunks[0].ChunkText;

    await langfuse.CreateTraceAsync(
        traceId: traceId,
        name: "google-meet-rag-search",
        userId: secContext.MasterId.ToString(),
        sessionId: bestMeetingId.HasValue ? $"meeting-{bestMeetingId.Value}" : null,
        metadata: new
        {
            master_id = secContext.MasterId,
            is_director = secContext.IsExecutive,
            meeting_id = bestMeetingId,
            meeting_title = meeting?.Title ?? protocol?.Title,
            matches_count = matchedChunks.Count,
            access_groups = meetingAccessGroups
        },
        tags: new[] { "channel:api", "module:meetings", "source:google_meet", "status:ok", secContext.IsExecutive ? "role:director" : "role:participant" },
        input: req.Query,
        output: new { meeting_id = bestMeetingId, title = meeting?.Title ?? protocol?.Title, summary = summaryText },
        ct: ct);

    await langfuse.CreateGenerationAsync(
        traceId: traceId,
        name: "Gen:Meeting_Embedding_Search",
        model: embeddingClient.ModelName,
        startTime: startSpan,
        endTime: endSpan,
        promptTokens: Math.Max(1, req.Query.Length / 4),
        completionTokens: 0,
        totalTokens: Math.Max(1, req.Query.Length / 4),
        input: req.Query,
        output: $"Matched meeting {bestMeetingId} ({matchedChunks.Count} chunks)",
        ct: ct);

    await langfuse.CreateSpanAsync(traceId, "Span:Meeting_RAG_Search", startSpan, endSpan,
        input: new { master_id = secContext.MasterId, query = req.Query, access_groups = meetingAccessGroups },
        output: new { matches_count = matchedChunks.Count, meeting_id = bestMeetingId }, ct: ct);

    await langfuse.LogScoreAsync(traceId, "meeting_rag_found", 1.0, $"Найден созвон {bestMeetingId}", ct: ct);

    return Results.Ok(new
    {
        found = true,
        search_mode = searchMode,
        trace_id = traceId,
        master_id = secContext.MasterId,
        is_director = secContext.IsExecutive,
        meeting_id = bestMeetingId,
        title = meeting?.Title ?? protocol?.Title ?? "Созвон",
        meeting_date = meeting?.StartTime.ToString("yyyy-MM-dd HH:mm"),
        summary = summaryText,
        decisions = protocol?.Decisions ?? new List<string>(),
        has_full_transcript = !string.IsNullOrWhiteSpace(protocol?.RawTranscript),
        export_docx_url = bestMeetingId.HasValue ? $"/api/meetings/{bestMeetingId.Value}/export-full?master_id={secContext.MasterId}&format=docx" : null,
        export_pdf_url = bestMeetingId.HasValue ? $"/api/meetings/{bestMeetingId.Value}/export-full?master_id={secContext.MasterId}&format=pdf" : null,
        matched_chunks = matchedChunks.Select(c => new { chunk_id = c.Id, chunk_index = c.ChunkIndex, text = c.ChunkText })
    });
});

// Экспорт полного созвона (саммари + решения + поручения + вся стенограмма) в .docx или .pdf (.html) с проверкой RBAC
app.MapGet("/api/meetings/{id:guid}/export-full", async (
    HttpContext httpContext,
    Guid id,
    [FromQuery] long master_id,
    [FromQuery] string? format,
    IFotoprintRepository repo,
    IMeetingProtocolGenerator generator,
    ILangfuseClient langfuse,
    CancellationToken ct) =>
{
    var traceId = langfuse.GenerateTraceId();
    var fmt = (format ?? "docx").Trim().ToLowerInvariant();
    var exportStart = DateTime.UtcNow;

    var secContext = await repo.GetEmployeeSecurityContextByMasterIdAsync(master_id, ct);
    if (secContext == null)
    {
        await langfuse.CreateTraceAsync(
            traceId: traceId,
            name: "google-meet-export-full",
            userId: master_id.ToString(),
            sessionId: $"meeting-{id}",
            metadata: new { meeting_id = id, master_id, format = fmt },
            tags: new[] { "channel:api", "module:meetings", "source:google_meet", $"format:{fmt}", "status:forbidden" },
            input: new { meeting_id = id, master_id, format = fmt },
            output: "403 Access Denied: сотрудник не найден.",
            ct: ct);
        await langfuse.LogSecurityAlertAsync(traceId, master_id, "ExportFullMeeting", "employee_exists",
            $"Неизвестный MasterId {master_id} запросил полную выгрузку созвона {id}.", ct);
        return Results.Json(new { error = "Access Denied", message = "Сотрудник не найден." }, statusCode: 403);
    }

    var meeting = await repo.GetMeetingByIdAsync(id, ct);
    if (meeting == null)
    {
        return Results.NotFound(new { error = $"Встреча {id} не найдена." });
    }

    bool hasAccess = secContext.IsExecutive ||
                     meeting.OrganizerId == secContext.EmployeeId ||
                     meeting.ParticipantIds.Contains(secContext.EmployeeId);
    if (!hasAccess)
    {
        await langfuse.CreateTraceAsync(
            traceId: traceId,
            name: "google-meet-export-full",
            userId: master_id.ToString(),
            sessionId: $"meeting-{id}",
            metadata: new { meeting_id = id, master_id, format = fmt, title = meeting.Title },
            tags: new[] { "channel:api", "module:meetings", "source:google_meet", $"format:{fmt}", "status:forbidden" },
            input: new { meeting_id = id, master_id, format = fmt },
            output: "403 Access Denied: пользователь не является участником или директором.",
            ct: ct);
        await langfuse.LogSecurityAlertAsync(traceId, master_id, "ExportFullMeeting", "meeting_participant_or_director",
            $"Сотрудник MasterId {master_id} ({secContext.FullName}) без прав попытался скачать полную стенограмму созвона {id}.", ct);
        return Results.Json(new
        {
            error = "Access Denied",
            message = "Доступ к материалам созвона имеют только его участники и директор."
        }, statusCode: 403);
    }

    var protocol = await repo.GetMeetingProtocolAsync(id, ct);
    var actionItems = await repo.GetActionItemsByMeetingAsync(id, ct);
    var allEmps = await repo.GetAllEmployeesAsync(ct);
    var empDict = allEmps.ToDictionary(e => e.Id, e => e.FullName);

    var exportData = new ProtocolExportData(
        Title: meeting.Title,
        MeetingDate: meeting.StartTime,
        OrganizerName: meeting.OrganizerId.HasValue && empDict.TryGetValue(meeting.OrganizerId.Value, out var org) ? org : "Организатор",
        Participants: meeting.ParticipantIds.Select(pid => empDict.TryGetValue(pid, out var p) ? p : $"Участник #{pid}").ToList(),
        Summary: protocol?.Summary ?? meeting.Description ?? "Итоги созвона.",
        Decisions: protocol?.Decisions ?? new List<string>(),
        ActionItems: actionItems.Select(a => new ActionItemExportData(
            a.Title,
            a.ResponsibleId.HasValue && empDict.TryGetValue(a.ResponsibleId.Value, out var r) ? r : "требует уточнения",
            a.Deadline,
            a.Status,
            a.SourceQuote)).ToList(),
        RawTranscript: protocol?.RawTranscript,
        Proposed: protocol?.Proposed,
        Discussed: protocol?.Discussed,
        Risks: protocol?.Risks,
        RequiresClarification: protocol?.RequiresClarification
    );

    byte[] exportBytes;
    string contentType;
    string fileName;
    if (fmt is "pdf" or "html")
    {
        exportBytes = generator.GeneratePdf(exportData);
        contentType = "text/html; charset=utf-8";
        fileName = $"Meeting_{id}.html";
    }
    else
    {
        exportBytes = generator.GenerateDocx(exportData);
        contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        fileName = $"Meeting_{id}.docx";
    }

    var exportEnd = DateTime.UtcNow;
    await langfuse.CreateTraceAsync(
        traceId: traceId,
        name: "google-meet-export-full",
        userId: master_id.ToString(),
        sessionId: $"meeting-{id}",
        metadata: new { meeting_id = id, master_id, format = fmt, file_name = fileName, bytes = exportBytes.Length, has_raw_transcript = !string.IsNullOrWhiteSpace(protocol?.RawTranscript) },
        tags: new[] { "channel:api", "module:meetings", "source:google_meet", $"format:{fmt}", "status:ok" },
        input: new { meeting_id = id, master_id, format = fmt },
        output: new { file_name = fileName, bytes = exportBytes.Length, content_type = contentType },
        ct: ct);
    await langfuse.CreateSpanAsync(traceId, "Span:Meeting_Export_Full", exportStart, exportEnd,
        input: new { meeting_id = id, master_id, format = fmt },
        output: new { file_name = fileName, bytes = exportBytes.Length }, ct: ct);

    httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (fmt is "pdf" or "html")
    {
        httpContext.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'";
    }

    return Results.File(exportBytes, contentType, fileName);
});

// Прямой вызов детектора смены контекста через LLM (2-й этап)
app.MapPost("/api/context/detect-switch", async (
    [FromBody] ContextSwitchRequest req,
    IContextSwitchDetector detector,
    ILangfuseClient langfuse) =>
{
    var traceId = langfuse.GenerateTraceId();
    var messages = (req.History ?? new List<ContextMessageDto>())
        .Select(m => new ChatMessage { Role = m.Role, Content = m.Content })
        .ToList();

    var result = await detector.DetectSwitchAsync(messages, req.CurrentMessage, traceId);
    return Results.Ok(new
    {
        is_context_switched = result.IsSwitched,
        suggested_branch = result.SuggestedBranch,
        reason = result.Reason,
        trace_id = traceId
    });
});

// Эндпоинты запуска LLM-as-a-Judge оценок (Epic 6.4)
app.MapPost("/api/telemetry/evaluate-faithfulness", async (
    [FromBody] EvaluateFaithfulnessRequest req,
    ILlmJudgeService judgeService,
    ILangfuseClient langfuse) =>
{
    var traceId = req.TraceId ?? langfuse.GenerateTraceId();
    var score = await judgeService.EvaluateFaithfulnessAsync(traceId, req.UserQuestion, req.RawContextData, req.AgentAnswer);
    return Results.Ok(new { trace_id = traceId, metric = "faithfulness", score });
});

app.MapPost("/api/telemetry/evaluate-formatting-policy", async (
    [FromBody] EvaluatePolicyRequest req,
    ILlmJudgeService judgeService,
    ILangfuseClient langfuse) =>
{
    var traceId = req.TraceId ?? langfuse.GenerateTraceId();
    var score = await judgeService.EvaluateFormattingAndPolicyAsync(traceId, req.OutputText, req.IsProtocol);
    return Results.Ok(new { trace_id = traceId, metric = "formatting_and_policy", score });
});

_ = Task.Run(async () =>
{
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var repo = app.Services.GetRequiredService<IFotoprintRepository>();
        var embClient = app.Services.GetRequiredService<IEmbeddingClient>();
        if (!embClient.IsConfigured) return;
        var updated = await BackfillMissingEmbeddingsAsync(repo, embClient, cts.Token);
        if (updated > 0)
        {
            app.Logger.LogInformation("🧠 [RAG] Доиндексировано {Count} чанков с помощью {Model}", updated, embClient.ModelName);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogDebug(ex, "[RAG] Фоновая доиндексация эмбеддингов пропущена (БД или API недоступны)");
    }
});


// ============================================================================
// КОРПОРАТИВНЫЙ КАЛЕНДАРЬ (CALENDAR INTEGRATION & PROXY TO MCP-CALENDAR:8084)
// ============================================================================

// 1. Сетка событий календаря (поддержка scope=my и scope=all с RBAC)
app.MapGet("/api/calendar/events", async (
    HttpContext httpContext,
    IHttpClientFactory httpFactory,
    FotoprintConfig config,
    CancellationToken ct) =>
{
    var client = httpFactory.CreateClient("CalendarMcpClient");
    var queryString = httpContext.Request.QueryString.Value ?? "";
    var targetUrl = $"tools/get_calendar_events{queryString}";

    var req = new HttpRequestMessage(HttpMethod.Get, targetUrl);
    req.Headers.TryAddWithoutValidation("X-Internal-Service-Key", config.Security?.InternalServiceSecretKey ?? "fotoprint-internal-service-secret-token-2026");
    if (httpContext.Request.Headers.TryGetValue("X-Telegram-Id", out var tgId))
        req.Headers.TryAddWithoutValidation("X-Telegram-Id", tgId.ToString());
    if (httpContext.Request.Headers.TryGetValue("X-Trace-Id", out var trId))
        req.Headers.TryAddWithoutValidation("X-Trace-Id", trId.ToString());

    var resp = await client.SendAsync(req, ct);
    var content = await resp.Content.ReadAsStringAsync(ct);
    return Results.Content(content, "application/json", statusCode: (int)resp.StatusCode);
});

// 2. Подтверждение и создание встречи (Stateless Human-in-the-Loop Commit)
app.MapPost("/api/calendar/confirm", async (
    HttpContext httpContext,
    IHttpClientFactory httpFactory,
    FotoprintConfig config,
    CancellationToken ct) =>
{
    var client = httpFactory.CreateClient("CalendarMcpClient");
    var req = new HttpRequestMessage(HttpMethod.Post, "tools/create_meeting");
    req.Headers.TryAddWithoutValidation("X-Internal-Service-Key", config.Security?.InternalServiceSecretKey ?? "fotoprint-internal-service-secret-token-2026");
    if (httpContext.Request.Headers.TryGetValue("X-Telegram-Id", out var tgId))
        req.Headers.TryAddWithoutValidation("X-Telegram-Id", tgId.ToString());
    if (httpContext.Request.Headers.TryGetValue("X-Trace-Id", out var trId))
        req.Headers.TryAddWithoutValidation("X-Trace-Id", trId.ToString());

    using var stream = new StreamReader(httpContext.Request.Body);
    var body = await stream.ReadToEndAsync(ct);
    req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

    var resp = await client.SendAsync(req, ct);
    var content = await resp.Content.ReadAsStringAsync(ct);
    return Results.Content(content, "application/json", statusCode: (int)resp.StatusCode);
});

// 3. Подбор свободных слотов
app.MapPost("/api/calendar/slots", async (
    HttpContext httpContext,
    IHttpClientFactory httpFactory,
    FotoprintConfig config,
    CancellationToken ct) =>
{
    var client = httpFactory.CreateClient("CalendarMcpClient");
    var req = new HttpRequestMessage(HttpMethod.Post, "tools/find_free_slots");
    req.Headers.TryAddWithoutValidation("X-Internal-Service-Key", config.Security?.InternalServiceSecretKey ?? "fotoprint-internal-service-secret-token-2026");
    if (httpContext.Request.Headers.TryGetValue("X-Telegram-Id", out var tgId))
        req.Headers.TryAddWithoutValidation("X-Telegram-Id", tgId.ToString());
    if (httpContext.Request.Headers.TryGetValue("X-Trace-Id", out var trId))
        req.Headers.TryAddWithoutValidation("X-Trace-Id", trId.ToString());

    using var stream = new StreamReader(httpContext.Request.Body);
    var body = await stream.ReadToEndAsync(ct);
    req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

    var resp = await client.SendAsync(req, ct);
    var content = await resp.Content.ReadAsStringAsync(ct);
    return Results.Content(content, "application/json", statusCode: (int)resp.StatusCode);
});

app.Run();

static async Task<int> BackfillMissingEmbeddingsAsync(
    IFotoprintRepository repo,
    IEmbeddingClient embeddingClient,
    CancellationToken ct)
{
    if (!embeddingClient.IsConfigured) return 0;

    var totalUpdated = 0;
    while (!ct.IsCancellationRequested)
    {
        var missing = await repo.GetChunksWithoutEmbeddingAsync(100, ct);
        if (missing.Count == 0) break;

        var texts = missing.Select(c => c.ChunkText).ToList();
        var vectors = await embeddingClient.EmbedBatchAsync(texts, "RETRIEVAL_DOCUMENT", ct);

        var updatedInBatch = 0;
        for (int i = 0; i < missing.Count && i < vectors.Count; i++)
        {
            var vec = vectors[i];
            if (vec != null)
            {
                await repo.UpdateChunkEmbeddingAsync(missing[i].Id, vec, ct);
                updatedInBatch++;
                totalUpdated++;
            }
        }

        if (updatedInBatch == 0) break;
    }

    while (!ct.IsCancellationRequested)
    {
        var missingMeetings = await repo.GetMeetingChunksWithoutEmbeddingAsync(100, ct);
        if (missingMeetings.Count == 0) break;

        var texts = missingMeetings.Select(c => c.ChunkText).ToList();
        var vectors = await embeddingClient.EmbedBatchAsync(texts, "RETRIEVAL_DOCUMENT", ct);

        var updatedInBatch = 0;
        for (int i = 0; i < missingMeetings.Count && i < vectors.Count; i++)
        {
            var vec = vectors[i];
            if (vec != null)
            {
                await repo.UpdateMeetingChunkEmbeddingAsync(missingMeetings[i].Id, vec, ct);
                updatedInBatch++;
                totalUpdated++;
            }
        }

        if (updatedInBatch == 0) break;
    }

    return totalUpdated;
}

static bool HasValidInternalServiceKey(HttpContext httpContext, FotoprintConfig config)
{
    return InternalServiceAuthMiddleware.IsAuthorized(httpContext.Request, config);
}

static async Task<EmployeeSecurityContext?> ResolveCallerSecurityContextAsync(
    HttpContext httpContext,
    IFotoprintRepository repo,
    long? masterId,
    long? telegramId,
    CancellationToken ct)
{
    long? effectiveMasterId = masterId;
    if (!effectiveMasterId.HasValue &&
        long.TryParse(httpContext.Request.Query["master_id"].FirstOrDefault(), out var qMasterId))
    {
        effectiveMasterId = qMasterId;
    }
    if (!effectiveMasterId.HasValue &&
        long.TryParse(httpContext.Request.Headers["X-Master-Id"].FirstOrDefault(), out var hMasterId))
    {
        effectiveMasterId = hMasterId;
    }

    if (effectiveMasterId is > 0)
    {
        return await repo.GetEmployeeSecurityContextByMasterIdAsync(effectiveMasterId.Value, ct);
    }

    long? effectiveTgId = telegramId;
    if (!effectiveTgId.HasValue &&
        long.TryParse(httpContext.Request.Query["telegram_id"].FirstOrDefault(), out var qTgId))
    {
        effectiveTgId = qTgId;
    }
    if (!effectiveTgId.HasValue &&
        long.TryParse(httpContext.Request.Headers["X-Telegram-Id"].FirstOrDefault(), out var hTgId))
    {
        effectiveTgId = hTgId;
    }

    if (effectiveTgId is > 0)
    {
        return await repo.GetEmployeeSecurityContextAsync(effectiveTgId.Value, ct);
    }

    return null;
}

static object[] BuildPendingActionButtons(string? actionId)
{
    if (string.IsNullOrWhiteSpace(actionId)) return Array.Empty<object>();
    return new object[]
    {
        new { id = actionId, type = "confirm", label = "✅ Подтвердить", endpoint = $"/api/actions/{actionId}/confirm", command = $"confirm:{actionId}" },
        new { id = actionId, type = "edit", label = "✏️ Изменить", endpoint = (string?)null, command = (string?)null },
        new { id = actionId, type = "cancel", label = "🚫 Отмена", endpoint = $"/api/actions/{actionId}/cancel", command = $"cancel:{actionId}" }
    };
}

public record ProtocolDraftUpdateRequest(
    string? Title = null,
    string? Summary = null,
    List<string>? Decisions = null,
    List<string>? Proposed = null,
    List<string>? Discussed = null,
    List<string>? Risks = null,
    List<string>? RequiresClarification = null,
    long? MasterId = null,
    long? TelegramId = null);

public record SaveAndIndexMeetingRequest(
    string Title,
    string? CalendarEventId = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null,
    long? OrganizerMasterId = null,
    string? OrganizerEmail = null,
    List<long>? ParticipantMasterIds = null,
    List<string>? ParticipantEmails = null,
    string? RawTranscript = null,
    string? Summary = null,
    List<string>? Decisions = null,
    List<MeetingActionItemInput>? ActionItems = null,
    List<string>? Proposed = null,
    List<string>? Discussed = null,
    List<string>? Risks = null,
    List<string>? RequiresClarification = null,
    string? Status = null);
public record MeetingActionItemInput(string Title, string? Description = null, string? Responsible = null, DateTime? Deadline = null, string? SourceQuote = null);
public record MeetingRagSearchRequest(long MasterId, string Query, int TopK = 5);
public record EvaluateFaithfulnessRequest(string? TraceId, string UserQuestion, string RawContextData, string AgentAnswer);
public record EvaluatePolicyRequest(string? TraceId, string OutputText, bool IsProtocol = false);
public record ContextSwitchRequest(List<ContextMessageDto>? History, string CurrentMessage);
public record ContextMessageDto(string Role, string Content);
public record RagSearchRequest(string Query, List<string> UserAccessGroups, int TopK = 5, long? MasterId = null, long? TelegramId = null);
public record GeminiPromptRequest(string Prompt, string? SystemInstruction = null, double? Temperature = null, int? MaxOutputTokens = null, string? TraceName = null, string? TraceId = null, string? SessionId = null, string? UserId = null);
public record AgentChatRequest(long TelegramId, long ChatId, string Message, string? TraceId = null);
public record ChannelChatIngressRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("channel")] string Channel,
    [property: System.Text.Json.Serialization.JsonPropertyName("external_user_id")] string ExternalUserId,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message,
    [property: System.Text.Json.Serialization.JsonPropertyName("chat_id")] long? ChatId = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("trace_id")] string? TraceId = null);
