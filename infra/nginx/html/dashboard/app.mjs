import { EMPLOYEES, SCREENS, INITIAL_DECISIONS, SYSTEMS_CONTOUR, CAPACITY_FORECAST } from './mock-data.mjs';

const $ = (id) => document.getElementById(id);

const urlParams = new URLSearchParams(window.location.search);
const tgParam = Number(urlParams.get('telegram_id') || localStorage.getItem('fotoprint_telegram_id') || 0);
const roleFromTg = tgParam === 100002 ? 'Production' : tgParam === 100003 ? 'Sales' : tgParam === 100004 ? 'Accounting' : tgParam === 100001 ? 'Director' : null;
const roleParam = urlParams.get('role');

const state = {
  authMode: 'login',
  role: (roleParam && EMPLOYEES[roleParam] ? roleParam : null) || roleFromTg || localStorage.getItem('picpac.role') || 'Director',
  screen: 'pulse',
  selectedSystem: 'erp',
  period: 30,
  dateFrom: '2026-10-13',
  dateTo: '2026-11-11',
  department: 'all',
  product: 'all',
  decisions: JSON.parse(localStorage.getItem('picpac.decisions') || 'null') || INITIAL_DECISIONS,
  lastPayload: null,
  backendConnected: false
};

function showToast(msg) {
  const t = $('toast');
  if (!t) return;
  t.textContent = msg;
  t.classList.add('show');
  clearTimeout(showToast._timer);
  showToast._timer = setTimeout(() => t.classList.remove('show'), 3600);
}

function fmt(num, unit = '') {
  const n = Number(num);
  if (!Number.isFinite(n)) return String(num);
  const s = new Intl.NumberFormat('ru-RU', {
    maximumFractionDigits: unit === '%' || unit === 'дн.' || unit === 'ч' ? 1 : 0
  }).format(n);
  return unit ? `${s} <small>${unit}</small>` : s;
}

function filterMultiplier() {
  const periodFactor = state.period === 7 ? 0.24 : state.period === 90 ? 2.85 : 1;
  const deptFactor = state.department === 'production' ? 0.92 : state.department === 'sales' ? 1.06 : state.department === 'accounting' ? 0.98 : 1;
  const prodFactor = state.product === 'books' ? 1.08 : state.product === 'prints' ? 0.89 : state.product === 'calendars' ? 0.94 : 1;
  return { periodFactor, deptFactor, prodFactor };
}

function renderArcGauge(card) {
  const pct = Math.max(0, Math.min(100, Math.round((card.current / card.plan) * 100)));
  const radius = 68;
  const circumference = Math.PI * radius;
  const offset = circumference * (1 - pct / 100);

  return `
    <article class="gauge-card">
      <div>
        <div class="kpi-top">
          <span>${card.title}</span>
          <span class="badge ${pct >= 90 ? 'good' : pct >= 75 ? 'warning' : 'critical'}">${card.time}</span>
        </div>
        <div class="kpi-val" style="margin:10px 0 6px">${fmt(card.current, card.unit)}</div>
        <div class="gauge-meta-grid">
          <div>
            <span>Дневной план</span>
            <strong>${new Intl.NumberFormat('ru-RU').format(card.plan)} ${card.unit}</strong>
          </div>
          <div>
            <span>Вып. плана</span>
            <strong style="color:${card.color}">${pct}%</strong>
          </div>
        </div>
      </div>
      <div class="gauge-wrap">
        <svg class="gauge-svg" viewBox="0 0 180 105" aria-label="Шкала выполнения дневного плана">
          <path d="M 22 90 A 68 68 0 0 1 158 90" fill="none" stroke="#E7DFD6" stroke-width="13" stroke-linecap="round"/>
          <path d="M 22 90 A 68 68 0 0 1 158 90" fill="none" stroke="${card.color}" stroke-width="13" stroke-linecap="round"
            stroke-dasharray="${circumference.toFixed(1)}" stroke-dashoffset="${offset.toFixed(1)}"/>
        </svg>
        <div class="gauge-center">
          <strong>${card.periodCurrent}</strong>
          <span>Итог периода · ${card.periodPlan}</span>
        </div>
      </div>
    </article>
  `;
}

function renderChart(series, forecastSeries) {
  const all = [...series, ...forecastSeries.slice(1)];
  const min = Math.min(...all) * 0.88;
  const max = Math.max(...all) * 1.06;
  const w = 720;
  const h = 165;

  const pt = (val, idx, total) => {
    const x = Math.round((idx / (total - 1)) * w);
    const y = Math.round(h - ((val - min) / (max - min || 1)) * (h - 24) - 10);
    return [x, y];
  };

  const totalPts = series.length + forecastSeries.length - 1;
  const factCoords = series.map((v, i) => pt(v, i, totalPts));
  const forecastCoords = forecastSeries.map((v, i) => pt(v, series.length - 1 + i, totalPts));

  const linePath = factCoords.map((p, i) => `${i === 0 ? 'M' : 'L'}${p[0]},${p[1]}`).join(' ');
  const areaPath = `${linePath} L${factCoords[factCoords.length - 1][0]},${h} L0,${h} Z`;
  const forecastPath = forecastCoords.map((p, i) => `${i === 0 ? 'M' : 'L'}${p[0]},${p[1]}`).join(' ');

  $('chartSvg').innerHTML = `
    <defs>
      <linearGradient id="picpacArea" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" stop-color="#DD5757" stop-opacity="0.28"/>
        <stop offset="100%" stop-color="#DD5757" stop-opacity="0.0"/>
      </linearGradient>
    </defs>
    <line x1="0" y1="35" x2="${w}" y2="35" stroke="#E6DFD6" stroke-dasharray="4 4"/>
    <line x1="0" y1="85" x2="${w}" y2="85" stroke="#E6DFD6" stroke-dasharray="4 4"/>
    <line x1="0" y1="135" x2="${w}" y2="135" stroke="#E6DFD6" stroke-dasharray="4 4"/>
    <path d="${areaPath}" fill="url(#picpacArea)"/>
    <path d="${linePath}" fill="none" stroke="#DD5757" stroke-width="3.5" stroke-linecap="round" stroke-linejoin="round"/>
    <path d="${forecastPath}" fill="none" stroke="#568CDE" stroke-width="3" stroke-dasharray="6 6" stroke-linecap="round"/>
    ${factCoords.map(([x, y]) => `<circle cx="${x}" cy="${y}" r="4.5" fill="#FFFFFF" stroke="#DD5757" stroke-width="3"/>`).join('')}
  `;
}

function renderScreen() {
  const payload = state.lastPayload;
  const emp = (payload && payload.employee) ? payload.employee : (EMPLOYEES[state.role] || EMPLOYEES.Director);
  const cfg = SCREENS[state.screen] || SCREENS.pulse;
  const { periodFactor, deptFactor, prodFactor } = filterMultiplier();

  const allowedList = emp.allowedDashboards || (EMPLOYEES[state.role] || EMPLOYEES.Director).allowedDashboards;
  $('userBadge').textContent = `${emp.name} · ${emp.roleLabel} (${emp.department})`;
  $('roleSwitcher').value = emp.role;
  $('pageTitle').textContent = (payload && payload.title) || cfg.title;
  $('pageSubtitle').textContent = (payload && payload.subtitle) || cfg.subtitle;
  $('chartTitle').textContent = (payload && payload.chartTitle) || cfg.chartTitle;

  document.querySelectorAll('[data-screen]').forEach((btn) => {
    const sc = btn.dataset.screen;
    const allowed = allowedList.includes(sc);
    btn.classList.toggle('active', sc === state.screen);
    btn.classList.toggle('locked', !allowed);
  });

  const activeKpis = (payload && Array.isArray(payload.kpis) && payload.kpis.length > 0) ? payload.kpis : cfg.kpis;

  $('kpiGrid').innerHTML = activeKpis.map((k, idx) => {
    const isFromBackend = Boolean(payload && Array.isArray(payload.kpis));
    const scale = isFromBackend ? 1 : (k.unit === 'BYN' || k.unit === 'кл.' || k.unit === 'шт.' ? periodFactor * deptFactor * prodFactor : deptFactor);
    const val = isFromBackend
      ? k.base
      : (k.unit === '%' ? (k.base * (0.98 + (prodFactor - 1) * 0.3)).toFixed(1) : Math.round(k.base * scale));
    const planVal = k.unit === '%'
      ? `${k.plan}%`
      : new Intl.NumberFormat('ru-RU').format(Math.round(k.plan * scale));
    return `
      <article class="kpi-card tone-${(idx % 5) + 1}" data-kpi="${k.id}">
        <div class="kpi-top">
          <span>${k.name}</span>
          <span class="badge ${k.status}">${k.delta}</span>
        </div>
        <div class="kpi-val">${fmt(val, k.unit)}</div>
        <div class="kpi-meta">
          <span>План: <strong>${planVal}</strong></span>
          <span>${k.source}</span>
        </div>
      </article>
    `;
  }).join('');

  document.querySelectorAll('[data-kpi]').forEach((card) => {
    card.onclick = () => {
      const k = activeKpis.find((item) => item.id === card.dataset.kpi);
      if (k) {
        openDecisionDrawer({
          title: `Детализация KPI: ${k.name}`,
          problem: `${k.name}: текущее отклонение (${k.delta}). Формула: ${k.formula}`,
          cause: `Источник: ${k.source}. Требуется проверка цепочки Заказ → Производство → 1С.`,
          action: `Назначить проверку показателя «${k.name}» ответственному подразделению и сверить результат через 72 ч.`
        });
      }
    };
  });

  const activeDailyPlans = (payload && Array.isArray(payload.dailyPlans) && payload.dailyPlans.length > 0)
    ? payload.dailyPlans
    : cfg.dailyPlans;
  $('dailyGrid').innerHTML = activeDailyPlans.map(renderArcGauge).join('');

  const activeSeries = (payload && Array.isArray(payload.series) && payload.series.length > 0) ? payload.series : cfg.series;
  const activeForecastSeries = (payload && Array.isArray(payload.forecastSeries) && payload.forecastSeries.length > 0) ? payload.forecastSeries : cfg.forecastSeries;
  renderChart(activeSeries, activeForecastSeries);

  const activeSystems = (payload && Array.isArray(payload.systems) && payload.systems.length > 0)
    ? payload.systems
    : SYSTEMS_CONTOUR;

  $('systemsList').innerHTML = activeSystems.map((sys) => `
    <div class="linked-item ${sys.id === state.selectedSystem ? 'active' : ''}" data-sys="${sys.id}">
      <div>
        <strong>${sys.name}</strong>
        <small>${sys.detail}</small>
      </div>
      <div style="text-align:right">
        <span class="badge ${sys.status === 'ok' ? 'good' : 'warning'}">${sys.freshness}</span>
        <small style="display:block;margin-top:4px">${sys.metric}</small>
      </div>
    </div>
  `).join('');

  document.querySelectorAll('[data-sys]').forEach((el) => {
    el.onclick = () => {
      state.selectedSystem = el.dataset.sys;
      const sys = activeSystems.find((s) => s.id === state.selectedSystem);
      renderScreen();
      if (sys) showToast(`Выбран контур: ${sys.name} (${sys.metric})`);
    };
  });

  const activeForecast = (payload && Array.isArray(payload.forecast) && payload.forecast.length > 0)
    ? payload.forecast
    : CAPACITY_FORECAST;

  $('forecastList').innerHTML = activeForecast.map((f) => `
    <div class="list-row">
      <div>
        <strong>${f.week}</strong>
        <small>Спрос: ${f.demand} · Мощность: ${f.capacity} · ${f.action}</small>
      </div>
      <div style="text-align:right">
        <span class="badge ${f.deficit ? 'critical' : 'good'}">${f.balance}</span>
        ${f.deficit ? `<button class="btn" style="margin-top:5px;padding:4px 8px;font-size:10px" data-deficit="1">В решение →</button>` : ''}
      </div>
    </div>
  `).join('');

  document.querySelectorAll('[data-deficit]').forEach((btn) => {
    btn.onclick = () => openDecisionDrawer({
      title: 'Решение по дефициту мощности (47-я неделя)',
      problem: 'Прогнозируемый дефицит мощности −640 ч на 47-й неделе (25 ноя – 1 дек)',
      cause: 'Пиковый сезонный спрос на фотокниги и календари превышает сменный фонд (4 680 ч)',
      action: 'Вывести резервную смену на выходные и перераспределить заказы Фотокниг Lite'
    });
  });

  const activeDecisions = (payload && Array.isArray(payload.decisions) && payload.decisions.length > 0)
    ? payload.decisions
    : state.decisions;

  $('decisionsList').innerHTML = activeDecisions.map((d) => `
    <div class="list-row">
      <div>
        <strong>${d.id} · ${d.problem}</strong>
        <small>Владелец: ${d.owner} · Срок: ${d.dueDate} · Эффект: ${d.beforeKpi} → ${d.afterKpi}</small>
      </div>
      <span class="badge ${d.status === 'Проверено' ? 'good' : d.status === 'В работе' ? 'critical' : 'warning'}">${d.status}</span>
    </div>
  `).join('');
}

async function syncWithBackend(targetScreen = state.screen) {
  if (state.dateFrom && state.dateTo && state.dateFrom > state.dateTo) {
    showToast('Ошибка: дата «От» не может быть позже даты «До»');
    return;
  }

  const emp = EMPLOYEES[state.role] || EMPLOYEES.Director;

  const qs = new URLSearchParams({
    dashboard: targetScreen,
    period: String(state.period),
    dateFrom: state.dateFrom,
    dateTo: state.dateTo,
    department: state.department,
    product: state.product,
    telegram_id: String(emp.telegramId)
  });

  try {
    const res = await fetch(`/api/system/stats?${qs}`, {
      headers: {
        'Accept': 'application/json',
        'X-Telegram-Id': String(emp.telegramId),
        'X-Master-Id': String(emp.masterId),
        'X-Demo-Role': emp.role
      }
    });

    if (res.status === 403) {
      const errData = await res.json().catch(() => ({}));
      showToast(errData.message || `403 Forbidden: раздел закрыт для роли ${emp.roleLabel} (УП-05)`);
      return;
    }

    if (res.ok) {
      const data = await res.json();
      state.screen = targetScreen;
      state.lastPayload = data;
      state.backendConnected = true;
      if (Array.isArray(data.decisions)) {
        state.decisions = data.decisions;
        localStorage.setItem('picpac.decisions', JSON.stringify(state.decisions));
      }
      const llmInfo = data.llm_config ? ` · LLM: ${data.llm_config.provider}` : '';
      $('syncStatus').textContent = `Подключено к бэкенду /api/system/stats · BYN · Europe/Minsk${llmInfo}`;
      renderScreen();
      return;
    }
  } catch {
    state.backendConnected = false;
  }

  if (!emp.allowedDashboards.includes(targetScreen)) {
    showToast(`403 Forbidden: раздел «${SCREENS[targetScreen].title}» закрыт для роли ${emp.roleLabel} (УП-05)`);
    return;
  }

  state.screen = targetScreen;
  state.lastPayload = null;
  $('syncStatus').textContent = `Автономный режим /api/system/stats · BYN · Europe/Minsk`;
  renderScreen();
}

function openDecisionDrawer(prefill = {}) {
  $('drawerTitle').textContent = prefill.title || 'Новое управленческое решение';
  if (prefill.problem) $('decProblem').value = prefill.problem;
  if (prefill.cause) $('decCause').value = prefill.cause;
  if (prefill.action) $('decAction').value = prefill.action;
  $('decisionDrawer').classList.add('open');
}

function closeDecisionDrawer() {
  $('decisionDrawer').classList.remove('open');
}

function initAuth() {
  if (tgParam || roleParam) {
    const emp = EMPLOYEES[state.role] || EMPLOYEES.Director;
    localStorage.setItem('picpac.session', emp.email);
    localStorage.setItem('picpac.role', emp.role);
  }

  const savedSession = localStorage.getItem('picpac.session');
  if (savedSession) {
    $('authOverlay').classList.add('hidden');
    $('appShell').classList.remove('hidden');
    syncWithBackend(state.screen);
  }

  const updateAuthMode = (mode) => {
    state.authMode = mode;
    $('tabLogin').classList.toggle('active', mode === 'login');
    $('tabRegister').classList.toggle('active', mode === 'register');
    $('regNameField').classList.toggle('hidden', mode !== 'register');
    $('regConfirmField').classList.toggle('hidden', mode !== 'register');
    $('authSubmitBtn').textContent = mode === 'register' ? 'Зарегистрироваться и войти' : 'Войти в дашборд';
    $('authError').classList.remove('show');
  };

  $('tabLogin').onclick = () => updateAuthMode('login');
  $('tabRegister').onclick = () => updateAuthMode('register');

  document.querySelectorAll('[data-preset-role]').forEach((btn) => {
    btn.onclick = () => {
      const roleKey = btn.dataset.presetRole;
      const emp = EMPLOYEES[roleKey];
      if (!emp) return;
      state.role = roleKey;
      $('authEmail').value = emp.email;
      $('authPassword').value = 'Fotoprint2026!';
      document.querySelectorAll('[data-preset-role]').forEach((b) => b.classList.toggle('active', b === btn));
    };
  });

  $('authForm').onsubmit = (e) => {
    e.preventDefault();
    const err = $('authError');
    const email = $('authEmail').value.trim();
    const pass = $('authPassword').value;

    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      err.textContent = 'Укажите корректный корпоративный e-mail (например, a.smirnov@fotoprint.by).';
      err.classList.add('show');
      return;
    }
    if (pass.length < 8) {
      err.textContent = 'Пароль должен содержать не менее 8 символов.';
      err.classList.add('show');
      return;
    }
    if (state.authMode === 'register' && pass !== $('authConfirm').value) {
      err.textContent = 'Пароли не совпадают.';
      err.classList.add('show');
      return;
    }

    const matchedRole = Object.values(EMPLOYEES).find((emp) => emp.email.toLowerCase() === email.toLowerCase());
    if (matchedRole) {
      state.role = matchedRole.role;
    }
    localStorage.setItem('picpac.session', email);
    localStorage.setItem('picpac.role', state.role);
    $('authOverlay').classList.add('hidden');
    $('appShell').classList.remove('hidden');
    showToast(`Добро пожаловать, ${(EMPLOYEES[state.role] || EMPLOYEES.Director).name}`);
    syncWithBackend(state.screen);
  };

  $('logoutBtn').onclick = () => {
    localStorage.removeItem('picpac.session');
    $('appShell').classList.add('hidden');
    $('authOverlay').classList.remove('hidden');
  };
}

function initEvents() {
  document.querySelectorAll('[data-screen]').forEach((btn) => {
    btn.onclick = () => {
      syncWithBackend(btn.dataset.screen);
    };
  });

  $('roleSwitcher').onchange = (e) => {
    state.role = e.target.value;
    localStorage.setItem('picpac.role', state.role);
    const emp = EMPLOYEES[state.role];
    localStorage.setItem('fotoprint_telegram_id', String(emp.telegramId));
    const nextScreen = emp.allowedDashboards.includes(state.screen) ? state.screen : 'pulse';
    showToast(`Переключена роль: ${emp.name} (${emp.roleLabel})`);
    syncWithBackend(nextScreen);
  };

  $('periodSelect').onchange = (e) => {
    state.period = Number(e.target.value);
    syncWithBackend(state.screen);
  };
  $('dateFrom').onchange = (e) => {
    state.dateFrom = e.target.value;
    syncWithBackend(state.screen);
  };
  $('dateTo').onchange = (e) => {
    state.dateTo = e.target.value;
    syncWithBackend(state.screen);
  };
  $('deptSelect').onchange = (e) => {
    state.department = e.target.value;
    syncWithBackend(state.screen);
  };
  $('productSelect').onchange = (e) => {
    state.product = e.target.value;
    syncWithBackend(state.screen);
  };

  $('refreshBtn').onclick = () => {
    syncWithBackend(state.screen);
    showToast('Срез /api/system/stats обновлён с бэкенда');
  };

  $('exportBtn').onclick = () => {
    const activeKpis = (state.lastPayload && Array.isArray(state.lastPayload.kpis))
      ? state.lastPayload.kpis
      : SCREENS[state.screen].kpis;
    const rows = activeKpis.map((k) => [k.name, k.base, k.unit, k.plan, k.source, k.formula]);
    const csv = 'Показатель;Значение;Ед.;План;Источник;Формула\n' +
      rows.map((r) => r.map((v) => `"${String(v).replaceAll('"', '""')}"`).join(';')).join('\n');
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8' }));
    a.download = `picpac-${state.screen}-${state.dateFrom}-${state.dateTo}.csv`;
    a.click();
    showToast('Отчёт выгружен в CSV');
  };

  $('newDecisionBtn').onclick = () => openDecisionDrawer();
  $('closeDrawerBtn').onclick = closeDecisionDrawer;
  $('cancelDrawerBtn').onclick = closeDecisionDrawer;
  $('decisionDrawer').onclick = (e) => {
    if (e.target === $('decisionDrawer')) closeDecisionDrawer();
  };

  $('decisionForm').onsubmit = async (e) => {
    e.preventDefault();
    const problem = $('decProblem').value.trim();
    const cause = $('decCause').value.trim();
    const action = $('decAction').value.trim();
    const owner = $('decOwner').value;
    const dueDate = $('decDue').value;
    const priority = $('decPriority').value;

    if (!problem || !action || !dueDate) {
      showToast('Заполните проблему, действие и срок');
      return;
    }

    const emp = EMPLOYEES[state.role] || EMPLOYEES.Director;
    const body = {
      problem,
      impact: 'Влияние зафиксировано в карточке KPI',
      cause,
      action,
      owner,
      dueDate,
      priority,
      department: state.department
    };

    try {
      const res = await fetch('/api/decisions', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Telegram-Id': String(emp.telegramId),
          'X-Master-Id': String(emp.masterId),
          'Idempotency-Key': `${emp.telegramId}-${problem.slice(0, 24)}-${dueDate}`
        },
        body: JSON.stringify(body)
      });

      if (res.ok) {
        const result = await res.json();
        if (Array.isArray(result.decisions)) {
          state.decisions = result.decisions;
          if (state.lastPayload) state.lastPayload.decisions = result.decisions;
          localStorage.setItem('picpac.decisions', JSON.stringify(state.decisions));
        }
        closeDecisionDrawer();
        renderScreen();
        showToast(`Решение ${result.decision?.id || ''} сохранено на бэкенде (/api/decisions)`);
        return;
      }
    } catch {
      // Fallback if offline
    }

    const newItem = {
      id: `DEC-${105 + state.decisions.length}`,
      problem,
      impact: 'Влияние зафиксировано в карточке KPI',
      cause,
      action,
      owner,
      dueDate,
      priority,
      status: 'Назначено',
      beforeKpi: 'Зафиксирован срез',
      afterKpi: `Проверка ${dueDate}`
    };

    state.decisions = [newItem, ...state.decisions];
    localStorage.setItem('picpac.decisions', JSON.stringify(state.decisions));
    closeDecisionDrawer();
    renderScreen();
    showToast(`Решение ${newItem.id} сохранено в реестр`);
  };
}

initEvents();
initAuth();
