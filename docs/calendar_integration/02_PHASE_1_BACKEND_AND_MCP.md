# 02. Фаза 1: Бэкенд, Репозиторий и MCP-Сервис Календаря

## 1. Задачи Фазы 1

1. **Репозиторный слой (`Fotoprint.Core/Data/FotoprintRepository.cs`)**:
   - Реализовать метод `GetMeetingsForRangeAsync(DateTime fromUtc, DateTime toUtc, int? employeeId = null, CancellationToken ct = default)`.
   - Реализовать алгоритм `FindFreeSlotsAsync(...)` с обязательным **отсечением прошедших часов текущего дня** и 10-минутным буфером.
2. **MCP Сервис (`Fotoprint.Mcp.Calendar/Program.cs`)**:
   - Добавить эндпоинты `/tools/find_free_slots`, `/tools/create_meeting`, `/tools/get_calendar_events`.
   - Встроить явную **заглушку уведомлений (Notification Dispatch Stub)** с метаданными в ответ.
   - Реализовать проверку прав: `scope=all` доступен только роли `Director` или `Admin`.
3. **Конфигурация и Сеть**:
   - Пробросить порт `127.0.0.1:8084:8084` в `docker-compose.yml`.
   - Добавить `CalendarConfig` в `config.yaml` и `FotoprintConfig.cs`.
   - Использовать `_config.McpUrls.Calendar` вместо хардкода.
4. **API Gateway (`Fotoprint.Api`)**:
   - Реализовать проксирующие эндпоинты:
     - `GET /api/calendar/events` $\to$ прокси в `http://127.0.0.1:8084/tools/get_calendar_events`.
     - `POST /api/calendar/confirm` $\to$ прокси в `http://127.0.0.1:8084/tools/create_meeting`.

---

## 2. Алгоритм Поиска Слотов с Отсечением Прошедшего Времени

### Пошаговая реализация `FindFreeSlotsAsync`:

```csharp
public async Task<List<TimeSlot>> FindFreeSlotsAsync(
    int organizerId,
    List<int> participantIds,
    DateOnly targetDate,
    int durationMinutes,
    TimeOnly workingStart,
    TimeOnly workingEnd,
    int bufferMinutes,
    CancellationToken ct = default)
{
    // 1. Определение текущего времени по Москве (UTC+3)
    var mskZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"); // или "Europe/Moscow" на Linux/macOS
    var nowMsk = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, mskZone);
    var todayMsk = DateOnly.FromDateTime(nowMsk);
    var currentMskTime = TimeOnly.FromDateTime(nowMsk);

    // 2. Отсечение прошедшего времени: если дата — сегодня, нельзя предлагать слоты в прошлом
    var effectiveStart = workingStart;
    if (targetDate < todayMsk)
    {
        // Дата уже в прошлом — свободных слотов быть не может
        return new List<TimeSlot>();
    }
    if (targetDate == todayMsk)
    {
        // Добавляем технологический запас 5 минут на оформление встречи
        var earliestPossible = currentMskTime.AddMinutes(5);
        if (earliestPossible > effectiveStart)
        {
            effectiveStart = earliestPossible;
        }
    }

    if (effectiveStart >= workingEnd)
    {
        // Рабочий день уже завершен
        return new List<TimeSlot>();
    }

    // 3. Запрос существующих встреч из PostgreSQL на целевой день (UTC)
    var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(targetDate.ToDateTime(workingStart), mskZone);
    var dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(targetDate.ToDateTime(workingEnd), mskZone);

    var allEmployeeIds = participantIds.Concat(new[] { organizerId }).Distinct().ToList();
    var existingMeetings = await GetMeetingsForEmployeesAsync(allEmployeeIds, dayStartUtc, dayEndUtc, ct);

    // 4. Поиск окон с шагом 30 минут (или durationMinutes, если меньше)
    var stepMinutes = Math.Min(30, durationMinutes);
    var availableSlots = new List<TimeSlot>();

    // Выравниваем старт сетки по 15/30-минутному интервалу
    var roundedMinute = (effectiveStart.Minute / stepMinutes + (effectiveStart.Minute % stepMinutes == 0 ? 0 : 1)) * stepMinutes;
    var cursor = new TimeOnly(effectiveStart.Hour, 0).AddMinutes(roundedMinute);
    if (cursor < effectiveStart) cursor = cursor.AddMinutes(stepMinutes);

    while (cursor.AddMinutes(durationMinutes) <= workingEnd)
    {
        var slotStartMsk = targetDate.ToDateTime(cursor);
        var slotEndMsk = slotStartMsk.AddMinutes(durationMinutes);

        var slotStartUtc = TimeZoneInfo.ConvertTimeToUtc(slotStartMsk, mskZone);
        var slotEndUtc = TimeZoneInfo.ConvertTimeToUtc(slotEndMsk, mskZone);

        // Проверка коллизии с учетом буфера
        var hasConflict = existingMeetings.Any(m =>
            slotStartUtc < (m.EndTime ?? m.StartTime.AddMinutes(30)).AddMinutes(bufferMinutes) &&
            slotEndUtc > m.StartTime.AddMinutes(-bufferMinutes)
        );

        if (!hasConflict)
        {
            availableSlots.Add(new TimeSlot
            {
                StartTimeUtc = slotStartUtc,
                EndTimeUtc = slotEndUtc,
                Label = $"{cursor:HH\\:mm} - {cursor.AddMinutes(durationMinutes):HH\\:mm}"
            });
        }

        cursor = cursor.AddMinutes(stepMinutes);
    }

    return availableSlots;
}
```

---

## 3. Заглушка Оповещений Участников (Notification Dispatch Stub)

Когда встреча успешно создается в `create_meeting`:
1. В таблицу `meetings` записывается строка со статусом `'scheduled'`.
2. В поле `metadata` добавляется JSON-блок:
   ```json
   {
     "notifications": {
       "status": "simulated",
       "channel": "Telegram & Bitrix24",
       "timestamp": "2026-11-12T14:30:00Z",
       "note": "⚠️ [Заглушка]: Оповещение участникам эмулировано для демонстрации трека хакатона"
     }
   }
   ```
3. В API-ответе возвращается структура `notification_dispatch`:
   ```csharp
   notification_dispatch = new
   {
       status = "simulated",
       channels = new[] { "Telegram", "Bitrix24" },
       message = "⚠️ [Заглушка спринта]: Оповещение участникам смоделировано (готово к подключению вебхука)"
   }
   ```
4. В веб-чате это выводится специальным бейджем, чтобы жюри видело: интеграционная точка заложена и работает как эмуляция.

---

## 4. Контроль Доступа RBAC в `get_calendar_events`

```csharp
app.MapGet("/tools/get_calendar_events", async (
    DateTime? start_date, 
    DateTime? end_date, 
    int? employee_id, 
    string? scope, 
    HttpContext context, 
    IFotoprintRepository repo, 
    ISecurityValidator security) =>
{
    var callerTelegramId = TryGetTelegramId(context);
    var caller = await security.GetEmployeeAsync(callerTelegramId);

    var isDirectorOrAdmin = caller.Roles.Contains("Director") || caller.Roles.Contains("Admin");

    // Если запрошен режим "Вся компания", но пользователь не директор/админ
    if (scope == "all" && !isDirectorOrAdmin)
    {
        return Results.Json(new
        {
            status = "forbidden",
            message = "Просмотр расписания всей компании доступен только руководству (роль Director)"
        }, statusCode: 403);
    }

    var filterEmployeeId = (scope == "all") ? (int?)null : (employee_id ?? caller.Id);
    var events = await repo.GetMeetingsForRangeAsync(start_date ?? DateTime.UtcNow, end_date ?? DateTime.UtcNow.AddDays(30), filterEmployeeId);

    return Results.Ok(new { status = "success", scope, events });
});
```
