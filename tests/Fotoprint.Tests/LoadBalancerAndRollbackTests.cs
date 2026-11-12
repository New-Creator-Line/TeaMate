using Fotoprint.Core.Resilience;
using Fotoprint.Core.Telemetry;
using Xunit;

namespace Fotoprint.Tests;

public class LoadBalancerAndRollbackTests
{
    private class MockLangfuseClient : ILangfuseClient
    {
        public List<string> LoggedEvents { get; } = new();

        public string GenerateTraceId() => "mock-trace-id-123";

        public Task CreateTraceAsync(string traceId, string name, string? userId = null, string? sessionId = null, object? metadata = null, string[]? tags = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task LogEventAsync(string traceId, string name, string level = "DEFAULT", string? message = null, object? metadata = null, CancellationToken ct = default)
        {
            LoggedEvents.Add($"{level}:{name}:{message}");
            return Task.CompletedTask;
        }

        public Task LogSecurityAlertAsync(string traceId, long telegramId, string toolOrAction, string requiredPermission, string reason, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task LogScoreAsync(string traceId, string name, double value, string? comment = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    // =========================================================================
    // 1. ТЕСТЫ БАЛАНСИРОВЩИКА НАГРУЗКИ (RATE LIMITER & CONCURRENCY LIMIT)
    // =========================================================================

    [Fact]
    public async Task LoadBalancer_Enforces_Concurrency_And_Processes_All_Requests()
    {
        // Настраиваем лимит параллелизма = 2, минимальный интервал = 10мс
        var options = new LoadBalancerOptions
        {
            MaxConcurrentRequests = 2,
            MinIntervalBetweenRequestsMs = 10,
            MaxRequestsPerMinute = 500
        };

        using var lb = new LlmLoadBalancer(options);
        int maxObservedConcurrent = 0;
        int activeCurrent = 0;
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 10).Select(i => lb.ScheduleAsync($"task-{i}", async ct =>
        {
            lock (lockObj)
            {
                activeCurrent++;
                if (activeCurrent > maxObservedConcurrent)
                    maxObservedConcurrent = activeCurrent;
            }

            await Task.Delay(25, ct);

            lock (lockObj)
            {
                activeCurrent--;
            }

            return i * 10;
        })).ToList();

        var results = await Task.WhenAll(tasks);

        // Проверяем, что параллелизм никогда не превысил 2
        Assert.True(maxObservedConcurrent <= 2, $"Параллелизм {maxObservedConcurrent} превысил максимальный лимит 2");
        Assert.Equal(10, results.Length);

        var stats = lb.GetStats();
        Assert.Equal(10, stats.TotalProcessed);
        Assert.Equal(0, stats.QueueLength);
    }

    [Fact]
    public async Task LoadBalancer_Throttles_Rapid_Bursts_To_Protect_Llm()
    {
        // 50мс пауза между запросами для защиты от rate limit
        var options = new LoadBalancerOptions
        {
            MaxConcurrentRequests = 5,
            MinIntervalBetweenRequestsMs = 50,
            MaxRequestsPerMinute = 120
        };

        using var lb = new LlmLoadBalancer(options);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var t1 = lb.ScheduleAsync("burst-1", async _ => { await Task.Delay(5); return 1; });
        var t2 = lb.ScheduleAsync("burst-2", async _ => { await Task.Delay(5); return 2; });
        var t3 = lb.ScheduleAsync("burst-3", async _ => { await Task.Delay(5); return 3; });

        await Task.WhenAll(t1, t2, t3);
        sw.Stop();

        // 3 запроса со сглаживанием 50мс должны занять не менее ~90-100мс
        Assert.True(sw.ElapsedMilliseconds >= 90, $"Запросы выполнились слишком быстро ({sw.ElapsedMilliseconds}мс) без сглаживания");
        var stats = lb.GetStats();
        Assert.Equal(3, stats.TotalProcessed);
        Assert.True(stats.TotalThrottled >= 1);
    }

    // =========================================================================
    // 2. ТЕСТЫ РОЛЛБЭК-СИСТЕМЫ (3 ПОВТОРА + КОМПЕНСАЦИЯ)
    // =========================================================================

    [Fact]
    public async Task RollbackManager_Succeeds_On_First_Attempt_Without_Retries()
    {
        var langfuse = new MockLangfuseClient();
        var rm = new RollbackManager(langfuse: langfuse);
        bool rollbackInvoked = false;

        var result = await rm.ExecuteWithRollbackAsync<string>(
            nodeName: "TestNode:Success",
            action: (attempt, ct) => Task.FromResult("OK"),
            compensatingRollbackAction: (ex, ct) => { rollbackInvoked = true; return Task.CompletedTask; },
            maxRetries: 3,
            initialDelayMs: 10);

        Assert.True(result.IsSuccess);
        Assert.Equal("OK", result.Value);
        Assert.Equal(1, result.AttemptsMade);
        Assert.False(result.RollbackExecuted);
        Assert.False(rollbackInvoked);
    }

    [Fact]
    public async Task RollbackManager_Recovers_After_Transient_Failures_Within_3_Retries()
    {
        var langfuse = new MockLangfuseClient();
        var rm = new RollbackManager(langfuse: langfuse);
        int attemptsCount = 0;
        bool rollbackInvoked = false;

        // Первые 2 попытки падают, на 3-й (2-й ретрай) восстанавливается
        var result = await rm.ExecuteWithRollbackAsync<string>(
            nodeName: "TestNode:TransientFail",
            action: (attempt, ct) =>
            {
                attemptsCount++;
                if (attemptsCount < 3)
                    throw new HttpRequestException("429 Too Many Requests");

                return Task.FromResult("Recovered!");
            },
            compensatingRollbackAction: (ex, ct) => { rollbackInvoked = true; return Task.CompletedTask; },
            traceId: "test-trace-recovery",
            maxRetries: 3,
            initialDelayMs: 10);

        Assert.True(result.IsSuccess);
        Assert.Equal("Recovered!", result.Value);
        Assert.Equal(3, result.AttemptsMade);
        Assert.False(result.RollbackExecuted);
        Assert.False(rollbackInvoked);
        Assert.Contains(langfuse.LoggedEvents, e => e.Contains("Node:RetryAttempt"));
    }

    [Fact]
    public async Task RollbackManager_Executes_Rollback_When_All_3_Retries_Fail()
    {
        var langfuse = new MockLangfuseClient();
        var rm = new RollbackManager(langfuse: langfuse);
        int attemptsCount = 0;
        bool rollbackInvoked = false;
        string? rollbackReason = null;

        // Все 4 попытки (1 основная + 3 повтора) завершаются ошибкой
        var result = await rm.ExecuteWithRollbackAsync<string>(
            nodeName: "MCP:Bitrix24:CreateTask",
            action: (attempt, ct) =>
            {
                attemptsCount++;
                throw new NodeTransientException($"503 Service Unavailable (попытка {attemptsCount})", 503);
            },
            compensatingRollbackAction: (ex, ct) =>
            {
                rollbackInvoked = true;
                rollbackReason = ex.Message;
                return Task.CompletedTask;
            },
            traceId: "trace-test-rollback",
            maxRetries: 3,
            initialDelayMs: 10);

        // Проверяем:
        // 1. Операция помечена как неуспешная
        Assert.False(result.IsSuccess);
        // 2. Сделано ровно 4 попытки (1 старт + 3 повтора)
        Assert.Equal(4, attemptsCount);
        Assert.Equal(4, result.AttemptsMade);
        // 3. Компенсирующее действие (РОЛЛБЭК) сработало
        Assert.True(result.RollbackExecuted);
        Assert.True(rollbackInvoked);
        Assert.Contains("503 Service Unavailable", rollbackReason);
        // 4. Событие роллбэка зарегистрировано в Langfuse
        Assert.Contains(langfuse.LoggedEvents, e => e.Contains("Node:RollbackTriggered"));

        var stats = rm.GetStats();
        Assert.Equal(1, stats.TotalFailedAndRolledBack);
    }
}
