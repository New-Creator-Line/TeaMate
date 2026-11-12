---
name: create-mcp-server
description: >-
  Use this skill when creating a new MCP microservice or adding a new MCP tool
  in Fotoprint AI Copilot (.NET 9 / Minimal API + FotoprintAiAgent), including
  mandatory RBAC permissions and Human-in-the-Loop [RequireConfirmation] enforcement.
---

# Создание MCP-сервера и MCP-инструментов в Fotoprint AI Copilot

Следуй этому регламенту при добавлении нового MCP-сервиса (`src/Fotoprint.Mcp.<Service>/`) или нового инструмента в существующий MCP-сервер и `FotoprintAiAgent`.

---

## 1. Обязательный стандарт безопасности и подтверждения (HITL)

Каждый MCP-эндпоинт и соответствующий `Tool_*` метод в агенте **ОБЯЗАН** иметь оба атрибута из `Fotoprint.Core.Security`:

1. **`[RequirePermission("<domain>:<action>")]`** — проверяется `SecurityInterceptorMiddleware` через `MCP-EmployeeReader` по заголовку `X-Telegram-Id`.
2. **`[RequireConfirmation(true | false)]`** — **ОБЯЗАТЕЛЬНЫЙ АТРИБУТ ПОДТВЕРЖДЕНИЯ**:
   - Для **любых изменяющих (мутирующих) операций** (создание, изменение, удаление записей, постановка задач, назначение встреч, отправка внешних запросов/уведомлений) **СТРОГО ставь `[RequireConfirmation(true)]`**.
   - Для операций **только для чтения** (`GET`, справочный поиск) явно ставь **`[RequireConfirmation(false)]`**, чтобы атрибут был задекларирован на каждом туле.

### Как работает `[RequireConfirmation(true)]` (двойной рубеж защиты):
- **На уровне агента** (`src/Fotoprint.Core/Agents/FotoprintAiAgent.cs`): `RequiresConfirmation(toolName)` через рефлексию читает `[RequireConfirmation]` на методе `Tool_<toolName>`. Без явного подтверждения пользователя (`IsMutatingActionConfirmed == true`) агент останавливает выполнение и возвращает план действий (`📋 План действий (требуется подтверждение)`), не вызывая инструмент; при подтверждении добавляет заголовок `X-User-Confirmed: true`.
- **На уровне MCP-сервера** (`src/Fotoprint.Core/Security/SecurityInterceptorMiddleware.cs`): проверяет метаданные эндпоинта `RequireConfirmationAttribute`. Если `Required == true`, а заголовок `X-User-Confirmed: true` отсутствует — запрос блокируется и возвращает `status: "confirmation_required"`.

> ⚠️ **Правило без исключений:** никогда не создавай мутирующий MCP-эндпоинт или `Tool_*` без `[RequireConfirmation(true)]` на обеих сторонах (и на `app.MapPost(...)` в MCP, и на `Tool_<name>` в `FotoprintAiAgent`).

---

## 2. Шаблон нового MCP-сервиса (`src/Fotoprint.Mcp.<Name>/Program.cs`)

Используй существующие сервисы (например, `src/Fotoprint.Mcp.Bitrix24/Program.cs`) как эталон:

```csharp
using Fotoprint.Core.Configuration;
using Fotoprint.Core.Data;
using Fotoprint.Core.Mcp;
using Fotoprint.Core.Security;
using Fotoprint.Core.Telemetry;

var builder = WebApplication.CreateBuilder(args);
var appConfig = FotoprintConfigLoader.Load();
builder.Services.AddSingleton(appConfig);
// Регистрация IFotoprintRepository, ILangfuseClient, ISecurityValidator (см. Fotoprint.Mcp.Bitrix24/Program.cs)

var app = builder.Build();

// 1. Строгий порядок Middleware периметра и RBAC (НЕ МЕНЯТЬ!)
app.UseMiddleware<PerimeterDefenseMiddleware>();
app.UseMiddleware<InternalServiceAuthMiddleware>();
app.UseMiddleware<SecurityInterceptorMiddleware>();

// 2. Healthcheck (для Docker healthcheck)
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "MCP-<Name>" }));

// 3. Каталог инструментов (/tools) с описанием InputSchema
app.MapGet("/tools", () => Results.Ok(new { tools }));

// 4. Read-only инструмент — обязательно [RequirePermission] + [RequireConfirmation(false)]
app.MapGet("/tools/get_example_data",
    [RequirePermission("<domain>:read")]
    [RequireConfirmation(false)]
    (string query) => Results.Ok(new { /* ... */ }));

// 5. Мутирующий инструмент — обязательно [RequirePermission] + [RequireConfirmation(true)]
app.MapPost("/tools/create_example_item",
    [RequirePermission("<domain>:write")]
    [RequireConfirmation(true)]
    (CreateExampleRequest req, HttpContext ctx) => Results.Ok(new { status = "created" }));

app.Run();
```

---

## 3. Подключение инструмента в `FotoprintAiAgent.cs`

В `src/Fotoprint.Core/Agents/FotoprintAiAgent.cs` выполни 3 шага:

1. **Диспетчер `ExecuteToolAsync`**: добавь ветку `"<tool_name>" => await Tool_<tool_name>(...)`.
2. **Метод `Tool_<tool_name>`**: обязательно повесь `[RequireConfirmation(true)]` (для записи) или `[RequireConfirmation(false)]` (для чтения) и при вызове `CreateMcpClient(telegramId, traceId, isConfirmed)` передай флаг `isConfirmed`.
3. **JSON-схема в `GetToolsSchema()`**: добавь декларацию функции с точным `name`, `description` (если мутирующее действие — укажи *«вызывать ТОЛЬКО после получения подтверждения от пользователя»*) и `parameters`.

> 💡 При передаче дат (например, для задач и встреч) поддерживай корректное определение актуальной текущей даты и перенос дедлайна с выходных на рабочий день, как в `Fotoprint.Mcp.Bitrix24`.

---

## 4. Инфраструктура, сериализация и чек-лист проверки

1. **Изоляция сети (`docker-compose.yml`)**:
   - Добавь сервис в сеть `fotoprint-net` на следующем внутреннем порту (существующие: `8081` EmployeeReader, `8082` OneC, `8083` Bitrix24, `8084` Calendar).
   - Не публикуй порт MCP наружу в `ports:` — внешний доступ к `/tools` и `/mcp` должен оставаться заблокированным в `infra/nginx/nginx.conf`.
2. **UTF-8 без экранирования кириллицы**: при ручной сериализации JSON используй `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`.
3. **Решение и тесты**:
   - Добавь проект в `Fotoprint.slnx`.
   - Покрой новый MCP-сервис и тул тестами в `tests/Fotoprint.Tests/` (проверь 403 при отсутствии `X-Internal-Service-Key` / права `RequirePermission` и `confirmation_required` при отсутствии `X-User-Confirmed: true` для `[RequireConfirmation(true)]`).
   - Прогони:
     ```powershell
     dotnet test
     ```

