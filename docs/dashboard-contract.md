# Контракт управленческих дашбордов

## GET `/api/system/stats`

Фронтенд передаёт выбранный экран и фильтры:

```http
GET /api/system/stats?dashboard=pulse&period=30&dateFrom=2026-10-13&dateTo=2026-11-11&department=all&product=all
X-Telegram-Id: 100001
```

Идентичность и права определяются сервером по `X-Telegram-Id` / существующему RBAC. Роль не берётся из тела запроса.

```json
{
  "employee": {"masterId": 100001, "name": "Алексей Смирнов", "role": "Director", "department": "Руководство"},
  "dashboard": "pulse",
  "allowedDashboards": ["pulse", "sales", "production", "finance", "decisions"],
  "period": {"from": "2026-10-13", "to": "2026-11-11", "timezone": "Europe/Minsk"},
  "currency": "BYN",
  "kpis": [{"id":"turnover","name":"Оборот","value":0,"plan":0,"forecast":0,"unit":"BYN","status":"good|warning|critical","formula":"...","source":"1C","owner":"...","updatedAt":"2026-11-11T09:42:00+03:00"}],
  "trend": [{"date":"2026-11-01","fact":0,"plan":0,"forecast":0}],
  "systems": [{"id":"crm","name":"CRM / Битрикс24","status":"ok|stale|error","freshness":"2026-11-11T09:42:00+03:00"}],
  "alerts": [{"id":"...","title":"...","reason":"...","metricId":"defect","department":"Производство"}],
  "decisions": [{"id":"...","problem":"...","impact":"...","cause":"...","action":"...","owner":"...","dueDate":"2026-11-14","priority":"high|medium|low","status":"new|assigned|in_progress|done|verified|rejected","version":1}],
  "quality": {"missing":0,"duplicates":0,"freshness":0,"financeReconciliation":"ok|warning|error"},
  "forecast": [{"week":"2026-W47","demand":0,"capacity":0,"deficit":0}],
  "updatedAt":"2026-11-11T09:42:00+03:00"
}
```

### Ошибки

- `401` - пользователь не авторизован;
- `403` - экран или данные недоступны для роли;
- `422` - неверные фильтры;
- `503` - источник временно недоступен.

Ответ ошибки: `{ "error": "Код ошибки", "message": "Понятное сообщение", "traceId": "..." }`.

### POST `/api/decisions`

Создание только после подтверждения пользователем. Заголовок `Idempotency-Key` обязателен. Повтор того же ключа не создаёт вторую карточку. Все изменения пишутся в audit log.

### Экраны

`pulse` - директорский агрегат; `sales` - CRM/клиент; `production` - ERP/качество; `finance` - 1С/люди; `decisions` - реестр решений. Роль и список доступных экранов возвращает сервер.
