# Task 010B: AI Gateway Calendar Endpoints & C# ActionCard Agent Interception

## 1. Goal
Implement gateway routing, tool calling in `FotoprintAiAgent`, and deterministic C# generation of the `ActionCard` DTO according to `docs/calendar_integration/01_ARCHITECTURE_AND_CONTRACTS.md` and `03_PHASE_2_AGENT_AND_INTERACTION_FLOW.md`.

## 2. Boundaries & Scope
- **Target Files**:
  - `src/Fotoprint.Core/Agents/FotoprintAiAgent.cs`:
    - Replace hardcoded `http://mcp-calendar:8084` with `_config.McpUrls.Calendar ?? "http://localhost:8084"`.
    - Register MCP tools: `find_free_slots`, `create_meeting`, `get_calendar_events`.
    - Add tool definition for LLM: `find_free_slots(organizer_employee_id, participant_employee_ids, date, duration_minutes)`.
    - Intercept `find_free_slots` tool execution in C#:
      - Parse available slots from MCP response.
      - Construct typed `ActionCard` object (`type: "slot_selection"`, `target_person`, `slots`, `duration_buttons`).
      - Return `action_card` attached to the agent response payload.
  - `src/Fotoprint.AiGateway.Api/Program.cs`:
    - Add endpoints:
      - `GET /api/calendar/events`: proxies query to `Fotoprint.Mcp.Calendar:8084/tools/get_calendar_events` passing `X-Internal-Service-Key` and `X-Telegram-Id`.
      - `POST /api/calendar/confirm`: proxies JSON payload to `Fotoprint.Mcp.Calendar:8084/tools/create_meeting`.
      - `POST /api/calendar/slots`: quick helper proxy to `Fotoprint.Mcp.Calendar:8084/tools/find_free_slots`.
  - `tests/Fotoprint.Tests/`:
    - Unit tests for calendar agent tool calls and `ActionCard` payload generation.
    - Ensure zero regressions in existing gateway endpoints.

## 3. Acceptance Criteria
- [ ] Calling agent via `POST /api/agent/chat` with prompt "Назначь встречу с Кузнецовым..." returns text reply AND typed `action_card` DTO with available slots.
- [ ] Direct `POST /api/calendar/confirm` creates the meeting via MCP.
- [ ] `GET /api/calendar/events` returns calendar events with correct RBAC headers.
- [ ] Test suite passes with 0 failures.
