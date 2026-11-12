using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Fotoprint.Core.Agents;

/// <summary>Одна запланированная мутирующая операция: имя инструмента и аргументы ровно в том виде, в каком их увидел пользователь.</summary>
public sealed record PendingToolCall(string Name, string ArgumentsJson);

/// <summary>План мутирующих действий, ожидающий явного подтверждения пользователя (AI-03, ТЗ 7.5).</summary>
public sealed record PendingAction(
    string Id,
    long OwnerKey,
    IReadOnlyList<PendingToolCall> Calls,
    string ArgsHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? TraceId);

public enum PendingActionOutcome { Confirmed, NotFound, Expired, HashMismatch }

public sealed record PendingActionTakeResult(PendingActionOutcome Outcome, PendingAction? Action);

/// <summary>
/// Двухфазное подтверждение: сначала <see cref="Propose"/> (ничего не выполняется), затем <see cref="TryTake"/> по id плана.
/// Исполняются сохранённые аргументы, а не повторный вызов LLM — подтверждённое действие побайтово совпадает с превью.
/// </summary>
public interface IPendingActionStore
{
    PendingAction Propose(long ownerKey, IReadOnlyList<PendingToolCall> calls, string? traceId);
    PendingAction? Peek(long ownerKey, string? actionId = null);
    PendingActionTakeResult TryTake(long ownerKey, string? actionId = null);
    bool Cancel(long ownerKey, string? actionId = null);
}

public sealed class InMemoryPendingActionStore : IPendingActionStore
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<long, PendingAction> _byOwner = new();
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _clock;

    public InMemoryPendingActionStore(TimeSpan? ttl = null, Func<DateTimeOffset>? clock = null)
    {
        _ttl = ttl ?? DefaultTtl;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public PendingAction Propose(long ownerKey, IReadOnlyList<PendingToolCall> calls, string? traceId)
    {
        var now = _clock();
        var action = new PendingAction(
            Id: Guid.NewGuid().ToString("N")[..12],
            OwnerKey: ownerKey,
            Calls: calls.ToList(),
            ArgsHash: ComputeHash(calls),
            CreatedAt: now,
            ExpiresAt: now.Add(_ttl),
            TraceId: traceId);

        // Новый план заменяет предыдущий: у пользователя одновременно один активный план.
        _byOwner[ownerKey] = action;
        return action;
    }

    public PendingAction? Peek(long ownerKey, string? actionId = null)
    {
        if (!_byOwner.TryGetValue(ownerKey, out var a)) return null;
        if (a.ExpiresAt <= _clock()) { _byOwner.TryRemove(ownerKey, out _); return null; }
        return actionId is null || string.Equals(a.Id, actionId, StringComparison.Ordinal) ? a : null;
    }

    public PendingActionTakeResult TryTake(long ownerKey, string? actionId = null)
    {
        if (!_byOwner.TryGetValue(ownerKey, out var a))
            return new(PendingActionOutcome.NotFound, null);

        if (actionId is not null && !string.Equals(a.Id, actionId, StringComparison.Ordinal))
            return new(PendingActionOutcome.NotFound, null);

        if (a.ExpiresAt <= _clock())
        {
            _byOwner.TryRemove(ownerKey, out _);
            return new(PendingActionOutcome.Expired, null);
        }

        // Атомарно забираем план: повторное подтверждение не выполнит действие второй раз (идемпотентность).
        if (!_byOwner.TryRemove(new KeyValuePair<long, PendingAction>(ownerKey, a)))
            return new(PendingActionOutcome.NotFound, null);

        return !string.Equals(ComputeHash(a.Calls), a.ArgsHash, StringComparison.Ordinal)
            ? new(PendingActionOutcome.HashMismatch, null)
            : new(PendingActionOutcome.Confirmed, a);
    }

    public bool Cancel(long ownerKey, string? actionId = null)
    {
        if (!_byOwner.TryGetValue(ownerKey, out var a)) return false;
        if (actionId is not null && !string.Equals(a.Id, actionId, StringComparison.Ordinal)) return false;
        return _byOwner.TryRemove(new KeyValuePair<long, PendingAction>(ownerKey, a));
    }

    public static string ComputeHash(IReadOnlyList<PendingToolCall> calls)
    {
        var sb = new StringBuilder();
        foreach (var c in calls) sb.Append(c.Name).Append('\u001f').Append(c.ArgumentsJson).Append('\u001e');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
