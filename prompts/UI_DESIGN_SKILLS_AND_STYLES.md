# 🎨 UI Design Skills for Claude & Modern Styling Concepts

В данном документе собраны проверенные скиллы (Claude Code Skills, Agent Skills, MCP-инструменты) с GitHub для генерации качественного веб-дизайна, а также подробный разбор необычных стилей оформления веб-интерфейса тестового дашборда Fotoprint.

---

## 🔗 Реестр скиллов Claude & инструментов UI/UX с GitHub

### 1. Официальные и ключевые скиллы для генерации дизайна

| Название / Репозиторий | Ссылка | Описание и особенности |
|---|---|---|
| **`anthropics/skills` — `frontend-design`** | [anthropics/skills (frontend-design)](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md) | **Официальный скилл от Anthropic**. Обучает ИИ-агента мыслить как ведущий дизайнер: планировать дизайн-систему (токены, цвета, шрифты) до написания кода, жестко отсекает «AI-штампы» (дешевые эмодзи, фиолетовые градиенты, одинаковые карточки) и внедряет визуальную сдержанность. |
| **`Koomook/claude-frontend-skills`** | [Koomook/claude-frontend-skills](https://github.com/Koomook/claude-frontend-skills) | Плагин скиллов Claude Code для создания выразительных, нестандартных интерфейсов. Содержит готовые пресеты: Liquid Glass, Cyberpunk, Nordic Minimalist, Brutalist, фоновые сетки и меш-градиенты. |
| **`jiji262/claude-design-skill`** | [jiji262/claude-design-skill](https://github.com/jiji262/claude-design-skill) | Портативный скилл, адаптированный из внутреннего системного промпта Claude.ai Design. Отлично подходит для сборки интерактивных веб-артефактов, лендингов и дашбордов. |
| **`VoltAgent/awesome-claude-design`** | [VoltAgent/awesome-claude-design](https://github.com/VoltAgent/awesome-claude-design) | Каталог из 68 готовых дизайн-систем в формате `DESIGN.md`. Позволяет быстро загрузить стиль в Claude Code / Cursor для генерации UI в едином визуальном языке. |
| **`bergside/awesome-design-skills`** | [bergside/awesome-design-skills](https://github.com/bergside/awesome-design-skills) | Реестр 67 дизайн-систем и скиллов для ИИ-агентов (Claude Code, Cursor, Codex). Готовые токены цветов, отступов и типографики. |
| **`nextlevelbuilder/ui-ux-pro-max-skill`** | [nextlevelbuilder/ui-ux-pro-max-skill](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill) | Комплексный профессиональный скилл: генерация дизайн-токенов, адаптивная верстка, семантическая иерархия и доступность (a11y). |
| **`plugin87/ux-ui-agent-skills`** | [plugin87/ux-ui-agent-skills](https://github.com/plugin87/ux-ui-agent-skills) | MCP-сервер и скиллы, превращающие модель в Senior UX/UI-дизайнера: аудит контраста по WCAG, устранение визуального шума, аудит интерактивных состояний. |
| **`artemnovitckii/claude-animation-skills`** | [artemnovitckii/claude-animation-skills](https://github.com/artemnovitckii/claude-animation-skills) | Набор скиллов для плавной микро-анимации интерфейсов: физика пружин, staggered reveals, WebGL и шейдерные эффекты. |
| **`Dammyjay93/interface-design`** | [Dammyjay93/interface-design](https://github.com/Dammyjay93/interface-design) | Скилл инжиниринга интерфейсов для Claude Code: компонентный подход, строгие токены, ревью макетов. |
| **`travisvn/awesome-claude-skills`** | [travisvn/awesome-claude-skills](https://github.com/travisvn/awesome-claude-skills) | Кураторский список лучших скиллов, включая `web-artifacts-builder` для генерации интерфейсов на React, Tailwind и shadcn/ui. |
| **`ComposioHQ/awesome-claude-skills`** | [ComposioHQ/awesome-claude-skills](https://github.com/ComposioHQ/awesome-claude-skills) | Большой каталог проверенных скиллов для автоматизации и фронтенд-разработки. |

---

## 🔮 Необычные стили оформления для интерфейса Fotoprint

Вместо текущего темного шаблона со смайликами (`🤖`, `🖨️`, `📦`, `✅`), интерфейс можно перевести на один из следующих выразительных стилей:

### 1. Liquid Glass & VisionOS (Жидкое стекло с оптической рефракцией)
* **Концепция**: Эволюция глассморфизма. Элементы ведут себя как линзы из оптического стекла: они преломляют свет и мягко отражают фон.
* **Фоновые элементы**: Темная бездна (`#06080d`) с 2–3 плавающими размытыми цветными сферами (ambient light mesh), создающими живую игру света при прокрутке.
* **Свойства стекла**:
  * `backdrop-filter: blur(25px) saturate(190%) brightness(110%)`
  * Полупрозрачный фон: `background: rgba(255, 255, 255, 0.03)`
  * Рамка-фаска с верхним бликом: `border: 1px solid rgba(255, 255, 255, 0.12)`, `border-top: 1px solid rgba(255, 255, 255, 0.25)`
  * Внутреннее свечение: `box-shadow: inset 0 1px 1px rgba(255, 255, 255, 0.1), 0 8px 32px rgba(0, 0, 0, 0.4)`
* **Иконки**: Тонкие векторные SVG-иконки **Lucide Icons** (штрих 1.5px), заменяющие все смайлики.
* **Звуковая волна микрофона**: Неоновый переливающийся градиент (Cyan → Emerald) внутри стеклянной капсулы.

---

### 2. High-Tech Cyber-Print (Полиграфический кибер-терминал)
* **Концепция**: Стиль современного промышленного пульта управления цифровой типографией.
* **Цветовая палитра CMYK & Carbon**:
  * Глубокий карбоновый фон: `#0a0b0e`
  * Акценты полиграфической триады в качестве светодиодных индикаторов:
    * **Cyan (`#00e5ff`)** — статус оборудования и подключение MCP
    * **Magenta (`#ff007f`)** — алерты и детекция смены контекста
    * **Yellow (`#ffe600`)** — дедлайны и задачи на контроле
* **Сетка**: Bento Grid (модульные блоки разного размера с тонкими техническими рамками).
* **Типографика**: Моноширинные метки (JetBrains Mono / Space Grotesk) для таймкодов, числовых ID и параметров тиражей 1С.

---

### 3. Linear / Raycast Obsidian (Инженерный ультра-минимализм)
* **Концепция**: Эстетика топовых инструментов для разработчиков (Linear, Raycast, Vercel).
* **Характеристики**:
  * Абсолютная строгость: почти черный матовый фон (`#09090b`), карточки с едва заметным градиентом от центра.
  * Субпиксельные границы: `border: 1px solid rgba(255, 255, 255, 0.07)`.
  * Интерактивность: клавиши-бейджы (`⌘K`, `Enter`, `Esc`), микро-анимации наведения без дерганий.
  * Никаких лишних цветов: 95% интерфейса монохромно, цвет появляется только на активных индикаторах статуса.

---

### 4. Swiss Neo-Brutalist Light / Editorial (Швейцарский журнальный стиль)
* **Концепция**: Светлая тема с контрастной газетно-журнальной типографикой высокого класса.
* **Характеристики**:
  * Фон: теплый бумажный оттенок (`#f9f8f6`), напоминающий качественную мелованную бумагу типографии.
  * Четкие графичные черные линии (`border: 1.5px solid #1a1a1a`), крупные акцидентные заголовки с засечками или плотный гротеск.
  * Архитектурные таблицы и плашки без размытий и теней — идеальная читаемость текста регламентов и протоколов встреч.

---

## 🛠 Как подключить скилл к Claude Code

Чтобы установить и применить выбранный скилл в Claude Code:

```bash
# 1. Установка официального frontend-design скилла от Anthropic:
claude plugin marketplace add anthropics/skills
claude plugin install example-skills@anthropic-agent-skills

# 2. Установка специализированных UI-скиллов от Koomook:
git clone https://github.com/Koomook/claude-frontend-skills.git ~/.claude/skills/claude-frontend-skills

# 3. Добавление дизайн-системы DESIGN.md:
# Скопируйте любой шаблон из VoltAgent/awesome-claude-design в корень проекта как DESIGN.md
```
