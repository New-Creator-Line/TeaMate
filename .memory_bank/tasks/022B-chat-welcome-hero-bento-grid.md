# Task 022B: Welcome Hero & Interactive Bento Quick-Start Grid

- **Scope & Assigned Worktree**: Frontend UX Designer & Component Developer (`coder-worker`)
- **File Boundaries**:
  - `infra/nginx/html/index.html` (Hero HTML & click handlers)
  - `infra/nginx/html/themes/theme-core.css` (Bento grid layout & typography)
- **Frozen Contract**: `.memory_bank/contracts/copilot_chat_ux_contract.md` (Section 2)

---

## 1. Problem Addressed
The initial state of the chat tab is an empty dark void with a single dull paragraph of text. It provides no visual guidance, fails to showcase the capabilities of the printing house copilot, and forces the user to recall what questions they can ask (violates Nielsen Heuristic #6: Recognition rather than Recall, and #8: Aesthetic & Minimalist Design).

## 2. Implementation Guidelines

1. **Replace the monolithic initial bot message with `#chat-welcome-hero`**:
   - **Hero Header**:
     - Dynamic personalized title: `«Добрый день, Алексей Сергеевич»` (or generic `«Добро пожаловать в Fotoprint Copilot»`).
     - Subtitle explaining the system's role: `Интеллектуальный ассистент типографии: заказы 1С, выработка цехов, поручения CRM и технологические регламенты.`
   - **4-Card Bento Quick-Start Grid (`#chat-bento-grid`)**:
     - **Card 1 (1С Заказы & Тиражи)**:
       - Icon: `📦` / SVG Box.
       - Title: `Заказы и тиражи 1С`
       - Desc: `Проверить статус заказа №44102 или загрузку печатной машины Heidelberg`
       - Action: Click sends or fills prompt: `«Какой статус у заказа 44102 в 1С и готов ли тираж к отгрузке?»`.
     - **Card 2 (Производственные KPI & Брак)**:
       - Icon: `📊` / SVG Chart.
       - Title: `Пульс производства`
       - Desc: `Показать процент технологического брака и сменную выработку оборудования`
       - Action: Click sends: `«Покажи текущие KPI цеха: процент брака и выработку машин»`.
     - **Card 3 (Совещания & Задачи CRM)**:
       - Icon: `🎙️` / SVG Mic/Check.
       - Title: `Планерки и поручения`
       - Desc: `Запустить аудиопротокол планерки (MoM) или проверить задачи в Битрикс24`
       - Action: Click sends: `«Какие у меня открытые задачи в Битрикс24 и когда ближайшая планерка?»`.
     - **Card 4 (База знаний & Регламенты RAG)**:
       - Icon: `📖` / SVG Book.
       - Title: `Технологические регламенты`
       - Desc: `Нормы отходов, допуски офсетной печати, коммерческая политика и скидки`
       - Action: Click sends: `«Какие нормы технологического брака установлены для офсетной печати?»`.

2. **Transition on First Interaction**:
   - When the user sends a message (or clicks a Bento card), `#chat-welcome-hero` fades out smoothly (`opacity: 0`, height collapses, `display: none`), seamlessly revealing the active conversational thread without layout shifts.

## 3. Acceptance Criteria
1. The initial screen displays an inspiring, industrial printing Bento grid of 4 cards instead of a plain text block.
2. Clicking any Bento card immediately populates the input and executes the query with clear loading feedback.
3. Once messages exist, the Bento grid disappears and the message history occupies the full viewport.
