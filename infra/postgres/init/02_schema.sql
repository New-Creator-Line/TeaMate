-- 02_schema.sql
-- Инициализация схемы БД Fotoprint AI Copilot

\c fotoprint;

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS vector;

-- ============================================================================
-- 1. БЕЗОПАСНОСТЬ И RBAC (Employees, Roles, Permissions)
-- ============================================================================

CREATE TABLE IF NOT EXISTS roles (
    id SERIAL PRIMARY KEY,
    name VARCHAR(50) UNIQUE NOT NULL,
    description TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS permissions (
    id SERIAL PRIMARY KEY,
    code VARCHAR(100) UNIQUE NOT NULL,
    description TEXT,
    category VARCHAR(50) DEFAULT 'general',
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS role_permissions (
    role_id INT NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    permission_id INT NOT NULL REFERENCES permissions(id) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_id)
);

CREATE TABLE IF NOT EXISTS employees (
    id SERIAL PRIMARY KEY,
    master_id BIGINT UNIQUE NOT NULL, -- Уникальный цифровой MasterId сотрудника (только цифры)
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    middle_name VARCHAR(100),
    email VARCHAR(150) UNIQUE,
    telegram_id BIGINT UNIQUE,
    telegram_username VARCHAR(100),
    whatsapp_id VARCHAR(50) UNIQUE,
    department VARCHAR(100) NOT NULL, -- 'Производство', 'Продажи', 'Бухгалтерия', 'Руководство'
    position VARCHAR(100) NOT NULL,
    access_groups JSONB NOT NULL DEFAULT '["all"]'::jsonb, -- ['all', 'production', 'sales', 'accounting', 'c-level']
    notifications_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    is_active BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS employee_roles (
    employee_id INT NOT NULL REFERENCES employees(id) ON DELETE CASCADE,
    role_id INT NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    assigned_at TIMESTAMPTZ DEFAULT NOW(),
    PRIMARY KEY (employee_id, role_id)
);

CREATE INDEX IF NOT EXISTS idx_employees_master_id ON employees(master_id);
CREATE INDEX IF NOT EXISTS idx_employees_telegram_id ON employees(telegram_id);
CREATE INDEX IF NOT EXISTS idx_employees_access_groups ON employees USING GIN(access_groups);

-- ============================================================================
-- 2. ЕДИНАЯ БАЗА ДИАЛОГОВ ВСЕХ ПОЛЬЗОВАТЕЛЕЙ (Sessions, Messages, MasterId)
-- ============================================================================

CREATE TABLE IF NOT EXISTS chat_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(), -- Уникальный UUID SessionId текущего диалога
    master_id BIGINT NOT NULL REFERENCES employees(master_id) ON DELETE CASCADE, -- Цифровой MasterId пользователя
    employee_id INT REFERENCES employees(id) ON DELETE SET NULL,
    messenger_type VARCHAR(50) NOT NULL DEFAULT 'telegram', -- 'telegram', 'whatsapp', 'web', 'max'
    messenger_chat_id BIGINT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE, -- Активна ли сессия (при смене контекста переходит в FALSE)
    title VARCHAR(255) DEFAULT 'Новый диалог',
    current_branch VARCHAR(50) DEFAULT 'general', -- 'meeting_prep', 'live_meeting_notes', 'meeting_summary', 'doc_search', 'task_management', 'executive_report'
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    closed_at TIMESTAMPTZ NULL, -- Время завершения сессии при смене диалога
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE, -- Привязка к SessionId
    master_id BIGINT NOT NULL,                                              -- Цифровой MasterId автора
    context_id VARCHAR(100) NOT NULL DEFAULT 'default',                   -- Группировка в рамках контекста
    role VARCHAR(20) NOT NULL,                                             -- 'user', 'assistant', 'system'
    content TEXT NOT NULL,
    is_frozen BOOLEAN DEFAULT FALSE,                                       -- Признак изоляции при смене темы
    metadata JSONB DEFAULT '{}'::jsonb,                                    -- trace_id, tokens, branch
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS context_batches (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
    summary TEXT,
    message_count INT NOT NULL DEFAULT 0,
    is_context_switched BOOLEAN DEFAULT FALSE,
    branch VARCHAR(50),
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_chat_sessions_master_active ON chat_sessions(master_id, is_active);
CREATE INDEX IF NOT EXISTS idx_messages_session_id ON messages(session_id);
CREATE INDEX IF NOT EXISTS idx_messages_master_id ON messages(master_id);
CREATE INDEX IF NOT EXISTS idx_messages_session_created ON messages(session_id, created_at);

-- ============================================================================
-- 3. ВЕКТОРНАЯ БАЗА (RAG: Corporate Documents & Document Chunks)
-- ============================================================================

CREATE TABLE IF NOT EXISTS corporate_documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(255) NOT NULL,
    source_type VARCHAR(50) NOT NULL, -- 'pdf', 'docx', 'xlsx', 'txt', 'manual'
    file_path TEXT,
    department VARCHAR(100),
    allowed_access_groups JSONB NOT NULL DEFAULT '["all"]'::jsonb,
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS corporate_document_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES corporate_documents(id) ON DELETE CASCADE,
    chunk_index INT NOT NULL,
    chunk_text TEXT NOT NULL,
    embedding vector(1536), -- стандарт OpenAI text-embedding-3-small / text-embedding-ada-002
    allowed_access_groups JSONB NOT NULL DEFAULT '["all"]'::jsonb,
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_chunks_allowed_access_groups ON corporate_document_chunks USING GIN(allowed_access_groups);
CREATE INDEX IF NOT EXISTS idx_documents_allowed_access_groups ON corporate_documents USING GIN(allowed_access_groups);
-- Векторный индекс для косинусного сходства (HNSW)
CREATE INDEX IF NOT EXISTS idx_chunks_embedding ON corporate_document_chunks 
USING hnsw (embedding vector_cosine_ops);

-- ============================================================================
-- 4. РЕЕСТР ВСТРЕЧ И ПРОТОКОЛЫ (Meetings, Notes, Protocols, Action Items)
-- ============================================================================

CREATE TABLE IF NOT EXISTS meetings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(255) NOT NULL,
    description TEXT,
    start_time TIMESTAMPTZ NOT NULL,
    end_time TIMESTAMPTZ,
    status VARCHAR(50) DEFAULT 'scheduled', -- 'scheduled', 'in_progress', 'completed', 'cancelled'
    organizer_id INT REFERENCES employees(id) ON DELETE SET NULL,
    participant_ids JSONB DEFAULT '[]'::jsonb, -- список employee_id: [1, 2, 3]
    calendar_event_id VARCHAR(100),
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS meeting_notes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    meeting_id UUID NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
    author_id INT REFERENCES employees(id) ON DELETE SET NULL,
    note_text TEXT NOT NULL,
    category VARCHAR(50) DEFAULT 'general', -- 'discussion', 'decision', 'question', 'action_point'
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS meeting_protocols (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    meeting_id UUID NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
    title VARCHAR(255) NOT NULL,
    summary TEXT NOT NULL,
    decisions JSONB DEFAULT '[]'::jsonb,
    proposed JSONB DEFAULT '[]'::jsonb,
    discussed JSONB DEFAULT '[]'::jsonb,
    risks JSONB DEFAULT '[]'::jsonb,
    requires_clarification JSONB DEFAULT '[]'::jsonb,
    status VARCHAR(30) NOT NULL DEFAULT 'draft', -- 'draft', 'final'
    raw_transcript TEXT,
    file_docx_path TEXT,
    file_pdf_path TEXT,
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

-- Изолированное векторное хранилище чанков созвонов (отдельно от корпоративных регламентов)
CREATE TABLE IF NOT EXISTS meeting_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    meeting_id UUID NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
    protocol_id UUID REFERENCES meeting_protocols(id) ON DELETE SET NULL,
    chunk_index INT NOT NULL,
    chunk_text TEXT NOT NULL,
    embedding vector(1536),
    allowed_access_groups JSONB NOT NULL DEFAULT '[]'::jsonb,
    metadata JSONB DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_meeting_chunks_meeting_id ON meeting_chunks(meeting_id);
CREATE INDEX IF NOT EXISTS idx_meeting_chunks_allowed_groups ON meeting_chunks USING GIN(allowed_access_groups);
CREATE INDEX IF NOT EXISTS idx_meeting_chunks_embedding ON meeting_chunks
USING hnsw (embedding vector_cosine_ops);

CREATE TABLE IF NOT EXISTS action_items (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    meeting_id UUID NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
    protocol_id UUID REFERENCES meeting_protocols(id) ON DELETE SET NULL,
    title VARCHAR(255) NOT NULL,
    description TEXT,
    responsible_id INT REFERENCES employees(id) ON DELETE SET NULL,
    deadline TIMESTAMPTZ,
    source_quote TEXT,
    status VARCHAR(50) DEFAULT 'pending', -- 'pending', 'confirmed', 'exported_to_bitrix', 'cancelled'
    bitrix24_task_id INT,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_meetings_start_time ON meetings(start_time);
CREATE INDEX IF NOT EXISTS idx_meetings_status ON meetings(status);
CREATE INDEX IF NOT EXISTS idx_meeting_notes_meeting_id ON meeting_notes(meeting_id);
CREATE INDEX IF NOT EXISTS idx_action_items_meeting_id ON action_items(meeting_id);
CREATE INDEX IF NOT EXISTS idx_action_items_responsible ON action_items(responsible_id);

-- ============================================================================
-- 5. ДОСТАВКА И ДЕДУПЛИКАЦИЯ ПРОАКТИВНЫХ УВЕДОМЛЕНИЙ И НАСТРОЙКИ КАНАЛОВ
-- ============================================================================

CREATE TABLE IF NOT EXISTS notification_deliveries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    dedup_key VARCHAR(255) NOT NULL UNIQUE,
    notification_type VARCHAR(100) NOT NULL, -- 'pre_meeting', 'morning_digest', 'task_deadline', 'manual'
    recipient_employee_id INT REFERENCES employees(id) ON DELETE SET NULL,
    recipient_telegram_id BIGINT NOT NULL DEFAULT 0,
    channel_type VARCHAR(50) NOT NULL DEFAULT 'telegram', -- 'telegram', 'crm_chat', 'web', 'webhook'
    recipient_address VARCHAR(255),
    entity_id UUID,
    status VARCHAR(30) NOT NULL DEFAULT 'pending', -- 'pending', 'sent', 'failed', 'skipped'
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sent_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_notification_deliveries_type_created
    ON notification_deliveries(notification_type, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_notification_deliveries_telegram_id
    ON notification_deliveries(recipient_telegram_id);

CREATE TABLE IF NOT EXISTS employee_notification_preferences (
    employee_id INT PRIMARY KEY REFERENCES employees(id) ON DELETE CASCADE,
    master_id BIGINT UNIQUE NOT NULL,
    digest_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    digest_time_msk VARCHAR(5) NOT NULL DEFAULT '09:00',
    digest_weekdays_only BOOLEAN NOT NULL DEFAULT TRUE,
    pre_meeting_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    pre_meeting_window_minutes INT NOT NULL DEFAULT 30,
    task_deadlines_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    task_due_soon_hours INT NOT NULL DEFAULT 24,
    task_urgent_hours INT NOT NULL DEFAULT 2,
    quiet_hours_start_msk VARCHAR(5),
    quiet_hours_end_msk VARCHAR(5),
    preferred_channel VARCHAR(50) NOT NULL DEFAULT 'telegram',
    channel_address VARCHAR(255),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS user_identities (
    id SERIAL PRIMARY KEY,
    employee_id INT NOT NULL REFERENCES employees(id) ON DELETE CASCADE,
    master_id BIGINT NOT NULL,
    provider VARCHAR(50) NOT NULL, -- 'telegram', 'whatsapp', 'crm', 'web', 'sso'
    external_user_id VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (provider, external_user_id)
);

CREATE INDEX IF NOT EXISTS idx_user_identities_master_id
    ON user_identities(master_id);

-- ============================================================================
-- 6. ЕДИНЫЙ ЖУРНАЛ АУДИТА ОПЕРАЦИЙ И БЕЗОПАСНОСТИ (ОТ-04, AI-06, 7.5)
-- ============================================================================

CREATE TABLE IF NOT EXISTS audit_events (
    id BIGSERIAL PRIMARY KEY,
    ts TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    actor_id BIGINT NOT NULL DEFAULT 0,
    channel TEXT NOT NULL DEFAULT 'unknown',
    action TEXT NOT NULL,
    object_type TEXT,
    object_id TEXT,
    result TEXT NOT NULL,
    trace_id TEXT,
    details JSONB
);

CREATE INDEX IF NOT EXISTS idx_audit_events_ts ON audit_events (ts DESC);
CREATE INDEX IF NOT EXISTS idx_audit_events_actor ON audit_events (actor_id, ts DESC);


