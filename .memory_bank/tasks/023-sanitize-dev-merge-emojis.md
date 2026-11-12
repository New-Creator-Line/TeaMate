# Task 023: Sanitize Unicode Emojis Introduced from DEV Merge into index.html

## 1. Scope & Objective
During merging `origin/dev` into `feature/ticktick-calendar-ui`, new features from `dev` (HITL pending action bar, protocol draft editing/confirmation) introduced 16 raw Unicode emojis into `infra/nginx/html/index.html`.
Our system has a strict architectural invariant: **Zero raw Unicode emojis in production UI** (enforced by `test_unified_index.py::test_zero_emojis` and `qa_comprehensive_audit.py::test_05_zero_emojis_in_index_html`).

Your task is to replace all raw Unicode emojis in `infra/nginx/html/index.html` with clean Lucide icons or semantic text badges.

## 2. Boundaries
- **Allowed Files**:
  - `infra/nginx/html/index.html`
- **Forbidden Files**:
  - Do NOT modify any other file in `infra/`, `src/`, or `tests/`.

## 3. Specific Emojis to Replace in `infra/nginx/html/index.html`
- **Lines ~4323-4328 (Pending Action Bar)**:
  - `makeBtn('✅ Подтвердить', ...)` -> Use Lucide icon or clean text: `makeBtn('<i data-lucide="check"></i> Подтвердить', ...)` or ensure `lucide.createIcons()` is called after appending, or text `'Подтвердить'`.
  - `makeBtn('✏️ Изменить', ...)` -> `'Изменить'` (or with Lucide icon).
  - `makeBtn('🚫 Отмена', ...)` -> `'Отмена'` (or with Lucide icon).
- **Line ~5126 (Proposed section header)**:
  - `<h4>💡 Предложено (инициативы):</h4>` -> `<h4><i data-lucide="lightbulb"></i> Предложено (инициативы):</h4>` or `<h4>Предложено (инициативы):</h4>`.
- **Lines ~5182-5184 (Action items meta)**:
  - `👤 Отв.:` -> `Отв.:` or `<i data-lucide="user"></i> Отв.:`
  - `📅 Срок:` -> `Срок:` or `<i data-lucide="calendar"></i> Срок:`
  - `📝 Цитата:` -> `Цитата:` or `<i data-lucide="quote"></i> Цитата:`
- **Lines ~5232-5235 (Draft protocol footer buttons)**:
  - `✏️ Править черновик` -> `Править черновик`
  - `✅ Финализировать протокол` -> `Финализировать протокол`
- **Lines ~5275-5305 (Status and Alert Messages in Bot responses)**:
  - `appendMessage('✅ Черновик протокола обновлён (DOCX пересобран).', 'bot');` -> `appendMessage('[OK] Черновик протокола обновлён (DOCX пересобран).', 'bot');`
  - `appendMessage('⚠️ Не удалось обновить черновик...', 'bot');` -> `appendMessage('[Внимание] Не удалось обновить черновик...', 'bot');`
  - `appendMessage('⚠️ Ошибка обновления...', 'bot');` -> `appendMessage('[Ошибка] Ошибка обновления...', 'bot');`
  - `confirmDraftBtn.textContent = '✅ Финализирован';` -> `confirmDraftBtn.textContent = 'Финализирован';`
  - `appendMessage('📋 Протокол подтверждён...', 'bot');` -> `appendMessage('[Протокол] Подтверждён и переведён в статус `final`.', 'bot');`
  - `appendMessage('⚠️ Ошибка финализации...', 'bot');` -> `appendMessage('[Ошибка] Ошибка финализации...', 'bot');`

## 4. Acceptance Criteria
1. `python3 infra/nginx/html/tests/test_unified_index.py` passes 8/8 tests with 0 failures (`test_zero_emojis` PASS).
2. `python3 tests/qa_comprehensive_audit.py` passes 13/13 tests with 0 failures (`test_05_zero_emojis_in_index_html` PASS).
3. `lucide.createIcons()` correctly runs where Lucide icons are used.
4. Clean git commit on worktree branch.
