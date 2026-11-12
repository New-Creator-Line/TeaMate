#!/usr/bin/env bash
set -e

# =============================================================================
# Fotoprint AI Copilot — Unified System Launcher
# =============================================================================

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_DIR"

export PATH="$HOME/.orbstack/bin:/opt/homebrew/bin:/usr/local/bin:$HOME/.dotnet:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_ROLL_FORWARD=Major

echo "🚀 [1/5] Проверка окружения и Docker / OrbStack..."
if ! command -v docker &> /dev/null; then
    echo "❌ Ошибка: Docker не найден в PATH."
    exit 1
fi

if ! docker info &> /dev/null; then
    echo "⚡ Запуск OrbStack..."
    open -a OrbStack || true
    echo "⏳ Ожидание готовности Docker daemon..."
    for i in {1..30}; do
        if docker info &> /dev/null; then
            break
        fi
        sleep 1
    done
fi

echo "🐘 [2/5] Запуск инфраструктурного стека (PostgreSQL 16, ClickHouse, Redis, MinIO, n8n, Langfuse)..."
docker-compose up -d postgres clickhouse langfuse-redis langfuse-minio n8n langfuse-worker langfuse-web

echo "⏳ [3/5] Ожидание готовности PostgreSQL на порту 5434..."
for i in {1..20}; do
    if docker exec fotoprint-postgres pg_isready -U fotoprint -d fotoprint &> /dev/null; then
        echo "✅ PostgreSQL готов к работе!"
        break
    fi
    sleep 1
done

echo "🔌 [4/5] Запуск микросервисов MCP (Employee Reader, 1C, Bitrix24, Calendar)..."
docker-compose up -d mcp-employee-reader mcp-1c mcp-bitrix24 mcp-calendar

echo "🧠 [5/5] Проверка AI Gateway (.NET 9 + Apple Silicon Metal GPU)..."
if lsof -iTCP:8899 -sTCP:LISTEN -n -P > /dev/null 2>&1; then
    echo "✅ AI Gateway уже работает на http://localhost:8899"
else
    echo "⚡ Запуск AI Gateway в фоновом режиме..."
    nohup dotnet run --project src/Fotoprint.AiGateway.Api > "$PROJECT_DIR/.gateway.log" 2>&1 &
    GATEWAY_PID=$!
    echo "$GATEWAY_PID" > "$PROJECT_DIR/.gateway.pid"
    echo "⏳ Ожидание старта Gateway..."
    for i in {1..25}; do
        if curl -s http://localhost:8899/health &> /dev/null; then
            echo "✅ AI Gateway запущен (PID: $GATEWAY_PID)!"
            break
        fi
        sleep 1
    done
fi

echo ""
echo "============================================================================="
echo "🎉 СИСТЕМА FOTOPRINT AI COPILOT УСПЕШНО ЗАПУЩЕНА!"
echo "============================================================================="
echo "🎙️ Веб-интерфейс (транскрибация, чат, аудио-визуализатор): http://localhost:8899"
echo "⚙️ n8n сценарии и автоматизация:                         http://localhost:5678"
echo "📊 Langfuse трейсинг и аналитика LLM:                    http://localhost:3000"
echo "🐘 База данных PostgreSQL 16 + pgvector:                  localhost:5434 (user: fotoprint)"
echo "⚡ ClickHouse аналитическое хранилище:                    localhost:8123 / 9000"
echo "============================================================================="
echo ""

# Автоматически открыть веб-интерфейс в браузере по умолчанию
open "http://localhost:8899" 2>/dev/null || true
