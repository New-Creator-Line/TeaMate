# Отчёт о тестировании — Fotoprint AI Copilot (Трек 3)

**Дата прогона:** 2026-11-12  
**Конфигурация:** .NET 9 (`net9.0`), решение `Fotoprint.slnx`  
**Команды проверки:**
- Сборка: `dotnet build Fotoprint.slnx --nologo` -> **0 ошибок** (все 8 проектов собраны).
- Тесты: `dotnet test tests/Fotoprint.Tests --nologo` -> **156/156 пройдено**, **0 провалено**, **0 пропущено**.

> [!NOTE]
> По условиям ветки в данной проверке исключены аутентификация (JWT/SSO) и Трек 1 (дашборд/реестр решений/датасет заказов >=500). Внешние вызовы LLM, PostgreSQL и MCP-HTTP в юнит/интеграционных тестах xUnit заменены локальными детерминированными заглушками; живой запуск `docker compose up` в рамках этого прогона не выполнялся.

---

## 1. Сводка прогона xUnit (`Fotoprint.Tests`)

| Категория тестов | Что проверялось | Результат |
|---|---|---|
| **PII Masking (`ОТ-03`, `AI-06`, п. 7.4)** | Маскирование email, BY/RU телефонов, карт (с проверкой Луна, номера заказов 1С `44102` не ломаются), IBAN, зарплат и ФИО в логах, в исходящих вызовах `NvidiaClient`, `LangfuseClient` и пост-фильтре ответа агента | ✅ Пройдено (`PiiMasker_Masks_Logs_And_Secrets_Correctly_Without_Breaking_OrderIds`, `NvidiaClient_Masks_Secrets_Before_Outbound_Http_Call`, `Agent_Guards_Source_Citations_And_Pii_Salary_In_Final_Answer_And_Passes_User_AccessGroups`) |
| **Защита от Prompt Injection (п. 7.4)** | Обнаружение EN/RU инъекций, экранирование `<untrusted_data>`, блокировка предложения мутирующих инструментов (`create_task`) при инъекции в RAG-контексте + запись `security.prompt_injection_detected` и `action.blocked` в `IAuditLog` | ✅ Пройдено (`PromptInjectionGuard_Detects_Injection_And_Escapes_Untrusted_Delimiters`, `Agent_Blocks_Mutating_Action_Proposal_When_Rag_Contains_Prompt_Injection_And_Audits`) |
| **Двухфазный HITL и Аудит (`ОТ-04`, `AI-03`, п. 7.5)** | Отложенный план `InMemoryPendingActionStore` (`ArgsHash`, TTL), выполнение сохранённых аргументов только по `confirm:<id>`, идемпотентный отказ при повторе, отмена (`cancel:<id>`), аннулирование устаревшего плана при новом сообщении, фиксация `action.*` в аудит | ✅ Пройдено (`Agent_TwoPhase_Confirmation_Executes_Exact_Stored_Arguments_Once_And_Cancels_Idempotently`, `Agent_Unrelated_Message_Supersedes_Pending_Plan_And_Cancel_Makes_Zero_Mutations`) |
| **Протоколы (4 статуса), `SourceQuote` и контрольные стенограммы (`AI-04`, `AI-05`, п. 7.5)** | Разделение на `Decisions` (Решено), `Proposed` (Предложено), `Discussed` (Обсуждалось), `Risks`, `RequiresClarification`; наличие `SourceQuote`; экспорт в HTML/DOCX; контрольный прогон **3 стенограмм** (Heidelberg валы, заказ №44102 / бумага 130 г/м², КК / ЦПМ) — точность извлечения поручений **100% (>= 80%)** | ✅ Пройдено (`Protocol_Statuses_And_SourceQuote_Work_EndToEnd_And_Pass_Control_Transcripts_80Pct_Bar`, `MeetingProtocolGenerator_*`) |
| **RBAC и изоляция доступа (`ОТ-02`, `AI-02`)** | Передача `X-Telegram-Id` / `X-User-Id` в MCP, передача пользовательских `AccessGroups` (`production`) в fallback-поиске RAG без расширения до `accounting`/`c-level`, fail-closed при недоступности БД | ✅ Пройдено |
| **Устойчивость, балансировщик, STT, Embeddings, Календарь, RAG-поиск встреч, Telegram/Notifications** | Весь объединённый регрессионный набор тестов (`LoadBalancer`, `RollbackManager`, `GeminiEmbeddingClient`, `AudioTranscription`, `NotificationService`, `TelegramWebhook`, `ContextSwitchDetector`, `CalendarIntegrationTests`, `MeetingSearchQueryParserTests`, `FotoprintAiAgent`) | ✅ Пройдено (156/156 после интеграции с `origin/dev`, включая 8 комплаенс-тестов) |

---

## 2. Контрольная оценка по 3 стенограммам (п. 7.5 ТЗ)

Тест [`ComplianceAndSecurityTests.Protocol_Statuses_And_SourceQuote_Work_EndToEnd_And_Pass_Control_Transcripts_80Pct_Bar`](tests/Fotoprint.Tests/ComplianceAndSecurityTests.cs) прогнал детерминированный разбор на 3 эталонных стенограммах ООО «Фотопринт»:

| № | Контрольная встреча | Ожидалось поручений | Извлечено поручений | Наличие `source_quote` и «требует уточнения» | Статус |
|---|---|---:|---:|---|---|
| 1 | Оперативка по красочным валам Heidelberg | 2 | 2 | ✅ + выделено предложение (`Proposed`) | ✅ 100% |
| 2 | Планерка по срочному заказу №44102 | 2 | 2 | ✅ + фиксация риска и вопроса без владельца | ✅ 100% |
| 3 | Контроль качества и калибровка ЦПМ | 2 | 2 | ✅ + предложение чек-листа и сроки | ✅ 100% |
| **Итого** | **3 контрольные расшифровки** | **6** | **6** | **Итоговый recall: 100% (порог ТЗ: >= 80%)** | ✅ |

---

## 3. Проверенные артефакты данных и документации

- **БД и Seed:** [`infra/postgres/init/02_schema.sql`](infra/postgres/init/02_schema.sql) (все даты в `TIMESTAMPTZ`, добавлены таблица `audit_events`, поля протокола `proposed`, `discussed`, `risks`, `requires_clarification`, `status` и `source_quote` у `action_items`) и [`infra/postgres/init/03_seed_data.sql`](infra/postgres/init/03_seed_data.sql) (20 документов с разными `allowed_access_groups`, 3 встречи с расшифровками и задачами-сиротами, 5 тестовых ролей включая `Employee` `100005`, домен `@fotoprint.by`, расчёты в `BYN`).
- **Лицензии и контракты:** [`libraries.yaml`](libraries.yaml), [`docs/openapi.yaml`](docs/openapi.yaml), [`docs/formulas.md`](docs/formulas.md), [`access.md`](access.md), [`README.md`](README.md).
