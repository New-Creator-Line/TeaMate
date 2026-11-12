# Интеграция Календаря и Планирования Встреч (Calendar MCP & Web UI)

## 1. Контекст и Цель
Данный документ регламентирует полный цикл проектирования, разработки, тестирования и внедрения модуля календарного планирования для ИИ-ассистента руководства ООО «Фотопринт» (Трек 3 хакатона).

Модуль решает ключевую бизнес-задачу: **автоматизированный подбор окон встреч, согласование расписания между сотрудниками, просмотр загрузки в веб-интерфейсе и фиксация подтвержденных событий в корпоративной базе данных**.

---

## 2. Ключевые Архитектурные Решения (Ревизия v2)

1. **Бесстейтовый (Stateless) Human-in-the-Loop**:
   - В БД не плодятся временные черновики (`draft`).
   - Параметры встречи хранятся во фронтенд-карточке и отправляются на сервер только при явном клике по кнопке «Подтвердить». До клика `meetings` в БД не создаются.
2. **Детерминированный C# ActionCard (Без галлюцинаций LLM)**:
   - Нейросеть отвечает только за разговорный текст и вызов тула `find_free_slots`.
   - C#-код шлюза перехватывает результат работы тула и гарантированно генерирует строго типизированный блок кнопок и чипов слотов.
3. **Отсечение прошедших часов текущего дня**:
   - Алгоритм `FindFreeSlotsAsync` жестко отсекает слоты, время которых уже наступило по часовому поясу Москвы (UTC+3) с запасом 5 минут.
4. **Явная заглушка отправки оповещений (Notification Stub)**:
   - Создание встречи генерирует метаданные эмуляции отправки в Telegram/Bitrix24, а в интерфейсе отображается четкий бейдж `[🔔 Оповещение: смоделировано (заглушка спринта)]`.
5. **RBAC-защита режима «Вся компания»**:
   - Полное расписание доступно только для ролей `Director` и `Admin`. Для остальных сотрудников и гостей переключатель заблокирован в UI и возвращает HTTP 403 Forbidden на бэкенде.

---

## 3. Стратегия Ветвления (Git Flow)

- **Базовая ветка**: `feature/audio-transcription` (коммит `1fe58d9`).
- **Новая рабочая ветка**: `feature/calendar-integration`.
- **Команда создания ветки**:
  ```bash
  git checkout feature/audio-transcription
  git checkout -b feature/calendar-integration
  ```

---

## 4. Навигация по Документации Модуля

1. [01_ARCHITECTURE_AND_CONTRACTS.md](file:///Users/test/LittleProjects/SpaceHackathonWithIGreatStone/docs/calendar_integration/01_ARCHITECTURE_AND_CONTRACTS.md) — Системная архитектура, сетевая топология, схемы таблиц БД, маршрутизация шлюза и JSON-контракты.
2. [02_PHASE_1_BACKEND_AND_MCP.md](file:///Users/test/LittleProjects/SpaceHackathonWithIGreatStone/docs/calendar_integration/02_PHASE_1_BACKEND_AND_MCP.md) — Алгоритм `FindFreeSlotsAsync` с отсечением прошедших часов, сервис `Fotoprint.Mcp.Calendar`, Notification Stub и RBAC.
3. [03_PHASE_2_AGENT_AND_INTERACTION_FLOW.md](file:///Users/test/LittleProjects/SpaceHackathonWithIGreatStone/docs/calendar_integration/03_PHASE_2_AGENT_AND_INTERACTION_FLOW.md) — C# перехват tool-вызовов, бесстейтовый диалог, выбор длительности и слотов, карточка подтверждения.
4. [04_PHASE_3_WEB_UI_SPECIFICATION.md](file:///Users/test/LittleProjects/SpaceHackathonWithIGreatStone/docs/calendar_integration/04_PHASE_3_WEB_UI_SPECIFICATION.md) — Кнопка в шапке, боковая шторка с цветными точками плотности (🟢/🟡/🔴), блокировка «Вся компания» по RBAC и реактивное обновление без перезагрузки.
5. [05_PHASE_4_VERIFICATION_AND_TEST_PLAN.md](file:///Users/test/LittleProjects/SpaceHackathonWithIGreatStone/docs/calendar_integration/05_PHASE_4_VERIFICATION_AND_TEST_PLAN.md) — Матрица проверок (TC-01...TC-09), проверка прошедшего времени, RBAC 403 и команды cURL.
