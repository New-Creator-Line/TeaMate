# Реестр и аудит HTTP-ручек (`src/`)

Дата аудита: `2026-11-12`  
Всего проверено микросервисов: **6** (`Fotoprint.AiGateway.Api` + 5 `Fotoprint.Mcp.*`)  
Всего обнаружено HTTP-маршрутов: **46**

---

## 1. Лишние, тестовые и дублирующие ручки (рекомендуются к удалению / закрытию)

Ниже перечислены **12 маршрутов**, которые не вызываются ни в одном из 10 рабочих пайплайнов `n8n/workflows/*.json`, ни во фронтенде `infra/nginx/html/index.html`, ни в `FotoprintAiAgent`.

### 1.1. `Fotoprint.AiGateway.Api` (6 ручек)

| № | Метод и маршрут | Файл и строки | Почему нужно удалить / закрыть | Рекомендация |
|---|---|---|---|---|
| 1 | `POST /api/gemini/generate` | `src/Fotoprint.AiGateway.Api/Program.cs:392-418` | Устаревшая тестовая ручка прямого вызова `IGeminiClient` («обратная совместимость»). Нигде в `n8n` и `Web UI` не используется (в `07_google_meet_post_processing.json` используется `/api/llm/generate`). Не проверяет `X-Internal-Service-Key`. | **Удалить** |
| 2 | `POST /api/context/detect-switch` | `src/Fotoprint.AiGateway.Api/Program.cs:2148-2166` | Стендовая ручка для изолированного тестирования `IContextSwitchDetector`. В боевом потоке детектор вызывается напрямую через DI внутри `/api/agent/chat` и `TelegramWebhookProcessor`. | **Удалить** |
| 3 | `POST /api/telemetry/evaluate-faithfulness` | `src/Fotoprint.AiGateway.Api/Program.cs:2169-2177` | Тестовая ручка для ручного запуска `ILlmJudgeService` (Epic 6.4). В реальной работе `ILlmJudgeService` вызывается напрямую в фоновом `Task.Run` внутри `FotoprintAiAgent.cs`. | **Удалить** |
| 4 | `POST /api/telemetry/evaluate-formatting-policy` | `src/Fotoprint.AiGateway.Api/Program.cs:2179-2187` | Тестовая ручка для ручного запуска `ILlmJudgeService` (Epic 6.4). В реальной работе вызывается напрямую внутри `FotoprintAiAgent.cs`. | **Удалить** |
| 5 | `POST /api/calendar/slots` | `src/Fotoprint.AiGateway.Api/Program.cs:2262-2283` | Неиспользуемый прокси-маршрут к `mcp-calendar/tools/find_free_slots`. Фронтенд `index.html` вызывает только `/api/calendar/events` и `/api/calendar/confirm`, а агент ходит напрямую в `mcp-calendar:8084/tools/find_free_slots`. | **Удалить** |
| 6 | `GET /api/telegram/info` | `src/Fotoprint.AiGateway.Api/Telegram/TelegramEndpoints.cs:65-104` | Отладочная ручка без авторизации: раскрывает наружу публичный URL Cloudflare Quick Tunnel (`tunnel_webhook_url`) и состояние вебхука Telegram-бота. | **Удалить** или закрыть проверкой `InternalServiceAuthMiddleware.IsAuthorized` |

### 1.2. Микросервисы `Fotoprint.Mcp.*` (6 ручек)

> [!NOTE]
> Бизнес-ручки с моками внешних систем (`/tools/get_order_status`, `/tools/get_production_kpis`, `/tools/get_user_tasks`, `/tools/create_task`, `/tools/get_crm_deal_info`, `/tools/schedule_meeting`) **не входят** в этот список и **оставляются** в проекте — они демонстрируют работу MCP-коннекторов с 1С и Битрикс24 при отсутствии прямого доступа к боевым API (с полным прохождением RBAC и `[RequireConfirmation]`).  
> К удалению ниже относятся только неиспользуемые дублирующие JSON-RPC/SSE обёртки. *(Исключение: если планируется подключать сторонние MCP-клиенты вроде Claude Desktop или Cursor по SSE/JSON-RPC, то `/sse`, `/message` и `/tools/call` нужно не удалять, а унифицировать.)*

| № | Сервис | Метод и маршрут | Файл и строки | Почему нужно удалить | Рекомендация |
|---|---|---|---|---|---|
| 7 | `Mcp.EmployeeReader` | `GET /sse` | `src/Fotoprint.Mcp.EmployeeReader/Program.cs:50-59` | Черновая тестовая заглушка SSE-транспорта (`event: endpoint\ndata: /message`), которая сразу закрывает соединение и нигде не используется. | **Удалить** |
| 8 | `Mcp.EmployeeReader` | `POST /message` | `src/Fotoprint.Mcp.EmployeeReader/Program.cs:245-293` | Неиспользуемый тестовый JSON-RPC обработчик к заглушке `/sse` (даже не поддерживает инструмент `resolve_master_id`). | **Удалить** |
| 9 | `Mcp.EmployeeReader` | `POST /tools/call` | `src/Fotoprint.Mcp.EmployeeReader/Program.cs:161-242` | Дублирующий JSON-RPC роутер. Все реальные вызовы из `n8n` и сервисов идут в REST-ручки `GET /tools/get_employee_security_context` и `GET /tools/resolve_master_id`. | **Удалить** |
| 10 | `Mcp.OneC` | `POST /tools/call` | `src/Fotoprint.Mcp.OneC/Program.cs:163-232` | Дублирующий JSON-RPC роутер с урезанными моками в обход атрибутов `[RequirePermission]` на конечных маршрутах. Агент и `n8n` вызывают `/tools/get_order_status` и `/tools/get_production_kpis`. | **Удалить** |
| 11 | `Mcp.Bitrix24` | `POST /tools/call` | `src/Fotoprint.Mcp.Bitrix24/Program.cs:299-394` | Дублирующий JSON-RPC роутер, в котором `create_task` не сохраняет задачу в БД (в отличие от основного `POST /tools/create_task`), а `get_crm_deal_info` вовсе не реализован. | **Удалить** |
| 12 | `Mcp.Calendar` | `POST /tools/call` | `src/Fotoprint.Mcp.Calendar/Program.cs:617-757` | Дублирующий JSON-RPC роутер с неполной реализацией (нет `create_meeting`, `get_calendar_events`, `search_meeting_rag`). Все вызовы идут в профильные `/tools/<tool_name>`. | **Удалить** |

---

## 2. Полный список всех ручек по сервисам (34 боевые + 12 кандидатов на удаление)

### 2.1. `Fotoprint.AiGateway.Api` (порт `8080` внутри Docker / `8088` на localhost)

| Метод | Маршрут | Файл | Защита / Авторизация | Где используется | Статус |
|---|---|---|---|---|---|
| `GET` | `/` | `Program.cs:199` | Публичный | Раздача `infra/nginx/html/index.html` при запуске без Nginx | Оставить |
| `GET` | `/health` | `Program.cs:306` | Публичный | Healthcheck контейнера и Nginx | Оставить |
| `GET` | `/api/system/stats` | `Program.cs:315` | Публичный | Кнопка «Метрики» в шапке `index.html:70` | Оставить |
| `POST` | `/api/audio/transcribe` | `Program.cs:220` | Лимит размера + санитизация файла | Голосовой ввод в `index.html:1379` и `n8n` (`07_google_meet_post_processing.json`) | Оставить |
| `POST` | `/api/llm/generate` | `Program.cs:343` | Закрыт в `nginx.conf` извне | `n8n` (`07_google_meet_post_processing.json:181`) | Оставить |
| `POST` | `/api/gemini/generate` | `Program.cs:392` | Закрыт в `nginx.conf` извне | **Не используется** | **Удалить** |
| `GET` | `/api/agent/users` | `Program.cs:421` | Публичный (для демо-переключателя ролей) | Переключатель пользователей RBAC в `index.html:353` | Оставить |
| `POST` | `/api/agent/execute` | `Program.cs:445` | `X-Internal-Service-Key` или проверка активного сотрудника в БД | `n8n` (`01_main_router_and_auth.json:180`) и fallback в `index.html:950` | Оставить |
| `POST` | `/api/agent/chat` | `Program.cs:492` | RBAC через оркестратор `n8n` | Основной чат Web UI (`index.html:938`) | Оставить |
| `POST` | `/api/chat/ingress` | `Program.cs:718` | `InternalServiceAuthMiddleware` + закрыт в `nginx.conf` | Входной шлюз для внешних CRM + `NotificationServiceTests` | Оставить |
| `POST` | `/api/rag/index-document` | `Program.cs:815` | `X-Internal-Service-Key` или право `rag:search_all` (Директор) | Загрузка и индексация регламентов в БД | Оставить |
| `POST` | `/api/rag/reindex-embeddings` | `Program.cs:891` | `X-Internal-Service-Key` или право `rag:search_all` + закрыт в `nginx.conf` | Доиндексация чанков без эмбеддингов | Оставить |
| `POST` | `/api/rag/search` | `Program.cs:933` | `X-Internal-Service-Key` или проверка `MasterId`/`TelegramId` | `n8n` (`02_rbac_rag_pipeline.json:20`) | Оставить |
| `POST` | `/api/meetings/{id:guid}/generate-protocol` | `Program.cs:1043` | Проверка существования встречи в БД | `n8n` (`05_post_meeting_mom_and_tasks.json:20`) | Оставить |
| `GET` | `/api/meetings/{id:guid}/export-docx` | `Program.cs:1153` | RBAC: только участники встречи или Директор | Скачивание `.docx` из карточки MoM в `index.html:1697` | Оставить |
| `POST` | `/api/meetings/process-audio` | `Program.cs:1209` | Лимит размера + санитизация имени файла | Загрузка аудио встречи (MoM) в `index.html:1435` | Оставить |
| `POST` | `/api/meetings/save-and-index` | `Program.cs:1555` | Закрыт в `nginx.conf` извне | `n8n` (`07_google_meet_post_processing.json:211`) | Оставить |
| `POST` | `/api/meetings/rag-search` | `Program.cs:1811` | RBAC по `MasterId` (только участники и Директор) | `n8n` (`08_meeting_recall_rag_and_export.json:91`) | Оставить |
| `GET` | `/api/meetings/{id:guid}/export-full` | `Program.cs:2025` | RBAC по `master_id` (только участники и Директор) + CSP | `n8n` (`08_meeting_recall_rag_and_export.json:170`) и ссылки выгрузки | Оставить |
| `POST` | `/api/context/detect-switch` | `Program.cs:2148` | Закрыт в `nginx.conf` извне | **Не используется** (тестовая ручка) | **Удалить** |
| `POST` | `/api/telemetry/evaluate-faithfulness` | `Program.cs:2169` | Закрыт в `nginx.conf` извне | **Не используется** (тестовая ручка) | **Удалить** |
| `POST` | `/api/telemetry/evaluate-formatting-policy` | `Program.cs:2179` | Закрыт в `nginx.conf` извне | **Не используется** (тестовая ручка) | **Удалить** |
| `GET` | `/api/calendar/events` | `Program.cs:2215` | Проксирует `X-Telegram-Id` в `mcp-calendar` с RBAC | Календарь встреч в `index.html:616` | Оставить |
| `POST` | `/api/calendar/confirm` | `Program.cs:2238` | Проксирует `X-Telegram-Id` в `mcp-calendar` с защитой от IDOR | Подтверждение слота встречи в `index.html:885` | Оставить |
| `POST` | `/api/calendar/slots` | `Program.cs:2262` | Прокси в `mcp-calendar` | **Не используется** | **Удалить** |
| `POST` | `/api/telegram/webhook` | `TelegramEndpoints.cs:57` | Секрет `X-Telegram-Bot-Api-Secret-Token` (Constant-Time) | Вебхук Telegram и кнопка задачи Битрикс24 в `index.html:1632` | Оставить |
| `GET` | `/api/telegram/info` | `TelegramEndpoints.cs:65` | Нет авторизации | **Не используется** в пайплайнах (отладочная ручка) | **Удалить / Закрыть** |
| `POST` | `/api/telegram/set-webhook` | `TelegramEndpoints.cs:106` | `X-Internal-Service-Key` + закрыт в `nginx.conf` | Ручная перерегистрация вебхука администратором | Оставить |
| `POST` | `/api/notifications/send` | `NotificationEndpoints.cs:65` | `InternalServiceAuthMiddleware` + закрыт в `nginx.conf` | Адресная отправка уведомлений + `NotificationServiceTests` | Оставить |
| `POST` | `/api/notifications/dispatch/morning-digest` | `NotificationEndpoints.cs:110` | `InternalServiceAuthMiddleware` + закрыт в `nginx.conf` | `n8n` (`09_morning_schedule_digest_cron.json`) | Оставить |
| `POST` | `/api/notifications/dispatch/pre-meeting` | `NotificationEndpoints.cs:133` | `InternalServiceAuthMiddleware` + закрыт в `nginx.conf` | `n8n` (`03_pre_meeting_prep_cron.json`) | Оставить |
| `POST` | `/api/notifications/dispatch/task-deadlines` | `NotificationEndpoints.cs:155` | `InternalServiceAuthMiddleware` + закрыт в `nginx.conf` | `n8n` (`10_task_deadline_reminders_cron.json`) | Оставить |
| `GET` | `/api/notifications/preferences/{employeeId:int}` | `NotificationEndpoints.cs:176` | `InternalServiceAuthMiddleware` + закрыт в `nginx.conf` | Управление настройками уведомлений + `NotificationServiceTests` | Оставить |
| `PUT` | `/api/notifications/preferences/{employeeId:int}` | `NotificationEndpoints.cs:213` | `InternalServiceAuthMiddleware` + закрыт в `nginx.conf` | Обновление настроек уведомлений + `NotificationServiceTests` | Оставить |

---

### 2.2. `Fotoprint.Mcp.EmployeeReader` (внутренний порт `8081`)

| Метод | Маршрут | Файл | Реализация | Защита / Авторизация | Где используется | Статус |
|---|---|---|---|---|---|---|
| `GET` | `/health` | `Program.cs:42` | Служебная | `InternalServiceAuthMiddleware` | Проверка состояния сервиса | Оставить |
| `GET` | `/sse` | `Program.cs:50` | Заглушка (SSE) | `InternalServiceAuthMiddleware` | **Не используется** | **Удалить** |
| `GET` | `/tools/list` | `Program.cs:95` | Каталог схем MCP | `InternalServiceAuthMiddleware` | Реестр MCP-инструментов сервиса | Оставить |
| `GET` | `/tools/get_employee_security_context` | `Program.cs:129` | Реальная БД (PostgreSQL + кэш) | `InternalServiceAuthMiddleware` | `SecurityValidator`, `n8n` (`01`, `06`) | Оставить |
| `GET` | `/tools/resolve_master_id` | `Program.cs:135` | Реальная БД (PostgreSQL + кэш) | `InternalServiceAuthMiddleware` | `n8n` (`08_meeting_recall_rag_and_export.json:57`) | Оставить |
| `POST` | `/tools/call` | `Program.cs:161` | Дубликат (JSON-RPC) | `InternalServiceAuthMiddleware` | **Не используется** | **Удалить** |
| `POST` | `/message` | `Program.cs:245` | Заглушка (JSON-RPC) | `InternalServiceAuthMiddleware` | **Не используется** | **Удалить** |

---

### 2.3. `Fotoprint.Mcp.OneC` (внутренний порт `8082`)

| Метод | Маршрут | Файл | Реализация | Защита / Авторизация | Где используется | Статус |
|---|---|---|---|---|---|---|
| `GET` | `/health` | `Program.cs:40` | Служебная | `InternalServiceAuthMiddleware` | Проверка состояния сервиса | Оставить |
| `GET` | `/tools/list` | `Program.cs:81` | Каталог схем MCP | `InternalServiceAuthMiddleware` | Реестр MCP-инструментов сервиса | Оставить |
| `GET` | `/tools/get_order_status` | `Program.cs:84` | **Демо-мок внешней 1С** | `onec:read_orders` | `FotoprintAiAgent` и `n8n` (`04_live_meeting_copilot.json:56`) | Оставить |
| `GET` | `/tools/get_production_kpis` | `Program.cs:135` | **Демо-мок внешней 1С** | `onec:read_production_kpis` | `FotoprintAiAgent` и `n8n` (`06_executive_summary.json:72`) | Оставить |
| `POST` | `/tools/call` | `Program.cs:163` | Дубликат (JSON-RPC) | Ручная валидация | **Не используется** | **Удалить** |

---

### 2.4. `Fotoprint.Mcp.Bitrix24` (внутренний порт `8083`)

| Метод | Маршрут | Файл | Реализация | Защита / Авторизация | Где используется | Статус |
|---|---|---|---|---|---|---|
| `GET` | `/health` | `Program.cs:45` | Служебная | `InternalServiceAuthMiddleware` | Проверка состояния сервиса | Оставить |
| `GET` | `/tools/list` | `Program.cs:104` | Каталог схем MCP | `InternalServiceAuthMiddleware` | Реестр MCP-инструментов сервиса | Оставить |
| `GET` | `/tools/get_user_tasks` | `Program.cs:200` | **Гибрид: БД (`action_items`) + демо-мок Б24** | `bitrix24:read_tasks` + защита от IDOR | `FotoprintAiAgent` | Оставить |
| `POST` | `/tools/create_task` | `Program.cs:249` | **Гибрид: БД (`action_items`) + демо-мок Б24** | `bitrix24:create_tasks` + `[RequireConfirmation(true)]` | `FotoprintAiAgent` и `TelegramWebhookProcessor` | Оставить |
| `GET` | `/tools/get_crm_deal_info` | `Program.cs:293` | **Демо-мок внешней Б24** | `bitrix24:read_tasks` | `FotoprintAiAgent` | Оставить |
| `POST` | `/tools/call` | `Program.cs:299` | Дубликат (JSON-RPC) | Ручная валидация | **Не используется** | **Удалить** |

---

### 2.5. `Fotoprint.Mcp.Calendar` (внутренний порт `8084`)

| Метод | Маршрут | Файл | Реализация | Защита / Авторизация | Где используется | Статус |
|---|---|---|---|---|---|---|
| `GET` | `/health` | `Program.cs:54` | Служебная | `InternalServiceAuthMiddleware` | Проверка состояния сервиса | Оставить |
| `GET` | `/tools/list` | `Program.cs:181` | Каталог схем MCP | `InternalServiceAuthMiddleware` | Реестр MCP-инструментов сервиса | Оставить |
| `GET` | `/tools/get_upcoming_meetings` | `Program.cs:191` | Реальная БД + fallback мок при пустой БД | `calendar:read_meetings` + RBAC фильтр участников | `FotoprintAiAgent` и `n8n` (`07_google_meet_post_processing.json:42`) | Оставить |
| `GET` | `/tools/get_meeting_history` | `Program.cs:236` | Реальная БД + fallback мок при пустой БД | `calendar:read_meetings` + RBAC фильтр участников | `FotoprintAiAgent` | Оставить |
| `POST` | `/tools/find_free_slots` | `Program.cs:302` | Реальная БД (PostgreSQL) | `calendar:read_meetings` | `FotoprintAiAgent` | Оставить |
| `POST` | `/tools/create_meeting` | `Program.cs:348` | Реальная БД + мок отправки оповещений | `calendar:read_meetings` + защита от IDOR | `AiGateway` (`/api/calendar/confirm`) | Оставить |
| `GET` | `/tools/get_calendar_events` | `Program.cs:420` | Реальная БД (PostgreSQL) | `calendar:read_meetings` + RBAC `scope=all` | `AiGateway` (`/api/calendar/events`) | Оставить |
| `POST` | `/tools/schedule_meeting` | `Program.cs:484` | **Демо-мок календаря** (генерация ID + Langfuse) | `calendar:read_meetings` + `[RequireConfirmation(true)]` | `FotoprintAiAgent` | Оставить |
| `GET` | `/tools/search_meeting_rag` | `Program.cs:510` | Реальная БД (`meeting_chunks` + `pgvector`) | `calendar:read_meetings` + RBAC по `MasterId` | `FotoprintAiAgent` | Оставить |
| `POST` | `/tools/call` | `Program.cs:617` | Дубликат (JSON-RPC) | Ручная валидация | **Не используется** | **Удалить** |

---

### 2.6. `Fotoprint.Mcp.KnowledgeBase` (внутренний порт `8086`)

| Метод | Маршрут | Файл | Реализация | Защита / Авторизация | Где используется | Статус |
|---|---|---|---|---|---|---|
| `GET` | `/health` | `Program.cs:57` | Служебная | `InternalServiceAuthMiddleware` | Проверка состояния сервиса | Оставить |
| `GET` | `/tools/list` | `Program.cs:101` | Каталог схем MCP | `InternalServiceAuthMiddleware` | Реестр MCP-инструментов сервиса | Оставить |
| `GET` | `/tools/search_typography_regulations` | `Program.cs:104` | Реальная БД (`corporate_document_chunks` + `pgvector`) | `rag:search_department` + RBAC `AccessGroups` | `FotoprintAiAgent` | Оставить |
| `POST` | `/tools/index_corporate_document` | `Program.cs:188` | Реальная БД (`corporate_documents` + `pgvector`) | `rag:search_all` + `[RequireConfirmation(true)]` | `FotoprintAiAgent` | Оставить |

