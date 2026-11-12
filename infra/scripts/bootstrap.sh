#!/bin/sh
set -e

echo "========================================================="
echo "📦 Запуск загрузчика библиотек Fotoprint (libraries.yaml)"
echo "========================================================="

CONFIG_FILE="/app/libraries.yaml"

if [ ! -f "$CONFIG_FILE" ]; then
  echo "⚠️ Файл $CONFIG_FILE не найден, пропуск автоматической загрузки."
  exit 0
fi

echo "✅ Файл конфигурации $CONFIG_FILE обнаружен."

# 1. Загрузка и кэширование NuGet пакетов
echo "🔄 Восстановление и кэширование NuGet пакетов .NET 9..."
if [ -f "/app/Fotoprint.slnx" ]; then
  dotnet restore /app/Fotoprint.slnx --packages /root/.nuget/packages
  echo "✅ NuGet пакеты успешно загружены и закэшированы."
fi

# 2. Установка npm пакетов для n8n в общий volume
if [ -d "/home/node/.n8n" ]; then
  if ! command -v npm >/dev/null 2>&1; then
    apk add --no-cache nodejs npm || true
  fi
  echo "🔄 Установка внешних npm библиотек в volume n8n..."
  cd /home/node/.n8n
  npm install --no-audit --no-fund axios lodash form-data pdf-parse docx || true
  echo "✅ npm модули для n8n установлены."
fi

echo "========================================================="
echo "🚀 Все необходимые библиотеки готовы к работе!"
echo "========================================================="

