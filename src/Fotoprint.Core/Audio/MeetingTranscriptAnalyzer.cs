using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Llm;
using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Audio;

public interface IMeetingTranscriptAnalyzer
{
    Task<MeetingAnalysisResult> AnalyzeTranscriptAsync(
        string transcriptText,
        string? defaultTitle = null,
        string? traceId = null,
        CancellationToken ct = default);
}

public class MeetingTranscriptAnalyzer : IMeetingTranscriptAnalyzer
{
    public const string ClarificationRequired = "требует уточнения";

    private static readonly HashSet<string> MissingValuePlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "",
        "-",
        "—",
        "?",
        "нет",
        "не указан",
        "не указано",
        "не указана",
        "неизвестно",
        "неизвестен",
        "не определено",
        "не определен",
        "без срока",
        "требует уточнения",
        "уточнить",
        "null",
        "none",
        "unknown",
        "unspecified",
        "n/a",
        "tbd"
    };

    private static readonly HashSet<string> StopCapitalizedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Нужно", "Необходимо", "Надо", "Сделать", "Подготовить", "Проверить", "Отправить",
        "Согласовать", "Настроить", "Исправить", "Заказать", "Связаться", "Выгрузить",
        "Обновить", "Провести", "Назначить", "Поручение", "Поручить", "Задача", "Срок",
        "Дедлайн", "Сегодня", "Завтра", "Послезавтра", "Также", "Кроме", "Коллеги",
        "Давайте", "Решили", "Решено", "Важно", "Протокол", "Встреча", "Планерка",
        "Отдел", "Типография", "Производство", "Бухгалтерия", "Клиент", "Заказ"
    };

    private static readonly Regex ExplicitResponsibleRegex = new(
        @"(?:ответственн(?:ый|ая|ым)|поручить|исполнитель)\s*[-—:]?\s*(?<name>[А-ЯЁ][а-яё]+(?:\s+[А-ЯЁ][а-яё]+)?)",
        RegexOptions.Compiled);

    private static readonly Regex SubjectObligationRegex = new(
        @"(?:^|[,;]\s*)(?<name>[А-ЯЁ][а-яё]+(?:\s+[А-ЯЁ][а-яё]+)?)\s+(?:должен|должна|нужно|необходимо|обязан|обязана|подготовит|проверит|сделает|отправит|согласует|настроит|выгрузит|закажет)\b",
        RegexOptions.Compiled);

    private static readonly Regex ExplicitDateRegex = new(
        @"\b(?<date>\d{1,2}\.\d{1,2}\.\d{2,4}|\d{4}-\d{2}-\d{2})\b",
        RegexOptions.Compiled);

    private static readonly Regex ExplicitRelativeDeadlineRegex = new(
        @"\b(?<dl>сегодня|завтра|послезавтра|до\s+конца\s+(?:дня|недели|месяца)|до\s+\d{1,2}\.\d{1,2}(?:\.\d{2,4})?|к\s+(?:понедельнику|вторнику|среде|четвергу|пятнице|субботе|воскресенью)|до\s+(?:понедельника|вторника|среды|четверга|пятницы|субботы|воскресенья)|до\s+\d{1,2}\s+(?:января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IGeminiClient? _geminiClient;
    private readonly FotoprintConfig _config;
    private readonly ILangfuseClient? _langfuse;
    private readonly ILogger<MeetingTranscriptAnalyzer>? _logger;

    public MeetingTranscriptAnalyzer(
        IGeminiClient geminiClient,
        FotoprintConfig config,
        ILangfuseClient? langfuse = null,
        ILogger<MeetingTranscriptAnalyzer>? logger = null)
    {
        _geminiClient = geminiClient;
        _config = config ?? new FotoprintConfig();
        _langfuse = langfuse;
        _logger = logger;
    }

    public async Task<MeetingAnalysisResult> AnalyzeTranscriptAsync(
        string transcriptText,
        string? defaultTitle = null,
        string? traceId = null,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        var fallbackTitle = !string.IsNullOrWhiteSpace(defaultTitle)
            ? defaultTitle.Trim()
            : $"Протокол совещания от {DateTime.UtcNow:dd.MM.yyyy}";

        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            return BuildHeuristicAnalysis(string.Empty, fallbackTitle);
        }

        if (_geminiClient != null)
        {
            try
            {
                const string systemInstruction = """
                    Ты — ИИ-секретарь типографии «Фотопринт». Твоя задача — составить точный структурированный протокол совещания (Minutes of Meeting) по сырой расшифровке аудиозаписи.
                    СТРОГОЕ ПРАВИЛО БЕЗ ГАЛЛЮЦИНАЦИЙ (NO HALLUCINATIONS):
                    1. Запрещено придумывать факты, фамилии ответственных или даты дедлайнов, если они явно не прозвучали в тексте.
                    2. Если для поручения (action_item) в тексте НЕ назван конкретный ответственный — пиши в поле "responsible" строго: "требует уточнения".
                    3. Если для поручения в тексте НЕ назван конкретный срок/дедлайн — пиши в поле "deadline_text" строго: "требует уточнения", а "parsed_deadline": null.
                    4. Все неясные моменты и поручения без срока или ответственного обязательно перечисли в списке "requires_clarification".
                    5. Верни ответ СТРОГО в формате валидного JSON без markdown-обертки.
                    """;

                var prompt = $$"""
                    Проанализируй следующую стенограмму встречи (Дата встречи: {{DateTime.UtcNow:yyyy-MM-dd}}, Название по умолчанию: "{{fallbackTitle}}"):

                    --- СТЕНОГРАММА ---
                    {{transcriptText}}
                    --- КОНЕЦ СТЕНОГРАММЫ ---

                    Верни строго JSON следующей структуры:
                    {
                      "title": "Название встречи",
                      "summary": "Краткое резюме встречи (2-4 предложения)",
                      "decisions": ["Принятое решение 1", "Принятое решение 2"],
                      "proposed": ["Предложение или инициатива 1 (ещё не утверждена)"],
                      "discussed": ["Обсуждавшийся вопрос 1"],
                      "risks": ["Озвученный риск или проблема 1"],
                      "requires_clarification": ["Вопрос, требующий уточнения (например, не указан срок или ответственный)"],
                      "action_items": [
                        {
                          "title": "Суть поручения",
                          "description": "Подробности поручения",
                          "responsible": "Имя/Фамилия ИЛИ 'требует уточнения'",
                          "deadline_text": "Срок словами из текста ИЛИ 'требует уточнения'",
                          "parsed_deadline": "YYYY-MM-DD ИЛИ null",
                          "source_quote": "Цитата из стенограммы"
                        }
                      ]
                    }
                    """;

                var llmResponse = await _geminiClient.GenerateContentAsync(
                    prompt: prompt,
                    systemInstruction: systemInstruction,
                    temperature: 0.1,
                    maxOutputTokens: 2048,
                    traceId: traceId,
                    ct: ct);

                if (llmResponse.IsSuccess && !string.IsNullOrWhiteSpace(llmResponse.Text))
                {
                    var parsed = ParseAnalysisJson(llmResponse.Text, fallbackTitle);

                    // If LLM missed action items that heuristic clearly detects, merge them
                    if (parsed.ActionItems.Count == 0)
                    {
                        var heuristic = BuildHeuristicAnalysis(transcriptText, fallbackTitle);
                        if (heuristic.ActionItems.Count > 0)
                        {
                            var mergedClarifications = parsed.RequiresClarification
                                .Union(heuristic.RequiresClarification)
                                .ToList();
                            parsed = parsed with
                            {
                                ActionItems = heuristic.ActionItems,
                                RequiresClarification = mergedClarifications
                            };
                        }
                    }

                    await LogAnalysisSpanAsync(traceId, startTime, transcriptText.Length, "gemini-llm", parsed, ct);
                    return parsed;
                }

                _logger?.LogWarning(
                    "[MeetingTranscriptAnalyzer] Gemini returned unsuccessful analysis ({Error}). Using deterministic heuristic analyzer.",
                    llmResponse.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[MeetingTranscriptAnalyzer] Gemini analysis failed; falling back to heuristic analyzer.");
            }
        }

        var fallbackResult = BuildHeuristicAnalysis(transcriptText, fallbackTitle);
        await LogAnalysisSpanAsync(traceId, startTime, transcriptText.Length, "heuristic", fallbackResult, ct);
        return fallbackResult;
    }

    public static ExtractedActionItem NormalizeActionItem(ExtractedActionItem item)
    {
        if (item == null)
        {
            return new ExtractedActionItem(
                Title: "Поручение (требует уточнения)",
                Description: null,
                Responsible: ClarificationRequired,
                DeadlineText: ClarificationRequired,
                ParsedDeadline: null,
                SourceQuote: null);
        }

        var cleanTitle = string.IsNullOrWhiteSpace(item.Title)
            ? "Поручение (требует уточнения)"
            : item.Title.Trim();

        var rawResp = item.Responsible?.Trim() ?? string.Empty;
        var normalizedResp = IsMissingPlaceholder(rawResp)
            ? ClarificationRequired
            : rawResp;

        var rawDeadline = item.DeadlineText?.Trim() ?? string.Empty;
        DateTime? parsedDeadline = item.ParsedDeadline;
        string normalizedDeadlineText;

        if (IsMissingPlaceholder(rawDeadline))
        {
            normalizedDeadlineText = ClarificationRequired;
            parsedDeadline = null;
        }
        else
        {
            normalizedDeadlineText = rawDeadline;
            if (!parsedDeadline.HasValue && TryParseDeadlineString(rawDeadline, out var parsedFromText))
            {
                parsedDeadline = parsedFromText;
            }
        }

        return new ExtractedActionItem(
            Title: cleanTitle,
            Description: string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
            Responsible: normalizedResp,
            DeadlineText: normalizedDeadlineText,
            ParsedDeadline: parsedDeadline,
            SourceQuote: string.IsNullOrWhiteSpace(item.SourceQuote) ? null : item.SourceQuote.Trim());
    }

    public static MeetingAnalysisResult ParseAnalysisJson(string jsonText, string? defaultTitle = null)
    {
        var fallbackTitle = !string.IsNullOrWhiteSpace(defaultTitle)
            ? defaultTitle.Trim()
            : "Протокол рабочей встречи";

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return BuildHeuristicAnalysis(string.Empty, fallbackTitle);
        }

        var extractedJson = ExtractJsonBlock(jsonText);
        if (string.IsNullOrWhiteSpace(extractedJson))
        {
            return BuildHeuristicAnalysis(jsonText, fallbackTitle);
        }

        try
        {
            using var doc = JsonDocument.Parse(extractedJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return BuildHeuristicAnalysis(jsonText, fallbackTitle);
            }

            var title = ReadString(root, "title") ?? fallbackTitle;
            if (string.IsNullOrWhiteSpace(title)) title = fallbackTitle;

            var summary = ReadString(root, "summary")
                          ?? "Протокол сформирован автоматически на основе аудиозаписи встречи.";

            var decisions = ReadStringList(root, "decisions");
            var proposed = ReadStringList(root, "proposed", "proposals");
            var discussed = ReadStringList(root, "discussed", "topics");
            var risks = ReadStringList(root, "risks");
            var requiresClarification = ReadStringList(root, "requires_clarification", "requiresClarification");

            var actionItems = new List<ExtractedActionItem>();
            if (TryGetProp(root, out var itemsArr, "action_items", "actionItems", "tasks") &&
                itemsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in itemsArr.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;

                    var itemTitle = ReadString(el, "title", "task", "text") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(itemTitle)) continue;

                    var desc = ReadString(el, "description", "details");
                    var resp = ReadString(el, "responsible", "assignee", "owner") ?? string.Empty;
                    var dlText = ReadString(el, "deadline_text", "deadlineText", "deadline") ?? string.Empty;
                    var parsedDlRaw = ReadString(el, "parsed_deadline", "parsedDeadline", "deadline_iso");
                    var quote = ReadString(el, "source_quote", "sourceQuote", "quote");

                    DateTime? parsedDate = null;
                    if (!string.IsNullOrWhiteSpace(parsedDlRaw) &&
                        !IsMissingPlaceholder(parsedDlRaw) &&
                        TryParseDeadlineString(parsedDlRaw, out var dt))
                    {
                        parsedDate = dt;
                    }

                    var normalized = NormalizeActionItem(new ExtractedActionItem(
                        Title: itemTitle,
                        Description: desc,
                        Responsible: resp,
                        DeadlineText: dlText,
                        ParsedDeadline: parsedDate,
                        SourceQuote: quote));

                    actionItems.Add(normalized);
                }
            }

            EnsureClarificationsForIncompleteItems(actionItems, requiresClarification);

            return new MeetingAnalysisResult(
                Title: title,
                Summary: summary,
                Decisions: decisions,
                Discussed: discussed,
                Risks: risks,
                RequiresClarification: requiresClarification,
                ActionItems: actionItems,
                Proposed: proposed);
        }
        catch (JsonException)
        {
            return BuildHeuristicAnalysis(jsonText, fallbackTitle);
        }
    }

    public static MeetingAnalysisResult BuildHeuristicAnalysis(string transcriptText, string? defaultTitle = null)
    {
        var title = !string.IsNullOrWhiteSpace(defaultTitle)
            ? defaultTitle.Trim()
            : "Протокол рабочей встречи";

        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            return new MeetingAnalysisResult(
                Title: title,
                Summary: "Стенограмма встречи пуста.",
                Decisions: new List<string>(),
                Discussed: new List<string>(),
                Risks: new List<string>(),
                RequiresClarification: new List<string> { "Стенограмма не содержит текста для анализа." },
                ActionItems: new List<ExtractedActionItem>(),
                Proposed: new List<string>());
        }

        // Remove timestamp prefixes like [00:00:00.000 --> 00:00:03.000]
        var cleanedTranscript = Regex.Replace(
            transcriptText,
            @"\[\d{1,2}:\d{2}(?::\d{2})?[\.,]\d{1,3}\s*-->\s*\d{1,2}:\d{2}(?::\d{2})?[\.,]\d{1,3}\]\s*",
            string.Empty);

        var rawSentences = Regex.Split(cleanedTranscript, @"(?<=[\.!\?\n;])\s+")
            .Select(s => s.Trim())
            .Where(s => s.Length > 3)
            .ToList();

        var decisions = new List<string>();
        var proposed = new List<string>();
        var discussed = new List<string>();
        var risks = new List<string>();
        var requiresClarification = new List<string>();
        var actionItems = new List<ExtractedActionItem>();

        foreach (var sentence in rawSentences)
        {
            var lower = sentence.ToLowerInvariant();
            var cleanSentence = sentence.TrimEnd('.', ';', ' ');

            bool isDecision = ContainsAny(lower, "решили", "решено", "утвердили", "утверждено", "согласовали", "приняли решение", "договорились", "фиксируем");
            bool isProposal = !isDecision && ContainsAny(lower, "предлаг", "предложен", "иде", "можно было бы", "как насч", "рассмотреть вариант", "стоит ли");
            if (isDecision)
            {
                decisions.Add(cleanSentence);
            }
            else if (isProposal)
            {
                proposed.Add(cleanSentence);
            }

            if (ContainsAny(lower, "риск", "опасност", "задержк", "проблем", "сбой", "срыв", "дефицит", "брак", "не успе"))
            {
                risks.Add(cleanSentence);
            }

            if (sentence.Contains('?') || ContainsAny(lower, "уточнить", "неясно", "под вопросом", "требует уточнения"))
            {
                requiresClarification.Add(cleanSentence);
            }

            if (ContainsAny(lower,
                    "поруч", "нужно", "необходимо", "надо ", "сделать", "подготовить", "проверить",
                    "отправить", "согласовать", "настроить", "исправить", "заказать", "связаться",
                    "выгрузить", "обновить", "провести", "назначить", "ответственн", "задач"))
            {
                var extracted = ExtractHeuristicActionItem(cleanSentence);
                actionItems.Add(extracted);
            }
            else if (!isDecision && !isProposal)
            {
                discussed.Add(cleanSentence);
            }
        }

        if (discussed.Count == 0 && rawSentences.Count > 0)
        {
            discussed.AddRange(rawSentences.Take(3).Select(s => s.TrimEnd('.', ';', ' ')));
        }

        if (decisions.Count == 0 && actionItems.Count > 0)
        {
            decisions.Add($"Принято к исполнению поручений: {actionItems.Count} шт.");
        }

        EnsureClarificationsForIncompleteItems(actionItems, requiresClarification);

        var summary = rawSentences.Count <= 3
            ? string.Join(" ", rawSentences)
            : string.Join(" ", rawSentences.Take(3));

        return new MeetingAnalysisResult(
            Title: title,
            Summary: summary,
            Decisions: decisions,
            Discussed: discussed,
            Risks: risks,
            RequiresClarification: requiresClarification,
            ActionItems: actionItems,
            Proposed: proposed);
    }

    private static ExtractedActionItem ExtractHeuristicActionItem(string sentence)
    {
        string responsible = ClarificationRequired;

        var explicitRespMatch = ExplicitResponsibleRegex.Match(sentence);
        if (explicitRespMatch.Success)
        {
            var candidate = explicitRespMatch.Groups["name"].Value.Trim();
            if (!IsStopWord(candidate))
            {
                responsible = candidate;
            }
        }
        else
        {
            var subjMatch = SubjectObligationRegex.Match(sentence);
            if (subjMatch.Success)
            {
                var candidate = subjMatch.Groups["name"].Value.Trim();
                if (!IsStopWord(candidate))
                {
                    responsible = candidate;
                }
            }
        }

        string deadlineText = ClarificationRequired;
        DateTime? parsedDeadline = null;

        var dateMatch = ExplicitDateRegex.Match(sentence);
        if (dateMatch.Success)
        {
            deadlineText = dateMatch.Groups["date"].Value.Trim();
            if (TryParseDeadlineString(deadlineText, out var dt))
            {
                parsedDeadline = dt;
            }
        }
        else
        {
            var relMatch = ExplicitRelativeDeadlineRegex.Match(sentence);
            if (relMatch.Success)
            {
                deadlineText = relMatch.Groups["dl"].Value.Trim();
                if (TryParseDeadlineString(deadlineText, out var dt))
                {
                    parsedDeadline = dt;
                }
            }
        }

        return NormalizeActionItem(new ExtractedActionItem(
            Title: sentence,
            Description: sentence,
            Responsible: responsible,
            DeadlineText: deadlineText,
            ParsedDeadline: parsedDeadline,
            SourceQuote: sentence));
    }

    private static void EnsureClarificationsForIncompleteItems(
        List<ExtractedActionItem> actionItems,
        List<string> requiresClarification)
    {
        foreach (var item in actionItems)
        {
            var missingResp = string.Equals(item.Responsible, ClarificationRequired, StringComparison.OrdinalIgnoreCase);
            var missingDeadline = string.Equals(item.DeadlineText, ClarificationRequired, StringComparison.OrdinalIgnoreCase);

            if (missingResp || missingDeadline)
            {
                var note = $"Поручение «{item.Title}»: ответственный — {item.Responsible}, срок — {item.DeadlineText}";
                if (!requiresClarification.Any(c => c.Contains(item.Title, StringComparison.OrdinalIgnoreCase)))
                {
                    requiresClarification.Add(note);
                }
            }
        }
    }

    private static bool IsMissingPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return MissingValuePlaceholders.Contains(value.Trim().TrimEnd('.'));
    }

    private static bool IsStopWord(string candidate)
    {
        var firstWord = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return StopCapitalizedWords.Contains(firstWord);
    }

    private static bool TryParseDeadlineString(string raw, out DateTime parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();
        var lower = trimmed.ToLowerInvariant();

        if (lower == "сегодня")
        {
            parsed = DateTime.UtcNow.Date;
            return true;
        }

        if (lower == "завтра")
        {
            parsed = DateTime.UtcNow.Date.AddDays(1);
            return true;
        }

        if (lower == "послезавтра")
        {
            parsed = DateTime.UtcNow.Date.AddDays(2);
            return true;
        }

        var dateTokenMatch = ExplicitDateRegex.Match(trimmed);
        var candidateDate = dateTokenMatch.Success ? dateTokenMatch.Groups["date"].Value : trimmed;

        string[] formats = ["dd.MM.yyyy", "dd.MM.yy", "yyyy-MM-dd", "d.M.yyyy"];
        if (DateTime.TryParseExact(
                candidateDate,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dtExact))
        {
            parsed = dtExact;
            return true;
        }

        if (DateTime.TryParse(
                candidateDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dtGeneral))
        {
            parsed = dtGeneral;
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (text.Contains(n, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ExtractJsonBlock(string input)
    {
        var start = input.IndexOf('{');
        var end = input.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return input.Substring(start, end - start + 1);
        }

        return null;
    }

    private static bool TryGetProp(JsonElement obj, out JsonElement val, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out val))
            {
                return true;
            }

            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    val = p.Value;
                    return true;
                }
            }
        }

        val = default;
        return false;
    }

    private static string? ReadString(JsonElement obj, params string[] names)
    {
        if (TryGetProp(obj, out var prop, names) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString()?.Trim();
        }

        return null;
    }

    private static List<string> ReadStringList(JsonElement obj, params string[] names)
    {
        var list = new List<string>();
        if (TryGetProp(obj, out var prop, names) && prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        list.Add(s);
                    }
                }
            }
        }

        return list;
    }

    private async Task LogAnalysisSpanAsync(
        string? traceId,
        DateTime startTime,
        int transcriptLength,
        string engine,
        MeetingAnalysisResult result,
        CancellationToken ct)
    {
        if (_langfuse == null) return;

        try
        {
            var tid = !string.IsNullOrWhiteSpace(traceId) ? traceId : _langfuse.GenerateTraceId();
            await _langfuse.CreateSpanAsync(
                traceId: tid,
                name: "Span:Meeting_Transcript_Analysis",
                startTime: startTime,
                endTime: DateTime.UtcNow,
                input: new { transcript_length = transcriptLength, engine = engine },
                output: new
                {
                    title = result.Title,
                    decisions_count = result.Decisions.Count,
                    action_items_count = result.ActionItems.Count,
                    clarifications_count = result.RequiresClarification.Count
                },
                metadata: new { engine = engine },
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[MeetingTranscriptAnalyzer] Failed to log Langfuse span.");
        }
    }
}
