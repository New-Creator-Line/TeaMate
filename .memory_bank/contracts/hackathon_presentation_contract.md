# Frozen Contract: Startup Hackathon Presentation (`Fotoprint AI Copilot`)

- **Version**: 1.0
- **Status**: FROZEN
- **Source Specification**: `ТЗ_Хакатон_Единая_система_управления_компанией.pdf` (Track 3: AI-ассистент руководителей ООО «Фотопринт», 8–10 slides, 5–7 min demo)
- **Deliverables**:
  1. Visual Assets: `docs/presentation/assets/` (live UI screenshots, Mermaid architecture PNG/SVG, AntV metric charts)
  2. Interactive Web Deck + PDF: `docs/presentation/fotoprint-pitch.html` & `docs/presentation/fotoprint-pitch.pdf`
  3. Native Editable PowerPoint Deck: `docs/presentation/fotoprint-pitch.pptx`

---

## 1. Exact 9-Slide Structure (Mandatory Content Mapping)

| Slide | Title & Narrative Role | Mandatory Content & Key Messages | Visual / Asset |
| :--- | :--- | :--- | :--- |
| **1** | **Титульный слайд: Fotoprint AI Copilot** | Единая система AI-управления компанией (Трек 3: AI-ассистент руководителей ООО «Фотопринт»). Запуск за ≤15 минут, полный стек AI + MCP + RBAC. | Hero dark-glass visual + logo badge |
| **2** | **100% выполнение обязательных условий ТЗ** | Открывающий тезис: **мы закрыли все обязательные требования ТЗ в полном объёме** — `ОТ-01`–`ОТ-08`, `AI-01`–`AI-07`, все 6 обязательных сценариев, воспроизводимый стенд, тестовые данные без секретов. | Compliance matrix / 100% KPI cards |
| **3** | **Архитектура и сквозной поток** | Стек: Web UI → Nginx → .NET AI Gateway → n8n Orchestrator → MCP-серверы (1С, Битрикс24, Календарь, Документы) → PostgreSQL & Whisper Metal STT. | `mermaid-diagrams` architecture PNG/SVG |
| **4** | **Ключевые фичи: 3 темы, голос и протоколы MoM** | 3 премиальные темы (*Apple Liquid Glass*, *Business Noir & Amber*, *Linear Obsidian Pro*), ChatGPT-style голосовой ввод (Whisper STT), авто-протоколирование встреч (MoM → `.docx` + задачи в Битрикс24). | Live screenshots of UI themes & MoM card |
| **5** | **Умный корпоративный календарь (TickTick UX)** | Отдельное 3-колоночное окно календаря: режимы `День / Неделя / Месяц / Год`, drag-to-create с динамическим поповером, быстрый поиск по ФИО/должности (1000+ записей). | Live screenshots of Calendar & Drag-to-Create |
| **6** | **Безопасность: RBAC (`employee_reader`), Anti-Injection & Confirm** | 1) Все MCP-инструменты, создающие/изменяющие данные, **требуют явного подтверждения пользователя** (`AI-03`). 2) Защита от **prompt injection** (текст документов = данные, не команды) и **анти-спам / rate-limiting**. 3) Доступ к MCP проверяется на сервере по таблице **`employee_reader`** (у LLM права **только на чтение**, выдать доступ может **только менеджер/руководитель**). | Security shield diagram + confirmation card screenshot |
| **7** | **Честный AI и предсказуемая обработка ошибок** | 1) Если агент что-то не умеет или данных нет — он **честно сообщает об этом и ничего не придумывает** (`AI-05`, пометка «требует уточнения»). 2) При сбое сети/API или нехватке прав выдаётся **понятная структурированная ошибка** без зависания интерфейса (`ОТ-05`). | Before/After response & error card examples |
| **8** | **Наблюдаемость: Langfuse + Grafana + Алертинг** | 1) **Langfuse**: сквозной трейсинг промптов, вызовов LLM/MCP и аудит прав — любая попытка пользователя сделать то, на что нет прав, сразу фиксируется. 2) **Grafana + Alerts**: все логи централизованно льются в **Grafana** (ИТ-отдел видит ошибки в реальном времени), а настроенные **алерты** отправляют инциденты в канал ответственным людям для быстрого реагирования. | Langfuse trace + Grafana dashboard/alert flow visual |
| **9** | **Метрики качества и план перехода к пилоту** | Сводка тестового покрытия, латентности STT/API, соответствия 100-балльной шкале ТЗ (раздел 10) + 3-шаговый план промышленного пилота в ООО «Фотопринт». | `chart-visualization` / `antv-g2-chart` bar & KPI charts |

---

## 2. Installed Skills Playbook (Why & How to Use `.agents/skills/`)

| Skill Path | Why Use It | How to Apply It |
| :--- | :--- | :--- |
| `.agents/skills/frontend-slides/SKILL.md` | Best-in-class HTML/CSS/JS pitch deck workflow with anti-AI-slop presets (`STYLE_PRESETS.md`, `bold-template-pack/`) and 1920×1080 Playwright PDF export. | Read `SKILL.md` + `STYLE_PRESETS.md`; build `docs/presentation/fotoprint-pitch.html` with fixed 16:9 viewport (`viewport-base.css`), then run `scripts/export-pdf.sh`. |
| `.agents/skills/pptx/SKILL.md` | Official Anthropic skill for native editable `.pptx` via `pptxgenjs`, native charts (`addChart()`), strict layout rules, OOXML validation, and LibreOffice QA. | Read `SKILL.md`; generate `docs/presentation/fotoprint-pitch.pptx`, run `scripts/office/validate.py`, convert to PDF/JPG via `scripts/office/soffice.py` + `pdftoppm`, and visually inspect every slide. |
| `.agents/skills/pro-slides/SKILL.md` & `.agents/skills/high-quality-slides/SKILL.md` | Narrative pacing, executive typography, and scannable card/grid layouts for 8–10 slide startup pitches. | Consult before structuring slide copy so each slide has 1 clear action title and ≤40–60 words of crisp text. |
| `.agents/skills/chart-visualization/SKILL.md` & `.agents/skills/antv-g2-chart/SKILL.md` | Clean, publication-grade metric charts (KPI bars, latency, test coverage) without hand-drawn SVG bugs. | Generate high-DPI chart images for Slide 2 and Slide 9, or use native `addChart()` in `.pptx`. |
| `.agents/skills/mermaid-diagrams/SKILL.md` | Accurate system-architecture & security-flow diagrams. | Render Slide 3 (architecture) and Slide 6/8 (RBAC + Grafana alert pipeline) diagrams to crisp high-res PNG/SVG. |
