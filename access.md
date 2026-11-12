# Права доступа и ролевая модель (RBAC) — Fotoprint AI Copilot

Документ описывает актуальную матрицу ролей, прав (`permissions`), привязку к MCP-инструментам и тестовых пользователей в PostgreSQL (`roles`, `permissions`, `role_permissions`, `employees`, `employee_roles`).

## 1. Механизм проверки доступа

1. Идентификатор пользователя (`X-Telegram-Id` / `telegram_id`) передаётся из AI Gateway в MCP-сервисы.
2. Каждый защищённый эндпоинт MCP помечен атрибутом `[RequirePermission("...")]`.
3. `SecurityInterceptorMiddleware` вызывает `SecurityValidator.ValidateAccessAsync`, который запрашивает паспорт безопасности сотрудника у `mcp-employee-reader` (`/tools/get_employee_security_context`).
4. В `EmployeeSecurityContext`:
   - для роли **`Director`** методы `HasPermission(...)`, `HasAccessGroup(...)` и свойство `IsExecutive` возвращают `true` без ограничений;
   - для остальных ролей проверяется наличие требуемого права в списке `Permissions` или группы доступа в `AccessGroups`;
   - при отказе возвращается **HTTP 403 (`Access Denied`)**, а инцидент логируется в Langfuse (`LogSecurityAlertAsync`).

---

## 2. Описание ролей

| Роль | Описание из БД |
|---|---|
| **`Director`** | Генеральный директор и топ-менеджмент (C-Level). Полный доступ ко всем модулям и отчётам. |
| **`Production`** | Производственный департамент (печать, постпресс, склад материалов). |
| **`Sales`** | Отдел продаж и работы с корпоративными клиентами. |
| **`Accounting`** | Бухгалтерия и финансовый отдел. |
| **`Employee`** | Базовая роль рядового сотрудника. |

---

## 3. Матрица прав доступа

| Право (`permissions.code`) | Описание | `Director` | `Production` | `Sales` | `Accounting` | `Employee` | Где проверяется (`[RequirePermission]`) |
|---|---|:---:|:---:|:---:|:---:|:---:|---|
| `onec:read_orders` | Просмотр статуса заказов в 1С | ✅ | ✅ | ✅ | ✅ | ❌ | `mcp-1c`: `GET /tools/get_order_status` |
| `onec:read_production_kpis` | Просмотр KPI цехов, объёмов брака и загрузки оборудования в 1С | ✅ | ✅ | ❌ | ❌ | ❌ | `mcp-1c`: `GET /tools/get_production_kpis` |
| `bitrix24:read_tasks` | Просмотр задач, поручений и CRM-сделок в Битрикс24 | ✅ | ✅ | ✅ | ✅ | ✅ | `mcp-bitrix24`: `GET /tools/get_user_tasks`, `GET /tools/get_crm_deal_info` |
| `bitrix24:create_tasks` | Создание задач и поручений в Битрикс24 | ✅ | ✅ | ✅ | ❌ | ❌ | `mcp-bitrix24`: `POST /tools/create_task` |
| `calendar:read_meetings` | Просмотр расписания и истории встреч | ✅ | ✅ | ✅ | ✅ | ✅ | `mcp-calendar`: `GET /tools/get_upcoming_meetings`, `GET /tools/get_meeting_history` |
| `rag:search_department` | Доступ к документам своего отдела и общим регламентам | ✅ | ✅ | ✅ | ✅ | ✅ | RAG / `access_groups` документов |
| `rag:search_all` | Доступ ко всей корпоративной базе знаний без ограничений | ✅ | ❌ | ❌ | ❌ | ❌ | RAG / `access_groups` документов |
| `executive:view_summary` | Формирование и просмотр управленческого отчёта Executive Summary | ✅ | ❌ | ❌ | ❌ | ❌ | Управленческая сводка (C-Level) |

---

## 4. Тестовые сотрудники в базе данных (`employees`)

Эти `telegram_id` предзаполнены в БД и могут использоваться для проверки доступа через Web UI или `POST /api/agent/chat`:

| `id` | ФИО | `telegram_id` | `master_id` | Роль | Отдел / Должность | `access_groups` |
|---:|---|---:|---:|---|---|---|
| 1 | Смирнов Алексей Сергеевич | `100001` | `100001` | `Director` | Руководство / Генеральный директор | `["all", "c-level", "production", "sales", "accounting"]` |
| 2 | Кузнецов Иван Петрович | `100002` | `100002` | `Production` | Производство / Начальник производства | `["all", "production"]` |
| 3 | Морозова Елена Алексеевна | `100003` | `100003` | `Sales` | Продажи / Руководитель отдела продаж | `["all", "sales"]` |
| 4 | Соколова Ольга Викторовна | `100004` | `100004` | `Accounting` | Бухгалтерия / Главный бухгалтер | `["all", "accounting"]` |
| 5 | Волков Дмитрий Игоревич | `100005` | `100005` | `Employee` | Производство / Печатник (`Employee`) | `["all"]` |

---

## 5. Примечания по текущей реализации

- Для роли **`Employee`** в таблице `employees` заведён тестовый пользователь №5 (`telegram_id = 100005`, доступ только к общим документам `["all"]`, без доступа к `production`, `sales`, `accounting` и `c-level`).
- При запросе от неизвестного `telegram_id` `mcp-employee-reader` возвращает `404 EmployeeNotFound`, а целевой MCP-сервис отклоняет запрос с **403** и пишет событие в `audit_events`.
- Инструмент `search_typography_regulations` и `Fotoprint.Mcp.KnowledgeBase` фильтруют документы по пересечению с `secContext.AccessGroups` текущего пользователя (RBAC в векторной БД).

