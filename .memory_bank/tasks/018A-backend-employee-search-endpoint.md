# Task 018A: Backend Scalable Employee Search Endpoint (`GET /api/employees/search`)

## 1. Scope & Assigned Worktree
- **Role**: Backend API Developer (`coder-worker`)
- **Target Branch / Worktree**: `feature/ticktick-calendar-ui` (`Workspace: "share"`)

## 2. File Boundaries (Strict Allowlist)
- `src/Fotoprint.Core/Data/FotoprintRepository.cs`
- `src/Fotoprint.AiGateway.Api/Program.cs`
- `tests/Fotoprint.Tests/`

*Do NOT modify `infra/nginx/html/**` in this task.*

## 3. Frozen Contract Reference
Implement Section 4 of `.memory_bank/contracts/employee_search_contract.md`:
1. Add `SearchEmployeesAsync(string query, int limit = 10, CancellationToken ct = default)` to `IFotoprintRepository` and `FotoprintRepository`:
   - Normalize `ё → е`, split query into whitespace tokens, and use parameterized SQL `WHERE is_active = TRUE` ensuring **every token** matches at least one of `(last_name, first_name, middle_name, position, department, email)` regardless of token order.
   - Rank results in C# or SQL by relevance (exact/prefix FIO token matches > position/department matches > substring matches) and return up to `limit` records.
2. Add `GET /api/employees/search` in `Fotoprint.AiGateway.Api/Program.cs`:
   - Query params: `q` (string, optional), `limit` (int, default `10`, max `50`).
   - Returns `{ employees: [...], total_matched: int }`.
   - Keep `GET /api/agent/users` 100% intact.
3. Add unit tests in `tests/Fotoprint.Tests/` verifying:
   - Order-independent FIO search (`"Алексей Смирнов"` and `"Смирнов Алексей"` return the same top match).
   - Position/department search (`"начальник производства"` finds `Кузнецов Иван Петрович`).
   - Empty/whitespace query returns top `limit` active employees.

## 4. Acceptance Criteria
1. `dotnet build Fotoprint.slnx` and `dotnet test` pass with 0 errors.
2. `GET /api/employees/search?q=Смирнов%20Алексей&limit=5` returns ranked JSON matching the contract.
