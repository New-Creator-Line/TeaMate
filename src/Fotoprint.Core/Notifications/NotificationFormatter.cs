using System.Globalization;
using System.Text;
using Fotoprint.Core.Models;

namespace Fotoprint.Core.Notifications;

/// <summary>
/// Formats Telegram notification messages in clean Russian text with MSK timestamps.
/// </summary>
public static class NotificationFormatter
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public static string FormatPreMeetingReminder(
        Employee employee,
        Meeting meeting,
        IReadOnlyList<ActionItem> openTasksForEmployee,
        IReadOnlyList<Employee> allEmployees,
        DateTimeOffset utcNow,
        TimeZoneInfo tz)
    {
        var startLocal = NotificationTimeHelper.ConvertUtcToZone(meeting.StartTime, tz);
        var nowLocal = NotificationTimeHelper.ConvertUtcToZone(utcNow.UtcDateTime, tz);
        var minutesUntilStart = (int)Math.Round((startLocal - nowLocal).TotalMinutes);

        var timingLabel = minutesUntilStart > 1
            ? $"через {minutesUntilStart} мин ({startLocal:HH:mm} МСК)"
            : minutesUntilStart >= 0
                ? $"прямо сейчас ({startLocal:HH:mm} МСК)"
                : $"уже началась в {startLocal:HH:mm} МСК";

        var organizerName = allEmployees
            .FirstOrDefault(e => e.Id == meeting.OrganizerId)?.FullName ?? "Не указан";

        var participantNames = meeting.ParticipantIds
            .Distinct()
            .Select(pid => allEmployees.FirstOrDefault(e => e.Id == pid)?.FullName ?? $"Сотрудник #{pid}")
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"⏰ Напоминание о встрече {timingLabel}");
        sb.AppendLine();
        sb.AppendLine($"📌 Тема: {meeting.Title}");
        sb.AppendLine($"🕒 Начало: {startLocal.ToString("dd.MM.yyyy HH:mm", RuCulture)} МСК");
        sb.AppendLine($"👤 Организатор: {organizerName}");
        if (participantNames.Count > 0)
        {
            sb.AppendLine($"👥 Участники: {string.Join(", ", participantNames)}");
        }

        if (!string.IsNullOrWhiteSpace(meeting.Description))
        {
            sb.AppendLine();
            sb.AppendLine($"📝 Повестка: {meeting.Description.Trim()}");
        }

        if (openTasksForEmployee.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"📋 Ваши открытые поручения ({openTasksForEmployee.Count}):");
            foreach (var task in openTasksForEmployee.Take(5))
            {
                sb.AppendLine($"• {FormatTaskLine(task, utcNow.UtcDateTime, tz)}");
            }

            if (openTasksForEmployee.Count > 5)
            {
                sb.AppendLine($"…и ещё {openTasksForEmployee.Count - 5} поруч.");
            }
        }

        sb.AppendLine();
        sb.Append("💬 Можете задать вопрос прямо здесь — контекст встречи уже сохранён.");
        return sb.ToString().TrimEnd();
    }

    public static string FormatMorningDigest(
        Employee employee,
        DateOnly mskDate,
        IReadOnlyList<Meeting> todaysMeetings,
        IReadOnlyList<ActionItem> employeeOpenTasks,
        DateTimeOffset utcNow,
        TimeZoneInfo tz)
    {
        var greetingName = string.IsNullOrWhiteSpace(employee.FirstName)
            ? employee.FullName
            : employee.FirstName;
        var dateLabel = mskDate.ToString("dd.MM.yyyy (dddd)", RuCulture);

        var sb = new StringBuilder();
        sb.AppendLine($"🌅 Доброе утро, {greetingName}!");
        sb.AppendLine($"Сводка на {dateLabel} МСК:");
        sb.AppendLine();

        sb.AppendLine($"📅 Встречи на сегодня ({todaysMeetings.Count}):");
        if (todaysMeetings.Count == 0)
        {
            sb.AppendLine("• На сегодня встреч не запланировано.");
        }
        else
        {
            foreach (var meeting in todaysMeetings)
            {
                var startLocal = NotificationTimeHelper.ConvertUtcToZone(meeting.StartTime, tz);
                var timeRange = meeting.EndTime.HasValue
                    ? $"{startLocal:HH:mm}–{NotificationTimeHelper.ConvertUtcToZone(meeting.EndTime.Value, tz):HH:mm}"
                    : $"{startLocal:HH:mm}";
                sb.AppendLine($"• {timeRange} МСК — {meeting.Title}");
            }
        }

        sb.AppendLine();
        var overdueTasks = employeeOpenTasks
            .Where(t => t.Deadline.HasValue && NotificationTimeHelper.EnsureUtc(t.Deadline.Value) < utcNow.UtcDateTime)
            .ToList();
        var activeTasks = employeeOpenTasks
            .Where(t => !t.Deadline.HasValue || NotificationTimeHelper.EnsureUtc(t.Deadline.Value) >= utcNow.UtcDateTime)
            .ToList();

        sb.AppendLine($"✅ Открытые поручения ({employeeOpenTasks.Count}):");
        if (employeeOpenTasks.Count == 0)
        {
            sb.AppendLine("• Незакрытых поручений нет.");
        }
        else
        {
            if (overdueTasks.Count > 0)
            {
                sb.AppendLine($"🔴 Просрочено ({overdueTasks.Count}):");
                foreach (var task in overdueTasks.Take(5))
                {
                    sb.AppendLine($"• {FormatTaskLine(task, utcNow.UtcDateTime, tz)}");
                }
            }

            if (activeTasks.Count > 0)
            {
                sb.AppendLine($"📌 В работе ({activeTasks.Count}):");
                foreach (var task in activeTasks.Take(7))
                {
                    sb.AppendLine($"• {FormatTaskLine(task, utcNow.UtcDateTime, tz)}");
                }

                if (activeTasks.Count > 7)
                {
                    sb.AppendLine($"…и ещё {activeTasks.Count - 7} задач(и).");
                }
            }
        }

        sb.AppendLine();
        sb.Append("ℹ️ Управление уведомлениями: /notifications");
        return sb.ToString().TrimEnd();
    }

    public static string FormatTaskDeadlineReminder(
        Employee employee,
        ActionItem item,
        TaskDeadlineTier tier,
        DateTimeOffset utcNow,
        TimeZoneInfo tz)
    {
        var greetingName = string.IsNullOrWhiteSpace(employee.FirstName)
            ? employee.FullName
            : employee.FirstName;

        var header = tier switch
        {
            TaskDeadlineTier.Overdue => "🔴 Просроченная задача",
            TaskDeadlineTier.DueUrgent => "🔥 Срочный дедлайн по задаче (менее 2 часов)",
            TaskDeadlineTier.DueSoon => "⏳ Напоминание о сроке задачи (в течение 24 часов)",
            _ => "📋 Напоминание о задаче"
        };

        var sb = new StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine();
        sb.AppendLine($"👤 Исполнитель: {greetingName}");
        sb.AppendLine($"📌 Задача: {item.Title}");

        if (item.Deadline.HasValue)
        {
            var deadlineLocal = NotificationTimeHelper.ConvertUtcToZone(item.Deadline.Value, tz);
            sb.AppendLine($"🗓 Дедлайн: {deadlineLocal.ToString("dd.MM.yyyy HH:mm", RuCulture)} МСК");
        }

        sb.AppendLine($"📊 Статус: {TranslateTaskStatus(item.Status)}");
        if (item.Bitrix24TaskId.HasValue)
        {
            sb.AppendLine($"🔗 Битрикс24 ID: #{item.Bitrix24TaskId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            sb.AppendLine();
            sb.AppendLine($"📝 Детали: {item.Description.Trim()}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatTaskLine(ActionItem task, DateTime utcNow, TimeZoneInfo tz)
    {
        if (!task.Deadline.HasValue)
        {
            return $"{task.Title} (без срока)";
        }

        var deadlineUtc = NotificationTimeHelper.EnsureUtc(task.Deadline.Value);
        var deadlineLocal = NotificationTimeHelper.ConvertUtcToZone(deadlineUtc, tz);
        var prefix = deadlineUtc < utcNow ? "ПРОСРОЧЕНО, срок " : "до ";
        return $"{task.Title} ({prefix}{deadlineLocal:dd.MM HH:mm} МСК)";
    }

    private static string TranslateTaskStatus(string? status) => (status ?? string.Empty).ToLowerInvariant() switch
    {
        "pending" => "Ожидает выполнения",
        "confirmed" => "Подтверждена",
        "exported_to_bitrix" => "В работе (Битрикс24)",
        "cancelled" => "Отменена",
        _ => string.IsNullOrWhiteSpace(status) ? "В работе" : status
    };

    public static string FormatNotificationSettingsSummary(
        Employee employee,
        EmployeeNotificationPreferences prefs)
    {
        var globalStatus = employee.NotificationsEnabled ? "✅ Включены" : "🔕 Выключены";
        var digestStatus = prefs.DigestEnabled
            ? $"✅ В {prefs.DigestTimeMsk} МСК ({(prefs.DigestWeekdaysOnly ? "пн–пт" : "ежедневно")})"
            : "🔕 Выключена";
        var preMeetingStatus = prefs.PreMeetingEnabled
            ? $"✅ За {prefs.PreMeetingWindowMinutes} мин до начала"
            : "🔕 Выключены";
        var taskDeadlinesStatus = prefs.TaskDeadlinesEnabled
            ? $"✅ За {prefs.TaskDueSoonHours} ч, за {prefs.TaskUrgentHours} ч и при просрочке"
            : "🔕 Выключены";
        var quietHours = !string.IsNullOrWhiteSpace(prefs.QuietHoursStartMsk) && !string.IsNullOrWhiteSpace(prefs.QuietHoursEndMsk)
            ? $"{prefs.QuietHoursStartMsk}–{prefs.QuietHoursEndMsk} МСК"
            : "не заданы";

        var sb = new StringBuilder();
        sb.AppendLine($"🔔 Настройки уведомлений ({employee.FullName}):");
        sb.AppendLine($"• Общий статус: {globalStatus}");
        sb.AppendLine($"• Утренняя сводка: {digestStatus}");
        sb.AppendLine($"• Напоминания о встречах: {preMeetingStatus}");
        sb.AppendLine($"• Напоминания о дедлайнах задач: {taskDeadlinesStatus}");
        sb.AppendLine($"• Тихие часы: {quietHours}");
        sb.AppendLine($"• Канал доставки: {prefs.PreferredChannel}");
        sb.AppendLine();
        sb.AppendLine("💬 Вы можете изменить время и параметры прямо в чате обычным сообщением, например:");
        sb.AppendLine("— «Перенеси утреннюю сводку на 08:30»");
        sb.AppendLine("— «Напоминай о встречах за 15 минут»");
        sb.AppendLine("— «Включи тихие часы с 22:00 до 08:00»");
        sb.AppendLine("— «Присылай сводку каждый день, включая выходные»");
        sb.Append("Также доступны быстрые команды: /notifications on | /notifications off");
        return sb.ToString();
    }
}
