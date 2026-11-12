# 01. Системная Архитектура и Контракты Взаимодействия

## 1. Архитектурные Решения и Исправления Развилок

### 1.1. Human-in-the-Loop: Бесстейтовый (Stateless) подход к черновикам
- **Решение**: В базе данных **не создаются** временные или мусорные записи со статусом `'draft'`.
- До момента нажатия кнопки «Подтвердить» в PostgreSQL ничего не пишется.
- Карточка подтверждения на фронтенде хранит строго валидированный объект параметров встречи (`title`, `startTime`, `endTime`, `organizerId`, `participantIds`) в `data-payload` кнопки.
- При клике на `[✅ Подтвердить и создать]` фронтенд отправляет эти параметры на `POST /api/calendar/confirm`. Только в этот момент в БД атомарно создается встреча со статусом `'scheduled'`. Это гарантирует 100% честность теста TC-06 (`COUNT(*) FROM meetings` до клика строго равен 0).

### 1.2. Генерация `action_card`: Детерминированный C# код (без галлюцинаций LLM)
- **Решение**: Нейросеть (Nemotron 120B / Gemini) отвечает **исключительно за текст на естественном языке** и вызов инструмента `find_free_slots`.
- Формирование JSON-структуры `action_card` (чипы слотов, кнопки длительности) выполняется **детерминированным кодом C#** в API Gateway / `FotoprintAiAgent.cs`:
  1. Модель вызывает инструмент `find_free_slots`.
  2. C#-агент получает результат работы инструмента от `Fotoprint.Mcp.Calendar`.
  3. C#-код гарантированно собирает строго типизированный DTO `ActionCard` и прикрепляет его к ответу API.
  4. Исключаются любые синтаксические поломки JSON или галлюцинации разметки со стороны LLM.

### 1.3. Маршрутизация и Изоляция MCP: Через шлюз и HTTP
- Веб-клиент обращается только к API Gateway (`Fotoprint.Api`).
- API Gateway не ходит в БД календаря напрямую, а проксирует запросы в микросервис `Fotoprint.Mcp.Calendar` (порт `8084`) по HTTP с передачей сервисного ключа `X-Internal-Service-Key` и контекста пользователя `X-Telegram-Id`.
- Это сохраняет единую точку контроля RBAC-доступа и микросервисную архитектуру.

---

## 2. Диаграмма Взаимодействия (To-Be)

```mermaid
flowchart TD
    User["Пользователь (Директор / Руководитель)"]
    WebUI["Веб-интерфейс (index.html)"]
    Gateway["API Gateway (Fotoprint.Api)"]
    Agent["FotoprintAiAgent (C# Шлюз)"]
    LLM["LLM (Nemotron-3.5 120B / Gemini)"]
    McpCal["MCP Сервис (Fotoprint.Mcp.Calendar:8084)"]
    PG[("PostgreSQL:5434 (meetings, employees)")]

    User -->|1. Текст запроса / Клик слота| WebUI
    WebUI -->|2. POST /api/agent/chat| Gateway
    Gateway -->|3. Вызов агента| Agent
    Agent -->|4. Запрос к LLM| LLM
    LLM -->|5. Tool Call: find_free_slots| Agent
    Agent -->|6. HTTP POST /tools/find_free_slots| McpCal
    McpCal -->|7. SQL выборка занятости (MSK)| PG
    PG -->|8. Занятые интервалы| McpCal
    McpCal -->|9. JSON свободных слотов| Agent
    Agent -->|10. C# формирует ActionCard DTO| Gateway
    Gateway -->|11. Ответ: Текст LLM + ActionCard| WebUI
    
    User -->|12. Клик [✅ Подтвердить]| WebUI
    WebUI -->|13. POST /api/calendar/confirm (Stateless Payload)| Gateway
    Gateway -->|14. POST /tools/create_meeting| McpCal
    McpCal -->|15. INSERT INTO meetings ('scheduled')| PG
    McpCal -->|16. Имитация оповещения (Stub)| McpCal
    McpCal -->|17. Success + Notification Stub Info| WebUI
```

---

## 3. Схема Данных PostgreSQL

```sql
CREATE TABLE IF NOT EXISTS meetings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(255) NOT NULL,
    description TEXT,
    start_time TIMESTAMPTZ NOT NULL,
    end_time TIMESTAMPTZ,
    status VARCHAR(50) DEFAULT 'scheduled', -- 'scheduled', 'in_progress', 'completed', 'cancelled'
    organizer_id INT REFERENCES employees(id) ON DELETE SET NULL,
    participant_ids JSONB DEFAULT '[]'::jsonb, -- список employee_id, например: [1, 2]
    calendar_event_id VARCHAR(100),
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
```

---

## 4. Контракты API и MCP

### 4.1. Tool: `find_free_slots`
- **URL**: `POST http://127.0.0.1:8084/tools/find_free_slots`
- **Заголовки**: `X-Internal-Service-Key: <secret>`, `X-Telegram-Id: <id>`
- **Request Body**:
```json
{
  "organizer_employee_id": 1,
  "participant_employee_ids": [2],
  "date": "2026-11-12",
  "duration_minutes": 30,
  "working_start": "09:00",
  "working_end": "18:00",
  "buffer_minutes": 10
}
```
- **Response Body**:
```json
{
  "status": "success",
  "date": "2026-11-12",
  "duration_minutes": 30,
  "available_slots": [
    {
      "start_time": "2026-11-12T14:30:00+03:00",
      "end_time": "2026-11-12T15:00:00+03:00",
      "label": "14:30 - 15:00"
    },
    {
      "start_time": "2026-11-12T16:00:00+03:00",
      "end_time": "2026-11-12T16:30:00+03:00",
      "label": "16:00 - 16:30"
    }
  ],
  "is_today": true,
  "past_slots_cutoff": "14:00",
  "has_free_slots": true
}
```

### 4.2. Tool: `create_meeting`
- **URL**: `POST http://127.0.0.1:8084/tools/create_meeting`
- **Request Body**:
```json
{
  "title": "Обсуждение качества УФ-печати каталогов",
  "description": "Совещание по браку партии 44102 с начальником производства",
  "start_time": "2026-11-12T14:30:00+03:00",
  "end_time": "2026-11-12T15:00:00+03:00",
  "organizer_id": 1,
  "participant_ids": [2],
  "status": "scheduled"
}
```
- **Response Body**:
```json
{
  "status": "success",
  "meeting_id": "8b51d8d8-7964-42b7-a396-98126b2b528b",
  "title": "Обсуждение качества УФ-печати каталогов",
  "start_time": "2026-11-12T14:30:00+03:00",
  "end_time": "2026-11-12T15:00:00+03:00",
  "organizer": { "id": 1, "name": "Смирнов Алексей Сергеевич" },
  "participants": [
    { "id": 2, "name": "Кузнецов Иван Петрович" }
  ],
  "notification_dispatch": {
    "status": "simulated",
    "channels": ["Telegram", "Bitrix24"],
    "recipient_ids": [2],
    "message": "⚠️ [Заглушка спринта]: Оповещение поставлено в очередь эмуляции Telegram/Bitrix24"
  }
}
```

### 4.3. Tool: `get_calendar_events` (Сетка месяца с RBAC)
- **URL**: `GET http://127.0.0.1:8084/tools/get_calendar_events?start_date=2026-11-01&end_date=2026-11-30&employee_id=1&scope=my`
- **RBAC**:
  - `scope=my`: доступно всем авторизованным сотрудникам. Фильтрует встречи, где пользователь — организатор или участник.
  - `scope=all`: разрешено **только роли `Director` и `Admin`** (проверяется по `X-Telegram-Id` через `employee-reader`). Если роль рядовая, возвращается 403 Forbidden.

---

## 5. Контракт Детерминированного `ActionCard` DTO

```json
{
  "reply": "Я проверил расписание с Иваном Кузнецовым. На сегодня после 14:00 свободно 2 окна:",
  "action_card": {
    "type": "slot_selection",
    "target_person": "Кузнецов Иван Петрович",
    "target_employee_id": 2,
    "duration_minutes": 30,
    "date": "2026-11-12",
    "slots": [
      {
        "id": "slot_1",
        "start": "14:30",
        "end": "15:00",
        "iso_start": "2026-11-12T14:30:00+03:00",
        "iso_end": "2026-11-12T15:00:00+03:00"
      },
      {
        "id": "slot_2",
        "start": "16:00",
        "end": "16:30",
        "iso_start": "2026-11-12T16:00:00+03:00",
        "iso_end": "2026-11-12T16:30:00+03:00"
      }
    ],
    "duration_buttons": [
      { "label": "⏱ 15 мин", "minutes": 15 },
      { "label": "⏱ 30 мин", "minutes": 30, "is_default": true },
      { "label": "⏱ 45 мин", "minutes": 45 },
      { "label": "⏱ 1 час", "minutes": 60 }
    ]
  }
}
```
