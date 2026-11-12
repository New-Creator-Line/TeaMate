# 04. Фаза 3: Спецификация Веб-Интерфейса и Календарного Виджета

## 1. Спецификация UI-Компонентов в `index.html`

### 1.1. Кнопка календаря в шапке (`<header>`)
Размещается рядом с `.user-switcher`:
```html
<button id="calendar-toggle-btn" class="nav-btn calendar-btn" onclick="toggleCalendarDrawer()" title="Открыть расписание и встречи">
  📅 Календарь
  <span id="calendar-today-badge" class="calendar-badge" title="Встреч на сегодня">0</span>
</button>
```

---

### 1.2. Боковая Шторка (Slide-over Drawer: `#calendar-drawer`)

#### Разметка шапки шторки и переключателя RBAC:
```html
<div id="calendar-drawer" class="calendar-drawer">
  <div class="drawer-header">
    <div class="drawer-title">📅 Календарь встреч</div>
    <button class="drawer-close-btn" onclick="toggleCalendarDrawer()">✕</button>
  </div>

  <!-- RBAC переключатель режима просмотра -->
  <div class="calendar-scope-bar">
    <button id="scope-my-btn" class="scope-btn active" onclick="setCalendarScope('my')">
      👤 Мои встречи
    </button>
    <button id="scope-all-btn" class="scope-btn" onclick="setCalendarScope('all')" title="Доступно только генеральному директору">
      🏢 Вся компания <span class="lock-icon" id="scope-lock-icon">🔒</span>
    </button>
  </div>

  <!-- Сетка календаря месяца -->
  <div class="month-navigation">
    <button class="nav-arrow" onclick="changeMonth(-1)">‹</button>
    <span id="current-month-label" class="month-label">Ноябрь 2026</span>
    <button class="nav-arrow" onclick="changeMonth(1)">›</button>
  </div>

  <div class="calendar-grid-header">
    <span>Пн</span><span>Вт</span><span>Ср</span><span>Чт</span><span>Пт</span><span>Сб</span><span>Вс</span>
  </div>
  <div id="calendar-days-grid" class="calendar-days-grid"></div>

  <!-- Детали встреч выбранного дня -->
  <div class="day-details-panel">
    <div id="selected-day-title" class="day-title">Встречи на 12 ноября 2026:</div>
    <div id="day-meetings-list" class="meetings-list"></div>
  </div>
</div>
```

#### Логика RBAC для переключателя `scope`:
```javascript
function updateCalendarRbacUI() {
  const user = getCurrentUser();
  const isDirector = user.roles && (user.roles.includes('Director') || user.roles.includes('Admin'));
  const scopeAllBtn = document.getElementById('scope-all-btn');
  const lockIcon = document.getElementById('scope-lock-icon');

  if (isDirector) {
    scopeAllBtn.classList.remove('disabled');
    scopeAllBtn.removeAttribute('disabled');
    scopeAllBtn.title = 'Просмотр расписания всей компании';
    lockIcon.style.display = 'none';
  } else {
    scopeAllBtn.classList.add('disabled');
    scopeAllBtn.setAttribute('disabled', 'true');
    scopeAllBtn.title = 'Доступно только руководству (роль Director)';
    lockIcon.style.display = 'inline';
    if (currentCalendarScope === 'all') {
      setCalendarScope('my');
    }
  }
}
```

---

### 1.3. Бесстейтовая Карточка Подтверждения в Ленте Сообщений

HTML-разметка генерируется фронтендом на основе выбранного слота:
```html
<div class="meeting-confirm-card" id="confirm-card-temp">
  <div class="confirm-card-header">📋 Подтверждение записи в календарь</div>
  <div class="confirm-card-body">
    <div><strong>Тема:</strong> Встреча с Кузнецовым И.П.</div>
    <div><strong>Время:</strong> 12 ноября 2026, 14:30 – 15:00 (МСК)</div>
    <div><strong>Участники:</strong> Смирнов А.С. (Вы), Кузнецов И.П.</div>
  </div>
  <div class="confirm-card-actions">
    <button class="btn-confirm-yes" onclick="submitMeetingConfirmation(this)" 
            data-title="Встреча с Кузнецовым И.П."
            data-start="2026-11-12T14:30:00+03:00"
            data-end="2026-11-12T15:00:00+03:00"
            data-organizer="1"
            data-participants="[2]">
      ✅ Подтвердить и создать
    </button>
    <button class="btn-confirm-no" onclick="cancelMeetingCard(this)">
      ❌ Отмена
    </button>
  </div>
</div>
```

#### Обработчик подтверждения:
```javascript
async function submitMeetingConfirmation(button) {
  const payload = {
    title: button.getAttribute('data-title'),
    start_time: button.getAttribute('data-start'),
    end_time: button.getAttribute('data-end'),
    organizer_id: Number(button.getAttribute('data-organizer')),
    participant_ids: JSON.parse(button.getAttribute('data-participants') || '[]'),
    status: 'scheduled'
  };

  button.disabled = true;
  button.textContent = '⏳ Запись в календарь...';

  try {
    const res = await fetch('/api/calendar/confirm', {
      method: 'POST',
      headers: { 
        'Content-Type': 'application/json',
        'X-Telegram-Id': String(currentTelegramId)
      },
      body: JSON.stringify(payload)
    });

    if (res.ok) {
      const data = await res.json();
      const card = button.closest('.meeting-confirm-card');
      card.innerHTML = `
        <div class="confirm-success-banner">
          ✅ <strong>Встреча успешно создана в календаре!</strong>
          <div class="confirm-meta">Время: ${escapeHtml(data.start_time)}</div>
          <div class="notification-stub-badge">
            🔔 Оповещение участникам: смоделировано (эмуляция Telegram / Bitrix24)
          </div>
        </div>
      `;
      // Мгновенное реактивное обновление сетки календаря и бейджа
      refreshCalendarEvents();
    }
  } catch (err) {
    button.disabled = false;
    button.textContent = '❌ Ошибка, повторить';
  }
}
```

---

### 1.4. Стилистика и Индикаторы Плотности (Dots)

```css
/* Цветные индикаторы встреч в ячейке дня */
.day-dots {
  display: flex;
  justify-content: center;
  gap: 3px;
  margin-top: 4px;
}
.dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  display: inline-block;
}
.dot-green {
  background-color: #10b981;
  box-shadow: 0 0 5px rgba(16, 185, 129, 0.6);
}
.dot-amber {
  background-color: #f59e0b;
  box-shadow: 0 0 5px rgba(245, 158, 11, 0.6);
}
.dot-red {
  background-color: #ef4444;
  box-shadow: 0 0 5px rgba(239, 68, 68, 0.6);
}

/* Бейдж эмуляции оповещения */
.notification-stub-badge {
  font-size: 11.5px;
  color: #38bdf8;
  background: rgba(56, 189, 248, 0.1);
  border: 1px dashed rgba(56, 189, 248, 0.4);
  padding: 4px 8px;
  border-radius: 6px;
  margin-top: 6px;
  display: inline-block;
}
```
