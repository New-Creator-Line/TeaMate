# Task 022C: Floating Composer Capsule & Single-Line Smart Chips Bar

- **Scope & Assigned Worktree**: Frontend Interactions Developer (`coder-worker`)
- **File Boundaries**:
  - `infra/nginx/html/index.html` (Composer markup & chip click logic)
  - `infra/nginx/html/themes/theme-core.css` (Composer island & chips scroll styling)
- **Frozen Contract**: `.memory_bank/contracts/copilot_chat_ux_contract.md` (Section 3)

---

## 1. Problem Addressed
In the current design, 6 prompt chips awkwardly wrap into 2 uneven lines (5 chips on the top line, 1 lonely chip on the second line), breaking visual rhythm. The composer uses an unstyled text input box and a bright blue submit button that clashes with the dark aesthetic (violates Nielsen Heuristics #4 Consistency and #8 Aesthetic/Minimalist Design).

## 2. Implementation Guidelines

1. **Smart Chips Bar (`#chat-smart-chips`)**:
   - Wrap chips inside a single-row flex container with horizontal overflow scroll and edge fade gradients:
     ```html
     <div class="chat-chips-scroller" role="region" aria-label="Быстрые запросы">
       <div class="chat-chips-track">
         <button class="chat-chip" data-prompt="Каков статус заказа 44102 в 1С?">
           <span class="chip-prefix">1С</span> Статус заказа 44102
         </button>
         <button class="chat-chip" data-prompt="Покажи выработку и брак цеха">
           <span class="chip-prefix">KPI</span> Выработка цеха
         </button>
         <button class="chat-chip" data-prompt="Мои открытые задачи в Битрикс24">
           <span class="chip-prefix">CRM</span> Задачи Битрикс24
         </button>
         <button class="chat-chip" data-prompt="Покажи расписание планерок на сегодня">
           <span class="chip-prefix">План</span> Планерки
         </button>
         <button class="chat-chip" data-prompt="Нормы брака для офсетной печати Heidelberg">
           <span class="chip-prefix">RAG</span> Нормы брака
         </button>
         <button class="chat-chip chip-action" id="chip-upload-audio-btn">
           <span class="chip-icon">🎙️</span> Загрузить аудио (MoM)
         </button>
       </div>
     </div>
     ```
   - Micro-tags (`1С`, `KPI`, `CRM`, `RAG`) visually categorize queries so users immediately recognize the data source.

2. **Floating Capsule Composer (`#chat-composer-island`)**:
   - Redesign the input container into a centered floating capsule with max-width `820px`:
     - Rounded corners (`border-radius: 16px`), subtle internal padding.
     - Attachment button `[ 📎 ]` with tooltip «Прикрепить документ (PDF, DOCX, XLSX)».
     - Microphone button `[ 🎙️ ]` with hover state and live recording indicator (waveform pulse).
     - Auto-resizing textarea (1 to 5 rows) that responds to `Enter` (send) and `Shift+Enter` (new line).
     - Submit button `[ 🚀 ]` or `[ Отправить ]` styled with theme accent colors and active/disabled states.

## 3. Acceptance Criteria
1. Chips never break into 2 lines; they scroll smoothly with touch/mouse wheel.
2. The composer feels like a unified, floating modern capsule rather than a raw `<textarea>` with adjacent disjointed buttons.
3. Voice recording and file uploading trigger clean visual indicators without jumping or distorting the layout.
