# Task QA-018: QA Verification of Smart Employee Search Across All Interfaces & 3 Themes

## 1. Scope & Assigned Role
- **Role**: Independent QA Verifier (`qa-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)
- **Audits**: Tasks `018A`, `018B`, `018C`

## 2. Verification Checklist

### A. Backend & Unit Tests
1. Run `dotnet build Fotoprint.slnx` and `dotnet test` (all tests must pass).
2. Verify `GET /api/employees/search?q=Алексей%20Смирнов` and `GET /api/employees/search?q=Смирнов%20Алексей` return `Смирнов Алексей Сергеевич` at index 0.

### B. Smart Search & Highlighting Verification (Playwright)
1. **1,000-User Scale Test**: Inject 1,000 synthetic employees into `availableUsers` in browser page context and verify search response time stays `< 15ms` and dropdown shows top 5 closest matches.
2. **Order-Independent & Role Search**:
   - Search `"Алексей Смирнов"` → matches `Смирнов Алексей Сергеевич`.
   - Search `"Смирнов Алексей"` → matches `Смирнов Алексей Сергеевич`.
   - Search `"бухгалтер"` → matches `Соколова Ольга Викторовна` (via position).
   - Search `"Смирнв"` (typo) → still finds `Смирнов Алексей Сергеевич` via fuzzy match.
3. **Clear `✕` Button**:
   - Click `#drag-create-emp-clear`, `#quick-emp-clear`, and `#sidebar-emp-clear` → verify input value becomes `""` and list resets.
4. **Theme Highlighting**:
   - Assert `<mark class="cal-search-highlight">` is present in dropdown and sidebar results.
   - Switch between `obsidian`, `liquid-glass`, and `amber` themes and verify computed `background-color` / `color` of `.cal-search-highlight` matches each theme's accent palette.
5. **End-to-End Selection & Submit**:
   - Drag-create a meeting, search and select a participant via `#drag-create-emp-search`, verify chip appears, submit, and confirm the created event includes the selected participant.
6. **Regression**:
   - Run `python3 infra/nginx/html/tests/test_unified_index.py`, `test_obsidian_theme.py`, and `node infra/nginx/html/themes/test-theme-liquid-glass.js`.
