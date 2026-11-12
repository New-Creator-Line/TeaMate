namespace Fotoprint.Core.Integrations;

/// <summary>
/// Канонические сущности (п. 4.2 ТЗ) и интерфейсы для замены моков (Bitrix24/Calendar/1C)
/// на адаптер кастомной CRM Заказчика без изменения ядра AI-ассистента.
/// </summary>
public sealed record CrmUser(
    long MasterId,
    string ExternalUserId,
    string Channel,
    string FullName,
    string Department,
    string Position,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> AccessGroups);

public sealed record CrmCustomer(
    string CustomerId,
    string Name,
    string Category,
    string? ContactMasked,
    decimal? CreditLimitByn = null);

public sealed record CrmProduct(
    string ProductCode,
    string Name,
    string Unit,
    decimal BasePriceByn);

public sealed record CrmOrder(
    string OrderId,
    string CustomerName,
    string ProductName,
    int Quantity,
    decimal AmountByn,
    string Currency,
    string Status,
    DateTimeOffset? DeadlineUtc);

public sealed record CrmTask(
    string TaskId,
    string Title,
    string? Description,
    string ResponsibleName,
    DateTimeOffset? DeadlineUtc,
    string Status,
    string? SourceQuote = null);

public sealed record CrmDeal(
    string DealId,
    string Title,
    string Stage,
    decimal AmountByn,
    string Currency,
    string? ManagerName);

public sealed record CrmAlert(
    string AlertId,
    string Severity,
    string Title,
    string Source,
    DateTimeOffset GeneratedAtUtc);

public sealed record CalendarSlot(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int DurationMinutes,
    bool IsAvailable);

public interface ICrmTasks
{
    Task<IReadOnlyList<CrmTask>> GetUserTasksAsync(long masterId, string? statusFilter = null, CancellationToken ct = default);
    Task<CrmTask> CreateTaskAsync(string title, string? description, int assigneeEmployeeId, DateTimeOffset? deadlineUtc, bool userConfirmed, CancellationToken ct = default);
}

public interface ICrmDeals
{
    Task<IReadOnlyList<CrmDeal>> SearchDealsAsync(string? query, CancellationToken ct = default);
    Task<CrmOrder?> GetOrderStatusAsync(string orderId, CancellationToken ct = default);
}

public interface ICalendar
{
    Task<IReadOnlyList<CalendarSlot>> FindFreeSlotsAsync(DateOnly date, int durationMinutes, CancellationToken ct = default);
    Task<string> ScheduleMeetingAsync(string title, string? description, DateTimeOffset startUtc, int durationMinutes, IReadOnlyList<string> participants, bool userConfirmed, CancellationToken ct = default);
}
