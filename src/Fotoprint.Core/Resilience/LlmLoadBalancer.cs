using System.Threading.Channels;
using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Resilience;

public record LoadBalancerOptions
{
    /// <summary>
    /// Максимальное число параллельных запросов к LLM / оркестратору (Concurrency Limit).
    /// </summary>
    public int MaxConcurrentRequests { get; init; } = 3;

    /// <summary>
    /// Максимальное число запросов в минуту (RPM limit, например 60).
    /// </summary>
    public int MaxRequestsPerMinute { get; init; } = 60;

    /// <summary>
    /// Минимальная пауза между запросами для сглаживания пиков (мс).
    /// </summary>
    public int MinIntervalBetweenRequestsMs { get; init; } = 250;

    /// <summary>
    /// Вместимость буферной очереди сообщений при пиках.
    /// </summary>
    public int QueueCapacity { get; init; } = 1000;
}

public interface ILlmLoadBalancer
{
    /// <summary>
    /// Отправляет задачу в балансировщик нагрузки с контролем частоты и ограничением параллелизма.
    /// </summary>
    Task<TResult> ScheduleAsync<TResult>(
        string taskName,
        Func<CancellationToken, Task<TResult>> work,
        string? traceId = null,
        int priority = 0,
        CancellationToken ct = default);

    /// <summary>
    /// Текущее количество задач в очереди ожидания.
    /// </summary>
    int PendingQueueCount { get; }

    /// <summary>
    /// Количество активных задач, исполняемых в данный момент.
    /// </summary>
    int ActiveRequestsCount { get; }

    /// <summary>
    /// Общая статистика обработанных и сглаженных пиковых задач.
    /// </summary>
    LoadBalancerStats GetStats();
}

public record LoadBalancerStats(
    int TotalProcessed,
    int TotalThrottled,
    int QueueLength,
    int ActiveExecutions,
    double CurrentRpmEstimate);

/// <summary>
/// Балансировщик нагрузки и сглаживатель очередей для предотвращения превышения Rate Limit (HTTP 429) нейросети.
/// Реализует Token Bucket / Leaky Bucket алгоритм с очередью сглаживания пиков.
/// </summary>
public class LlmLoadBalancer : ILlmLoadBalancer, IDisposable
{
    private readonly LoadBalancerOptions _options;
    private readonly ILogger<LlmLoadBalancer>? _logger;
    private readonly ILangfuseClient? _langfuse;

    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly object _queueLock = new();
    private readonly PriorityQueue<TaskCompletionSource<bool>, (int NegPriority, long Seq)> _priorityWaiters = new();
    private long _waiterSeq = 0;
    private int _availableSlots;

    private readonly object _rateLock = new();
    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly List<DateTime> _requestTimestamps = new();

    private int _totalProcessed = 0;
    private int _totalThrottled = 0;
    private int _activeCount = 0;
    private int _pendingQueueCount = 0;

    public int PendingQueueCount => _pendingQueueCount;
    public int ActiveRequestsCount => _activeCount;

    public LlmLoadBalancer(
        LoadBalancerOptions? options = null,
        ILogger<LlmLoadBalancer>? logger = null,
        ILangfuseClient? langfuse = null)
    {
        _options = options ?? new LoadBalancerOptions();
        _logger = logger;
        _langfuse = langfuse;
        _availableSlots = _options.MaxConcurrentRequests;
        _concurrencySemaphore = new SemaphoreSlim(_options.MaxConcurrentRequests, _options.MaxConcurrentRequests);
    }

    private Task AcquireSlotAsync(int priority, CancellationToken ct)
    {
        lock (_queueLock)
        {
            if (_availableSlots > 0 && _priorityWaiters.Count == 0)
            {
                _availableSlots--;
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var seq = Interlocked.Increment(ref _waiterSeq);
            _priorityWaiters.Enqueue(tcs, (-priority, seq));

            if (ct.CanBeCanceled)
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
            }

            return tcs.Task;
        }
    }

    private void ReleaseSlot()
    {
        lock (_queueLock)
        {
            while (_priorityWaiters.Count > 0)
            {
                var nextTcs = _priorityWaiters.Dequeue();
                if (nextTcs.TrySetResult(true))
                {
                    return;
                }
            }

            _availableSlots++;
        }
    }

    public async Task<TResult> ScheduleAsync<TResult>(
        string taskName,
        Func<CancellationToken, Task<TResult>> work,
        string? traceId = null,
        int priority = 0,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _pendingQueueCount);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        bool slotAcquired = false;

        try
        {
            // 1. Ограничение конкурентных запросов с приоритетной очередью (C-Level / Director проходят первыми)
            await AcquireSlotAsync(priority, ct);
            slotAcquired = true;
            Interlocked.Increment(ref _activeCount);
            Interlocked.Decrement(ref _pendingQueueCount);

            // 2. Token Bucket / Rate Limiter: сглаживание пиков (41+ запросов ждут освобождения минутного слота без ошибок)
            int waitMs = 0;
            lock (_rateLock)
            {
                var now = DateTime.UtcNow;

                // Очищаем таймстемпы старше 1 минуты
                var oneMinuteAgo = now.AddMinutes(-1);
                _requestTimestamps.RemoveAll(ts => ts < oneMinuteAgo);

                // Проверяем лимит RPM (учитываем точный индекс слота при очереди 41, 42, 43... запросов)
                if (_requestTimestamps.Count >= _options.MaxRequestsPerMinute)
                {
                    var slotIndex = _requestTimestamps.Count - _options.MaxRequestsPerMinute;
                    if (slotIndex >= _requestTimestamps.Count) slotIndex = _requestTimestamps.Count - 1;
                    var slotTimestamp = _requestTimestamps[slotIndex];
                    var timeUntilReset = (int)(slotTimestamp.AddMinutes(1) - now).TotalMilliseconds;
                    if (timeUntilReset > waitMs) waitMs = Math.Max(timeUntilReset, 50);
                }

                // Проверяем минимальный интервал сглаживания между запросами (Leaky Bucket spacing)
                var elapsedSinceLast = (int)(now - _lastRequestTime).TotalMilliseconds;
                if (elapsedSinceLast < _options.MinIntervalBetweenRequestsMs)
                {
                    var spacingWait = _options.MinIntervalBetweenRequestsMs - elapsedSinceLast;
                    if (spacingWait > waitMs) waitMs = spacingWait;
                }

                // Фиксируем планируемое время выполнения
                var scheduledExecutionTime = now.AddMilliseconds(waitMs);
                _lastRequestTime = scheduledExecutionTime;
                _requestTimestamps.Add(scheduledExecutionTime);
            }

            if (waitMs > 0)
            {
                Interlocked.Increment(ref _totalThrottled);
                _logger?.LogInformation(
                    "🚦 [LoadBalancer] Сглаживание пика для '{TaskName}' (приоритет={Priority}). Ожидание {WaitMs}мс перед вызовом LLM. В очереди: {QueueCount}",
                    taskName, priority, waitMs, _pendingQueueCount);

                if (!string.IsNullOrEmpty(traceId) && _langfuse != null)
                {
                    _ = _langfuse.LogEventAsync(traceId, "LoadBalancer:Throttling", "INFO",
                        $"Запрос отложен на {waitMs}мс для защиты от 429 Rate Limit. Активно: {_activeCount}, В очереди: {_pendingQueueCount}", ct: ct);
                }

                await Task.Delay(waitMs, ct);
            }

            // 3. Выполнение непосредственной работы
            var result = await work(ct);
            Interlocked.Increment(ref _totalProcessed);

            stopwatch.Stop();
            if (!string.IsNullOrEmpty(traceId) && _langfuse != null && stopwatch.ElapsedMilliseconds > 1000)
            {
                _ = _langfuse.LogEventAsync(traceId, "LoadBalancer:ExecutionCompleted", "INFO",
                    $"Узел '{taskName}' выполнен за {stopwatch.ElapsedMilliseconds}мс", ct: ct);
            }

            return result;
        }
        finally
        {
            if (slotAcquired)
            {
                Interlocked.Decrement(ref _activeCount);
                ReleaseSlot();
            }
            else
            {
                Interlocked.Decrement(ref _pendingQueueCount);
            }
        }
    }

    public LoadBalancerStats GetStats()
    {
        lock (_rateLock)
        {
            var now = DateTime.UtcNow;
            var oneMinuteAgo = now.AddMinutes(-1);
            _requestTimestamps.RemoveAll(ts => ts < oneMinuteAgo);

            return new LoadBalancerStats(
                TotalProcessed: _totalProcessed,
                TotalThrottled: _totalThrottled,
                QueueLength: _pendingQueueCount,
                ActiveExecutions: _activeCount,
                CurrentRpmEstimate: _requestTimestamps.Count
            );
        }
    }

    public void Dispose()
    {
        _concurrencySemaphore.Dispose();
    }
}

