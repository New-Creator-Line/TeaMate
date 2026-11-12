using Fotoprint.Core.Models;

namespace Fotoprint.Core.Notifications;

/// <summary>
/// Provides timezone conversion, MSK day windowing, and deadline tier classification helpers.
/// </summary>
public static class NotificationTimeHelper
{
    private static readonly HashSet<string> TerminalTaskStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "cancelled",
        "completed",
        "done",
        "closed"
    };

    /// <summary>
    /// Resolves a configured timezone ID (e.g. "Europe/Moscow") with cross-platform fallback to UTC+3.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId = "Europe/Moscow")
    {
        var candidates = new[]
        {
            timeZoneId,
            "Europe/Moscow",
            "Russian Standard Time"
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Fotoprint-MSK",
            TimeSpan.FromHours(3),
            "Moscow Standard Time",
            "Moscow Standard Time");
    }

    /// <summary>
    /// Normalizes a <see cref="DateTime"/> value to UTC (treating <see cref="DateTimeKind.Unspecified"/> as UTC).
    /// </summary>
    public static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>
    /// Converts a UTC timestamp into the specified target timezone.
    /// </summary>
    public static DateTime ConvertUtcToZone(DateTime dateTime, TimeZoneInfo tz)
    {
        var utc = EnsureUtc(dateTime);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    /// <summary>
    /// Computes UTC boundaries matching the current calendar day in the target timezone.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc, DateOnly LocalDate) GetDayBoundsUtc(
        DateTimeOffset utcNow,
        TimeZoneInfo tz)
    {
        var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);
        var localStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, tz);
        return (startUtc, endUtc, DateOnly.FromDateTime(localStart));
    }

    /// <summary>
    /// Classifies an action item's deadline urgency and builds a reschedule-aware deduplication key.
    /// Tasks overdue beyond <paramref name="maxOverdueDays"/> are skipped to prevent alert floods.
    /// </summary>
    public static (TaskDeadlineTier Tier, string? DedupKey) EvaluateTaskDeadline(
        ActionItem item,
        int employeeId,
        DateTimeOffset utcNow,
        TimeZoneInfo tz,
        int urgentHours = 2,
        int dueSoonHours = 24,
        int maxOverdueDays = 30)
    {
        if (!item.Deadline.HasValue || TerminalTaskStatuses.Contains(item.Status ?? string.Empty))
        {
            return (TaskDeadlineTier.None, null);
        }

        var nowUtc = utcNow.UtcDateTime;
        var deadlineUtc = EnsureUtc(item.Deadline.Value);
        var effectiveUrgentHours = Math.Max(1, urgentHours);
        var effectiveDueSoonHours = Math.Max(effectiveUrgentHours + 1, dueSoonHours);
        var effectiveMaxOverdueDays = Math.Max(1, maxOverdueDays);

        if (deadlineUtc < nowUtc)
        {
            if (deadlineUtc < nowUtc.AddDays(-effectiveMaxOverdueDays))
            {
                return (TaskDeadlineTier.None, null);
            }

            var localDate = GetDayBoundsUtc(utcNow, tz).LocalDate;
            return (TaskDeadlineTier.Overdue, $"task_overdue:{localDate:yyyy-MM-dd}:{item.Id:N}:emp:{employeeId}");
        }

        if (deadlineUtc <= nowUtc.AddHours(effectiveUrgentHours))
        {
            return (TaskDeadlineTier.DueUrgent, $"task_due_{effectiveUrgentHours}h:{deadlineUtc:yyyyMMddHHmm}:{item.Id:N}:emp:{employeeId}");
        }

        if (deadlineUtc <= nowUtc.AddHours(effectiveDueSoonHours))
        {
            return (TaskDeadlineTier.DueSoon, $"task_due_{effectiveDueSoonHours}h:{deadlineUtc:yyyyMMddHHmm}:{item.Id:N}:emp:{employeeId}");
        }

        return (TaskDeadlineTier.None, null);
    }

    /// <summary>
    /// Builds a deduplication key for pre-meeting reminders keyed by meeting start timestamp (UTC minute).
    /// </summary>
    public static string BuildPreMeetingDedupKey(Guid meetingId, DateTime startTimeUtc, int employeeId) =>
        $"pre_meeting:{EnsureUtc(startTimeUtc):yyyyMMddHHmm}:{meetingId:N}:emp:{employeeId}";

    /// <summary>
    /// Builds a daily deduplication key for the employee's morning schedule digest.
    /// </summary>
    public static string BuildMorningDigestDedupKey(DateOnly localDateMsk, int employeeId) =>
        $"morning_digest:{localDateMsk:yyyy-MM-dd}:emp:{employeeId}";

    /// <summary>
    /// Parses flexible HH:mm / H:mm / H.mm / HH inputs into a <see cref="TimeOnly"/> value.
    /// </summary>
    public static bool TryParseTimeMsk(string? raw, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim().Replace('.', ':');
        if (int.TryParse(normalized, out var hourOnly) && hourOnly is >= 0 and <= 23)
        {
            time = new TimeOnly(hourOnly, 0);
            return true;
        }

        var parts = normalized.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var h)
            && int.TryParse(parts[1], out var m)
            && h is >= 0 and <= 23
            && m is >= 0 and <= 59)
        {
            time = new TimeOnly(h, m);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Formats a parsed time string as canonical "HH:mm", or returns <paramref name="fallback"/> when invalid.
    /// </summary>
    public static string NormalizeTimeMsk(string? raw, string fallback = "09:00") =>
        TryParseTimeMsk(raw, out var parsed)
            ? $"{parsed.Hour:D2}:{parsed.Minute:D2}"
            : fallback;

    /// <summary>
    /// Evaluates whether the current local time falls inside the employee's configured quiet hours.
    /// Supports both daytime ranges (e.g. 13:00–14:00) and overnight ranges (e.g. 22:00–08:00).
    /// </summary>
    public static bool IsQuietHoursActive(
        DateTimeOffset utcNow,
        TimeZoneInfo tz,
        string? quietStartMsk,
        string? quietEndMsk)
    {
        if (!TryParseTimeMsk(quietStartMsk, out var start) || !TryParseTimeMsk(quietEndMsk, out var end))
        {
            return false;
        }

        if (start == end)
        {
            return false;
        }

        var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);
        var current = TimeOnly.FromDateTime(localNow.DateTime);

        return start < end
            ? current >= start && current < end
            : current >= start || current < end;
    }

    /// <summary>
    /// Determines whether an employee's scheduled morning digest is due at <paramref name="utcNow"/>.
    /// Uses a bounded catch-up window (<paramref name="scheduleWindowMinutes"/>) so afternoon updates to morning times
    /// do not fire late in the afternoon, while brief scheduler restarts still catch up cleanly.
    /// </summary>
    public static bool IsMorningDigestDueNow(
        DateTimeOffset utcNow,
        TimeZoneInfo tz,
        EmployeeNotificationPreferences prefs,
        int scheduleWindowMinutes = 90)
    {
        if (!prefs.DigestEnabled)
        {
            return false;
        }

        var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);
        if (prefs.DigestWeekdaysOnly && localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        if (IsQuietHoursActive(utcNow, tz, prefs.QuietHoursStartMsk, prefs.QuietHoursEndMsk))
        {
            return false;
        }

        if (!TryParseTimeMsk(prefs.DigestTimeMsk, out var targetTime))
        {
            targetTime = new TimeOnly(9, 0);
        }

        var currentMinutes = localNow.Hour * 60 + localNow.Minute;
        var targetMinutes = targetTime.Hour * 60 + targetTime.Minute;
        var window = Math.Clamp(scheduleWindowMinutes, 5, 360);

        return currentMinutes >= targetMinutes && currentMinutes <= targetMinutes + window;
    }
}
