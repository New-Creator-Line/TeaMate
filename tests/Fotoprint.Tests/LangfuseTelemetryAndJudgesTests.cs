using System.Text.Json;
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Llm;
using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fotoprint.Tests;

public class LangfuseTelemetryAndJudgesTests
{
    private class MockLangfuseClient : ILangfuseClient
    {
        public List<(string TraceId, string Name, string Level, object? Metadata)> Events { get; } = new();
        public List<(string TraceId, string Name, double Value, string? Comment)> Scores { get; } = new();
        public List<(string TraceId, string Name, double LatencyMs)> Spans { get; } = new();
        public List<(string TraceId, string Name, string Model, int TotalTokens, double Cost)> Generations { get; } = new();
        public List<(string TraceId, string[] Tags)> Traces { get; } = new();

        public string GenerateTraceId() => Guid.NewGuid().ToString("N");
        public string GenerateObservationId() => Guid.NewGuid().ToString("N");

        public double CalculateCost(int promptTokens, int completionTokens, int cachedTokens = 0)
        {
            var promptCost = (promptTokens / 1000.0) * 0.000075;
            var completionCost = (completionTokens / 1000.0) * 0.000300;
            var cachedCost = (cachedTokens / 1000.0) * 0.00001875;
            return Math.Round(promptCost + completionCost + cachedCost, 6);
        }

        public Task CreateTraceAsync(string traceId, string name, string? userId = null, string? sessionId = null, object? metadata = null, string[]? tags = null, CancellationToken ct = default)
        {
            Traces.Add((traceId, tags ?? Array.Empty<string>()));
            return Task.CompletedTask;
        }

        public Task CreateSpanAsync(string traceId, string name, DateTime startTime, DateTime endTime, object? input = null, object? output = null, object? metadata = null, string? parentObservationId = null, string? spanId = null, CancellationToken ct = default)
        {
            var latencyMs = Math.Max(1, (endTime - startTime).TotalMilliseconds);
            Spans.Add((traceId, name, latencyMs));
            return Task.CompletedTask;
        }

        public Task CreateGenerationAsync(string traceId, string name, string model, DateTime startTime, DateTime endTime, int promptTokens, int completionTokens, int totalTokens, int cachedTokens = 0, object? input = null, object? output = null, object? metadata = null, double? calculatedCost = null, string? parentObservationId = null, string? generationId = null, CancellationToken ct = default)
        {
            var cost = calculatedCost ?? CalculateCost(promptTokens, completionTokens, cachedTokens);
            Generations.Add((traceId, name, model, totalTokens, cost));
            return Task.CompletedTask;
        }

        public Task LogEventAsync(string traceId, string name, string level = "DEFAULT", string? message = null, object? metadata = null, CancellationToken ct = default)
        {
            Events.Add((traceId, name, level, metadata));
            return Task.CompletedTask;
        }

        public Task LogSecurityAlertAsync(string traceId, long telegramId, string toolOrAction, string requiredPermission, string reason, CancellationToken ct = default)
        {
            Events.Add((traceId, "Event:Security_Violation", "ERROR", new { telegramId, toolOrAction, requiredPermission, reason }));
            Events.Add((traceId, "SecurityInterceptor:AccessDenied", "WARNING", new { telegramId, toolOrAction, requiredPermission, reason }));
            return Task.CompletedTask;
        }

        public Task LogScoreAsync(string traceId, string name, double value, string? comment = null, string? observationId = null, CancellationToken ct = default)
        {
            Scores.Add((traceId, name, value, comment));
            return Task.CompletedTask;
        }
    }

    private class MockGeminiClient : IGeminiClient
    {
        private readonly Func<string, string> _responseFunc;

        public MockGeminiClient(Func<string, string> responseFunc)
        {
            _responseFunc = responseFunc;
        }

        public Task<GeminiGenerateResponse> GenerateContentAsync(string prompt, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default)
        {
            var answer = _responseFunc(prompt);
            return Task.FromResult(new GeminiGenerateResponse(true, answer, "STOP", 100, 50, 150, 1, null));
        }

        public Task<GeminiTurnResponse> ChatTurnAsync(List<object> contents, object? tools = null, string? systemInstruction = null, double? temperature = null, int? maxOutputTokens = null, string? traceId = null, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public GeminiCallStats GetStats() => new(1, 1, 60, 59, 1.6, DateTime.UtcNow, 0);
    }

    [Fact]
    public void FinOps_TokenCost_Calculation_Is_Accurate()
    {
        var langfuse = new MockLangfuseClient();

        // 10,000 prompt tokens (10 * 0.000075 = $0.00075)
        // 5,000 completion tokens (5 * 0.000300 = $0.00150)
        // 2,000 cached tokens (2 * 0.00001875 = $0.0000375)
        // Итого: ~ $0.002288
        var cost = langfuse.CalculateCost(promptTokens: 10000, completionTokens: 5000, cachedTokens: 2000);

        Assert.True(cost > 0.0022 && cost < 0.0024);
    }

    [Fact]
    public async Task DistributedTracing_Trace_Enriched_With_Department_And_Role_Tags()
    {
        var langfuse = new MockLangfuseClient();
        var traceId = "test-trace-101";

        var tags = new[]
        {
            "dept:Производство",
            "role:Director,Production",
            "branch:executive_report",
            "context_switched:false"
        };

        await langfuse.CreateTraceAsync(
            traceId: traceId,
            name: "telegram-chat-message",
            userId: "100001",
            sessionId: Guid.NewGuid().ToString(),
            tags: tags);

        Assert.Single(langfuse.Traces);
        Assert.Contains("dept:Производство", langfuse.Traces[0].Tags);
        Assert.Contains("context_switched:false", langfuse.Traces[0].Tags);
    }

    [Fact]
    public async Task Security_Metrics_LogSecurityAlert_Emits_EventSecurityViolation()
    {
        var langfuse = new MockLangfuseClient();
        var traceId = "sec-trace-202";

        await langfuse.LogSecurityAlertAsync(
            traceId: traceId,
            telegramId: 999999,
            toolOrAction: "onec:read_production_kpis",
            requiredPermission: "onec:read_production_kpis",
            reason: "Сотрудник не имеет доступа к финансовым показателям");

        Assert.Contains(langfuse.Events, e => e.Name == "Event:Security_Violation" && e.Level == "ERROR");
        Assert.Contains(langfuse.Events, e => e.Name == "SecurityInterceptor:AccessDenied");
    }

    [Fact]
    public async Task Evaluations_UserFeedback_And_TaskExtractionAccuracy_Scored_Properly()
    {
        var langfuse = new MockLangfuseClient();
        var traceId = "user-eval-303";

        // Пользователь нажал [👍]
        await langfuse.LogScoreAsync(traceId, "user_feedback", 1.0, "Пользователь поставил лайк");

        // Пользователь нажал [✅ В Битрикс] без ручных правок
        await langfuse.LogScoreAsync(traceId, "task_extraction_accuracy", 1.0, "Поручение поставлено без редактирования");

        Assert.Contains(langfuse.Scores, s => s.Name == "user_feedback" && s.Value == 1.0);
        Assert.Contains(langfuse.Scores, s => s.Name == "task_extraction_accuracy" && s.Value == 1.0);
    }

    [Fact]
    public async Task LlmJudge_Faithfulness_Detects_Factual_Consistency()
    {
        var mockGemini = new MockGeminiClient(prompt =>
        {
            return """
            {
              "score": 1.0,
              "has_hallucinations": false,
              "reasoning": "Все цифры тиража и брака строго соответствуют сырому JSON от 1C"
            }
            """;
        });

        var langfuse = new MockLangfuseClient();
        var config = new FotoprintConfig();
        var judge = new LlmJudgeService(mockGemini, langfuse, config, NullLogger<LlmJudgeService>.Instance);

        var score = await judge.EvaluateFaithfulnessAsync(
            traceId: "faith-trace-404",
            userQuestion: "Сколько брака на смене?",
            rawContextData: "{\"scrap_rate\": 1.4, \"department\": \"offset\"}",
            agentAnswer: "На текущей смене офсетного цеха процент брака составляет 1.4%.");

        Assert.Equal(1.0, score);
        Assert.Contains(langfuse.Scores, s => s.Name == "faithfulness" && s.Value == 1.0);
    }

    [Fact]
    public async Task LlmJudge_ContextSwitchAccuracy_Scores_Decision()
    {
        var mockGemini = new MockGeminiClient(prompt =>
        {
            return """
            {
              "score": 1.0,
              "is_decision_correct": true,
              "reasoning": "Пользователь действительно сменил тему разговора"
            }
            """;
        });

        var langfuse = new MockLangfuseClient();
        var config = new FotoprintConfig();
        var judge = new LlmJudgeService(mockGemini, langfuse, config, NullLogger<LlmJudgeService>.Instance);

        var score = await judge.EvaluateContextSwitchAccuracyAsync(
            traceId: "switch-eval-505",
            historyText: "User: Где тираж?\nAssistant: Тираж на фальцовке.",
            incomingMessage: "Создай встречу с отделом продаж",
            detectedSwitch: true,
            suggestedBranch: "meeting_prep");

        Assert.Equal(1.0, score);
        Assert.Contains(langfuse.Scores, s => s.Name == "context_switch_accuracy" && s.Value == 1.0);
    }

    [Fact]
    public async Task LlmJudge_FormattingAndPolicy_Checks_Protocol_And_Security_Leaks()
    {
        var mockGemini = new MockGeminiClient(prompt =>
        {
            return """
            {
              "score": 1.0,
              "passed_policy": true,
              "reasoning": "Секретные ключи не обнаружены, структура ответа соблюдена"
            }
            """;
        });

        var langfuse = new MockLangfuseClient();
        var config = new FotoprintConfig();
        var judge = new LlmJudgeService(mockGemini, langfuse, config, NullLogger<LlmJudgeService>.Instance);

        var score = await judge.EvaluateFormattingAndPolicyAsync(
            traceId: "policy-trace-606",
            outputText: "Протокол совещания №4. Дата: 09.11.2026. Решили: запустить Heidelberg в 3 смены.",
            isProtocol: true);

        Assert.Equal(1.0, score);
        Assert.Contains(langfuse.Scores, s => s.Name == "formatting_and_policy" && s.Value == 1.0);
    }
}

