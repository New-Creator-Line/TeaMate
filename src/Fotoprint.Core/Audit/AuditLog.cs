using System.Text.Json;
using Fotoprint.Core.Security;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Fotoprint.Core.Audit;

/// <summary>Стандартные результаты аудируемых операций.</summary>
public static class AuditResult
{
    public const string Success = "success";
    public const string Denied = "denied";
    public const string Error = "error";
    public const string Cancelled = "cancelled";
    public const string Pending = "pending";
}

/// <summary>Запись журнала: пользователь, время, действие, объект и результат (ОТ-04, AI-06).</summary>
public sealed record AuditEvent(
    long ActorId,
    string Channel,
    string Action,
    string? ObjectType,
    string? ObjectId,
    string Result,
    string? TraceId = null,
    object? Details = null,
    DateTimeOffset? Timestamp = null,
    long Id = 0);

public interface IAuditLog
{
    /// <summary>Записывает событие. Никогда не выбрасывает исключения в вызывающий код.</summary>
    Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default);

    Task<IReadOnlyList<AuditEvent>> QueryAsync(long? actorId, string? action, int limit, CancellationToken ct = default);
}

public sealed class NullAuditLog : IAuditLog
{
    public static readonly NullAuditLog Instance = new();
    public Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<AuditEvent>> QueryAsync(long? actorId, string? action, int limit, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AuditEvent>>(Array.Empty<AuditEvent>());
}

/// <summary>Потокобезопасный журнал в памяти для тестов и деградации при недоступной БД.</summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly List<AuditEvent> _events = new();
    private readonly object _gate = new();

    public IReadOnlyList<AuditEvent> Events { get { lock (_gate) return _events.ToList(); } }

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        lock (_gate) _events.Add(auditEvent with { Timestamp = auditEvent.Timestamp ?? DateTimeOffset.UtcNow });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(long? actorId, string? action, int limit, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<AuditEvent> r = _events
                .Where(e => (actorId is null || e.ActorId == actorId) && (action is null || e.Action == action))
                .OrderByDescending(e => e.Timestamp).Take(Math.Clamp(limit, 1, 500)).ToList();
            return Task.FromResult(r);
        }
    }
}

/// <summary>
/// Ambient-доступ к журналу для кода, не охваченного DI (middleware, статические хелперы).
/// Устанавливается один раз при старте процесса.
/// </summary>
public static class AuditTrail
{
    private static IAuditLog _instance = NullAuditLog.Instance;
    public static IAuditLog Instance { get => _instance; set => _instance = value ?? NullAuditLog.Instance; }
}

/// <summary>Журнал аудита в PostgreSQL (таблица <c>audit_events</c>, TIMESTAMPTZ). Детали проходят через <see cref="PiiMasker"/>.</summary>
public sealed class PostgresAuditLog : IAuditLog, IAsyncDisposable
{
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS audit_events (
            id BIGSERIAL PRIMARY KEY,
            ts TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            actor_id BIGINT NOT NULL DEFAULT 0,
            channel TEXT NOT NULL DEFAULT 'unknown',
            action TEXT NOT NULL,
            object_type TEXT,
            object_id TEXT,
            result TEXT NOT NULL,
            trace_id TEXT,
            details JSONB
        );
        CREATE INDEX IF NOT EXISTS idx_audit_events_ts ON audit_events (ts DESC);
        CREATE INDEX IF NOT EXISTS idx_audit_events_actor ON audit_events (actor_id, ts DESC);
        """;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresAuditLog>? _logger;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private volatile bool _initialized;

    public PostgresAuditLog(string connectionString, ILogger<PostgresAuditLog>? logger = null)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _logger = logger;
    }

    public async Task RecordAsync(AuditEvent e, CancellationToken ct = default)
    {
        try
        {
            await EnsureSchemaAsync(ct);
            var detailsJson = e.Details is null
                ? null
                : PiiMasker.MaskForLogs(JsonSerializer.Serialize(e.Details, Json));

            await using var cmd = _dataSource.CreateCommand(
                "INSERT INTO audit_events (ts, actor_id, channel, action, object_type, object_id, result, trace_id, details) " +
                "VALUES (@ts, @actor, @channel, @action, @otype, @oid, @result, @trace, @details::jsonb)");
            cmd.Parameters.AddWithValue("ts", (e.Timestamp ?? DateTimeOffset.UtcNow).UtcDateTime);
            cmd.Parameters.AddWithValue("actor", e.ActorId);
            cmd.Parameters.AddWithValue("channel", e.Channel);
            cmd.Parameters.AddWithValue("action", e.Action);
            cmd.Parameters.AddWithValue("otype", (object?)e.ObjectType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("oid", (object?)e.ObjectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("result", e.Result);
            cmd.Parameters.AddWithValue("trace", (object?)e.TraceId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("details", (object?)detailsJson ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Аудит не должен ронять бизнес-операцию, но сбой записи обязан быть виден.
            _logger?.LogError(ex, "[Audit] Не удалось записать событие {Action} ({Result})", e.Action, e.Result);
        }
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(long? actorId, string? action, int limit, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var cmd = _dataSource.CreateCommand(
            "SELECT id, ts, actor_id, channel, action, object_type, object_id, result, trace_id, details::text " +
            "FROM audit_events WHERE (@actor::bigint IS NULL OR actor_id = @actor) AND (@action::text IS NULL OR action = @action) " +
            "ORDER BY ts DESC LIMIT @lim");
        cmd.Parameters.AddWithValue("actor", (object?)actorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("action", (object?)action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lim", Math.Clamp(limit, 1, 500));

        var list = new List<AuditEvent>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            object? details = null;
            if (!r.IsDBNull(9))
            {
                try { details = JsonSerializer.Deserialize<JsonElement>(r.GetString(9)); } catch (JsonException) { details = r.GetString(9); }
            }
            list.Add(new AuditEvent(
                ActorId: r.GetInt64(2), Channel: r.GetString(3), Action: r.GetString(4),
                ObjectType: r.IsDBNull(5) ? null : r.GetString(5), ObjectId: r.IsDBNull(6) ? null : r.GetString(6),
                Result: r.GetString(7), TraceId: r.IsDBNull(8) ? null : r.GetString(8), Details: details,
                Timestamp: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc)), Id: r.GetInt64(0)));
        }
        return list;
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            await using var cmd = _dataSource.CreateCommand(Ddl);
            await cmd.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }
        finally { _initGate.Release(); }
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
