using Fotoprint.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace Fotoprint.Core.Resilience;

public record RollbackResult<T>(
    bool IsSuccess,
    T? Value,
    bool RollbackExecuted,
    string? ErrorMessage,
    int AttemptsMade,
    Exception? LastException)
{
    public static RollbackResult<T> Success(T value, int attempts) =>
        new(true, value, false, null, attempts, null);

    public static RollbackResult<T> FailedWithRollback(string errorMessage, int attempts, Exception? lastException, bool rollbackSuccess) =>
        new(false, default, rollbackSuccess, errorMessage, attempts, lastException);
}

public interface IRollbackManager
{
    /// <summary>
    /// Выполняет действие с политикой 3 повторов (3 retries).
    /// В случае неудачи после 3 повторов автоматически вызывает compensating action (откат транзакции / состояния)
    /// и фиксирует инцидент в Langfuse.
    /// </summary>
    Task<RollbackResult<T>> ExecuteWithRollbackAsync<T>(
        string nodeName,
        Func<int, CancellationToken, Task<T>> action,
        Func<Exception, CancellationToken, Task> compensatingRollbackAction,
        string? traceId = null,
        int maxRetries = 3,
        int initialDelayMs = 150,
        CancellationToken ct = default);

    /// <summary>
    /// Вариант без возвращаемого значения.
    /// </summary>
    Task<RollbackResult<bool>> ExecuteWithRollbackAsync(
        string nodeName,
        Func<int, CancellationToken, Task> action,
        Func<Exception, CancellationToken, Task> compensatingRollbackAction,
        string? traceId = null,
        int maxRetries = 3,
        int initialDelayMs = 150,
        CancellationToken ct = default);

    /// <summary>
    /// Получить статистику инцидентов и откатов.
    /// </summary>
    RollbackStats GetStats();
}

public record RollbackStats(
    int TotalExecutions,
    int TotalRecoveredOnRetry,
    int TotalFailedAndRolledBack);

public class RollbackManager : IRollbackManager
{
    private readonly ILogger<RollbackManager>? _logger;
    private readonly ILangfuseClient? _langfuse;

    private int _totalExecutions = 0;
    private int _totalRecoveredOnRetry = 0;
    private int _totalFailedAndRolledBack = 0;

    public RollbackManager(ILogger<RollbackManager>? logger = null, ILangfuseClient? langfuse = null)
    {
        _logger = logger;
        _langfuse = langfuse;
    }

    public async Task<RollbackResult<T>> ExecuteWithRollbackAsync<T>(
        string nodeName,
        Func<int, CancellationToken, Task<T>> action,
        Func<Exception, CancellationToken, Task> compensatingRollbackAction,
        string? traceId = null,
        int maxRetries = 3,
        int initialDelayMs = 150,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _totalExecutions);
        int attempts = 0;
        Exception? lastException = null;

        // Всего попыток: 1 основная + maxRetries повторов (итого 4 попытки при maxRetries = 3)
        int totalAttemptsAllowed = 1 + maxRetries;

        while (attempts < totalAttemptsAllowed)
        {
            attempts++;
            try
            {
                ct.ThrowIfCancellationRequested();

                var result = await action(attempts, ct);

                if (attempts > 1)
                {
                    Interlocked.Increment(ref _totalRecoveredOnRetry);
                    _logger?.LogInformation(
                        "✅ [RollbackSystem] Узел '{NodeName}' успешно восстановился на попытке {Attempt}/{TotalAttempts}",
                        nodeName, attempts, totalAttemptsAllowed);

                    if (!string.IsNullOrEmpty(traceId) && _langfuse != null)
                    {
                        _ = _langfuse.LogEventAsync(traceId, "Node:RecoveredOnRetry", "INFO",
                            $"Узел '{nodeName}' восстановился на повторе {attempts - 1}", ct: ct);
                    }
                }

                return RollbackResult<T>.Success(result, attempts);
            }
            catch (Exception ex) when (IsRetryable(ex) && attempts < totalAttemptsAllowed)
            {
                lastException = ex;

                // Экспоненциальный backoff: initialDelay * 2^(attempt-1) + небольшой джиттер
                int delayMs = initialDelayMs * (1 << (attempts - 1)) + Random.Shared.Next(10, 50);

                _logger?.LogWarning(
                    "⚠️ [RollbackSystem] Ошибка узла '{NodeName}' (попытка {Attempt}/{TotalAttempts}): {Message}. Повтор через {DelayMs}мс...",
                    nodeName, attempts, totalAttemptsAllowed, ex.Message, delayMs);

                if (!string.IsNullOrEmpty(traceId) && _langfuse != null)
                {
                    _ = _langfuse.LogEventAsync(traceId, "Node:RetryAttempt", "WARNING",
                        $"Повтор {attempts} для узла '{nodeName}'. Задержка {delayMs}мс. Ошибка: {ex.Message}", ct: ct);
                }

                await Task.Delay(delayMs, ct);
            }
            catch (Exception ex)
            {
                // Неперехватываемая или фатальная ошибка
                lastException = ex;
                break;
            }
        }

        // =========================================================================
        // ВСЕ 3 ПОВТОРА ИСЧЕРПАНЫ -> ЗАПУСК РОЛЛБЭКА (КОМПЕНСИРУЮЩЕГО ДЕЙСТВИЯ)
        // =========================================================================
        Interlocked.Increment(ref _totalFailedAndRolledBack);
        _logger?.LogError(
            lastException,
            "🚨 [RollbackSystem] Узел '{NodeName}' окончательно упал после {Attempts} попыток. Запуск процедуры РОЛЛБЭКА (компенсации)...",
            nodeName, attempts);

        bool rollbackSuccess = false;
        try
        {
            await compensatingRollbackAction(lastException!, ct);
            rollbackSuccess = true;
            _logger?.LogInformation("🔄 [RollbackSystem] Компенсирующее действие (РОЛЛБЭК) для узла '{NodeName}' успешно выполнено.", nodeName);
        }
        catch (Exception rollbackEx)
        {
            _logger?.LogCritical(rollbackEx, "💥 [RollbackSystem] Сбой выполнения РОЛЛБЭКА для узла '{NodeName}': {Message}", nodeName, rollbackEx.Message);
        }

        // Аудит в Langfuse
        if (!string.IsNullOrEmpty(traceId) && _langfuse != null)
        {
            var rollbackStatusMsg = rollbackSuccess ? "Роллбэк успешно применен" : "Сбой применения роллбэка";
            _ = _langfuse.LogEventAsync(
                traceId,
                "Node:RollbackTriggered",
                "ERROR",
                $"Узел '{nodeName}' упал после {attempts} попыток. {rollbackStatusMsg}. Причина: {lastException?.Message}",
                metadata: new
                {
                    node_name = nodeName,
                    attempts_made = attempts,
                    error = lastException?.Message,
                    rollback_success = rollbackSuccess
                },
                ct: ct);
        }

        return RollbackResult<T>.FailedWithRollback(
            errorMessage: lastException?.Message ?? "Превышено максимальное число повторов (3 ретрая)",
            attempts: attempts,
            lastException: lastException,
            rollbackSuccess: rollbackSuccess);
    }

    public async Task<RollbackResult<bool>> ExecuteWithRollbackAsync(
        string nodeName,
        Func<int, CancellationToken, Task> action,
        Func<Exception, CancellationToken, Task> compensatingRollbackAction,
        string? traceId = null,
        int maxRetries = 3,
        int initialDelayMs = 150,
        CancellationToken ct = default)
    {
        return await ExecuteWithRollbackAsync<bool>(
            nodeName,
            async (attempt, token) =>
            {
                await action(attempt, token);
                return true;
            },
            compensatingRollbackAction,
            traceId,
            maxRetries,
            initialDelayMs,
            ct);
    }

    public RollbackStats GetStats() =>
        new(_totalExecutions, _totalRecoveredOnRetry, _totalFailedAndRolledBack);

    private static bool IsRetryable(Exception ex)
    {
        // Не ретраим отмену операции по токену
        if (ex is OperationCanceledException) return false;

        // Сетевые сбои, таймауты, HttpRequestException
        if (ex is HttpRequestException or TimeoutException or IOException)
            return true;

        // Пользовательское исключение сбоя узла
        if (ex is NodeTransientException)
            return true;

        return true;
    }
}

/// <summary>
/// Маркер временной ошибки узла, подлежащей ретраю
/// </summary>
public class NodeTransientException : Exception
{
    public int? StatusCode { get; }

    public NodeTransientException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

