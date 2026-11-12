#  Prompt for Developer Agent: Redesign UI to Apple Liquid Glass & VisionOS Style

> **Цель для ИИ-агента**: Полностью переписать файл `infra/nginx/html/index.html`, преобразовав текущий устаревший шаблон в премиальный интерфейс **Apple Liquid Glass / VisionOS**, полностью устранив эмодзи и сохранив 100% существующей логики, API-эндпоинтов и UX-возможностей.

---

## 🧠 Скиллы, обязательные к использованию
При разработке и генерации кода агент обязан активировать и строго следовать правилам следующих дизайн-скиллов:
1. **`anthropics/skills/frontend-design`** ([Репозиторий](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md)):
   - Мыслить как арт-директор дизайн-студии: сначала сформировать дизайн-токены (цвета, шрифты, радиусы, свет), затем писать чистый код.
   - **Запрет AI-штампов**: Никаких системных эмодзи (`🤖`, `🖨️`, `📦`, `✅`), дешевых плоских рамок и случайных градиентов.
   - Сдержанность и фокус: одна главная запоминающаяся деталь (оптическое жидкое стекло с преломлением света), остальное — выверенная функциональная иерархия.
2. **`Koomook/claude-frontend-skills`** ([Репозиторий](https://github.com/Koomook/claude-frontend-skills)):
   - Применить пресет **Liquid Glass & Atmospheric Mesh**.
   - Использовать анимированные градиентные подложки, многослойный `backdrop-filter: blur(...)`, тонкие грани-фаски и контрастные веса шрифтов (100–300 vs 700–800).

---

## 🎨 Дизайн-система: Apple Liquid Glass & VisionOS

### 1. Цветовая палитра и материалы
* **Фоновое пространство**: Глубокий темный космический фон `#060810` с двумя плавающими полупрозрачными сферами подсветки (ambient aura mesh на чистом CSS):
  * Сфера 1 (верх-лево): `radial-gradient(circle, rgba(14, 165, 233, 0.15) 0%, transparent 70%)`
  * Сфера 2 (низ-право): `radial-gradient(circle, rgba(99, 102, 241, 0.12) 0%, transparent 70%)`
* **Материал «Жидкое стекло» (Liquid Glass Panels)**:
  * Фон: `background: rgba(255, 255, 255, 0.035)`
  * Размытие: `backdrop-filter: blur(24px) saturate(190%) brightness(105%)` (с префиксом `-webkit-backdrop-filter`)
  * Оптическая фаска (световой блик сверху):
    `border: 1px solid rgba(255, 255, 255, 0.1); border-top: 1px solid rgba(255, 255, 255, 0.28);`
  * Внутреннее преломление: `box-shadow: inset 0 1px 1px 0 rgba(255, 255, 255, 0.12), 0 12px 40px rgba(0, 0, 0, 0.45)`
  * Скругления: `border-radius: 18px` для крупных контейнеров, `12px` для кнопок, `9999px` для капсул.
* **Типографика**:
  * Системный стек Apple / Modern UI: `-apple-system, BlinkMacSystemFont, "SF Pro Display", "Inter", "Segoe UI", sans-serif`.
  * Четкая контрастность: основной текст `#f8fafc`, вторичный `#94a3b8`, ссылки и акценты `#38bdf8`.

### 2. Иконки: Полный отказ от эмодзи
* Подключить официальный CDN **Lucide Icons**:
  `<script src="https://unpkg.com/lucide@latest"></script>`
* Вызывать `lucide.createIcons()` при загрузке страницы и динамическом рендеринге сообщений / карточек MoM.
* Заменить все эмодзи на SVG-иконки с толщиной линии `data-lucide-stroke-width="1.5"`:
  * Логотип: `<i data-lucide="printer"></i>`
  * Бот / Пользователь: `<i data-lucide="bot"></i>`, `<i data-lucide="user"></i>`
  * Кнопки микрофона и вложений: `<i data-lucide="mic"></i>`, `<i data-lucide="paperclip"></i>`
  * Внешние сервисы: `<i data-lucide="cpu"></i>` (n8n), `<i data-lucide="activity"></i>` (Langfuse), `<i data-lucide="gauge"></i>` (Метрики)
  * Быстрые подсказки: `<i data-lucide="package"></i>`, `<i data-lucide="trending-up"></i>`, `<i data-lucide="check-square"></i>`, `<i data-lucide="calendar"></i>`, `<i data-lucide="book-open"></i>`
  * Карточка протокола (MoM): `<i data-lucide="file-text"></i>`, `<i data-lucide="sparkles"></i>`, `<i data-lucide="download"></i>`, `<i data-lucide="alert-triangle"></i>`, `<i data-lucide="help-circle"></i>`

---

## ⚙️ Функциональные инварианты (ОБЯЗАТЕЛЬНО СОХРАНИТЬ)
Новый файл `infra/nginx/html/index.html` обязан полностью сохранять работоспособность следующих механизмов:

1. **Диалоговый движок (`/api/agent/chat`)**:
   * Отправка сообщений с передачей `{ telegramId, chatId, message }`.
   * Отображение индикатора набора текста (`.typing`).
   * Рендеринг ответа ассистента с markdown-форматированием (заголовки, списки, жирный/курсивный шрифт, таблицы с `overflow-x: auto`, блоки `<code>`).
   * Отображение бейджей вызванных MCP-инструментов (`tools_invoked`) и веток n8n (`meta.orchestrator`, `meta.branch`).

2. **RBAC Переключатель пользователей (`/api/agent/users`)**:
   * Загрузка пользователей из API с резервным списком `FALLBACK_USERS` (Директор, Производство, Продажи, Бухгалтерия) и `GUEST_TEST_USER` (ID 999999 для проверки 403 Forbidden).
   * Сохранение `currentTelegramId` в `localStorage`.
   * Динамическое обновление бейджа роли (`Director`, `Production`, `Sales`, `Accounting`, `Guest`) и вывод системного уведомления в чат при переключении.

3. **Голосовой ввод и звуковой Canvas-визуализатор**:
   * Запись аудио через `navigator.mediaDevices.getUserMedia({ audio: true })`.
   * ChatGPT-style плавающая капсула записи (`#voice-bar`): кнопка отмены `✕` (Esc), таймер, кнопка отправки `✓` (Enter).
   * Web Audio API: `AudioContext`, `AnalyserNode` и анимированный холст `#voice-waveform`, отрисовывающий скругленные полосы эквалайзера с градиентом в реальном времени.
   * Определение поддерживаемого MIME-типа (с учетом iOS/Safari `audio/mp4` и Chrome/Firefox `audio/webm;codecs=opus`).
   * Отправка на `/api/audio/transcribe?language=ru` через `FormData` и вставка распознанного текста в поле ввода с бейджем статуса.

4. **Анализ аудио встреч и генерация протокола MoM (`/api/meetings/process-audio`)**:
   * Загрузка файла через скрытый `input[type="file"]` и кнопку-скрепку.
   * Отображение интерактивной карточки протокола:
     * Заголовок, длительность, провайдер (Gemini / Whisper).
     * Блоки: Резюме встречи, Принятые решения, Обсуждали, Риски и блокеры, Требует уточнения.
     * Список поручений (Action Items) с ответственными, дедлайнами и кнопками «В Битрикс24».
     * Кнопка скачивания протокола `.DOCX` (раскодирование base64 или переход на `/api/meetings/{id}/export-docx`).

5. **Постановка задач в Битрикс24 (`/api/telegram/webhook`)**:
   * Отправка `callback_query` с `data: create_bitrix_task:<id>` и визуальное переключение кнопки в состояние успеха («Задача создана»).

6. **Внешняя навигация и быстрые промпты**:
   * Ссылки на n8n (`:5678`), Langfuse (`:3000`), Метрики (`/api/system/stats`).
   * Кнопки-пилюли быстрых вопросов с мгновенной отправкой в чат.

---

## 📋 Инструкция по выполнению
1. Сформировать CSS с переменными темы Liquid Glass (`--glass-bg`, `--glass-border`, `--glass-shine`, `--text-primary`, `--accent-glow`).
2. Заменить HTML-структуру, внедрив фоновые ambient-сферы и стильные контейнеры с классом `.glass-panel`.
3. Подключить CDN `lucide.min.js`, заменить все emoji-строки на теги `<i data-lucide="..."></i>` и инициализировать их через `lucide.createIcons()`.
4. Проверить в браузере корректность рендеринга стекла, скролла чата, анимации микрофона и карточки MoM.
