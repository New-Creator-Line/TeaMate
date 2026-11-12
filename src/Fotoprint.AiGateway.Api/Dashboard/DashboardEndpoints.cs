using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Llm;
using Fotoprint.Core.Resilience;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace Fotoprint.AiGateway.Api.Dashboard;

public static class DashboardEndpoints
{
    private static readonly object SyncLock = new();
    private static readonly ConcurrentDictionary<string, DashboardDecisionDto> IdempotencyStore = new(StringComparer.Ordinal);

    private static readonly List<DashboardDecisionDto> DecisionStore =
    [
        new(
            Id: "DEC-104",
            Problem: "Брак на линии фотокниг Premium вырос до 8,1% (норма <= 5%)",
            Impact: "Потери маржи ~4 200 BYN/нед., риск срыва SLA доставки",
            Cause: "Партия мелованной бумаги 170 г/м² с повышенной влажностью + увод прижима на Kolbus",
            Action: "Заменить партию бумаги со склада B2, провести калибровку Kolbus и тестовый тираж 30 кн.",
            Owner: "Иван Кузнецов · Производство",
            DueDate: "2026-11-14",
            Priority: "Высокий",
            Status: "В работе",
            BeforeKpi: "8,1% брака",
            AfterKpi: "Ожидается <= 4,8% (проверка 15.11)",
            Department: "production",
            CreatedAt: "2026-11-11T09:15:00+03:00"),
        new(
            Id: "DEC-103",
            Problem: "Ожидаемый дефицит мощности −640 ч на 47-й неделе (преддверие пика сезона)",
            Impact: "Риск увеличения Lead Time с 2,8 до 4,5 дней по фотокнигам и календарям",
            Cause: "Сезонный рост спроса + 2 открытые вакансии операторов послепечатного участка",
            Action: "Вывести дополнительную смену на выходные и перераспределить тиражи Lite на линию B",
            Owner: "Алексей Смирнов · Руководство",
            DueDate: "2026-11-18",
            Priority: "Высокий",
            Status: "Назначено",
            BeforeKpi: "Дефицит −640 ч",
            AfterKpi: "Прогноз после меры: +80 ч резерва",
            Department: "production",
            CreatedAt: "2026-11-10T16:40:00+03:00"),
        new(
            Id: "DEC-101",
            Problem: "Просадка повторных покупок в B2C сегменте на 2,8 п.п.",
            Impact: "Недобор ~18 500 BYN оборота за 30 дней",
            Cause: "Задержка триггерной рассылки после вручения заказа в Битрикс24",
            Action: "Исправить вебхук статуса delivered → запуск промокода на следующую фотокнигу через 48 ч",
            Owner: "Елена Морозова · Продажи",
            DueDate: "2026-11-10",
            Priority: "Средний",
            Status: "Проверено",
            BeforeKpi: "38,9% повторных",
            AfterKpi: "41,2% (+2,3 п.п. подтверждено)",
            Department: "sales",
            CreatedAt: "2026-11-05T11:20:00+03:00")
    ];

    private static readonly List<DashboardAuditEntryDto> AuditStore =
    [
        new("AUD-03", "2026-11-11T09:15:00+03:00", "Кузнецов И.П. (Production)", "Создание решения DEC-104", "Брак на линии Premium 8,1%", "OK"),
        new("AUD-02", "2026-11-10T16:40:00+03:00", "Смирнов А.С. (Director)", "Назначение решения DEC-103", "Дефицит мощности 47-й недели", "OK"),
        new("AUD-01", "2026-11-10T12:05:00+03:00", "Морозова Е.А. (Sales)", "Повторная проверка DEC-101", "KPI улучшился: 38,9% -> 41,2%", "VERIFIED")
    ];

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dashboard", (HttpContext ctx) =>
        {
            var qs = ctx.Request.QueryString.Value ?? string.Empty;
            return Results.Redirect($"/dashboard/{qs}");
        });

        app.MapGet("/dashboard/", () => ServeDashboardHtml());

        // Основной эндпоинт /api/system/stats:
        // - При прямом переходе в браузере (Accept: text/html без параметра dashboard/format) открывает визуальный дашборд
        // - При API-вызове возвращает ролевой дашборд Трека 1 + системную телеметрию LLM
        app.MapGet("/api/system/stats", async (
            HttpContext httpContext,
            IFotoprintRepository repo,
            ILlmLoadBalancer loadBalancer,
            IRollbackManager rollbackManager,
            IGeminiClient gemini,
            ILangfuseClient langfuse,
            FotoprintConfig config,
            CancellationToken ct) =>
        {
            var accept = httpContext.Request.Headers.Accept.ToString();
            var hasDashboardParam = httpContext.Request.Query.ContainsKey("dashboard") ||
                                    httpContext.Request.Query.ContainsKey("format");

            if (!hasDashboardParam &&
                accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
                !accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                var qs = httpContext.Request.QueryString.Value ?? string.Empty;
                return Results.Redirect($"/dashboard/{qs}");
            }

            var profile = await ResolveDashboardEmployeeAsync(httpContext, repo, ct);
            var requestedScreen = (httpContext.Request.Query["dashboard"].FirstOrDefault() ?? "pulse").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(requestedScreen))
            {
                requestedScreen = "pulse";
            }

            // Проверка серверного RBAC (УП-05: руководитель подразделения не видит закрытые показатели чужого подразделения)
            if (!profile.AllowedDashboards.Contains(requestedScreen, StringComparer.OrdinalIgnoreCase))
            {
                var traceId = langfuse.GenerateTraceId();
                try
                {
                    await langfuse.LogSecurityAlertAsync(
                        traceId,
                        profile.TelegramId,
                        $"/api/system/stats?dashboard={requestedScreen}",
                        $"dashboard:{requestedScreen}",
                        $"Роль {profile.Role} ({profile.Department}) запросила закрытый дашборд '{requestedScreen}'.",
                        ct);
                }
                catch
                {
                    // Ignore telemetry transport errors
                }

                lock (SyncLock)
                {
                    AuditStore.Insert(0, new DashboardAuditEntryDto(
                        $"AUD-{AuditStore.Count + 1:00}",
                        DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-ddTHH:mm:sszzz"),
                        $"{profile.Name} ({profile.Role})",
                        $"Попытка открытия раздела '{requestedScreen}'",
                        "Отклонено сервером (УП-05)",
                        "403_FORBIDDEN"));
                }

                return Results.Json(new
                {
                    error = "Access Denied",
                    message = $"Раздел «{requestedScreen}» недоступен для роли {profile.RoleLabel} ({profile.Department}). Требуется уровень доступа соответствующего подразделения или Director.",
                    required_permission = $"dashboard:{requestedScreen}",
                    employee = profile,
                    trace_id = traceId
                }, statusCode: 403);
            }

            var period = int.TryParse(httpContext.Request.Query["period"].FirstOrDefault(), out var p) && p is 7 or 30 or 90 ? p : 30;
            var dateFrom = httpContext.Request.Query["dateFrom"].FirstOrDefault() ?? "2026-10-13";
            var dateTo = httpContext.Request.Query["dateTo"].FirstOrDefault() ?? "2026-11-11";
            var department = (httpContext.Request.Query["department"].FirstOrDefault() ?? "all").Trim().ToLowerInvariant();
            var product = (httpContext.Request.Query["product"].FirstOrDefault() ?? "all").Trim().ToLowerInvariant();

            if (string.CompareOrdinal(dateFrom, dateTo) > 0)
            {
                return Results.BadRequest(new
                {
                    error = "ValidationError",
                    message = "Дата начала периода (dateFrom) не может быть позже даты окончания (dateTo)."
                });
            }

            var payload = BuildDashboardPayload(profile, requestedScreen, period, dateFrom, dateTo, department, product);

            return Results.Ok(new
            {
                service = "Fotoprint.AiGateway.Api",
                currency = "BYN",
                timezone = "Europe/Minsk",
                updatedAt = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-ddTHH:mm:sszzz"),
                employee = profile,
                dashboard = requestedScreen,
                allowedDashboards = profile.AllowedDashboards,
                filters = new { period, dateFrom, dateTo, department, product },
                title = payload.Title,
                subtitle = payload.Subtitle,
                chartTitle = payload.ChartTitle,
                kpis = payload.Kpis,
                dailyPlans = payload.DailyPlans,
                series = payload.Series,
                forecastSeries = payload.ForecastSeries,
                systems = GetSystemsContour(),
                forecast = GetCapacityForecast(),
                decisions = GetDecisionsSnapshot(),
                auditLog = GetAuditSnapshot(),
                llm_config = new
                {
                    provider = config.Llm.Provider,
                    chat_model = config.Llm.Models.ChatModel,
                    fast_model = config.Llm.Models.FastModel,
                    temperature = config.Llm.Parameters.Temperature,
                    max_tokens = config.Llm.Parameters.MaxTokens,
                    rpm_limit = config.Llm.RateLimits.Rpm,
                    tpm_limit = config.Llm.RateLimits.Tpm,
                    proxy_enabled = config.Llm.Proxy.Enabled
                },
                gemini_rpm_tracker = gemini.GetStats(),
                load_balancer = loadBalancer.GetStats(),
                rollback_system = rollbackManager.GetStats(),
                timestamp = DateTime.UtcNow
            });
        });

        app.MapGet("/api/decisions", () => Results.Ok(new
        {
            decisions = GetDecisionsSnapshot(),
            auditLog = GetAuditSnapshot()
        }));

        app.MapPost("/api/decisions", async (
            HttpContext httpContext,
            [FromBody] CreateDecisionRequest req,
            IFotoprintRepository repo,
            CancellationToken ct) =>
        {
            if (req is null ||
                string.IsNullOrWhiteSpace(req.Problem) ||
                string.IsNullOrWhiteSpace(req.Action) ||
                string.IsNullOrWhiteSpace(req.DueDate))
            {
                return Results.BadRequest(new
                {
                    error = "ValidationError",
                    message = "Поля problem, action и dueDate обязательны для сохранения решения в реестр."
                });
            }

            var profile = await ResolveDashboardEmployeeAsync(httpContext, repo, ct);
            var idemKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(idemKey) && IdempotencyStore.TryGetValue(idemKey, out var existing))
            {
                return Results.Ok(new
                {
                    idempotent_replay = true,
                    decision = existing,
                    decisions = GetDecisionsSnapshot(),
                    auditLog = GetAuditSnapshot()
                });
            }

            DashboardDecisionDto created;
            lock (SyncLock)
            {
                var nextNum = 105 + DecisionStore.Count;
                created = new DashboardDecisionDto(
                    Id: $"DEC-{nextNum}",
                    Problem: req.Problem.Trim(),
                    Impact: string.IsNullOrWhiteSpace(req.Impact) ? "Влияние зафиксировано в карточке KPI" : req.Impact.Trim(),
                    Cause: string.IsNullOrWhiteSpace(req.Cause) ? "Требуется верификация первопричины" : req.Cause.Trim(),
                    Action: req.Action.Trim(),
                    Owner: string.IsNullOrWhiteSpace(req.Owner) ? $"{profile.Name} · {profile.Department}" : req.Owner.Trim(),
                    DueDate: req.DueDate.Trim(),
                    Priority: string.IsNullOrWhiteSpace(req.Priority) ? "Высокий" : req.Priority.Trim(),
                    Status: "Назначено",
                    BeforeKpi: string.IsNullOrWhiteSpace(req.BeforeKpi) ? "Зафиксирован срез KPI" : req.BeforeKpi.Trim(),
                    AfterKpi: $"Повторная проверка {req.DueDate.Trim()}",
                    Department: string.IsNullOrWhiteSpace(req.Department) ? profile.Department : req.Department.Trim(),
                    CreatedAt: DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-ddTHH:mm:sszzz"));

                DecisionStore.Insert(0, created);
                AuditStore.Insert(0, new DashboardAuditEntryDto(
                    $"AUD-{AuditStore.Count + 1:00}",
                    created.CreatedAt,
                    $"{profile.Name} ({profile.Role})",
                    $"Создано решение {created.Id}",
                    created.Problem,
                    "CREATED"));

                if (!string.IsNullOrWhiteSpace(idemKey))
                {
                    IdempotencyStore[idemKey] = created;
                }
            }

            return Results.Created($"/api/decisions/{created.Id}", new
            {
                idempotent_replay = false,
                decision = created,
                decisions = GetDecisionsSnapshot(),
                auditLog = GetAuditSnapshot()
            });
        });

        return app;
    }

    private static IResult ServeDashboardHtml()
    {
        var roots = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var root in roots)
        {
            var dir = root;
            for (var i = 0; i <= 6 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(dir, "infra", "nginx", "html", "dashboard", "index.html");
                if (File.Exists(candidate))
                {
                    return Results.File(candidate, "text/html; charset=utf-8");
                }
                dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
            }
        }

        return Results.NotFound("Dashboard UI (infra/nginx/html/dashboard/index.html) not found.");
    }

    private static async Task<DashboardEmployeeDto> ResolveDashboardEmployeeAsync(
        HttpContext httpContext,
        IFotoprintRepository repo,
        CancellationToken ct)
    {
        long? tgId = null;
        if (long.TryParse(httpContext.Request.Query["telegram_id"].FirstOrDefault(), out var qTg))
            tgId = qTg;
        else if (long.TryParse(httpContext.Request.Headers["X-Telegram-Id"].FirstOrDefault(), out var hTg))
            tgId = hTg;
        else if (long.TryParse(httpContext.Request.Query["master_id"].FirstOrDefault(), out var qMaster))
            tgId = qMaster;
        else if (long.TryParse(httpContext.Request.Headers["X-Master-Id"].FirstOrDefault(), out var hMaster))
            tgId = hMaster;

        var roleParam = httpContext.Request.Query["role"].FirstOrDefault() ??
                        httpContext.Request.Headers["X-Demo-Role"].FirstOrDefault();

        if (!tgId.HasValue && !string.IsNullOrWhiteSpace(roleParam))
        {
            tgId = roleParam.Trim().ToLowerInvariant() switch
            {
                "production" => 100002,
                "sales" => 100003,
                "accounting" => 100004,
                _ => 100001
            };
        }

        var effectiveId = tgId ?? 100001L;

        try
        {
            var sec = await repo.GetEmployeeSecurityContextAsync(effectiveId, ct);
            if (sec != null)
            {
                var primaryRole = sec.Roles.FirstOrDefault() ?? "Director";
                return BuildEmployeeDto(sec.MasterId, sec.TelegramId, sec.FullName, primaryRole, sec.Department, sec.Position);
            }
        }
        catch
        {
            // Fallback to deterministic seed profiles when PostgreSQL is offline
        }

        return effectiveId switch
        {
            100002 => BuildEmployeeDto(100002, 100002, "Иван Кузнецов", "Production", "Производство", "Начальник производства"),
            100003 => BuildEmployeeDto(100003, 100003, "Елена Морозова", "Sales", "Продажи", "Руководитель отдела продаж"),
            100004 => BuildEmployeeDto(100004, 100004, "Ольга Соколова", "Accounting", "Бухгалтерия", "Главный бухгалтер"),
            _ => BuildEmployeeDto(100001, 100001, "Алексей Смирнов", "Director", "Руководство", "Генеральный директор")
        };
    }

    private static DashboardEmployeeDto BuildEmployeeDto(
        long masterId,
        long telegramId,
        string name,
        string role,
        string department,
        string position)
    {
        var normalized = role.Trim();
        string[] allowed = normalized.ToLowerInvariant() switch
        {
            "production" => ["pulse", "production", "decisions"],
            "sales" => ["pulse", "sales", "decisions"],
            "accounting" => ["pulse", "finance", "decisions"],
            _ => ["pulse", "sales", "production", "finance", "decisions"]
        };

        return new DashboardEmployeeDto(
            MasterId: masterId,
            TelegramId: telegramId,
            Name: name,
            Role: normalized,
            RoleLabel: position,
            Department: department,
            Email: normalized.ToLowerInvariant() switch
            {
                "production" => "i.kuznetsov@fotoprint.by",
                "sales" => "e.morozova@fotoprint.by",
                "accounting" => "o.sokolova@fotoprint.by",
                _ => "a.smirnov@fotoprint.by"
            },
            AllowedDashboards: allowed);
    }

    private static DashboardScreenPayload BuildDashboardPayload(
        DashboardEmployeeDto emp,
        string screen,
        int period,
        string dateFrom,
        string dateTo,
        string department,
        string product)
    {
        double periodFactor = period == 7 ? 0.24 : period == 90 ? 2.85 : 1.0;
        double deptFactor = department switch
        {
            "production" => 0.92,
            "sales" => 1.06,
            "accounting" => 0.98,
            _ => 1.0
        };
        double prodFactor = product switch
        {
            "books" => 1.08,
            "prints" => 0.89,
            "calendars" => 0.94,
            _ => 1.0
        };

        var scaleVolume = periodFactor * deptFactor * prodFactor;
        var pctShift = (prodFactor - 1.0) * 0.3 + (deptFactor - 1.0) * 0.2;
        var nowIso = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-ddTHH:mm:sszzz");

        return screen switch
        {
            "sales" => new DashboardScreenPayload(
                Title: "Продажи и клиент (CRM / Битрикс24)",
                Subtitle: "Воронка, конверсия, средний чек, удержание клиентов и SLA омниканала",
                ChartTitle: "Конверсия и поток заказов по неделям",
                Kpis:
                [
                    ScalePctKpi("conv", "Конверсия воронки", 23.4, 21.0, "%", "+2,4 п.п.", "good", "Битрикс24", "Оплаченные заказы / Лиды", pctShift, nowIso),
                    ScaleVolKpi("avg_check", "Средний чек", 164, 150, "BYN", "+9,3%", "good", "CRM / ERP", "Оборот / Число заказов", deptFactor * prodFactor, nowIso),
                    ScaleVolKpi("new_cust", "Новые клиенты", 1420, 1350, "кл.", "+5,1%", "good", "CRM", "Клиенты с 1-м заказом в периоде", scaleVolume, nowIso),
                    ScalePctKpi("repeat", "Повторные покупки", 41.2, 44.0, "%", "−2,8 п.п.", "warning", "CRM", "Клиенты с 2+ заказами / Активные", pctShift, nowIso),
                    ScaleVolKpi("active_cust", "Активные клиенты", 6840, 6500, "кл.", "+5,2%", "good", "CRM", "Клиенты с заказом за N дней", scaleVolume, nowIso)
                ],
                DailyPlans:
                [
                    new("dp_leads", "Квалифицированные лиды", "на 14:30", Math.Round(186 * deptFactor), 210, "лид.", "5 420", "5 200 лидов", "#568CDE"),
                    new("dp_orders", "Оплаченные заказы B2C/B2B", "на 14:30", Math.Round(94 * prodFactor), 105, "зак.", "2 890", "2 750 зак.", "#DD5757"),
                    new("dp_ret", "Удержание B2B сегмента", "на 14:30", 91, 95, "%", "91,0%", "План 95,0%", "#8C71D8")
                ],
                Series: [52, 58, 61, 66, 72, 78, Math.Round(84 * prodFactor, 1)],
                ForecastSeries: [Math.Round(84 * prodFactor, 1), Math.Round(88 * prodFactor, 1), Math.Round(92 * prodFactor, 1)]),

            "production" => new DashboardScreenPayload(
                Title: "Производство и качество (ERP)",
                Subtitle: "План/факт выпуска, очередь цехов, Lead Time, простои машин и разбор брака",
                ChartTitle: "Выпуск продукции и нагрузка цехов по сменам",
                Kpis:
                [
                    ScalePctKpi("cap", "Загрузка мощности", 86.5, 85.0, "%", "+1,5 п.п.", "good", "ERP", "Плановые часы / Доступная мощность", pctShift, nowIso),
                    ScalePctKpi("lead", "Lead Time заказа", 2.8, 2.5, "дн.", "+0,3 дн.", "warning", "ERP", "AVG(released_at − created_at)", pctShift, nowIso),
                    ScaleVolKpi("downtime", "Неплановые простои", 19.5, 12.0, "ч", "+7,5 ч", "critical", "ERP / Цех", "SUM(unplanned_downtime_hours)", periodFactor, nowIso),
                    ScalePctKpi("defect_prod", "Брак на линии Premium", 8.1, 5.0, "%", "+3,1 п.п.", "critical", "ERP", "Брак тиражей / Выпущенные тиражи", pctShift, nowIso),
                    ScaleVolKpi("queue", "Очередь модерации", 148, 120, "зак.", "+28 зак.", "warning", "ERP", "Заказы в статусе moderation_pending", prodFactor, nowIso)
                ],
                DailyPlans:
                [
                    new("dp_indigo", "Выработка HP Indigo", "на 14:30", Math.Round(4120 * prodFactor), 4500, "отт.", "118 тыс.", "125 тыс. отт.", "#DD5757"),
                    new("dp_bind", "Переплётный участок Kolbus", "на 14:30", Math.Round(265 * prodFactor), 320, "кн.", "7 640 кн.", "8 200 кн.", "#E8996C"),
                    new("dp_qc", "Контроль ОТК без перепечатки", "на 14:30", 91.9, 95.0, "%", "91,9%", "Норма 95,0%", "#568CDE")
                ],
                Series: [78, 82, 79, 88, 91, 85, Math.Round(87 * prodFactor, 1)],
                ForecastSeries: [Math.Round(87 * prodFactor, 1), Math.Round(94 * prodFactor, 1), Math.Round(98 * prodFactor, 1)]),

            "finance" => new DashboardScreenPayload(
                Title: "Финансы и люди (1С + HR)",
                Subtitle: "Статусы оплат и затрат, БДР/БДДС индекс, штатная обеспеченность и смены",
                ChartTitle: "Индекс исполнения БДР и обеспеченности сменами (%)",
                Kpis:
                [
                    ScalePctKpi("bdr", "Исполнение БДР (индекс)", 104.2, 100.0, "%", "+4,2 п.п.", "good", "1С:УНФ", "Факт маржинального дохода / План * 100", pctShift, nowIso),
                    ScalePctKpi("cost_idx", "Индекс себестоимости", 102.8, 100.0, "%", "+2,8 п.п.", "warning", "1С", "Факт затрат на бумагу и химию / План", pctShift, nowIso),
                    ScalePctKpi("staff", "Обеспеченность штатом", 93.5, 96.0, "%", "−2,5 п.п.", "warning", "HR витрина", "Факт на смене / Плановый штат", pctShift, nowIso),
                    ScaleVolKpi("vac", "Открытые вакансии", 6, 2, "шт.", "+4 вак.", "critical", "HR", "Активные заявки на подбор в цех и саппорт", 1.0, nowIso),
                    ScalePctKpi("paid_ratio", "Доля подтверждённых оплат", 98.2, 98.0, "%", "+0,2 п.п.", "good", "1С", "Оплаченные счета / Выставленные счета", pctShift, nowIso)
                ],
                DailyPlans:
                [
                    new("dp_pay", "Проведено оплат 1С", "на 14:30", 96, 100, "%", "98,2%", "План 98,0%", "#689452"),
                    new("dp_shift", "Выход сменного персонала", "на 14:30", 58, 62, "чел.", "93,5%", "План 96,0%", "#8C71D8"),
                    new("dp_ot", "Лимит сверхурочных часов", "на 14:30", 24, 30, "ч", "142 ч", "Лимит 180 ч", "#568CDE")
                ],
                Series: [92, 95, 94, 98, 101, 103, 104],
                ForecastSeries: [104, 103, 105]),

            "decisions" => new DashboardScreenPayload(
                Title: "Реестр управленческих решений",
                Subtitle: "Назначение владельца, контроль сроков, журнал аудита и повторная проверка KPI до/после",
                ChartTitle: "Темп закрытия и верификации решений по неделям",
                Kpis:
                [
                    ScaleVolKpi("dec_open", "Активные решения", GetDecisionsSnapshot().Count, 5, "шт.", "в работе", "good", "Реестр", "COUNT(status IN assigned, in_progress)", 1.0, nowIso),
                    ScaleVolKpi("dec_over", "Просроченные решения", 1, 0, "шт.", "требует контроля", "critical", "Реестр", "due_date < NOW() AND status != verified", 1.0, nowIso),
                    ScaleVolKpi("dec_ver", "Проверено с эффектом", 14, 12, "шт.", "+16%", "good", "Реестр", "Решения со статусом verified и улучшением KPI", 1.0, nowIso),
                    ScalePctKpi("dq_score", "Индекс качества данных", 97.4, 98.0, "%", "−0,6 п.п.", "warning", "Контур 5 систем", "100% − доля пропусков и дублей", 0.0, nowIso)
                ],
                DailyPlans:
                [
                    new("dp_dec1", "Назначено владельцев по алертам", "на 14:30", 5, 6, "реш.", "83,3%", "Цель 100%", "#DD5757"),
                    new("dp_dec2", "Повторная проверка эффекта KPI", "на 14:30", 4, 4, "реш.", "100%", "План 4 реш.", "#689452"),
                    new("dp_dec3", "Сверка 1С и ERP без расхождений", "на 14:30", 97.4, 99.0, "%", "97,4%", "Норма 99,0%", "#568CDE")
                ],
                Series: [60, 68, 72, 75, 82, 88, 91],
                ForecastSeries: [91, 95, 97]),

            _ => new DashboardScreenPayload(
                Title: "Пульс компании",
                Subtitle: "Единый контур 5 систем: от сигнала отклонения до решения и повторной проверки",
                ChartTitle: "Динамика оборота и прогноз выполнения плана (BYN)",
                Kpis:
                [
                    ScaleVolKpi("turnover", "Оборот", 1284600, 1209000, "BYN", "+6,2%", "good", "1С / ERP", "SUM(order.amount) по оплаченным заказам", scaleVolume, nowIso),
                    ScalePctKpi("margin", "Валовая маржа", 38.4, 36.6, "%", "+1,8 п.п.", "good", "1С", "(Оборот − Себестоимость) / Оборот", pctShift, nowIso),
                    ScalePctKpi("ontime", "Заказы в срок", 91.8, 94.2, "%", "−2,4 п.п.", "warning", "ERP", "delivered_at <= promised_at / completed", pctShift, nowIso),
                    ScalePctKpi("defect", "Брак / перепечатка", 8.1, 5.0, "%", "+3,1 п.п.", "critical", "ERP", "orders(defective=true) / released_orders", pctShift, nowIso),
                    ScalePctKpi("sla", "SLA поддержки", 95.4, 96.0, "%", "−0,6 п.п.", "good", "Омниканал", "closed_in_sla / total_closed", pctShift, nowIso)
                ],
                DailyPlans:
                [
                    new("dp_rev", "Выручка за смену", "на 14:30", Math.Round(42800 * deptFactor * prodFactor), 48500, "BYN", "1,28 млн", "1,21 млн BYN", "#DD5757"),
                    new("dp_print", "Выпуск фотокниг", "на 14:30", Math.Round(312 * prodFactor), 360, "шт.", "8 420 шт.", "8 900 шт.", "#568CDE"),
                    new("dp_sla", "Закрыто обращений SLA", "на 14:30", 118, 124, "обр.", "95,4%", "План 96,0%", "#689452")
                ],
                Series: [68, 74, 71, 83, 89, 94, Math.Round(102 * prodFactor, 1)],
                ForecastSeries: [Math.Round(102 * prodFactor, 1), Math.Round(108 * prodFactor, 1), Math.Round(115 * prodFactor, 1)])
        };
    }

    private static DashboardKpiDto ScaleVolKpi(
        string id, string name, double baseVal, double planVal, string unit,
        string delta, string status, string source, string formula, double scale, string updatedAt)
    {
        var v = Math.Round(baseVal * scale);
        var p = Math.Round(planVal * scale);
        return new DashboardKpiDto(id, name, v, p, unit, delta, status, source, formula, updatedAt);
    }

    private static DashboardKpiDto ScalePctKpi(
        string id, string name, double baseVal, double planVal, string unit,
        string delta, string status, string source, string formula, double shift, string updatedAt)
    {
        var v = Math.Round(baseVal * (1.0 + shift * 0.25), 1);
        return new DashboardKpiDto(id, name, v, planVal, unit, delta, status, source, formula, updatedAt);
    }

    private static List<object> GetSystemsContour() =>
    [
        new { id = "crm", name = "CRM / Битрикс24", dept = "sales", detail = "Клиенты · сделки · задачи · коммуникации", freshness = "2 мин назад", status = "ok", metric = "Конверсия 23,4% · 6 840 активных кл." },
        new { id = "support", name = "Поддержка / Омниканал", dept = "sales", detail = "Обращения · темы · SLA · качество ответа", freshness = "4 мин назад", status = "ok", metric = "SLA 95,4% · ср. ответ 6 мин" },
        new { id = "erp", name = "ERP / Производство", dept = "production", detail = "Заказ · модерация · очередь · выпуск · доставка", freshness = "1 мин назад", status = "warning", metric = "Загрузка 86,5% · брак Premium 8,1%" },
        new { id = "onec", name = "1С / Финансы", dept = "accounting", detail = "Статусы оплат и затрат · индекс БДР", freshness = "5 мин назад", status = "ok", metric = "Исполнение БДР 104,2% · 2 расхождения в сверке" },
        new { id = "hr", name = "HR / Персонал", dept = "accounting", detail = "Штат · смены · вакансии · нагрузка", freshness = "18 мин назад", status = "warning", metric = "Обеспеченность 93,5% · 6 вакансий" }
    ];

    private static List<object> GetCapacityForecast() =>
    [
        new { week = "45 неделя (11–17 ноя)", demand = "4 120 ч", capacity = "4 480 ч", balance = "+360 ч запаса", deficit = false, action = "В штатном режиме" },
        new { week = "46 неделя (18–24 ноя)", demand = "4 590 ч", capacity = "4 680 ч", balance = "+90 ч запаса", deficit = false, action = "Подготовить бумагу под пик" },
        new { week = "47 неделя (25 ноя – 1 дек)", demand = "5 320 ч", capacity = "4 680 ч", balance = "−640 ч дефицит", deficit = true, action = "Нужна доп. смена + сверхурочные" },
        new { week = "48 неделя (2–8 дек)", demand = "4 850 ч", capacity = "5 100 ч", balance = "+250 ч запаса", deficit = false, action = "После ввода резервной смены" }
    ];

    private static List<DashboardDecisionDto> GetDecisionsSnapshot()
    {
        lock (SyncLock)
        {
            return [.. DecisionStore];
        }
    }

    private static List<DashboardAuditEntryDto> GetAuditSnapshot()
    {
        lock (SyncLock)
        {
            return [.. AuditStore];
        }
    }
}

public record DashboardEmployeeDto(
    long MasterId,
    long TelegramId,
    string Name,
    string Role,
    string RoleLabel,
    string Department,
    string Email,
    string[] AllowedDashboards);

public record DashboardKpiDto(
    string Id,
    string Name,
    double Base,
    double Plan,
    string Unit,
    string Delta,
    string Status,
    string Source,
    string Formula,
    string UpdatedAt);

public record DashboardDailyPlanDto(
    string Id,
    string Title,
    string Time,
    double Current,
    double Plan,
    string Unit,
    string PeriodCurrent,
    string PeriodPlan,
    string Color);

public record DashboardScreenPayload(
    string Title,
    string Subtitle,
    string ChartTitle,
    List<DashboardKpiDto> Kpis,
    List<DashboardDailyPlanDto> DailyPlans,
    List<double> Series,
    List<double> ForecastSeries);

public record DashboardDecisionDto(
    string Id,
    string Problem,
    string Impact,
    string Cause,
    string Action,
    string Owner,
    string DueDate,
    string Priority,
    string Status,
    string BeforeKpi,
    string AfterKpi,
    string Department,
    string CreatedAt);

public record DashboardAuditEntryDto(
    string Id,
    string Timestamp,
    string Actor,
    string Action,
    string Details,
    string Outcome);

public record CreateDecisionRequest(
    string Problem,
    string? Impact,
    string? Cause,
    string Action,
    string? Owner,
    string DueDate,
    string? Priority,
    string? Department,
    string? BeforeKpi);
