export const EMPLOYEES = {
  Director: {
    masterId: 100001,
    telegramId: 100001,
    name: 'Алексей Смирнов',
    role: 'Director',
    roleLabel: 'Генеральный директор',
    department: 'Руководство',
    email: 'a.smirnov@fotoprint.by',
    allowedDashboards: ['pulse', 'sales', 'production', 'finance', 'decisions']
  },
  Production: {
    masterId: 100002,
    telegramId: 100002,
    name: 'Иван Кузнецов',
    role: 'Production',
    roleLabel: 'Начальник производства',
    department: 'Производство',
    email: 'i.kuznetsov@fotoprint.by',
    allowedDashboards: ['pulse', 'production', 'decisions']
  },
  Sales: {
    masterId: 100003,
    telegramId: 100003,
    name: 'Елена Морозова',
    role: 'Sales',
    roleLabel: 'Руководитель отдела продаж',
    department: 'Продажи',
    email: 'e.morozova@fotoprint.by',
    allowedDashboards: ['pulse', 'sales', 'decisions']
  },
  Accounting: {
    masterId: 100004,
    telegramId: 100004,
    name: 'Ольга Соколова',
    role: 'Accounting',
    roleLabel: 'Главный бухгалтер',
    department: 'Бухгалтерия',
    email: 'o.sokolova@fotoprint.by',
    allowedDashboards: ['pulse', 'finance', 'decisions']
  }
};

export const SCREENS = {
  pulse: {
    title: 'Пульс компании',
    subtitle: 'Единый контур 5 систем: от сигнала отклонения до решения и повторной проверки',
    kpis: [
      { id: 'turnover', name: 'Оборот', base: 1284600, plan: 1209000, unit: 'BYN', delta: '+6,2%', status: 'good', source: '1С / ERP', formula: 'SUM(order.amount) по оплаченным заказам' },
      { id: 'margin', name: 'Валовая маржа', base: 38.4, plan: 36.6, unit: '%', delta: '+1,8 п.п.', status: 'good', source: '1С', formula: '(Оборот − Себестоимость) / Оборот' },
      { id: 'ontime', name: 'Заказы в срок', base: 91.8, plan: 94.2, unit: '%', delta: '−2,4 п.п.', status: 'warning', source: 'ERP', formula: 'delivered_at <= promised_at / completed' },
      { id: 'defect', name: 'Брак / перепечатка', base: 8.1, plan: 5.0, unit: '%', delta: '+3,1 п.п.', status: 'critical', source: 'ERP', formula: 'orders(defective=true) / released_orders' },
      { id: 'sla', name: 'SLA поддержки', base: 95.4, plan: 96.0, unit: '%', delta: '−0,6 п.п.', status: 'good', source: 'Омниканал', formula: 'closed_in_sla / total_closed' }
    ],
    dailyPlans: [
      { id: 'dp_rev', title: 'Выручка за смену', time: 'на 14:30', current: 42800, plan: 48500, unit: 'BYN', periodCurrent: '1,28 млн', periodPlan: '1,21 млн BYN', color: '#DD5757' },
      { id: 'dp_print', title: 'Выпуск фотокниг', time: 'на 14:30', current: 312, plan: 360, unit: 'шт.', periodCurrent: '8 420 шт.', periodPlan: '8 900 шт.', color: '#568CDE' },
      { id: 'dp_sla', title: 'Закрыто обращений SLA', time: 'на 14:30', current: 118, plan: 124, unit: 'обр.', periodCurrent: '95,4%', periodPlan: 'План 96,0%', color: '#689452' }
    ],
    chartTitle: 'Динамика оборота и прогноз выполнения плана (BYN)',
    series: [68, 74, 71, 83, 89, 94, 102],
    forecastSeries: [102, 108, 115]
  },
  sales: {
    title: 'Продажи и клиент (CRM / Битрикс24)',
    subtitle: 'Воронка, конверсия, средний чек, удержание клиентов и SLA омниканала',
    kpis: [
      { id: 'conv', name: 'Конверсия воронки', base: 23.4, plan: 21.0, unit: '%', delta: '+2,4 п.п.', status: 'good', source: 'Битрикс24', formula: 'Оплаченные заказы / Лиды' },
      { id: 'avg_check', name: 'Средний чек', base: 164, plan: 150, unit: 'BYN', delta: '+9,3%', status: 'good', source: 'CRM / ERP', formula: 'Оборот / Число заказов' },
      { id: 'new_cust', name: 'Новые клиенты', base: 1420, plan: 1350, unit: 'кл.', delta: '+5,1%', status: 'good', source: 'CRM', formula: 'Клиенты с 1-м заказом в периоде' },
      { id: 'repeat', name: 'Повторные покупки', base: 41.2, plan: 44.0, unit: '%', delta: '−2,8 п.п.', status: 'warning', source: 'CRM', formula: 'Клиенты с 2+ заказами / Активные' },
      { id: 'active_cust', name: 'Активные клиенты', base: 6840, plan: 6500, unit: 'кл.', delta: '+5,2%', status: 'good', source: 'CRM', formula: 'Клиенты с заказом за N дней' }
    ],
    dailyPlans: [
      { id: 'dp_leads', title: 'Квалифицированные лиды', time: 'на 14:30', current: 186, plan: 210, unit: 'лид.', periodCurrent: '5 420', periodPlan: '5 200 лидов', color: '#568CDE' },
      { id: 'dp_orders', title: 'Оплаченные заказы B2C/B2B', time: 'на 14:30', current: 94, plan: 105, unit: 'зак.', periodCurrent: '2 890', periodPlan: '2 750 зак.', color: '#DD5757' },
      { id: 'dp_ret', title: 'Удержание B2B сегмента', time: 'на 14:30', current: 91, plan: 95, unit: '%', periodCurrent: '91,0%', periodPlan: 'План 95,0%', color: '#8C71D8' }
    ],
    chartTitle: 'Конверсия и поток заказов по неделям',
    series: [52, 58, 61, 66, 72, 78, 84],
    forecastSeries: [84, 88, 92]
  },
  production: {
    title: 'Производство и качество (ERP)',
    subtitle: 'План/факт выпуска, очередь цехов, Lead Time, простои машин и разбор брака',
    kpis: [
      { id: 'cap', name: 'Загрузка мощности', base: 86.5, plan: 85.0, unit: '%', delta: '+1,5 п.п.', status: 'good', source: 'ERP', formula: 'Плановые часы / Доступная мощность' },
      { id: 'lead', name: 'Lead Time заказа', base: 2.8, plan: 2.5, unit: 'дн.', delta: '+0,3 дн.', status: 'warning', source: 'ERP', formula: 'AVG(released_at − created_at)' },
      { id: 'downtime', name: 'Неплановые простои', base: 19.5, plan: 12.0, unit: 'ч', delta: '+7,5 ч', status: 'critical', source: 'ERP / Цех', formula: 'SUM(unplanned_downtime_hours)' },
      { id: 'defect_prod', name: 'Брак на линии Premium', base: 8.1, plan: 5.0, unit: '%', delta: '+3,1 п.п.', status: 'critical', source: 'ERP', formula: 'Брак тиражей / Выпущенные тиражи' },
      { id: 'queue', name: 'Очередь модерации', base: 148, plan: 120, unit: 'зак.', delta: '+28 зак.', status: 'warning', source: 'ERP', formula: 'Заказы в статусе moderation_pending' }
    ],
    dailyPlans: [
      { id: 'dp_indigo', title: 'Выработка HP Indigo', time: 'на 14:30', current: 4120, plan: 4500, unit: 'отт.', periodCurrent: '118 тыс.', periodPlan: '125 тыс. отт.', color: '#DD5757' },
      { id: 'dp_bind', title: 'Переплётный участок Kolbus', time: 'на 14:30', current: 265, plan: 320, unit: 'кн.', periodCurrent: '7 640 кн.', periodPlan: '8 200 кн.', color: '#E8996C' },
      { id: 'dp_qc', title: 'Контроль ОТК без перепечатки', time: 'на 14:30', current: 91.9, plan: 95.0, unit: '%', periodCurrent: '91,9%', periodPlan: 'Норма 95,0%', color: '#568CDE' }
    ],
    chartTitle: 'Выпуск продукции и нагрузка цехов по сменам',
    series: [78, 82, 79, 88, 91, 85, 87],
    forecastSeries: [87, 94, 98]
  },
  finance: {
    title: 'Финансы и люди (1С + HR)',
    subtitle: 'Статусы оплат и затрат, БДР/БДДС индекс, штатная обеспеченность и смены',
    kpis: [
      { id: 'bdr', name: 'Исполнение БДР (индекс)', base: 104.2, plan: 100.0, unit: '%', delta: '+4,2 п.п.', status: 'good', source: '1С:УНФ', formula: 'Факт маржинального дохода / План * 100' },
      { id: 'cost_idx', name: 'Индекс себестоимости', base: 102.8, plan: 100.0, unit: '%', delta: '+2,8 п.п.', status: 'warning', source: '1С', formula: 'Факт затрат на бумагу и химию / План' },
      { id: 'staff', name: 'Обеспеченность штатом', base: 93.5, plan: 96.0, unit: '%', delta: '−2,5 п.п.', status: 'warning', source: 'HR витрина', formula: 'Факт на смене / Плановый штат' },
      { id: 'vac', name: 'Открытые вакансии', base: 6, plan: 2, unit: 'шт.', delta: '+4 вак.', status: 'critical', source: 'HR', formula: 'Активные заявки на подбор в цех и саппорт' },
      { id: 'paid_ratio', name: 'Доля подтверждённых оплат', base: 98.2, plan: 98.0, unit: '%', delta: '+0,2 п.п.', status: 'good', source: '1С', formula: 'Оплаченные счета / Выставленные счета' }
    ],
    dailyPlans: [
      { id: 'dp_pay', title: 'Проведено оплат 1С', time: 'на 14:30', current: 96, plan: 100, unit: '%', periodCurrent: '98,2%', periodPlan: 'План 98,0%', color: '#689452' },
      { id: 'dp_shift', title: 'Выход сменного персонала', time: 'на 14:30', current: 58, plan: 62, unit: 'чел.', periodCurrent: '93,5%', periodPlan: 'План 96,0%', color: '#8C71D8' },
      { id: 'dp_ot', title: 'Лимит сверхурочных часов', time: 'на 14:30', current: 24, plan: 30, unit: 'ч', periodCurrent: '142 ч', periodPlan: 'Лимит 180 ч', color: '#568CDE' }
    ],
    chartTitle: 'Индекс исполнения БДР и обеспеченности сменами (%)',
    series: [92, 95, 94, 98, 101, 103, 104],
    forecastSeries: [104, 103, 105]
  },
  decisions: {
    title: 'Реестр управленческих решений',
    subtitle: 'Назначение владельца, контроль сроков, журнал аудита и повторная проверка KPI до/после',
    kpis: [
      { id: 'dec_open', name: 'Активные решения', base: 7, plan: 5, unit: 'шт.', delta: 'в работе', status: 'good', source: 'Реестр', formula: 'COUNT(status IN assigned, in_progress)' },
      { id: 'dec_over', name: 'Просроченные решения', base: 1, plan: 0, unit: 'шт.', delta: 'требует контроля', status: 'critical', source: 'Реестр', formula: 'due_date < NOW() AND status != verified' },
      { id: 'dec_ver', name: 'Проверено с эффектом', base: 14, plan: 12, unit: 'шт.', delta: '+16%', status: 'good', source: 'Реестр', formula: 'Решения со статусом verified и улучшением KPI' },
      { id: 'dq_score', name: 'Индекс качества данных', base: 97.4, plan: 98.0, unit: '%', delta: '−0,6 п.п.', status: 'warning', source: 'Контур 5 систем', formula: '100% − доля пропусков и дублей' }
    ],
    dailyPlans: [
      { id: 'dp_dec1', title: 'Назначено владельцев по алертам', time: 'на 14:30', current: 5, plan: 6, unit: 'реш.', periodCurrent: '83,3%', periodPlan: 'Цель 100%', color: '#DD5757' },
      { id: 'dp_dec2', title: 'Повторная проверка эффекта KPI', time: 'на 14:30', current: 4, plan: 4, unit: 'реш.', periodCurrent: '100%', periodPlan: 'План 4 реш.', color: '#689452' },
      { id: 'dp_dec3', title: 'Сверка 1С и ERP без расхождений', time: 'на 14:30', current: 97.4, plan: 99.0, unit: '%', periodCurrent: '97,4%', periodPlan: 'Норма 99,0%', color: '#568CDE' }
    ],
    chartTitle: 'Темп закрытия и верификации решений по неделям',
    series: [60, 68, 72, 75, 82, 88, 91],
    forecastSeries: [91, 95, 97]
  }
};

export const INITIAL_DECISIONS = [
  {
    id: 'DEC-104',
    problem: 'Брак на линии фотокниг Premium вырос до 8,1% (норма <= 5%)',
    impact: 'Потери маржи ~4 200 BYN/нед., риск срыва SLA доставки',
    cause: 'Партия мелованной бумаги 170 г/м² с повышенной влажностью + увод прижима на Kolbus',
    action: 'Заменить партию бумаги со склада B2, провести калибровку Kolbus и тестовый тираж 30 кн.',
    owner: 'Иван Кузнецов · Производство',
    dueDate: '2026-11-14',
    priority: 'Высокий',
    status: 'В работе',
    beforeKpi: '8,1% брака',
    afterKpi: 'Ожидается <= 4,8% (проверка 15.11)'
  },
  {
    id: 'DEC-103',
    problem: 'Ожидаемый дефицит мощности −640 ч на 47-й неделе (преддверие пика сезона)',
    impact: 'Риск увеличения Lead Time с 2,8 до 4,5 дней по фотокнигам и календарям',
    cause: 'Сезонный рост спроса + 2 открытые вакансии операторов послепечатного участка',
    action: 'Вывести дополнительную смену на выходные и перераспределить тиражи Lite на линию B',
    owner: 'Алексей Смирнов · Руководство',
    dueDate: '2026-11-18',
    priority: 'Высокий',
    status: 'Назначено',
    beforeKpi: 'Дефицит −640 ч',
    afterKpi: 'Прогноз после меры: +80 ч резерва'
  },
  {
    id: 'DEC-101',
    problem: 'Просадка повторных покупок в B2C сегменте на 2,8 п.п.',
    impact: 'Недобор ~18 500 BYN оборота за 30 дней',
    cause: 'Задержка триггерной рассылки после вручения заказа в Битрикс24',
    action: 'Исправить вебхук статуса delivered → запуск промокода на следующую фотокнигу через 48 ч',
    owner: 'Елена Морозова · Продажи',
    dueDate: '2026-11-10',
    priority: 'Средний',
    status: 'Проверено',
    beforeKpi: '38,9% повторных',
    afterKpi: '41,2% (+2,3 п.п. подтверждено)'
  }
];

export const SYSTEMS_CONTOUR = [
  { id: 'crm', name: 'CRM / Битрикс24', dept: 'sales', detail: 'Клиенты · сделки · задачи · коммуникации', freshness: '2 мин назад', status: 'ok', metric: 'Конверсия 23,4% · 6 840 активных кл.' },
  { id: 'support', name: 'Поддержка / Омниканал', dept: 'sales', detail: 'Обращения · темы · SLA · качество ответа', freshness: '4 мин назад', status: 'ok', metric: 'SLA 95,4% · ср. ответ 6 мин' },
  { id: 'erp', name: 'ERP / Производство', dept: 'production', detail: 'Заказ · модерация · очередь · выпуск · доставка', freshness: '1 мин назад', status: 'warning', metric: 'Загрузка 86,5% · брак Premium 8,1%' },
  { id: 'onec', name: '1С / Финансы', dept: 'accounting', detail: 'Статусы оплат и затрат · индекс БДР', freshness: '5 мин назад', status: 'ok', metric: 'Исполнение БДР 104,2% · 2 расхождения в сверке' },
  { id: 'hr', name: 'HR / Персонал', dept: 'accounting', detail: 'Штат · смены · вакансии · нагрузка', freshness: '18 мин назад', status: 'warning', metric: 'Обеспеченность 93,5% · 6 вакансий' }
];

export const CAPACITY_FORECAST = [
  { week: '45 неделя (11–17 ноя)', demand: '4 120 ч', capacity: '4 480 ч', balance: '+360 ч запаса', deficit: false, action: 'В штатном режиме' },
  { week: '46 неделя (18–24 ноя)', demand: '4 590 ч', capacity: '4 680 ч', balance: '+90 ч запаса', deficit: false, action: 'Подготовить бумагу под пик' },
  { week: '47 неделя (25 ноя – 1 дек)', demand: '5 320 ч', capacity: '4 680 ч', balance: '−640 ч дефицит', deficit: true, action: 'Нужна доп. смена + сверхурочные' },
  { week: '48 неделя (2–8 дек)', demand: '4 850 ч', capacity: '5 100 ч', balance: '+250 ч запаса', deficit: false, action: 'После ввода резервной смены' }
];
