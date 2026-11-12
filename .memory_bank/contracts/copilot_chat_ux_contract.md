# Contract: Copilot Chat Workspace & Header UX Redesign (Stage 12)

- **Document ID**: `CONTRACT-020-CHAT-UX`
- **Status**: Frozen
- **Focus**: Redesign of the primary View / Chat Workspace according to Nielsen's 10 Usability Heuristics.
- **Affected Surface**: Header (`<header>`), `#chat-container`, welcome screen, prompt chips, and bottom composer in `infra/nginx/html/index.html` and themes.
- **Zero-Regression Guarantee**: Does **NOT** alter `#calendar-window-view` or any calendar logic. 100% rollbackable.

---

## 1. Header Redesign Specification (Single-Line 3-Zone Architecture)

Currently, 3 different controls (Theme Select, Calendar Button with badge, User Select) are stacked vertically in the center, causing awkward overlapping and misalignment.

### Target Layout (Strict 56px Single-Line Flexbox)
```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ [🖨️ Fotoprint Copilot] [• Model Pill] │ [💬 Чат Copilot | 📅 Календарь (0)] │ [Theme] [👤 User Profile] [Tools ▾] │
│           ZONE 1: Brand & Status      │      ZONE 2: Segmented Navigation   │     ZONE 3: Identity & Tools        │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

1. **Zone 1 (Left: Brand & System Status)**:
   - Logo icon `🖨️` + `Fotoprint Copilot` (16px semi-bold).
   - Status pill: `• NVIDIA Nemotron 120B` (compact, subtle glow, 11px font).
2. **Zone 2 (Center: Navigation Switcher - Segmented Control)**:
   - Modern sliding pill / segmented control (`.cal-segmented-control`):
     - `[ 💬 Ассистент ]` (Active by default on tab 1)
     - `[ 📅 Календарь (0) ]` (Toggles `#calendar-window-view`)
   - Eliminates vertical stacking and resolves Nielsen Heuristic #4 (Consistency) and #3 (User Control & Freedom).
3. **Zone 3 (Right: Settings, User Profile & System Tools)**:
   - **Theme Selector**: Custom-styled pill dropdown (`.theme-switcher-pill`), not a raw native `<select>`.
   - **User Profile Pill**: Clickable avatar + user name + role badge (e.g. `[ 👤 Алексей Смирнов | Директор ▾ ]`), which opens a clean modal/popover to switch role or view permissions.
   - **External Links Popover / Menu**: Compact `[ ⚙️ Инструменты ▾ ]` button grouping `n8n`, `Langfuse`, `Grafana Метрики`, cleaning up the right margin.

---

## 2. Chat Workspace & Hero Redesign (Bento Quick-Start Grid)

Currently, the chat workspace is an empty dark void with a single dull text paragraph.

### Target Layout:
1. **Hero Greeting Section (`#chat-welcome-hero`)**:
   - Personalized warm greeting based on time of day and selected role:
     `«Добрый день, Алексей Сергеевич. Чем Fotoprint Copilot может помочь сегодня?»`
   - Subtitle: `Корпоративный ассистент типографии: заказы 1С, выработка цехов, задачи Битрикс24 и регламенты.`
2. **Bento Quick-Start Action Cards (`#chat-bento-grid`)**:
   A 4-card responsive grid showcasing core capabilities (Nielsen #6: Recognition over Recall):
   - **Card 1: 📦 Заказы и тиражи (1С)**: «Проверить статус заказа №44102 или загрузку печатной машины» (Icon: Box / Printer).
   - **Card 2: 📊 Пульс цехов и KPI**: «Показать процент технологического брака и сменную выработку» (Icon: TrendingUp).
   - **Card 3: 🎙️ Совещания и MoM**: «Запустить аудиопротокол планерки или поставить задачи в Битрикс24» (Icon: Mic / Tasks).
   - **Card 4: 📖 Регламенты и нормы**: «Найти технологические карты, нормы отходов и скидки в RAG» (Icon: BookOpen).
   *Clicking any card pre-fills or immediately executes the contextual prompt.*
3. **Hide on First Message**:
   - The `#chat-welcome-hero` smoothly transitions away (`opacity: 0; display: none`) once the conversation starts, leaving full space for the message stream.

---

## 3. Quick Chips & Chat Composer Redesign

Currently, 6 chips wrap unevenly across 2 lines (5 on top, 1 lonely chip below), and the composer uses an unstyled text box and generic blue button.

### Target Layout:
1. **Dynamic Smart Chips Bar (`#chat-smart-chips`)**:
   - Single-line horizontal scroll with subtle fade masks on edges (`.chips-scroll-container`).
   - Chips categorized by intent with micro-badges:
     `[ 1С: Статус заказа 44102 ]` `[ 1С: Выработка цеха ]` `[ CRM: Мои задачи ]` `[ Календарь: Планерки ]` `[ RAG: Нормы брака ]` `[ 🎙️ Загрузить аудио MoM ]`
2. **Floating Capsule Composer (`#chat-composer-island`)**:
   - Floating rounded container (`border-radius: 16px`, subtle backdrop blur, hairline border).
   - Action buttons:
     - `[ 📎 ]` Attach document (PDF, DOCX, XLSX).
     - `[ 🎙️ ]` Mic button with pulsing waveform visualizer.
     - Auto-expanding textarea with placeholder `«Задайте вопрос по производству, тиражам или совещаниям...»`.
     - `[ 🚀 Отправить ]` (Clean, theme-tinted button, not generic bright blue).

---

## 4. Multi-Theme Token Mapping

| Component | Linear Obsidian Pro | Apple Liquid Glass | Business Noir & Amber |
| :--- | :--- | :--- | :--- |
| **Header Surface** | Matte `#141414`, hairline `rgba(255,255,255,0.08)` | Translucent glass `blur(20px)`, specular border | Deep Noir `#0d0f14`, amber hairline `rgba(245,158,11,0.18)` |
| **Segmented Active** | Solid `#222`, white text | Frosted glass pill, glowing border | Dark amber `#1f1a10`, electric amber border |
| **Bento Card Surface**| Subtle dark card `#18181b`, border `#27272a` | Frosted glass `blur(16px)`, border `rgba(255,255,255,0.12)`| Noir `#12151c`, amber border `rgba(245,158,11,0.15)` |
| **Composer Island** | Card `#1c1c1f`, focus indigo `#6366f1` | Glass `blur(24px)`, focus sky `#38bdf8` | Noir `#161922`, focus amber `#f59e0b` |
| **Primary Button** | Accent indigo `#6366f1` / `#4f46e5` | Vibrant sky blue `#0284c7` / `#38bdf8` | Electric Amber `#f59e0b` with black text |
