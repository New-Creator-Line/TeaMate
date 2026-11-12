# 04. Фаза 3: Векторизация и Meeting RAG (Vector Search)

## 1. Цель фазы
Создать высокоточный семантический индекс по стенограммам совещаний, позволяющий отвечать на вопросы:
* *«Что Максим говорил про сроки отгрузки тиража?»*
* *«Кто обещал проверить постаматы 5Post?»*
* *«На каком созвоне обсуждали задержку запчастей для Heidelberg?»*

---

## 2. Стратегия чанкования диалогов (Turn-Based Chunking)

Обычное чанкование по количеству символов (например, по 1000 символов) разрушает структуру диалога. В нашем пайплайне применяется **Turn-based Chunking**:

1. **Атомарная единица**: цельная реплика говорящего (`SpeakerTurn`).
2. **Размер скользящего окна**: группа последовательных реплик суммарным объемом **150–250 слов**.
3. **Перекрытие (Overlap)**: ровно **1 целая реплика** (`overlap_turns = 1`). Благодаря этому связка «вопрос $\to$ ответ» гарантированно присутствует в обоих соседних чанках.
4. **Контекстный заголовок (Context Header)**: к каждому чанку перед векторизацией принудительно добавляется мета-заголовок:

```text
[Встреча: Производственная планерка] [Дата: 2026-11-11] [Спикеры: Максим, Иван Иванов]
Иван Иванов: Максим, когда закончим приладку на Heidelberg?
Максим: К 18:00 тираж отдадим на резку. Сейчас проверяем краску и УФ-лак.
```

Благодаря Context Header вектор становится семантически самодостаточным даже при изолированном поиске.

---

## 3. Двухуровневый поиск (Parent-Child Retrieval)

```
[Поисковый запрос пользователя: "Что Максим говорил про Heidelberg?"]
                      │
                      ▼
[1. Векторный поиск по компактным чанкам (Child Chunks)]
                      │
                      ▼
[Найдена реплика]: Максим: "К 18:00 тираж отдадим на резку..."
                      │
                      ▼
[2. Parent Context Expansion (Расширение контекста)]
Автоматически подтягиваем 1 реплику ДО и 1 реплику ПОСЛЕ из таблицы meeting_speaker_turns:
- Иван Иванов: "Максим, когда закончим приладку на Heidelberg?"
- Максим: "К 18:00 тираж отдадим на резку..."
- Иван Иванов: "Хорошо, тогда логистику СДЭК предупреждаю на 18:30."
                      │
                      ▼
[3. Передача в Gemini]: Модель видит весь диалог и дает исчерпывающий ответ!
```

---

## 4. Гибридный поиск: pgvector + BM25 (RRF)

В производственной среде полиграфии критически важен поиск точных терминов (марки машин, артикулы, фамилии), которые чистый векторный поиск иногда размывает.

Применяется гибридный поиск с формулой **Reciprocal Rank Fusion (RRF)**:
$$RRF(d) = \frac{1}{60 + rank_{vector}(d)} + \frac{1}{60 + rank_{text}(d)}$$

### SQL-запрос гибридного поиска:
```sql
WITH vector_matches AS (
    SELECT id, meeting_id, content_with_header, start_time_seconds, end_time_seconds,
           ROW_NUMBER() OVER (ORDER BY embedding <=> @query_vector) AS rank_v
    FROM meeting_rag_chunks
    ORDER BY embedding <=> @query_vector
    LIMIT 20
),
text_matches AS (
    SELECT id, meeting_id, content_with_header, start_time_seconds, end_time_seconds,
           ROW_NUMBER() OVER (ORDER BY ts_rank_cd(to_tsvector('russian', content_with_header), plainto_tsquery('russian', @query_text)) DESC) AS rank_t
    FROM meeting_rag_chunks
    WHERE to_tsvector('russian', content_with_header) @@ plainto_tsquery('russian', @query_text)
    LIMIT 20
)
SELECT COALESCE(v.id, t.id) AS id,
       COALESCE(v.meeting_id, t.meeting_id) AS meeting_id,
       COALESCE(v.content_with_header, t.content_with_header) AS content,
       COALESCE(v.start_time_seconds, t.start_time_seconds) AS start_time,
       (COALESCE(1.0 / (60 + v.rank_v), 0.0) + COALESCE(1.0 / (60 + t.rank_t), 0.0)) AS score
FROM vector_matches v
FULL OUTER JOIN text_matches t ON v.id = t.id
ORDER BY score DESC
LIMIT 5;
```
