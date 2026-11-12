#!/usr/bin/env bash

# =============================================================================
# Fotoprint AI Copilot — Unified System Stopper
# =============================================================================

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_DIR"

export PATH="$HOME/.orbstack/bin:/opt/homebrew/bin:/usr/local/bin:$PATH"

echo "🛑 Остановка AI Gateway..."
if [ -f "$PROJECT_DIR/.gateway.pid" ]; then
    PID=$(cat "$PROJECT_DIR/.gateway.pid")
    kill -9 "$PID" 2>/dev/null || true
    rm -f "$PROJECT_DIR/.gateway.pid"
fi

# Также проверяем любой процесс на порту 8899
GATEWAY_PIDS=$(lsof -tiTCP:8899 -sTCP:LISTEN 2>/dev/null || true)
if [ -n "$GATEWAY_PIDS" ]; then
    kill -9 $GATEWAY_PIDS 2>/dev/null || true
fi
echo "✅ AI Gateway остановлен."

echo "🛑 Остановка Docker контейнеров..."
docker-compose down 2>/dev/null || true
echo "✅ Все контейнеры остановлены."
echo "🎉 Система Fotoprint AI Copilot успешно остановлена."
