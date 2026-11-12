# Task Registry & DAG — Fotoprint AI Copilot

| Task ID | Name | Assigned Role | Worktree / Branch | Paired QA Agent | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `001`–`016` | SOLID Audio v2.0, Calendar MCP, TickTick 3-Column Calendar & 3 Themes | Core / API / UI Teams | `feature/ticktick-calendar-ui` | `QA-001`–`QA-015` | `[Completed - Verified]` |
| `017A` | TickTick Drag-to-Create Selection & Dynamic Left/Right Anchored Popover | Frontend Calendar Interactions Developer | `feature/ticktick-calendar-ui` | `QA-017` | `[Completed - Verified]` |
| `017B` | Multi-Theme Styling for Drag Ghost & Anchored Popover | UI Theme & Styling Specialist | `feature/ticktick-calendar-ui` | `QA-017` | `[Completed - Verified]` |
| `QA-017` | Playwright & Multi-Theme QA Verification of Drag-to-Create | QA Verifier | `feature/ticktick-calendar-ui` | Audits `017A–017B` | `[Completed - PASS]` |
| `018A` | Backend Scalable Employee Search Endpoint (`GET /api/employees/search`) | Backend API Developer (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-018` | `[To Do]` |
| `018B` | Frontend Smart Tokenized FIO/Role Search, Clear `✕` & Autocomplete UI | Frontend Calendar & Search Developer (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-018` | `[To Do — Parallel with 018A, 018C]` |
| `018C` | Multi-Theme Styling for Employee Search, Chips, Dropdown & `<mark>` Highlights | UI Theme & Styling Specialist (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-018` | `[To Do — Parallel with 018A, 018B]` |
| `QA-018` | Comprehensive QA & 1,000-User Scale Test of Smart Employee Search | QA Verifier (`qa-worker`) | `feature/ticktick-calendar-ui` | Audits `018A–018C` | `[To Do — Depends on 018A, 018B, 018C]` |
| `019A` | Implement Calendar Views (`Day`, `Month`, `Year`) & Fix `#cal-view-select` | Frontend Calendar Developer (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-019` | `[Completed - Merged]` |
| `019B` | Multi-Theme Styling for `Day`, `Month`, and `Year` Views | UI Theme & Styling Specialist (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-019` | `[Completed - Merged]` |
| `QA-019` | Playwright & Multi-Theme QA Verification of `Day`, `Week`, `Month`, `Year` Views | QA Verifier (`qa-worker`) | `feature/ticktick-calendar-ui` | Audits `019A–019B` | `[Completed - Verified]` |
| `020A` | Capture Live UI Screenshots & Generate Mermaid/AntV Diagrams & Charts | Visual Assets Specialist (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-020` | `[To Do]` |
| `020B` | Build 9-Slide Interactive HTML Presentation & 1920×1080 PDF (`frontend-slides`) | Frontend Presentation Developer (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-020` | `[To Do — Depends on 020A, Parallel with 020C]` |
| `020C` | Build 9-Slide Native Editable `.pptx` Deck (`pptx` + `ppt-design-skill`) | PowerPoint Deck Developer (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-020` | `[To Do — Depends on 020A, Parallel with 020B]` |
| `QA-020` | Visual, OOXML & ТЗ Content Audit of Startup Presentation | QA Verifier (`qa-worker`) | `feature/ticktick-calendar-ui` | Audits `020A–020C` | `[To Do — Depends on 020B, 020C]` |
| `021A` | Fix Time-Grid / Header Column Misalignment & Cumulative Drift | Frontend Layout Engineer (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-021` | `[Completed - Merged]` |
| `021B` | Fix Drag-to-Create Popover Time Row Width & Overflow Defect | UI Theme & Styling Specialist (`coder-worker`) | `feature/ticktick-calendar-ui` | `QA-021` | `[Completed - Merged]` |
| `QA-021` | QA Verification for Timegrid Column Alignment and Popover Overflow | QA Verifier (`qa-worker`) | `feature/ticktick-calendar-ui` | Audits `021A–021B` | `[Completed - Verified]` |

---

## Execution DAG for Stage 12 (Startup Hackathon Presentation) & Stage 13 (Layout & Popover Remediation)

```text
Stage 13 (Layout & Popover Remediation):
   [021A: Column Dividers Drift Fix] + [021B: Popover Time Overflow Fix] ──▶ [QA-021]
```

```text
   ┌────────────────────────────────────────────────────────┐
   │ Task 020A: Screenshots, Mermaid Diagrams & AntV Charts │
   └─────────────────────────┬──────────────────────────────┘
                             │
             ┌───────────────┴───────────────┐
             ▼                               ▼
   ┌───────────────────┐           ┌───────────────────┐
   │    Task 020B      │           │    Task 020C      │
   │ HTML + PDF (16:9) │           │ Editable .pptx    │
   └─────────┬─────────┘           └─────────┬─────────┘
             └───────────────┬───────────────┘
                             ▼
                   ┌───────────────────┐
                   │    Task QA-020    │
                   │ Visual & ТЗ Audit │
                   └───────────────────┘
```

---

## Stage 14: Copilot Chat Workspace & Header UX Redesign (Nielsen 10 Overhaul)

| Task ID | Name | Assigned Role | Worktree / Branch | Paired QA Agent | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `022A` | Single-Line 3-Zone Header & Segmented Navigation Switcher | Frontend UX Architect (`coder-worker`) | `feature/copilot-chat-redesign` | `QA-022` | `[To Do — Parallel with 022B, 022C, 022D]` |
| `022B` | Welcome Hero & Interactive Bento Quick-Start Grid | Frontend UX Designer (`coder-worker`) | `feature/copilot-chat-redesign` | `QA-022` | `[To Do — Parallel with 022A, 022C, 022D]` |
| `022C` | Floating Composer Capsule & Single-Line Smart Chips Bar | Frontend Interactions Developer (`coder-worker`) | `feature/copilot-chat-redesign` | `QA-022` | `[To Do — Parallel with 022A, 022B, 022D]` |
| `022D` | Multi-Theme Styling for Header, Hero & Composer (3 Themes) | UI Theme & Styling Specialist (`coder-worker`) | `feature/copilot-chat-redesign` | `QA-022` | `[To Do — Parallel with 022A, 022B, 022C]` |
| `QA-022` | End-to-End Verification of Nielsen 10 Checklist & Zero Calendar Regression | QA Verifier (`qa-worker`) | `feature/copilot-chat-redesign` | Audits `022A–022D` | `[To Do — Depends on 022A–022D]` |

```text
Stage 14 (Copilot Chat Workspace UX Redesign):
   [022A: Header & Nav] + [022B: Bento Hero] + [022C: Composer & Chips] + [022D: 3 Themes] ──▶ [QA-022]
```

