# Task QA-022: Verification of Chat Workspace & Header UX Redesign (Nielsen 10 Checklist)

- **Scope & Assigned Worktree**: QA Verifier & Design Auditor (`qa-worker`)
- **Dependencies**: `022A`, `022B`, `022C`, `022D`
- **Focus**: End-to-end verification of Nielsen Heuristics, layout integrity, multi-theme consistency, and zero-regression on `#calendar-window-view`.

---

## 1. Nielsen Heuristic Verification Checklist

| # | Nielsen Heuristic | Verification Step | Pass Criteria |
| :--- | :--- | :--- | :--- |
| **1** | **Visibility of system status** | Check header model badge, active role indicator, and live recording wave. | Status is visible at a glance in a clean single-line header without visual ambiguity. |
| **2** | **Match system & real world** | Inspect Bento cards and typography terminology. | Uses real printing industry domains (1С тиражи, выработка цеха, нормы брака, MoM планерки). |
| **3** | **User control & freedom** | Toggle between `[💬 Ассистент]` and `[📅 Календарь]`. | Segmented control toggles views instantly without reload, preserved scroll and state. |
| **4** | **Consistency & standards** | Inspect header layout across `1024px`, `1440px`, and `1920px`. | **Strictly 56px height, ZERO vertical stacking or overlap** of controls. |
| **5** | **Error prevention** | Test quick chip clicks and file attachment triggers. | Clear modal/tooltip feedback before executing destructive or heavy actions. |
| **6** | **Recognition over recall** | Inspect initial empty state. | Bento grid provides 4 clear prompt pathways with icons and descriptive subtitles. |
| **7** | **Flexibility & efficiency** | Test Bento click pre-fill and `Enter` / `Shift+Enter` in composer. | Instant execution on Bento click; seamless multiline typing in composer. |
| **8** | **Aesthetic & minimalist design** | Visual inspection of whitespace, alignment, and typography. | Generous breathing room, clear focal point, no awkward 2-line chip wrapping. |
| **9** | **Help users recognize & recover** | Trigger an invalid API or network error in chat. | Clean contextual toast or message bubble with actionable retry button. |
| **10**| **Help & documentation** | Check tooltips on mic, paperclip, and system menu. | Self-explanatory microcopy and clear tooltips on every interactive control. |

---

## 2. Zero-Regression Gate for Calendar
- Verify that opening the Calendar via the segmented control displays the complete 3-column TickTick layout (`#calendar-window-view`) with drag-to-create, timegrid, and all existing features intact.
- Verify all existing backend tests (`dotnet test`) pass with 0 errors.
