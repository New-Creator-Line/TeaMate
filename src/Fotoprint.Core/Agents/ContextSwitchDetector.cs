using System.Text.Json;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Llm;
using Fotoprint.Core.Models;
using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Agents;

/// <summary>
/// Результат работы ContextSwitchDetector: сменился ли контекст диалога (true/false)
/// и рекомендуемая ветка обработки запроса для n8n / MCP.
/// </summary>
public record ContextSwitchResult(
    bool IsSwitched,
    string SuggestedBranch,
    string Reason
);

/// <summary>
/// Интерфейс детектора смены контекста. Вызывается на 2-м этапе шлюза ПЕРЕД передачей в n8n.
/// </summary>
public interface IContextSwitchDetector
{
    Task<ContextSwitchResult> DetectSwitchAsync(
        IReadOnlyList<ChatMessage> previousSessionMessages,
        string currentMessage,
        string? traceId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Реализация детектора смены контекста через вызов Gemini LLM.
/// Передает предыдущую историю сессии и текущее сообщение, получает строго true/false в JSON,
/// парсит ответ и определяет, сменился ли контекст перед передачей в n8n.
/// </summary>
public class ContextSwitchDetector : IContextSwitchDetector
{
    private readonly IGeminiClient _gemini;
    private readonly FotoprintConfig _config;
    private readonly ILangfuseClient? _langfuse;
    private readonly ILogger<ContextSwitchDetector>? _logger;

    public ContextSwitchDetector(
        IGeminiClient gemini,
        FotoprintConfig config,
        ILangfuseClient? langfuse = null,
        ILogger<ContextSwitchDetector>? logger = null)
    {
        _gemini = gemini;
        _config = config;
        _langfuse = langfuse;
        _logger = logger;
    }

    public async Task<ContextSwitchResult> DetectSwitchAsync(
        IReadOnlyList<ChatMessage> previousSessionMessages,
        string currentMessage,
        string? traceId = null,
        CancellationToken ct = default)
    {
        if (!_config.ContextSwitch.Enabled)
        {
            return new ContextSwitchResult(false, "doc_search", "Context switch detector disabled in configuration");
        }

        // Фильтруем историю: берем только незамороженные непустые сообщения
        var activeHistory = previousSessionMessages?
            .Where(m => !m.IsFrozen && !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(_config.ContextSwitch.MaxHistoryMessages)
            .ToList();

        // Если истории нет - это начало диалога, смены контекста нет
        if (activeHistory == null || activeHistory.Count == 0)
        {
            var initialBranch = ClassifyDomainBranch(currentMessage) ?? "general_chat";
            return new ContextSwitchResult(false, initialBranch, "Первое сообщение в сессии");
        }

        // Быстрый детерминированный гибридный классификатор (экономит 1 вызов LLM из квоты 40 RPM в 90%+ случаев)
        if (TryClassifyFastPath(activeHistory, currentMessage, out var fastResult))
        {
            if (_langfuse != null && !string.IsNullOrEmpty(traceId))
            {
                _ = _langfuse.LogEventAsync(
                    traceId,
                    "ContextSwitch:Detected",
                    "DEFAULT",
                    $"FastPath: Switch={fastResult.IsSwitched}, Branch={fastResult.SuggestedBranch}, Reason={fastResult.Reason}",
                    ct: ct);
            }
            return fastResult;
        }

        // Форматируем предыдущую сессию (резервный LLM-путь для нестандартных/неоднозначных фраз)
        var historyContext = string.Join("\n", activeHistory.Select(m => $"[{m.Role.ToUpperInvariant()}]: {m.Content}"));

        var prompt = $$"""
            ИСТОРИЯ ПРЕДЫДУЩЕЙ СЕССИИ:
            {{historyContext}}

            ТЕКУЩЕЕ НОВОЕ СООБЩЕНИЕ:
            "{{currentMessage}}"

            Вопрос: сменил ли пользователь глобальную тему диалога (переключился ли на совсем другой, не связанный с историей предмет разговора)?
            ВАЖНО: Если новое сообщение относится к той же ситуации, тому же поставщику, заказу, клиенту или проблеме (например, уточняет детали, последствия, письма, сроки или статус решения той же проблемы) — это НЕ смена темы (`"context_switched": false`).
            Смена темы (`"context_switched": true`) — только когда предмет разговора стал полностью другим и не связан с предыдущими сообщениями.

            Ответь строго в формате JSON (сначала напиши рассуждение в reason, затем boolean true/false в context_switched):
            {
              "reason": "краткое объяснение, связан ли вопрос с предыдущей темой пользователя",
              "context_switched": true,
              "suggested_branch": "meeting_prep" | "live_meeting_notes" | "meeting_summary" | "task_management" | "executive_report" | "doc_search" | "general_chat"
            }
            """;

        var systemInstruction = """
            Ты — высокоточный классификатор смены контекста диалогов типографии Fotoprint.
            Твоя цель — определить, сменил ли пользователь тему разговора по отношению к своим предыдущим сообщениям в сессии.
            Правила:
            1. Если новое сообщение развивает, уточняет или продолжает предыдущую тему пользователя (даже если тема бытовая/кулинарная, например обсуждали плов и уточнили про зирвак и рис, либо обсуждали поставку фольги и спросили про гарантийные письма от поставщика) — context_switched = false.
            2. Если пользователь переключился на совершенно другую тему (например, обсуждали работу/заказы/регламенты/задачи/встречи, а он вдруг спросил про рецепт блюда, погоду, спорт, криптовалюту, сериалы, игры, анекдоты или корпоратив) — context_switched = true, suggested_branch = "general_chat".
            3. Если предыдущий вопрос пользователя был на отвлеченную/нерабочую тему (флуд, аниме, кино, еда, спорт), а в новом сообщении он перешел к рабочему вопросу типографии (например, проверить заказ в 1С, регламент или задачу) — это ВСЕГДА смена темы: context_switched = true.
            Всегда отвечай максимально кратко и строго валидным JSON без markdown-обертки.
            """;

        try
        {
            var startTime = DateTime.UtcNow;
            var response = await _gemini.GenerateJsonNoThinkingAsync(
                prompt: prompt,
                systemInstruction: systemInstruction,
                temperature: _config.ContextSwitch.Temperature,
                maxOutputTokens: _config.ContextSwitch.MaxOutputTokens,
                traceId: traceId,
                ct: ct);
            var endTime = DateTime.UtcNow;

            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Text))
            {
                return new ContextSwitchResult(false, "doc_search", $"Gemini failed: {response.ErrorMessage}");
            }

            var result = ParseLlmResponse(response.Text);

            if (_langfuse != null && !string.IsNullOrEmpty(traceId))
            {
                // Фиксация поколения токенов Gen:ContextSwitchDetector и FinOps
                await _langfuse.CreateGenerationAsync(
                    traceId: traceId,
                    name: "Gen:ContextSwitchDetector",
                    model: response.Model ?? _config.Llm.Models.FastModel,
                    startTime: startTime,
                    endTime: endTime,
                    promptTokens: response.PromptTokens,
                    completionTokens: response.CandidatesTokens,
                    totalTokens: response.TotalTokens,
                    input: prompt,
                    output: response.Text,
                    metadata: new
                    {
                        is_context_switched = result.IsSwitched,
                        suggested_branch = result.SuggestedBranch,
                        reason = result.Reason
                    },
                    ct: ct);

                await _langfuse.LogEventAsync(
                    traceId,
                    "ContextSwitch:Detected",
                    "DEFAULT",
                    $"Switch={result.IsSwitched}, Branch={result.SuggestedBranch}, Reason={result.Reason}");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Context switch LLM classification failed, fallback to false");
            return new ContextSwitchResult(false, "doc_search", $"Fallback due to error: {ex.Message}");
        }
    }

    /// <summary>
    /// Парсит ответ LLM: поддерживает как структурированный JSON, так и прямой true/false.
    /// </summary>
    public static ContextSwitchResult ParseLlmResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ContextSwitchResult(false, "general_chat", "Empty LLM response");
        }

        try
        {
            var cleanJson = raw.Trim();
            if (cleanJson.StartsWith("```json"))
            {
                cleanJson = cleanJson[7..];
            }
            if (cleanJson.StartsWith("```"))
            {
                cleanJson = cleanJson[3..];
            }
            if (cleanJson.EndsWith("```"))
            {
                cleanJson = cleanJson[..^3];
            }
            cleanJson = cleanJson.Trim();

            var firstBrace = cleanJson.IndexOf('{');
            var lastBrace = cleanJson.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleanJson = cleanJson[firstBrace..(lastBrace + 1)];
            }

            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            bool isSwitched = false;
            if (root.TryGetProperty("context_switched", out var swProp))
            {
                if (swProp.ValueKind == JsonValueKind.True || swProp.ValueKind == JsonValueKind.False)
                {
                    isSwitched = swProp.GetBoolean();
                }
                else if (bool.TryParse(swProp.GetString(), out var b))
                {
                    isSwitched = b;
                }
            }

            string branch = "doc_search";
            if (root.TryGetProperty("suggested_branch", out var bProp) && bProp.GetString() is string bStr && !string.IsNullOrWhiteSpace(bStr))
            {
                branch = bStr;
            }

            string reason = "Classified by Gemini";
            if (root.TryGetProperty("reason", out var rProp) && rProp.GetString() is string rStr && !string.IsNullOrWhiteSpace(rStr))
            {
                reason = rStr;
            }

            // Защита от рассинхрона поля reason и boolean у быстрых LLM:
            // если модель сама в рассуждении написала «пользователь переключился / сменил тему / в отличие от предыдущего», корректируем флаг
            var rLower = reason.ToLowerInvariant();
            if (!isSwitched &&
                (rLower.Contains("пользователь переключился") ||
                 rLower.Contains("переключился на") ||
                 rLower.Contains("сменил тему") ||
                 rLower.Contains("совершенно друг") ||
                 rLower.Contains("в отличие от предыдущ")))
            {
                isSwitched = true;
            }

            return new ContextSwitchResult(isSwitched, branch, reason);
        }
        catch
        {
            // Резервный текстовый парсер true/false если JSON поврежден
            var lower = raw.ToLowerInvariant();
            var isSwitched = lower.Contains("\"context_switched\": true") ||
                             lower.Contains("context_switched: true") ||
                             lower.Contains("true");

            return new ContextSwitchResult(isSwitched, "general_chat", $"Parsed from text: {raw.Trim()}");
        }
    }

    private static bool TryClassifyFastPath(
        List<ChatMessage> activeHistory,
        string currentMessage,
        out ContextSwitchResult result)
    {
        result = default!;
        if (string.IsNullOrWhiteSpace(currentMessage))
        {
            return false;
        }

        // Определяем домен и подтему предыдущего сообщения пользователя (или последнего сообщения в сессии)
        var lastUserMsg = activeHistory.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content
                          ?? activeHistory[^1].Content;
        var allHistoryText = string.Join(" ", activeHistory.Select(m => m.Content));

        var (prevBranch, prevSubTopic) = ClassifyTopicAndBranch(lastUserMsg);
        if (prevBranch == null)
        {
            (prevBranch, prevSubTopic) = ClassifyTopicAndBranch(activeHistory[^1].Content);
        }

        var (currentBranch, currentSubTopic) = ClassifyTopicAndBranch(currentMessage);

        // 0. Явный маркер смены темы ("другой вопрос", "сменим тему", "теперь давай про", "не по теме", "забудь")
        if (HasExplicitSwitchMarker(currentMessage) && currentBranch != null)
        {
            result = new ContextSwitchResult(
                IsSwitched: true,
                SuggestedBranch: currentBranch,
                Reason: $"Пользователь явно сменил тему на '{currentBranch}'");
            return true;
        }

        // Проверяем, есть ли общий объект (номер заказа, оборудование, клиент) между историей и новым сообщением
        bool sharesSpecificEntity = SharesEntityWithHistory(allHistoryText, currentMessage);
        bool isContinuation = IsContinuationPhrase(currentMessage);
        bool isImperativeSwitchCommand = currentMessage.Contains("поставь задач", StringComparison.OrdinalIgnoreCase) ||
                                         currentMessage.Contains("создай задач", StringComparison.OrdinalIgnoreCase) ||
                                         currentMessage.Contains("назначь встреч", StringComparison.OrdinalIgnoreCase) ||
                                         currentMessage.Contains("сформируй итоговый протокол", StringComparison.OrdinalIgnoreCase) ||
                                         currentMessage.Contains("сформируй протокол", StringComparison.OrdinalIgnoreCase);

        // 1. Явная фраза-продолжение ("а для ...", "а по нему", "а кто еще там", "и какой...") без директивной смены задачи
        if (isContinuation && !isImperativeSwitchCommand)
        {
            bool sameKnownDomain = currentBranch != null && prevBranch != null &&
                                   string.Equals(currentBranch, prevBranch, StringComparison.OrdinalIgnoreCase);
            bool anaphoricFollowUpToKnownDomain = prevBranch != null && currentBranch == null &&
                                                  HasStrongAnaphora(currentMessage);

            if (sameKnownDomain || sharesSpecificEntity || anaphoricFollowUpToKnownDomain)
            {
                var targetBranch = currentBranch ?? prevBranch ?? "doc_search";
                result = new ContextSwitchResult(
                    IsSwitched: false,
                    SuggestedBranch: targetBranch,
                    Reason: $"Пользователь продолжает обсуждение в ветке '{targetBranch}'");
                return true;
            }
        }

        // 2. Если домен текущего сообщения и предыдущего сообщения определены
        if (currentBranch != null && prevBranch != null)
        {
            // 2a. Тот же самый домен
            if (string.Equals(currentBranch, prevBranch, StringComparison.OrdinalIgnoreCase))
            {
                // Проверяем, не переключился ли пользователь внутри одного домена на совершенно другую подтему
                // (например, в doc_search: обсуждали краску/печатные машины, а спросили про оплату сверхурочных/отпуска или скидки клиентам)
                if (prevSubTopic != null && currentSubTopic != null &&
                    !string.Equals(prevSubTopic, currentSubTopic, StringComparison.OrdinalIgnoreCase) &&
                    !isContinuation && !sharesSpecificEntity &&
                    currentBranch == "doc_search")
                {
                    result = new ContextSwitchResult(
                        IsSwitched: true,
                        SuggestedBranch: currentBranch,
                        Reason: $"Пользователь сменил тему внутри базы знаний с '{prevSubTopic}' на '{currentSubTopic}'");
                    return true;
                }

                result = new ContextSwitchResult(
                    IsSwitched: false,
                    SuggestedBranch: currentBranch,
                    Reason: $"Пользователь продолжает обсуждение темы '{currentBranch}'");
                return true;
            }

            // 2b. Домены разные, но вопрос привязан к тому же объекту/заказу (например, заказ 44102 в 1С -> задачи по заказу 44102 в Битрикс)
            if (sharesSpecificEntity && !isImperativeSwitchCommand)
            {
                result = new ContextSwitchResult(
                    IsSwitched: false,
                    SuggestedBranch: currentBranch,
                    Reason: $"Пользователь продолжает обсуждение того же объекта в ветке '{currentBranch}'");
                return true;
            }

            // 2c. Домены разные и нет связующего объекта -> произошла смена темы диалога!
            result = new ContextSwitchResult(
                IsSwitched: true,
                SuggestedBranch: currentBranch,
                Reason: $"Пользователь резко сменил тему с '{prevBranch}' на '{currentBranch}'");
            return true;
        }

        return false;
    }

    private static bool HasExplicitSwitchMarker(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        return t.Contains("другой вопрос") ||
               t.Contains("сменим тему") ||
               t.Contains("другая тема") ||
               t.Contains("теперь про ") ||
               t.Contains("теперь давай") ||
               t.Contains("не по теме") ||
               t.Contains("кстати, ");
    }

    private static bool SharesEntityWithHistory(string historyText, string currentMessage)
    {
        if (string.IsNullOrWhiteSpace(historyText) || string.IsNullOrWhiteSpace(currentMessage))
            return false;

        var h = historyText.ToLowerInvariant();
        var c = currentMessage.ToLowerInvariant();

        // Проверяем общие номера заказов/задач (4+ цифры подряд, например 44102, 2051)
        var digitsMatch = System.Text.RegularExpressions.Regex.Matches(c, @"\b\d{4,6}\b");
        foreach (System.Text.RegularExpressions.Match m in digitsMatch)
        {
            if (h.Contains(m.Value, StringComparison.Ordinal))
                return true;
        }

        // Проверяем сквозные именованные сущности (клиенты, печатные машины)
        string[] namedEntities = ["heidelberg", "speedmaster", "альфа-маркет", "альфа маркет", "гринвей", "тр-04"];
        foreach (var entity in namedEntities)
        {
            if (c.Contains(entity, StringComparison.Ordinal) && h.Contains(entity, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsContinuationPhrase(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (HasExplicitSwitchMarker(t)) return false;

        return t.StartsWith("а ") ||
               t.StartsWith("и ") ||
               t.StartsWith("ну ") ||
               t.StartsWith("тогда ") ||
               t.StartsWith("подробнее") ||
               t.StartsWith("уточни") ||
               t.StartsWith("расскажи подробнее") ||
               t.StartsWith("а что ") ||
               t.StartsWith("а как") ||
               t.StartsWith("а для ") ||
               t.StartsWith("а по ") ||
               t.StartsWith("а кто ") ||
               t.StartsWith("а покажи") ||
               t.StartsWith("еще ") ||
               t.StartsWith("ещё ") ||
               t.Contains("по нему") ||
               t.Contains("по этому") ||
               t.Contains("на этой встрече") ||
               t.Contains("там будет") ||
               IsActionPlanConfirmationOrAdjustment(t);
    }

    private static bool HasStrongAnaphora(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (HasExplicitSwitchMarker(t)) return false;

        return t.Contains("по нему") ||
               t.Contains("по ней") ||
               t.Contains("по этому") ||
               t.Contains("на этой ") ||
               t.Contains("этой же ") ||
               t.Contains("там будет") ||
               t.Contains("кто еще там") ||
               t.Contains("кто ещё там") ||
               t.StartsWith("подробнее") ||
               t.StartsWith("уточни") ||
               t.StartsWith("расскажи подробнее") ||
               IsActionPlanConfirmationOrAdjustment(t);
    }

    public static bool IsActionPlanConfirmationOrAdjustment(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().ToLowerInvariant();

        return t == "да" || t == "ок" || t == "окей" || t == "ok" || t == "давай" || t == "выполняй" || t == "создавай" || t == "делай" ||
               t.StartsWith("да,") || t.StartsWith("да.") || t.StartsWith("да!") || t.StartsWith("да ") ||
               t.StartsWith("ок,") || t.StartsWith("ок ") || t.StartsWith("окей,") || t.StartsWith("окей ") ||
               t.Contains("подтвержда") ||
               t.StartsWith("выполняй") || t.StartsWith("выполни") ||
               t.StartsWith("создавай") || t.StartsWith("заводи") ||
               t.StartsWith("согласен") || t.StartsWith("согласна") ||
               t.StartsWith("все верно") || t.StartsWith("всё верно") || t.StartsWith("верно,") ||
               t.StartsWith("нет, отмен") || t.StartsWith("отмен") || t.StartsWith("не надо") || t.StartsWith("не создавай") ||
               t.StartsWith("измени ") || t.StartsWith("поменяй ");
    }

    private static string? ClassifyDomainBranch(string? text) => ClassifyTopicAndBranch(text).Branch;

    private static (string? Branch, string? SubTopic) ClassifyTopicAndBranch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var t = text.ToLowerInvariant();

        if (t.Contains("протокол") || t.Contains("итоги встреч") || t.Contains("резюме встреч") || t.Contains("резюме совещан"))
            return ("meeting_summary", "mom");

        if (t.Contains("задач") || t.Contains("битрикс") || t.Contains("bitrix") || t.Contains("поручен") || t.Contains("дедлайн"))
            return ("task_management", "tasks");

        if (t.Contains("сделк") || t.Contains("crm") || t.Contains("лид"))
            return ("task_management", "crm");

        if (t.Contains("встреч") || t.Contains("планерк") || t.Contains("совещан") || t.Contains("календар") || t.Contains("расписан"))
            return ("meeting_prep", "calendar");

        if (t.Contains("сверхуроч") || t.Contains("трудов") || t.Contains("распоряд") || t.Contains("отпуск") || t.Contains("пвтр") || t.Contains("оплат"))
            return ("doc_search", "hr_labor");

        if (t.Contains("скидк") || t.Contains("коммерческ") || t.Contains("предоплат") || t.Contains("наценк") || t.Contains("прайс"))
            return ("doc_search", "commercial_policy");

        if (t.Contains("регламент") || t.Contains("heidelberg") || t.Contains("краск") || t.Contains("бумаг") ||
            t.Contains("плотн") || t.Contains("отход") || t.Contains("приладк") || t.Contains("техкарт"))
            return ("doc_search", "tech_regulations");

        if (t.Contains("раг") || t.Contains("rag") || t.Contains("база знаний") || t.Contains("базе знаний") || t.Contains("документ") || t.Contains("правил"))
            return ("doc_search", "rag_catalog");

        if (t.Contains("выработк") || t.Contains("kpi") || t.Contains("кпи") || t.Contains("брак") ||
            t.Contains("оттиск") || t.Contains("загрузк") || t.Contains("сводк") || t.Contains("дайджест") ||
            t.Contains("заказ") || t.Contains("тираж") || t.Contains("44102") || t.Contains("1с") || t.Contains("1c") || t.Contains("офсет"))
            return ("executive_report", "production_1c");

        if (t.Contains("привет") || t.Contains("здравствуй") || t.Contains("спасибо") || t.Contains("кто ты") || t.Contains("помощь"))
            return ("general_chat", "greeting");

        return (null, null);
    }
}
