# UI Multi-Theme & Calendar Integration Contract

## 1. Objective
Enable parallel development of three distinct high-end UI themes:
1. **Business Noir & Electric Amber** (`amber`)
2. **Linear & Raycast Obsidian Pro** (`obsidian`)
3. **Apple Liquid Glass / VisionOS** (`liquid-glass`)

And ensure seamless merging into a single production frontend (`infra/nginx/html/index.html`) featuring an instant Theme Switcher in the top-left navigation, while maintaining full compatibility with the **Calendar Integration Module** (`#calendar-drawer`, action cards, density dots).

---

## 2. Shared Functional DOM Invariants
All UI themes MUST preserve the following DOM element IDs and JavaScript contracts:

### Core Interaction
- `#chat-history`: The chat messages container.
- `#prompt-input`: Main text textarea/input.
- `#send-btn`: Message submission button.
- `#attach-audio-btn` & `#audio-file-input`: MoM meeting recording file upload.
- `#voice-toggle-btn`: Microphone recording trigger.
- `#voice-bar`: Floating recording control pill.
- `#voice-cancel-btn`: Cancel recording (`Esc`).
- `#voice-done-btn`: Confirm and transcribe recording (`Enter`).
- `#voice-timer`: Real-time recording duration badge.
- `#voice-waveform`: Canvas element (HTML5 2D context) for dynamic frequency visualization.
- `#user-select`: User/role switcher dropdown (`/api/agent/users`).
- `#role-badge`: Active RBAC role indicator (`Director`, `Production`, `Sales`, `Accounting`, `Guest`).

### Calendar Integration Elements
- `#calendar-toggle-btn`: Header button opening the calendar slide-over drawer.
- `#calendar-today-badge`: Count badge of meetings scheduled for the current day.
- `#calendar-drawer`: Slide-over panel (`.calendar-drawer`) with monthly grid and meeting list.
- `.chat-duration-selector`: Duration quick-selection buttons (`15m`, `30m`, `45m`, `60m`).
- `.chat-slots-container` & `.slot-chip`: Free slot chips in assistant chat bubbles.
- `.meeting-confirm-card`: Human-in-the-Loop booking confirmation card with `.btn-confirm-yes` and `.btn-confirm-no`.

---

## 3. Theme Architecture & Switching Mechanism

### Attribute-Based Theming
The active theme is governed by `data-theme` on the root `<html>` element:
```html
<html lang="ru" data-theme="liquid-glass">
```
Values:
- `data-theme="amber"`
- `data-theme="obsidian"`
- `data-theme="liquid-glass"`

### Theme Persistence
- Stored in `localStorage.getItem('fotoprint_theme') ?? 'liquid-glass'`.
- Dynamic switcher in header (top-left):
```html
<div class="theme-switcher" title="Переключение стиля оформления интерфейса">
  <label for="theme-select"><i data-lucide="palette"></i></label>
  <select id="theme-select" onchange="switchTheme(this.value)">
    <option value="liquid-glass">Apple Liquid Glass</option>
    <option value="amber">Business Noir & Amber</option>
    <option value="obsidian">Linear Obsidian Pro</option>
  </select>
</div>
```

### Modular Theme Stylesheets
Themes are isolated into separate CSS stylesheets to prevent git conflicts during parallel development:
- `infra/nginx/html/themes/theme-core.css` — Layout, grid, typography structure, shared utility classes.
- `infra/nginx/html/themes/theme-amber.css` — Business Noir & Electric Amber tokens, gradients, gold glowing borders.
- `infra/nginx/html/themes/theme-obsidian.css` — Monochrome onyx, subpixel borders (0.5px), keyboard chips (`⌘K`).
- `infra/nginx/html/themes/theme-liquid-glass.css` — Multi-layered frosted glass panels, ambient blur mesh spheres, optical bevels.

---

## 4. Iconography Standard
- **Lucide Icons** via official CDN: `https://unpkg.com/lucide@latest`.
- Zero system emojis (`🤖`, `🖨️`, `📦`, `✅`, `📅`).
- Dynamic icon hydration via `lucide.createIcons()` on page load and post message rendering.
