using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fotoprint.Core.Agents;
using Fotoprint.Core.Audio;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Formatting;
using Fotoprint.Core.Resilience;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telegram;
using Fotoprint.Core.Telemetry;

namespace Fotoprint.AiGateway.Api.Telegram;

/// <summary>
/// Processes incoming Telegram Webhook updates asynchronously while returning 200 OK immediately
/// to stay well below Telegram and Nginx proxy timeout boundaries.
/// </summary>
public sealed class TelegramWebhookProcessor
{
    private static readonly TimeSpan BackgroundExecutionTimeout = TimeSpan.FromSeconds(195);
    private static readonly TimeSpan TypingHeartbeatInterval = TimeSpan.FromSeconds(4);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClient _telegramBot;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILangfuseClient _langfuse;
    private readonly TelegramBotOptions _botOptions;
    private readonly TelegramUpdateDeduplicator _deduplicator;
    private readonly FotoprintConfig _config;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<TelegramWebhookProcessor> _logger;

    private sealed record N8nForwardOutcome(
        bool IsAuthorized,
        JsonElement? Payload = null,
        string? ErrorMessage = null);

    private static string? ExtractPendingActionId(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.Object } p &&
        p.TryGetProperty("pending_action_id", out var v) &&
        v.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    private sealed record VoiceMediaInfo(
        string? FileId,
        string FileName,
        string? AudioBase64);

    public TelegramWebhookProcessor(
        IServiceScopeFactory scopeFactory,
        ITelegramBotClient telegramBot,
        IHttpClientFactory httpFactory,
        ILangfuseClient langfuse,
        TelegramBotOptions botOptions,
        TelegramUpdateDeduplicator deduplicator,
        FotoprintConfig config,
        IHostApplicationLifetime appLifetime,
        ILogger<TelegramWebhookProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _telegramBot = telegramBot;
        _httpFactory = httpFactory;
        _langfuse = langfuse;
        _botOptions = botOptions;
        _deduplicator = deduplicator;
        _config = config;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    public async Task<IResult> HandleUpdateAsync(HttpContext httpContext, JsonElement rawUpdate)
    {
        var providedSecret = httpContext.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (!TelegramWebhookSecurity.IsSecretValid(_botOptions.WebhookSecret, providedSecret))
        {
            _logger.LogWarning("Rejected Telegram webhook request due to invalid secret token header.");
            return Results.Unauthorized();
        }

        if (rawUpdate.TryGetProperty("update_id", out var uidProp) &&
            uidProp.TryGetInt64(out var updateId) &&
            !_deduplicator.TryAcquire(updateId))
        {
            _logger.LogDebug("Duplicate Telegram update_id={UpdateId} skipped.", updateId);
            return Results.Ok(new { status = "duplicate_ignored", update_id = updateId });
        }

        var traceId = _langfuse.GenerateTraceId();

        if (rawUpdate.TryGetProperty("callback_query", out var cbQuery))
        {
            return await HandleCallbackQueryAsync(cbQuery, traceId, httpContext.RequestAborted);
        }

        if (rawUpdate.TryGetProperty("message", out var message) &&
            TryExtractIncomingMessage(message, out var chatId, out var userId, out var text, out var voiceMedia))
        {
            if (string.IsNullOrWhiteSpace(text) && voiceMedia == null)
            {
                return Results.Ok(new { status = "ignored_empty_message", trace_id = traceId });
            }

            var updateClone = rawUpdate.Clone();
            _ = Task.Run(() => ProcessMessageSafelyAsync(chatId, userId, text, voiceMedia, updateClone, traceId));

            return Results.Ok(new
            {
                status = "accepted",
                trace_id = traceId
            });
        }

        return Results.Ok(new { status = "ignored", trace_id = traceId });
    }

    private async Task<IResult> HandleCallbackQueryAsync(JsonElement cbQuery, string traceId, CancellationToken ct)
    {
        var cbId = cbQuery.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
        var cbData = cbQuery.TryGetProperty("data", out var dataProp) ? dataProp.GetString() ?? string.Empty : string.Empty;

        if (!cbQuery.TryGetProperty("from", out var fromUser) ||
            !fromUser.TryGetProperty("id", out var userIdProp) ||
            !userIdProp.TryGetInt64(out var userId))
        {
            return Results.Ok(new { status = "ignored_callback", trace_id = traceId });
        }

        long? chatId = null;
        if (cbQuery.TryGetProperty("message", out var msgObj) &&
            msgObj.TryGetProperty("chat", out var chatObj) &&
            chatObj.TryGetProperty("id", out var chatIdProp) &&
            chatIdProp.TryGetInt64(out var parsedChatId))
        {
            chatId = parsedChatId;
        }

        if (cbData.StartsWith("notify:set:", StringComparison.OrdinalIgnoreCase))
        {
            var enable = cbData.EndsWith(":on", StringComparison.OrdinalIgnoreCase);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFotoprintRepository>();
            var masterId = await ResolveMasterIdAsync(userId, repo, traceId, ct);
            if (masterId.HasValue)
            {
                await repo.SetEmployeeNotificationsEnabledAsync(masterId.Value, enable, ct);
            }

            if (!string.IsNullOrWhiteSpace(cbId))
            {
                var toast = enable ? "🔔 Уведомления включены" : "🔕 Уведомления отключены";
                await _telegramBot.AnswerCallbackQueryAsync(cbId, toast, showAlert: false);
            }

            if (chatId.HasValue)
            {
                var confirmation = enable
                    ? "🔔 Проактивные уведомления о расписании и задачах включены."
                    : "🔕 Проактивные уведомления приостановлены. Чтобы снова включить, нажмите /notifications on.";
                await _telegramBot.SendMessageAsync(chatId.Value, confirmation, ct: ct);
            }

            return Results.Ok(new { status = "callback_notifications_updated", enabled = enable, trace_id = traceId });
        }

        if (cbData.StartsWith("action:", StringComparison.OrdinalIgnoreCase))
        {
            // Двухфазное подтверждение (AI-03): кнопки плана действий. Исполняется сохранённый план, а не повторный вывод LLM.
            var actionParts = cbData.Split(':', StringSplitOptions.RemoveEmptyEntries);
            var actionVerb = actionParts.Length > 1 ? actionParts[1].ToLowerInvariant() : string.Empty;
            var actionPlanId = actionParts.Length > 2 ? actionParts[2] : string.Empty;

            if (!string.IsNullOrWhiteSpace(cbId))
            {
                var toast = actionVerb switch
                {
                    "confirm" => "⏳ Выполняю подтверждённый план...",
                    "cancel" => "🚫 Отменяю план...",
                    _ => "✏️ Опишите правку сообщением"
                };
                await _telegramBot.AnswerCallbackQueryAsync(cbId, toast, showAlert: false);
            }

            if (chatId.HasValue)
            {
                if (actionVerb is "confirm" or "cancel")
                {
                    await using var actionScope = _scopeFactory.CreateAsyncScope();
                    var actionAgent = actionScope.ServiceProvider.GetRequiredService<IFotoprintAgent>();
                    var actionResult = await actionAgent.ProcessMessageAsync(userId, chatId.Value, $"{actionVerb}:{actionPlanId}", traceId, ct);
                    await _telegramBot.SendMessageAsync(chatId.Value, actionResult.AnswerText, ct: ct);
                }
                else if (actionVerb == "edit")
                {
                    await _telegramBot.SendMessageAsync(chatId.Value,
                        "✏️ Напишите, что нужно изменить (например, «срок — пятница, ответственный — Иванов»), и я подготовлю новый план. Текущий план пока не выполнен.",
                        ct: ct);
                }
            }

            return Results.Ok(new { status = "callback_action_handled", trace_id = traceId });
        }

        if (cbData.StartsWith("rate:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = cbData.Split(':', StringSplitOptions.RemoveEmptyEntries);
            var isLike = parts.Length > 1 && parts[1].Equals("like", StringComparison.OrdinalIgnoreCase);
            var targetTrace = parts.Length > 2 ? parts[2] : traceId;
            var score = isLike ? 1.0 : 0.0;

            await _langfuse.LogScoreAsync(targetTrace, "user_feedback", score, $"Telegram user {userId} rated {cbData}");

            if (!string.IsNullOrWhiteSpace(cbId))
            {
                var toast = isLike
                    ? "Спасибо за положительную оценку! 👍"
                    : "Спасибо за отзыв! Мы учтём это для улучшения работы бота. 👎";
                await _telegramBot.AnswerCallbackQueryAsync(cbId, toast, showAlert: false);
            }

            return Results.Ok(new { status = "callback_rated", trace_id = traceId });
        }

        if (cbData.StartsWith("create_bitrix_task:", StringComparison.OrdinalIgnoreCase))
        {
            var actionItemStr = cbData["create_bitrix_task:".Length..];
            if (Guid.TryParse(actionItemStr, out var actionItemId))
            {
                if (!string.IsNullOrWhiteSpace(cbId))
                {
                    await _telegramBot.AnswerCallbackQueryAsync(cbId, "⏳ Создаю задачу в Битрикс24...", showAlert: false);
                }

                _ = Task.Run(() => ProcessBitrixCallbackSafelyAsync(chatId, userId, actionItemId, traceId));
                return Results.Ok(new { status = "callback_accepted", trace_id = traceId });
            }
        }

        if (!string.IsNullOrWhiteSpace(cbId))
        {
            await _telegramBot.AnswerCallbackQueryAsync(cbId, showAlert: false);
        }

        return Results.Ok(new { status = "callback_ignored", trace_id = traceId });
    }

    private async Task ProcessBitrixCallbackSafelyAsync(long? chatId, long userId, Guid actionItemId, string traceId)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime.ApplicationStopping);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFotoprintRepository>();
            var rollbackManager = scope.ServiceProvider.GetRequiredService<IRollbackManager>();

            var rollbackResult = await rollbackManager.ExecuteWithRollbackAsync(
                nodeName: "Bitrix24:CreateTask",
                action: async (attempt, ct) =>
                {
                    var bitrixClient = _httpFactory.CreateClient("BitrixMcpClient");
                    var bitrixReq = new
                    {
                        title = "Поручение по протоколу встречи",
                        responsible_id = 1,
                        deadline = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd")
                    };

                    using var reqMessage = new HttpRequestMessage(HttpMethod.Post, "tools/create_task")
                    {
                        Content = JsonContent.Create(bitrixReq)
                    };
                    reqMessage.Headers.TryAddWithoutValidation("X-Telegram-Id", userId.ToString());
                    reqMessage.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);

                    using var response = await bitrixClient.SendAsync(reqMessage, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new NodeTransientException(
                            $"Bitrix24 вернул код {(int)response.StatusCode}: {response.ReasonPhrase}",
                            (int)response.StatusCode);
                    }

                    await repo.UpdateActionItemStatusAsync(actionItemId, "exported_to_bitrix", ct: ct);
                },
                compensatingRollbackAction: async (ex, ct) =>
                {
                    await repo.UpdateActionItemStatusAsync(actionItemId, "draft_rollback", ct: ct);
                },
                traceId: traceId,
                ct: cts.Token);

            if (rollbackResult.IsSuccess)
            {
                await _langfuse.LogEventAsync(traceId, "HumanInTheLoop:Approved", "INFO", $"Задача {actionItemId} одобрена пользователем {userId}");
                await _langfuse.LogScoreAsync(traceId, "task_extraction_accuracy", 1.0, $"Пользователь {userId} утвердил поручение {actionItemId} в Битрикс24 без правок");

                if (chatId.HasValue)
                {
                    await _telegramBot.SendMessageAsync(chatId.Value, "✅ Поручение успешно поставлено в Битрикс24!", ct: cts.Token);
                }
            }
            else if (chatId.HasValue)
            {
                await _telegramBot.SendMessageAsync(
                    chatId.Value,
                    "⚠️ Не удалось связаться с Битрикс24 после 3 попыток. Статус задачи безопасно откачен (Rollback). Попробуйте позже.",
                    ct: cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background Bitrix24 callback failed for actionItem={ActionItemId}", actionItemId);
            if (chatId.HasValue)
            {
                await TryNotifyFailureAsync(chatId.Value, "⚠️ Произошла ошибка при создании задачи в Битрикс24.");
            }
        }
    }

    private async Task ProcessMessageSafelyAsync(
        long chatId,
        long userId,
        string text,
        VoiceMediaInfo? voiceMedia,
        JsonElement rawUpdateClone,
        string traceId)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime.ApplicationStopping);
        cts.CancelAfter(BackgroundExecutionTimeout);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFotoprintRepository>();
            var switchDetector = scope.ServiceProvider.GetRequiredService<IContextSwitchDetector>();
            var judgeService = scope.ServiceProvider.GetRequiredService<ILlmJudgeService>();
            var loadBalancer = scope.ServiceProvider.GetRequiredService<ILlmLoadBalancer>();
            var rollbackManager = scope.ServiceProvider.GetRequiredService<IRollbackManager>();
            var transcriber = scope.ServiceProvider.GetService<IAudioTranscriber>();

            // ШАГ 1: Получение MasterId из EmployeeReader или fallback к Postgres
            var masterId = await ResolveMasterIdAsync(userId, repo, traceId, cts.Token);
            if (!masterId.HasValue)
            {
                string? uName = null, fName = null, lName = null;
                if (rawUpdateClone.TryGetProperty("message", out var msgEl) && msgEl.TryGetProperty("from", out var fromEl))
                {
                    if (fromEl.TryGetProperty("username", out var up)) uName = up.GetString();
                    if (fromEl.TryGetProperty("first_name", out var fp)) fName = fp.GetString();
                    if (fromEl.TryGetProperty("last_name", out var lp)) lName = lp.GetString();
                }
                var ensured = await repo.EnsureTelegramDirectorAsync(userId, uName, fName, lName, cts.Token);
                masterId = ensured.MasterId;
            }

            var secContext = await repo.GetEmployeeSecurityContextByMasterIdAsync(masterId.Value, cts.Token);
            if (secContext != null && !secContext.Roles.Contains("Director", StringComparer.OrdinalIgnoreCase))
            {
                secContext.Roles.Add("Director");
            }
            if (secContext != null && !secContext.AccessGroups.Contains("c-level", StringComparer.OrdinalIgnoreCase))
            {
                secContext.AccessGroups.Add("c-level");
            }
            if (text.TrimStart().StartsWith("/notifications", StringComparison.OrdinalIgnoreCase))
            {
                await HandleNotificationsCommandAsync(chatId, masterId.Value, text, repo, cts.Token);
                return;
            }
            if (text.TrimStart().StartsWith("/start", StringComparison.OrdinalIgnoreCase))
            {
                var greeting = BuildStartGreeting(secContext, masterId.Value);
                await _telegramBot.SendMessageAsync(chatId, greeting, ct: cts.Token);
                return;
            }

            using var typingCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            var typingTask = RunTypingHeartbeatAsync(chatId, typingCts.Token);

            try
            {
                if (voiceMedia != null && transcriber != null)
                {
                    var voiceText = await TryTranscribeVoiceAsync(voiceMedia, transcriber, traceId, cts.Token);
                    if (!string.IsNullOrWhiteSpace(voiceText))
                    {
                        text = string.IsNullOrWhiteSpace(text)
                            ? voiceText
                            : $"{text}\n{voiceText}";
                    }
                    else if (string.IsNullOrWhiteSpace(text))
                    {
                        await _telegramBot.SendMessageAsync(
                            chatId,
                            "⚠️ Не удалось распознать голосовое сообщение. Попробуйте сказать чётче или напишите текстом.",
                            ct: cts.Token);
                        return;
                    }
                }

                // ШАГ 2: Активный диалог в БД по MasterId
                var activeSession = await repo.GetOrCreateActiveSessionByMasterIdAsync(masterId.Value, chatId, "telegram", cts.Token);
                var currentSessionId = activeSession.Id;
                var dept = secContext?.Department ?? "Общий";
                var rolesStr = string.Join(",", secContext?.Roles ?? new());

                // ШАГ 3: Определение смены контекста
                var recentSessionMessages = await repo.GetMessagesBySessionIdAsync(currentSessionId, limit: _config.ContextSwitch.MaxHistoryMessages, ct: cts.Token);
                var switchResult = await switchDetector.DetectSwitchAsync(recentSessionMessages, text, traceId, cts.Token);

                var previousSessionId = currentSessionId;
                if (switchResult.IsSwitched)
                {
                    activeSession = await repo.SwitchToNewSessionAsync(
                        currentSessionId: currentSessionId,
                        masterId: masterId.Value,
                        messengerChatId: chatId,
                        newBranch: switchResult.SuggestedBranch,
                        messengerType: "telegram",
                        ct: cts.Token);
                }

                var effectiveSessionId = activeSession.Id;

                await _langfuse.CreateTraceAsync(
                    traceId: traceId,
                    name: "telegram-chat-message",
                    userId: masterId.Value.ToString(),
                    sessionId: effectiveSessionId.ToString(),
                    metadata: new
                    {
                        chat_id = chatId,
                        master_id = masterId.Value,
                        telegram_id = userId,
                        department = dept,
                        roles = rolesStr,
                        is_context_switched = switchResult.IsSwitched,
                        branch = switchResult.SuggestedBranch,
                        switch_reason = switchResult.Reason,
                        text = text
                    },
                    tags: new[]
                    {
                        $"dept:{dept}",
                        $"role:{rolesStr}",
                        $"branch:{switchResult.SuggestedBranch}",
                        $"context_switched:{switchResult.IsSwitched.ToString().ToLowerInvariant()}"
                    },
                    input: text);

                await repo.SaveMessageToSessionAsync(
                    sessionId: effectiveSessionId,
                    masterId: masterId.Value,
                    role: "user",
                    content: text,
                    isFrozen: false,
                    metadata: new
                    {
                        trace_id = traceId,
                        is_context_switched = switchResult.IsSwitched,
                        branch = switchResult.SuggestedBranch,
                        switch_reason = switchResult.Reason,
                        previous_session_id = switchResult.IsSwitched ? previousSessionId.ToString() : null
                    },
                    ct: cts.Token);

                if (_config.Telemetry.Evaluations.EnableLlmAsJudge && recentSessionMessages.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var histText = string.Join("\n", recentSessionMessages.Select(m => $"{m.Role}: {m.Content}"));
                            await judgeService.EvaluateContextSwitchAccuracyAsync(
                                traceId: traceId,
                                historyText: histText,
                                incomingMessage: text,
                                detectedSwitch: switchResult.IsSwitched,
                                suggestedBranch: switchResult.SuggestedBranch);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Background ContextSwitch judge evaluation failed");
                        }
                    });
                }

                // ШАГ 4: Вызов n8n webhook (/webhook/fotoprint-chat) -> .NET AI Agent
                var n8nPayload = new
                {
                    trace_id = traceId,
                    master_id = masterId.Value,
                    session_id = effectiveSessionId.ToString(),
                    previous_session_id = switchResult.IsSwitched ? previousSessionId.ToString() : null,
                    is_context_switched = switchResult.IsSwitched,
                    branch = switchResult.SuggestedBranch,
                    switch_reason = switchResult.Reason,
                    chat_id = chatId,
                    user_id = userId,
                    text = text,
                    raw_update = rawUpdateClone
                };

                var scheduledResult = await rollbackManager.ExecuteWithRollbackAsync<N8nForwardOutcome>(
                    nodeName: "N8n:FotoprintChat",
                    action: async (attempt, token) =>
                    {
                        var n8nClient = _httpFactory.CreateClient("N8nClient");
                        using var n8nRequest = new HttpRequestMessage(HttpMethod.Post, "fotoprint-chat")
                        {
                            Content = JsonContent.Create(n8nPayload)
                        };
                        AttachTracingHeaders(n8nRequest, traceId, effectiveSessionId, masterId.Value, userId);

                        using var n8nResponse = await n8nClient.SendAsync(n8nRequest, token);
                        if (n8nResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        {
                            var errBody = await n8nResponse.Content.ReadAsStringAsync(token);
                            return new N8nForwardOutcome(false, ErrorMessage: errBody);
                        }

                        if (!n8nResponse.IsSuccessStatusCode)
                        {
                            throw new NodeTransientException(
                                $"n8n вернул код {(int)n8nResponse.StatusCode}: {n8nResponse.ReasonPhrase}",
                                (int)n8nResponse.StatusCode);
                        }

                        var json = await n8nResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
                        return new N8nForwardOutcome(true, Payload: json);
                    },
                    compensatingRollbackAction: async (ex, token) =>
                    {
                        await repo.SaveMessageToSessionAsync(
                            effectiveSessionId,
                            masterId.Value,
                            "assistant",
                            "⚠️ Узел обработки n8n временно перегружен после 3 попыток. Ваше сообщение сохранено, состояние сессии безопасно зафиксировано.",
                            isFrozen: false,
                            metadata: new { rollback = true, error = ex.Message, trace_id = traceId },
                            ct: token);
                    },
                    traceId: traceId,
                    ct: cts.Token);

                if (scheduledResult.IsSuccess && scheduledResult.Value is { } outcome)
                {
                    if (!outcome.IsAuthorized)
                    {
                        await _telegramBot.SendMessageAsync(
                            chatId,
                            "⛔ Доступ ограничен: ваша роль не позволяет выполнить данный запрос.",
                            ct: cts.Token);
                        return;
                    }

                    var replyText = ExtractReplyText(outcome.Payload);
                    await repo.SaveMessageToSessionAsync(
                        sessionId: effectiveSessionId,
                        masterId: masterId.Value,
                        role: "assistant",
                        content: replyText,
                        isFrozen: false,
                        metadata: new { trace_id = traceId, orchestrator = "n8n", branch = switchResult.SuggestedBranch },
                        ct: cts.Token);

                    await _langfuse.CreateTraceAsync(
                        traceId: traceId,
                        name: "telegram-chat-message",
                        userId: masterId.Value.ToString(),
                        sessionId: effectiveSessionId.ToString(),
                        metadata: new
                        {
                            chat_id = chatId,
                            master_id = masterId.Value,
                            telegram_id = userId,
                            department = dept,
                            roles = rolesStr,
                            is_context_switched = switchResult.IsSwitched,
                            branch = switchResult.SuggestedBranch,
                            switch_reason = switchResult.Reason,
                            text = text
                        },
                        tags: new[]
                        {
                            $"dept:{dept}",
                            $"role:{rolesStr}",
                            $"branch:{switchResult.SuggestedBranch}",
                            $"context_switched:{switchResult.IsSwitched.ToString().ToLowerInvariant()}",
                            "status:ok"
                        },
                        input: text,
                        output: replyText,
                        ct: cts.Token);

                    var feedbackButtons = new[]
                    {
                        new[]
                        {
                            new TelegramInlineButton("👍 Помогло", $"rate:like:{traceId}"),
                            new TelegramInlineButton("👎 Не помогло", $"rate:dislike:{traceId}")
                        }
                    };

                    var pendingActionId = ExtractPendingActionId(outcome.Payload);
                    var replyButtons = pendingActionId == null
                        ? feedbackButtons
                        : new[]
                        {
                            new[]
                            {
                                new TelegramInlineButton("✅ Подтвердить", $"action:confirm:{pendingActionId}"),
                                new TelegramInlineButton("✏️ Изменить", $"action:edit:{pendingActionId}"),
                                new TelegramInlineButton("🚫 Отмена", $"action:cancel:{pendingActionId}")
                            },
                            feedbackButtons[0]
                        };

                    var sendRes = await _telegramBot.SendMessageAsync(chatId, replyText, replyButtons, ct: cts.Token);
                    if (!sendRes.Ok)
                    {
                        _logger.LogWarning(
                            "Failed to deliver Telegram reply for trace={TraceId}: {ErrorCode} {Desc}",
                            traceId, sendRes.ErrorCode, sendRes.Description);
                    }
                    return;
                }

                await _telegramBot.SendMessageAsync(
                    chatId,
                    "⚠️ Не удалось получить ответ от AI-ассистента (таймаут или временная перегрузка сервиса). Попробуйте повторить запрос чуть позже.",
                    ct: cts.Token);
            }
            finally
            {
                await typingCts.CancelAsync();
                try
                {
                    await typingTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning("Background Telegram processing timed out for chatId={ChatId}, trace={TraceId}", chatId, traceId);
            await TryNotifyFailureAsync(chatId, "⚠️ Время ожидания ответа от AI-ассистента истекло. Попробуйте упростить или повторить запрос.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing Telegram message for chatId={ChatId}, trace={TraceId}", chatId, traceId);
            await TryNotifyFailureAsync(chatId, "⚠️ Произошла внутренняя ошибка при обработке сообщения. Попробуйте позже.");
        }
    }

    private async Task<long?> ResolveMasterIdAsync(long userId, IFotoprintRepository repo, string traceId, CancellationToken ct)
    {
        long? masterId = null;
        var startEmp = DateTime.UtcNow;

        try
        {
            var empClient = _httpFactory.CreateClient("EmployeeReaderClient");
            var empResp = await empClient.GetAsync($"tools/resolve_master_id?messenger=telegram&messenger_id={userId}", ct);
            if (empResp.IsSuccessStatusCode)
            {
                var json = await empResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (json.TryGetProperty("master_id", out var mProp) && mProp.TryGetInt64(out var mVal))
                {
                    masterId = mVal;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "EmployeeReaderClient unavailable for telegram_id={TelegramId}; falling back to local DB.", userId);
        }

        if (!masterId.HasValue)
        {
            masterId = await repo.ResolveMasterIdByMessengerAsync("telegram", userId.ToString(), ct);
        }

        if (!masterId.HasValue)
        {
            var ensured = await repo.EnsureTelegramDirectorAsync(userId, ct: ct);
            masterId = ensured.MasterId;
        }

        var endEmp = DateTime.UtcNow;
        await _langfuse.CreateSpanAsync(
            traceId: traceId,
            name: "Span:MCP-EmployeeReader",
            startTime: startEmp,
            endTime: endEmp,
            input: new { telegram_id = userId },
            output: new { master_id = masterId });

        return masterId;
    }

    private void AttachTracingHeaders(HttpRequestMessage request, string traceId, Guid sessionId, long masterId, long telegramId)
    {
        request.Headers.TryAddWithoutValidation("X-Trace-Id", traceId);
        request.Headers.TryAddWithoutValidation("X-Session-Id", sessionId.ToString());
        request.Headers.TryAddWithoutValidation("X-Master-Id", masterId.ToString());
        request.Headers.TryAddWithoutValidation("X-Telegram-Id", telegramId.ToString());
        request.Headers.TryAddWithoutValidation("X-Internal-Service-Key", _config.Security.InternalServiceSecretKey);
    }

    private async Task RunTypingHeartbeatAsync(long chatId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _telegramBot.SendChatActionAsync(chatId, "typing", ct);
                await Task.Delay(TypingHeartbeatInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal termination when reply is ready
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Typing indicator heartbeat failed for chatId={ChatId}", chatId);
        }
    }

    private async Task TryNotifyFailureAsync(long chatId, string text)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _telegramBot.SendMessageAsync(chatId, text, ct: timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send failure notification to Telegram chatId={ChatId}", chatId);
        }
    }

    private async Task<string?> TryTranscribeVoiceAsync(
        VoiceMediaInfo media,
        IAudioTranscriber transcriber,
        string traceId,
        CancellationToken ct)
    {
        var startAudio = DateTime.UtcNow;
        TranscriptionResult? result = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(media.AudioBase64))
            {
                var maxBytes = Math.Max(1, _config.Audio.MaxFileSizeMb) * 1024L * 1024L;
                if (media.AudioBase64.Length <= maxBytes * 2)
                {
                    var bytes = Convert.FromBase64String(media.AudioBase64);
                    if (bytes.LongLength <= maxBytes)
                    {
                        using var ms = new MemoryStream(bytes);
                        result = await transcriber.TranscribeAsync(ms, media.FileName, "ru", ct);
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(media.FileId) && _botOptions.IsConfigured)
            {
                var apiBase = _botOptions.ApiBaseUrl.TrimEnd('/');
                var client = _httpFactory.CreateClient();
                var getFileResp = await client.GetAsync(
                    $"{apiBase}/bot{_botOptions.BotToken}/getFile?file_id={Uri.EscapeDataString(media.FileId)}",
                    ct);
                if (getFileResp.IsSuccessStatusCode)
                {
                    var fileJson = await getFileResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                    if (fileJson.TryGetProperty("ok", out var okProp) && okProp.GetBoolean() &&
                        fileJson.TryGetProperty("result", out var resProp) &&
                        resProp.TryGetProperty("file_path", out var fpProp))
                    {
                        var tgFilePath = fpProp.GetString();
                        if (!string.IsNullOrWhiteSpace(tgFilePath))
                        {
                            var dlResp = await client.GetAsync($"{apiBase}/file/bot{_botOptions.BotToken}/{tgFilePath}", ct);
                            if (dlResp.IsSuccessStatusCode)
                            {
                                await using var tgStream = await dlResp.Content.ReadAsStreamAsync(ct);
                                var resolvedName = Path.GetFileName(tgFilePath);
                                result = await transcriber.TranscribeAsync(
                                    tgStream,
                                    string.IsNullOrWhiteSpace(resolvedName) ? media.FileName : resolvedName,
                                    "ru",
                                    ct);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice transcription error for trace={TraceId}", traceId);
            await _langfuse.LogEventAsync(traceId, "Telegram:VoiceTranscriptionError", "ERROR", ex.Message, ct: ct);
        }

        if (result != null)
        {
            await _langfuse.CreateSpanAsync(
                traceId: traceId,
                name: "Span:Telegram_VoiceTranscription",
                startTime: startAudio,
                endTime: DateTime.UtcNow,
                input: new { file_name = media.FileName, has_file_id = !string.IsNullOrWhiteSpace(media.FileId) },
                output: new { success = result.Success, provider = result.Provider, text = result.Text },
                metadata: new { duration_ms = result.DurationMs, error = result.ErrorMessage },
                ct: ct);

            if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
            {
                return result.Text.Trim();
            }
        }

        return null;
    }

    private static bool TryExtractIncomingMessage(
        JsonElement message,
        out long chatId,
        out long userId,
        out string text,
        out VoiceMediaInfo? voiceMedia)
    {
        chatId = 0;
        userId = 0;
        text = string.Empty;
        voiceMedia = null;

        if (!message.TryGetProperty("chat", out var chatObj) ||
            !chatObj.TryGetProperty("id", out var chatIdProp) ||
            !chatIdProp.TryGetInt64(out chatId))
        {
            return false;
        }

        if (!message.TryGetProperty("from", out var fromUser) ||
            !fromUser.TryGetProperty("id", out var userIdProp) ||
            !userIdProp.TryGetInt64(out userId))
        {
            return false;
        }

        if (message.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
        {
            text = textProp.GetString() ?? string.Empty;
        }
        else if (message.TryGetProperty("caption", out var capProp) && capProp.ValueKind == JsonValueKind.String)
        {
            text = capProp.GetString() ?? string.Empty;
        }

        JsonElement mediaElem = default;
        string? defaultAudioName = null;
        if (message.TryGetProperty("voice", out var voiceElem) && voiceElem.ValueKind == JsonValueKind.Object)
        {
            mediaElem = voiceElem;
            defaultAudioName = "voice.ogg";
        }
        else if (message.TryGetProperty("audio", out var audioElem) && audioElem.ValueKind == JsonValueKind.Object)
        {
            mediaElem = audioElem;
            defaultAudioName = "audio.m4a";
        }

        if (defaultAudioName != null)
        {
            var fileId = mediaElem.TryGetProperty("file_id", out var fidProp) ? fidProp.GetString() : null;
            var rawFileName = mediaElem.TryGetProperty("file_name", out var fnProp) ? fnProp.GetString() : null;
            var safeFileName = !string.IsNullOrWhiteSpace(rawFileName) ? Path.GetFileName(rawFileName) : null;
            var fileName = !string.IsNullOrWhiteSpace(safeFileName)
                ? safeFileName
                : defaultAudioName;
            var audioBase64 = mediaElem.TryGetProperty("audio_base64", out var b64Prop)
                ? b64Prop.GetString()
                : (message.TryGetProperty("audio_base64", out var msgB64) ? msgB64.GetString() : null);

            voiceMedia = new VoiceMediaInfo(fileId, fileName, audioBase64);
        }

        return true;
    }

    private static string ExtractReplyText(JsonElement? n8nJson)
    {
        const string defaultReply = "Запрос выполнен.";
        if (!n8nJson.HasValue || n8nJson.Value.ValueKind != JsonValueKind.Object)
        {
            return defaultReply;
        }

        var obj = n8nJson.Value;
        string? raw = null;
        if (obj.TryGetProperty("answer", out var ansProp) && ansProp.ValueKind == JsonValueKind.String)
        {
            raw = ansProp.GetString();
        }
        else if (obj.TryGetProperty("response_text", out var rtProp) && rtProp.ValueKind == JsonValueKind.String)
        {
            raw = rtProp.GetString();
        }
        else if (obj.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
        {
            raw = msgProp.GetString();
        }

        var sanitized = PlainTextFormatter.Sanitize(raw);
        return string.IsNullOrWhiteSpace(sanitized) ? defaultReply : sanitized;
    }

    private async Task HandleNotificationsCommandAsync(
        long chatId,
        long masterId,
        string commandText,
        IFotoprintRepository repo,
        CancellationToken ct)
    {
        var tokens = commandText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var arg = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : string.Empty;

        if (arg is "on" or "вкл" or "enable" or "1")
        {
            await repo.SetEmployeeNotificationsEnabledAsync(masterId, true, ct);
        }
        else if (arg is "off" or "выкл" or "disable" or "0")
        {
            await repo.SetEmployeeNotificationsEnabledAsync(masterId, false, ct);
        }

        var secCtx = await repo.GetEmployeeSecurityContextByMasterIdAsync(masterId, ct);
        var employee = secCtx is not null ? await repo.GetEmployeeByIdAsync(secCtx.EmployeeId, ct) : null;
        var enabled = employee?.NotificationsEnabled ?? true;

        string msg;
        if (employee is not null)
        {
            var prefs = await repo.GetNotificationPreferencesAsync(employee.Id, masterId, ct);
            msg = Fotoprint.Core.Notifications.NotificationFormatter.FormatNotificationSettingsSummary(employee, prefs);
        }
        else
        {
            var statusEmoji = enabled ? "🔔 Включены" : "🔕 Отключены";
            msg = new StringBuilder()
                .AppendLine($"Статус проактивных уведомлений: {statusEmoji}")
                .AppendLine()
                .AppendLine("Что приходит автоматически (время МСК):")
                .AppendLine("• Утренняя сводка встреч и задач в 09:00 по будням")
                .AppendLine("• Напоминания за 30 минут до начала встречи")
                .AppendLine("• Напоминания о дедлайнах задач (за 24 ч, за 2 ч и по просрочке)")
                .AppendLine()
                .Append("Быстрая команда: /notifications on или /notifications off")
                .ToString();
        }

        var keyboard = new[]
        {
            new[]
            {
                enabled
                    ? new TelegramInlineButton("🔕 Отключить уведомления", "notify:set:off")
                    : new TelegramInlineButton("🔔 Включить уведомления", "notify:set:on")
            }
        };

        await _telegramBot.SendMessageAsync(chatId, msg, inlineKeyboard: keyboard, ct: ct);
    }

    private static string BuildStartGreeting(EmployeeSecurityContext? ctx, long masterId)
    {
        var sb = new StringBuilder();
        var fullName = string.IsNullOrWhiteSpace(ctx?.FullName) ? $"Сотрудник #{masterId}" : ctx.FullName;
        sb.AppendLine($"👋 Здравствуйте, {fullName}!");
        sb.AppendLine();
        if (ctx is not null)
        {
            if (!string.IsNullOrWhiteSpace(ctx.Position) || !string.IsNullOrWhiteSpace(ctx.Department))
            {
                sb.AppendLine($"🏢 Отдел: {ctx.Department} ({ctx.Position})");
            }

            var roles = ctx.Roles.Count > 0 ? new List<string>(ctx.Roles) : new List<string> { "Director" };
            if (!roles.Contains("Director", StringComparer.OrdinalIgnoreCase))
            {
                roles.Add("Director");
            }
            sb.AppendLine($"🔐 Роли: {string.Join(", ", roles)}");

            sb.AppendLine();
        }

        sb.AppendLine("Я корпоративный AI-ассистент Fotoprint. Задайте любой вопрос в свободной форме, например:");
        sb.AppendLine("• Какие задачи у меня сейчас в работе в Битрикс24?");
        sb.AppendLine("• Покажи мои встречи в календаре на завтра");
        sb.AppendLine("• Найди информацию в базе знаний и регламентах");
        sb.AppendLine("• /notifications — управление утренней сводкой и напоминаниями о встречах/задачах");
        return sb.ToString().TrimEnd();
    }
}
