# 05. Фаза 4: План Верификации и Тестовые Сценарии

## 1. Регламент Проверки (Anti-Hallucination QA Gate)

- Каждый тестовый кейс проверяется реальным выполнением запросов и логированием кодов ответа.
- Не допускается моделирование успеха: если слот в прошлом вернулся — тест считается проваленным.

---

## 2. Матрица Тестовых Сценариев

| ID | Тестовый сценарий | Входные данные | Ожидаемый результат | Метод верификации |
|---|---|---|---|---|
| **TC-01** | Доступность MCP Calendar на хосте | `GET http://127.0.0.1:8084/health` | HTTP 200, `status: "healthy"` | `curl -f http://127.0.0.1:8084/health` |
| **TC-02** | Поиск слотов без коллизий | Дата: завтра, 30 мин, участники 1 и 2 | Список слотов с 09:00 до 18:00 с шагом 30 мин | `POST /tools/find_free_slots` |
| **TC-03** | **Отсечение прошедшего времени (Сегодня)** | Дата: сегодня, время 14:00 | Все слоты до 14:05 исключены из выдачи | `POST /tools/find_free_slots` |
| **TC-04** | Учет коллизии и буфера 10 минут | Встреча в 14:00–15:00 | Слоты с 13:30 до 15:10 исключены | Unit-тест `FotoprintRepositoryTests` |
| **TC-05** | Бесстейтовая карточка подтверждения | Клик по слоту в UI | Встреча в БД **НЕ создается**, генерируется DOM-карточка с атрибутами `data-*` | Проверка `SELECT COUNT(*) FROM meetings` |
| **TC-06** | **Создание встречи по кнопке подтверждения** | Клик `[✅ Подтвердить и создать]` с payload | Встреча создается в БД (`status: 'scheduled'`), возвращается UUID | `POST /api/calendar/confirm` + проверка `SELECT COUNT(*)` (+1) |
| **TC-07** | **Проверка эмуляции оповещения (Stub)** | Ответ после подтверждения встречи | В ответе поле `notification_dispatch.status: "simulated"` и бейдж в чате | Проверка JSON-ответа |
| **TC-08** | **RBAC: Режим «Вся компания» (Director vs Guest)** | `GET /tools/get_calendar_events?scope=all` | Для Director (ID 100001) $\to$ HTTP 200.<br>Для Guest (ID 999999) $\to$ HTTP 403 Forbidden. | `curl` с разными заголовками `X-Telegram-Id` |
| **TC-09** | Реактивное обновление кружков в UI | Добавление встречи через чат | На дате появляется кружок `.dot-green` без вызова `location.reload()` | DOM-проверка Playwright / JS консоль |

---

## 3. Команды для Локального Тестирования

### 3.1. Проверка отсечения прошедших часов (TC-03)
```bash
curl -X POST http://127.0.0.1:8084/tools/find_free_slots \
  -H "Content-Type: application/json" \
  -H "X-Internal-Service-Key: local-dev-service-key" \
  -H "X-Telegram-Id: 100001" \
  -d "{
    \"organizer_employee_id\": 1,
    \"participant_employee_ids\": [2],
    \"date\": \"$(date +%Y-%m-%d)\",
    \"duration_minutes\": 30,
    \"working_start\": \"09:00\",
    \"working_end\": \"18:00\",
    \"buffer_minutes\": 10
  }" | jq .
```
*Критерий успеха: время начала первого доступного слота строго позже текущего времени по Москве.*

### 3.2. Проверка RBAC доступа к расписанию всей компании (TC-08)
```bash
# 1. Запрос от лица Генерального директора (ID 100001) -> Ожидается 200 OK
curl -s -o /dev/null -w "%{http_code}\n" \
  -H "X-Internal-Service-Key: local-dev-service-key" \
  -H "X-Telegram-Id: 100001" \
  "http://127.0.0.1:8084/tools/get_calendar_events?scope=all"

# 2. Запрос от лица Гостя (ID 999999) -> Ожидается 403 Forbidden
curl -s -o /dev/null -w "%{http_code}\n" \
  -H "X-Internal-Service-Key: local-dev-service-key" \
  -H "X-Telegram-Id: 999999" \
  "http://127.0.0.1:8084/tools/get_calendar_events?scope=all"
```

### 3.3. Запуск модульных тестов
```bash
dotnet test tests/Fotoprint.Tests/Fotoprint.Tests.csproj --filter "FullyQualifiedName~Calendar" --logger "console;verbosity=normal"
```

---

## 4. Критерии Приемки (Definition of Done)

1. [ ] Порт `8084` открыт на `127.0.0.1` и доступен шлюзу и тестам.
2. [ ] Алгоритм `FindFreeSlotsAsync` гарантированно не выдает слоты из прошлого времени.
3. [ ] C#-код шлюза перехватывает вызовы `find_free_slots` и детерминированно строит `action_card`.
4. [ ] До подтверждения пользователем встречи нет в базе данных (Stateless flow).
5. [ ] После подтверждения создается встреча, возвращается статус эмуляции оповещения.
6. [ ] Режим «Вся компания» закрыт для неуполномоченных ролей (403 Forbidden).
7. [ ] В веб-интерфейсе отображается боковая шторка с календарем, точками плотности и реактивным обновлением.
