# 🤖 AGENT.MD — Руководство по работе с кодовой базой Fotoprint AI Copilot

Данный документ предназначен для **ИИ-агентов (нейросетей, LLM-ассистентов) и разработчиков**, работающих с проектом **Fotoprint AI Copilot**. В нем описана актуальная архитектура, структура каталогов, инварианты безопасности и правила внесения изменений в код.

---

## 🧭 1. Назначение и контекст проекта

**Fotoprint AI Copilot** — корпоративная мультиагентная AI-платформа для типографии полного цикла («Фотопринт»).
Система автоматизирует:
1. **Протоколирование планерок (MoM) и постобработку Google Meet**: транскрибация записей созвонов и голосовых сообщений через Whisper / fallback `gemini-2.5-flash`, суммаризация LLM, выделение решений и поручений, экспорт в DOCX/PDF-HTML (включая полную стенограмму) и постановка задач в Битрикс24.
2. **Семантический и гибридный поиск (RBAC RAG)**: векторный поиск (`gemini-embedding-2`, `1536` измерений) с fallback на текстовый стем-поиск по внутренней базе знаний, техкартам, регламентам и созвонам Google Meet в `pgvector` с жесткой фильтрацией по группам доступа (`allowed_access_groups`).
3. **Автономный доступ к производственным данным (ReAct + Parallel MCP Tool Calling + HITL)**: предоставление актуальных данных из **1С:УНТ**, **Битрикс24** и **Корпоративного календаря** через изолированные MCP-микросервисы с проверкой RBAC и обязательным подтверждением пользователя (`[RequireConfirmation(true)]`) перед мутирующими действиями.
4. **LLM Observability & FinOps**: сквозной аудит всех запросов, спанов, стоимости токенов (`usageDetails` / `costDetails`) и операционных метрик (`scores`) через **Langfuse v3** (PostgreSQL + ClickHouse + Redis + MinIO).

---

## 📁 2. Детальная структура проекта

```text
Photoprint/
├── .env.example                       # Шаблон переменных окружения
├── config.yaml                        # Единый конфигурационный манифест (Single Source of Truth)
├── docker-compose.yml                 # Манифест оркестрации контейнеров
├── Fotoprint.slnx                     # Спецификация решения .NET 9 (формат slnx)
├── libraries.yaml                     # Декларативный реестр зависимостей
├── telegram_bridge.py                 # Вспомогательный скрипт моста Telegram
├── README.md                          # Пользовательская документация и диаграмма последовательности
├── agent.md                           # Данный файл: инструкции и инварианты для ИИ-агента
│
├── .agents/
│   └── skills.json                    # Регистрация локальных скиллов из ./skills для автообнаружения
├── skills/                            # Скиллы (runbook-инструкции) для агентов разработки
│   └── create-mcp-server/
│       └── SKILL.md                   # Инструкция по созданию MCP-серверов/инструментов с [RequireConfirmation]
│
├── infra/                             # Инфраструктурные скрипты и конфигурации
│   ├── nginx/
│   │   ├── nginx.conf                 # Reverse-proxy, Anti-Scanner (444), Honeypot, Rate Limiting (burst=200/150)
│   │   └── html/
│   │       └── index.html             # Веб-интерфейс Fotoprint AI Copilot (чат, STT, переключение ролей)
│   ├── postgres/
│   │   └── init/                      # SQL-скрипты автоматической инициализации PostgreSQL 16 + pgvector
│   │       ├── 01_init_databases.sql  # Создание БД: fotoprint, n8n, langfuse
│   │       ├── 02_schema.sql          # Таблицы: сотрудники, роли, сессии, документы, чанки (vector 1536), встречи
│   │       └── 03_seed_data.sql       # Начальные данные типографии, регламенты RAG и тестовые сотрудники
│   └── scripts/
│       └── bootstrap.sh               # Скрипт автоматической предзагрузки NuGet и npm модулей
│
├── n8n/                               # Сценарии оркестрации n8n (10 воркфлоу)
│   └── workflows/
│       ├── 01_main_router_and_auth.json           # Входной роутер, проверка сотрудника в EmployeeReader и вызов агента
│       ├── 02_rbac_rag_pipeline.json              # Векторный RAG-поиск с фильтрацией по ролям
│       ├── 03_pre_meeting_prep_cron.json          # Утренний CRON-сбор повестки и KPI цехов
│       ├── 04_live_meeting_copilot.json           # Интерактивный ассистент во время планерки
│       ├── 05_post_meeting_mom_and_tasks.json     # Постобработка заметок протокола (MoM) и постановка задач
│       ├── 06_executive_summary.json              # Аналитическая сводка для генерального директора
│       ├── 07_google_meet_post_processing.json    # Google Meet (webhook/cron) -> Whisper STT -> LLM саммари -> БД + RAG
│       ├── 08_meeting_recall_rag_and_export.json  # Поиск созвона (MasterId + gemini-embedding-2 + RBAC) -> саммари / DOCX|PDF
│       ├── 09_morning_schedule_digest_cron.json   # Утренний CRON-дайджест расписания в Telegram
│       └── 10_task_deadline_reminders_cron.json   # CRON-напоминания по дедлайнам задач в Telegram
│
├── src/                               # Исходный код сервисов на .NET 9 (C# 13)
│   ├── Fotoprint.Core/                # Общая библиотека ядра предметной области
│   │   ├── Agents/
│   │   │   ├── ContextSwitchDetector.cs       # Гибридный детектор смены контекста (FastPath + LLM fallback)
│   │   │   └── FotoprintAiAgent.cs            # Автономный ReAct-агент (история 10 сообщений, RBAC-кэш 45с, HITL, MCP+RAG)
│   │   ├── Audio/
│   │   │   ├── AudioEngineResolver.cs         # Поиск локального бинарника Whisper / Vulkan
│   │   │   ├── AudioModels.cs                 # Модели транскрибации и анализа встреч
│   │   │   ├── CloudGeminiAudioTranscriber.cs # Облачный STT-fallback на Gemini
│   │   │   ├── CompositeAudioTranscriber.cs   # Композитный STT (локальный Whisper -> облачный fallback)
│   │   │   ├── IAudioTranscriber.cs           # Интерфейс транскрибатора аудио
│   │   │   ├── LocalEngineAudioTranscriber.cs # Локальная транскрибация через Whisper CLI
│   │   │   ├── MeetingTranscriptAnalyzer.cs   # MoM-анализатор стенограмм без галлюцинаций
│   │   │   ├── VocabularyPostProcessor.cs     # Коррекция полиграфических терминов после STT
│   │   │   └── VocabularyProvider.cs          # Загрузчик словаря терминов из config/vocabulary.yaml
│   │   ├── Configuration/
│   │   │   └── FotoprintConfig.cs             # Строго типизированная модель и загрузчик config.yaml
│   │   ├── Data/
│   │   │   └── FotoprintRepository.cs         # Dapper-репозиторий (PostgreSQL, pgvector, RBAC-поиск, JsonbListTypeHandler)
│   │   ├── Documents/
│   │   │   └── DocumentParser.cs              # Парсинг DOCX (OpenXML), PDF (PdfPig), XLSX и нарезка на чанки
│   │   ├── Llm/
│   │   │   ├── GeminiClient.cs                # Клиент NVIDIA NIM / OpenAI API (NvidiaClient, Circuit Breaker, пул ключей, SSE)
│   │   │   └── GeminiEmbeddingClient.cs       # Клиент Google Gemini Embeddings API (gemini-embedding-2, 1536d, L2-норма)
│   │   ├── Mcp/
│   │   │   └── McpModels.cs                   # Контракты протокола MCP
│   │   ├── Models/
│   │   │   └── Entities.cs                    # Доменные модели (Employee, ChatSession, CorporateDocumentChunk, Meeting, Protocol)
│   │   ├── Protocols/
│   │   │   └── MeetingProtocolGenerator.cs    # Генератор структурированных протоколов DOCX и HTML/PDF (с RawTranscript)
│   │   ├── Resilience/
│   │   │   ├── LlmLoadBalancer.cs             # Приоритетная очередь (PriorityQueue) + сглаживание 40 RPM
│   │   │   └── RollbackSystem.cs              # Политика 3 повторов (Exponential Backoff) + компенсирующий роллбэк
│   │   ├── Security/
│   │   │   ├── EmployeeSecurityContext.cs     # Паспорт прав, RequirePermissionAttribute, RequireConfirmationAttribute
│   │   │   ├── PerimeterDefenseMiddleware.cs  # Защита от сканеров портов, ботов и запрещенных путей
│   │   │   └── SecurityInterceptorMiddleware.cs # Перехватчик RBAC, X-Internal-Service-Key и X-User-Confirmed (HITL)
│   │   ├── Telegram/
│   │   │   └── TelegramBotClient.cs           # Клиент Telegram Bot API, дедупликатор update_id, парсер Cloudflare Tunnel
│   │   └── Telemetry/
│   │       ├── LangfuseClient.cs              # Клиент Langfuse v3 (Traces с input/output, Spans, Generations с costDetails, Scores)
│   │       └── LlmJudgeService.cs             # Сервис автоматических оценок LLM-as-a-Judge
│   │
│   ├── Fotoprint.AiGateway.Api/       # Входной шлюз API (порт 8080 за Nginx, внешний маппинг 8088)
│   │   ├── Dockerfile
│   │   ├── Program.cs                         # /api/agent/*, /api/audio/*, /api/rag/*, /api/meetings/*, бэкфилл эмбеддингов
│   │   └── Telegram/
│   │       ├── CloudflareWebhookRegistrarService.cs # Фоновая авторегистрация вебхука через Quick Tunnel cloudflared
│   │       ├── TelegramEndpoints.cs           # Эндпоинты /api/telegram/webhook, /api/telegram/info, /api/telegram/webhook/register
│   │       └── TelegramWebhookProcessor.cs    # Асинхронная обработка текста, голоса/аудио и callback-кнопок Telegram
│   │
│   ├── Fotoprint.Mcp.EmployeeReader/  # Сервис аутентификации и RBAC-контекста (внутренний порт 8081)
│   ├── Fotoprint.Mcp.OneC/            # MCP-фасад к 1С:УНТ (внутренний порт 8082: заказы, производственные KPI)
│   ├── Fotoprint.Mcp.Bitrix24/        # MCP-фасад к Битрикс24 (внутренний порт 8083: задачи, сделки CRM)
│   ├── Fotoprint.Mcp.Calendar/        # MCP-фасад календаря и RAG созвонов (внутренний порт 8084: встречи, history, search_meeting_rag)
│   └── Fotoprint.Mcp.KnowledgeBase/   # Изолированный MCP-сервер базы знаний регламентов (внутренний порт 8086: search_typography_regulations, index_corporate_document)
│
└── tests/
    └── Fotoprint.Tests/               # Модульные и интеграционные тесты (xUnit, 119 тестов)
        ├── AudioTranscriptionTests.cs         # Тесты Whisper/Gemini STT, постобработки словаря и MoM-анализатора
        ├── ContextSwitchDetectorTests.cs      # Тесты классификации смены контекста (FastPath + LLM)
        ├── DocumentParserAndProtocolTests.cs  # Тесты парсинга документов и генератора MoM (включая RawTranscript)
        ├── FotoprintAiAgentTests.cs           # Тесты автономного агента, HITL-подтверждения, пакетного вызова тулов и RBAC
        ├── FotoprintConfigTests.cs            # Тесты загрузки единого манифеста config.yaml
        ├── GeminiClientTests.cs               # Тесты клиента NVIDIA/Gemini, SSE-стриминга и сериализации тулов
        ├── GeminiEmbeddingClientTests.cs      # Тесты gemini-embedding-2 (1536d, L2-нормализация, батчи, ошибки)
        ├── LangfuseTelemetryAndJudgesTests.cs # Тесты трейсинга, FinOps, Latency и LLM-as-a-Judge
        ├── LoadBalancerAndRollbackTests.cs    # Тесты приоритетной очереди, сглаживания RPM и роллбэка на 3 повтора
        ├── MasterIdAndSessionSwitchingTests.cs # Тесты резолвинга MasterId и ротации UUID SessionId
        ├── PerimeterDefenseTests.cs           # Тесты защиты от сканеров портов и honeypot-ловушек
        ├── SecurityContextTests.cs            # Тесты RBAC-прав и групп доступа
        ├── SecurityInterceptorTests.cs        # Тесты 403 Forbidden и проверки X-User-Confirmed
        └── TelegramBotClientTests.cs          # Тесты Telegram-клиента, дедупликации апдейтов и Cloudflare-туннеля
```

---

## 🔒 3. Главные архитектурные инварианты (НЕ НАРУШАТЬ!)

### 3.1. Принцип Zero Trust, Fail-Closed, RBAC и HITL (`[RequireConfirmation]`)
- **Каждый вызов к любому MCP-инструменту обязан проходить проверку прав доступа** через `InternalServiceAuthMiddleware` (`X-Internal-Service-Key`) и `SecurityInterceptorMiddleware` (`X-Telegram-Id`).
- **Обязательное использование `[RequireConfirmation]` и `[RequirePermission]`**:
  - На **каждом** MCP-эндпоинте и на **каждом** `Tool_*` методе в `FotoprintAiAgent` обязательны атрибуты `[RequirePermission("...")]` и `[RequireConfirmation(true/false)]` (`Fotoprint.Core.Security`).
  - Для **всех мутирующих операций** (`create_task`, `schedule_meeting` и любых новых инструментов создания/изменения/отправки) **строго ставится `[RequireConfirmation(true)]`**. Без подтверждения пользователя (`X-User-Confirmed: true`) агент выводит план действий и ждёт подтверждения, а `SecurityInterceptorMiddleware` блокирует прямой мутирующий вызов (`status: "confirmation_required"`).
  - При создании новых MCP-серверов или инструментов обязательно используй скилл **[skills/create-mcp-server/SKILL.md](skills/create-mcp-server/SKILL.md)**.
- **Fail-Closed при сбое БД**: В `FotoprintAiAgent` при недоступности PostgreSQL сотрудник переводится в безопасный профиль `Guest` (`EmployeeId = 0`, роль `Guest`, группа доступа `["all"]`) без повышения привилегий.
- **Передача реальных групп доступа в RAG**: Поиск в базе знаний (`SearchSimilarChunksAsync` / `SearchTextChunksAsync`) всегда принимает реальный список `secContext.AccessGroups` сотрудника (объединенный с `"all"` и личной меткой `master:{MasterId}`, а для руководителей — `c-level`).
- Если у пользователя нет прав на MCP-инструмент:
  1. Возвращается `403 Forbidden` с JSON-описанием требуемого разрешения.
  2. В Langfuse логируется инцидент `Event:Security_Violation` через `ILangfuseClient.LogSecurityAlertAsync`.
- **Защита от IDOR и подделки заголовков (RBAC / HITL Parity)**:
  - **`MCP-Bitrix24`**: эндпоинты `get_user_tasks` и `/tools/call` разрешают чтение задач только для своего `EmployeeId` (если пользователь не `Director`), а мутирующий `create_task` через `/tools/call` требует `X-User-Confirmed: true`.
  - **`MCP-Calendar`**: `get_upcoming_meetings`, `get_meeting_history` и `/tools/call` фильтруют встречи по участию (`CanAccessMeeting`: организатор, участник или `Director`), а `schedule_meeting` через `/tools/call` требует `X-User-Confirmed: true`.
  - **`AiGateway`**: внешние вызовы без валидного `X-Internal-Service-Key` к `/api/agent/execute`, `/api/rag/search`, `/api/rag/index-document`, `/api/rag/reindex-embeddings`, `/api/meetings/{id}/export-docx` и `/api/meetings/{id}/export-full` проходят обязательную проверку личности (`TelegramId` / `MasterId`) и ролей в БД (`Guest` / неизвестный ID -> `403 Forbidden`); передача произвольных `UserAccessGroups` извне игнорируется.

### 3.2. Матрица ролей типографии (Seed DB `03_seed_data.sql`)
| Сотрудник (Seed) | `MasterId` / `TelegramId` | Отдел / Роль | Группы доступа RAG (`AccessGroups`) |
| :--- | :---: | :--- | :--- |
| **Алексей Смирнов** | `100001` | Руководство / `Director` | `all`, `c-level`, `production`, `sales`, `accounting` |
| **Иван Кузнецов** | `100002` | Производство / `Production` | `all`, `production` |
| **Елена Морозова** | `100003` | Продажи / `Sales` | `all`, `sales` |
| **Ольга Соколова** | `100004` | Бухгалтерия / `Accounting` | `all`, `accounting` |
| **Внешние / Гости** | Неизвестный ID | `Guest` | Только `all` (на уровне БД), отказ на входе в `n8n` (`403 Forbidden`) |

### 3.3. Векторный и гибридный поиск RAG и созвоны Google Meet (`FotoprintRepository.cs`)
- **Эмбеддинги `gemini-embedding-2`**: Векторизация документов и запросов выполняется через `GeminiEmbeddingClient` (`outputDimensionality: 1536`, L2-нормализация, `taskType: RETRIEVAL_DOCUMENT` при индексации и `RETRIEVAL_QUERY` при поиске). При старте `ai-gateway` выполняется фоновый `BackfillMissingEmbeddingsAsync` для чанков без вектора, а также доступен ручной запуск `POST /api/rag/reindex-embeddings`.
- **Раздельные таблицы RAG (`corporate_document_chunks` vs `meeting_chunks`) и раздельные MCP**:
  - Таблицы `corporate_documents` и `corporate_document_chunks` хранят **только** корпоративные регламенты и инструкции и обслуживаются отдельным сервером **`MCP-KnowledgeBase`** (`http://mcp-knowledge-base:8086`, инструменты `search_typography_regulations` и `index_corporate_document`). SQL-поиск по регламентам фильтрует `d.source_type <> 'google_meet'`.
  - Таблица `meeting_chunks` (`meeting_id UUID REFERENCES meetings(id) ON DELETE CASCADE`, отдельный HNSW-индекс `vector(1536)` и GIN-индекс `allowed_access_groups`) хранит **только** векторизованные стенограммы и саммари созвонов Google Meet и обслуживается **`MCP-Calendar`** (`http://mcp-calendar:8084`, инструмент `search_meeting_rag`), а также REST-пайплайном `/api/meetings/save-and-index` и `/api/meetings/rag-search`.
- **Тип колонки RBAC — `jsonb`**: В таблицах `corporate_documents`, `corporate_document_chunks` и `meeting_chunks` колонка `allowed_access_groups` имеет тип **`jsonb`** (не `TEXT[]`!). В SQL-запросах используется оператор `allowed_access_groups ?| ARRAY(SELECT jsonb_array_elements_text(@Groups::jsonb))`. Для Dapper-маппинга `jsonb` → `List<int>` и `List<string>` в `FotoprintRepository` зарегистрирован `JsonbListTypeHandler<T>`.
- **Строгий RBAC по созвонам Google Meet**:
  - При сохранении созвона через `/api/meetings/save-and-index` чанки записываются в `meeting_chunks` с группами допуска `["c-level", "master:<id_организатора>", "master:<id_участника1>", ...]` (группа `"all"` **НЕ** добавляется).
  - Поиск `SearchMeetingSimilarChunksAsync` (и в `MCP-Calendar` `/tools/search_meeting_rag`, и в `/api/meetings/rag-search`) и выгрузка полной стенограммы (`/api/meetings/{id}/export-full`) **исключают** байпас по `'all'` — доступ разрешён только организатору, участникам созвона или пользователю с ролью `Director` (`c-level`).

### 3.4. Интеграция с NVIDIA NIM (`NvidiaClient`) и защита от квоты `40 RPM`
- **Актуальные модели**:
  - Основная модель (`LLM_MODEL` / `chat_model`) — `nvidia/nemotron-3-ultra-550b-a55b` (используется на шаге 1 выбора инструментов ReAct и при прямой генерации/саммаризации `GenerateContentAsync` с `enable_thinking = true`).
  - Быстрая и fallback-модель (`fast_model` / `fallback_model`) — `nvidia/nemotron-3.5-bolt-e4b` (используется на шаге 2 синтеза ответа `ChatTurnNoThinkingAsync` и в `ContextSwitchDetector` через `GenerateJsonNoThinkingAsync` с `enable_thinking = false`, а также как резервная модель Circuit Breaker при сбоях основной).
- **Чистая UTF-8 сериализация**: Все JSON-результаты инструментов в `FotoprintAiAgent` и тела запросов в `NvidiaClient` сериализуются с `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, чтобы кириллица передавалась в LLM без `\u04XX`-экранирования (экономия токенов в 3–4 раза и отсутствие искажений фактов).
- **Пул API-ключей (`Round-Robin`)**: Переменная `NVIDIA_API_KEY` (или `NVIDIA_API_KEYS`) поддерживает передачу нескольких ключей через запятую/точку с запятой с атомарной ротацией на каждом запросе.
- **Singleton-регистрация**: `NvidiaClient` и `GeminiEmbeddingClient` регистрируются в DI как `Singleton`.

### 3.5. Ограничение истории контекста до 10 сообщений и RBAC-кэш
- **Лимит истории в промпте (`max_history_messages: 10`)**: В `FotoprintAiAgent.ProcessMessageAsync` загружаются активные (`!IsFrozen`) сообщения текущей сессии, отфильтровывается дубликат текущего входящего сообщения и берется `.TakeLast(10)`.
- **Краткосрочный RBAC-кэш (`45 секунд`)**: Идентичные **read-only** запросы сотрудников с одинаковым профилем прав отдаются из памяти за `~200 мс` с `0` вызовов к LLM. Запросы, вызвавшие инструменты с `[RequireConfirmation(true)]`, в кэш **не** сохраняются.

### 3.6. Балансировка нагрузки (`Nginx` + `LlmLoadBalancer`) и Роллбэк (3 повтора)
- **Nginx (`infra/nginx/nginx.conf`)**:
  - `/api/telegram/webhook`: `rate=50r/s`, `burst=200 delay=50`.
  - `/api/`: `rate=30r/s`, `burst=150 nodelay`.
  - Периметровая защита MCP и anti-spoofing: прямые внешние маршруты `^/(tools|mcp|employee-reader|1c|bitrix24|calendar|knowledge-base|knowledgebase|get_user_tasks|create_task|schedule_meeting|get_upcoming_meetings|get_meeting_history|search_typography_regulations|search_meeting_rag|index_corporate_document|get_financial_report)` и `^/api/(tools|mcp)` блокируются на уровне Nginx с кодом `403`, а заголовки `X-Internal-Service-Key` и `X-User-Confirmed` принудительно очищаются на проксируемых маршрутах (`/api/telegram/webhook`, `/api/`, `/webhook/`).
- **`LlmLoadBalancer` (`src/Fotoprint.Core/Resilience/LlmLoadBalancer.cs`)**:
  - Ограничивает **только непосредственные вызовы к LLM** внутри `NvidiaClient` (внешний вызов `n8n` не занимает слот семафора LLM).
  - Реализует приоритетную очередь `PriorityQueue` (запросы руководства с `priority > 0` проходят первыми) и расчет окна `40 RPM`.
- **`RollbackManager` (`src/Fotoprint.Core/Resilience/RollbackSystem.cs`)**:
  - Выполняет 1 основную попытку + до 3 повторов с экспоненциальным backoff и джиттером, а при исчерпании попыток вызывает `compensatingRollbackAction` и пишет событие `Node:RollbackTriggered` в Langfuse.

### 3.7. Гибридный `ContextSwitchDetector` и ротация `SessionId`
- В `ContextSwitchDetector.DetectSwitchAsync`:
  1. Сначала выполняется детерминированный `TryClassifyFastPath` (`0` вызовов LLM, `< 1 мс`), определяющий ветку и маркеры продолжения/подтверждения плана действий.
  2. Мягкие переходы между рабочими вопросами сохраняют `IsSwitched = false` (общая память сессии).
  3. Явные команды смены задачи ставят `IsSwitched = true`, архивируют старую сессию (`SwitchToNewSessionAsync`) и создают новую сессию с новым UUID `SessionId`.
  4. Для нестандартных фраз вызывается резервный LLM-классификатор (`GenerateJsonNoThinkingAsync`).

### 3.8. Телеметрия Langfuse v3
- **Traces (`CreateTraceAsync`)**: обязательно заполняются `input`, `output`, `sessionId`, `userId`, теги статуса `status:ok` / `status:error` / `status:forbidden`, `branch:<branch>`.
- **Generations (`CreateGenerationAsync`)**: передаются `usageDetails` и `costDetails`, а в метаданные `Gen:Main_Agent` пишется `history_messages_count`.
- **Spans (`CreateSpanAsync`)**: логируются `Span:N8n_Orchestrator`, `Span:Audio_Transcribe`, `Span:MCP-*`.
- **Детерминированные Scores (`LogScoreAsync`)**: `agent_success` (`1`/`0`), `tools_invoked_count`, `context_switched` (`1`/`0`), `response_latency_sec`.

---

## 🧪 4. Обязательный чек-лист верификации после внесения правок

1. **Проверить сборку и прогнать все 119 тестов**:
   ```powershell
   dotnet test
   ```
   *Результат должен быть: не пройдено 0, пройдено 119.*

2. **При изменении сервисов пересобрать нужный контейнер (`ai-gateway` / MCP) и перезапустить Nginx**:
   ```powershell
   docker compose up -d --build --no-deps ai-gateway
   docker restart fotoprint-nginx
   ```

3. **Проверить health и ключевые эндпоинты RAG/агента**:
   ```powershell
   Invoke-RestMethod -Uri "http://localhost:8088/health"
   ```

---

## ⚠️ 5. Что категорически ЗАПРЕЩЕНО делать

- ❌ **Создавать мутирующие MCP-эндпоинты или `Tool_*` методы без `[RequireConfirmation(true)]` и `[RequirePermission(...)]`**.
- ❌ **Хардкодить секреты, токены и пароли** в исходном коде C# или конфигурациях.
- ❌ **Сериализовать русскоязычные данные для LLM без `UnsafeRelaxedJsonEscaping`**.
- ❌ **Оборачивать внешний вызов `n8n` в тот же семафор `LlmLoadBalancer`, который используется внутри `NvidiaClient`**.
- ❌ **Отключать проверки RBAC (`SecurityInterceptorMiddleware`, `InternalServiceAuthMiddleware`), фильтрацию `allowed_access_groups` в RAG или допускать группу `'all'` в поиске по созвонам `google_meet`**.
