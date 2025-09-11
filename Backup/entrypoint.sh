#!/bin/bash

echo "Starting Minecraft Backup Service..."

# Проверяем наличие необходимых директорий
if [ ! -d "$MINECRAFT_DATA_PATH" ]; then
    echo "Warning: Minecraft data directory not found: $MINECRAFT_DATA_PATH"
fi

if [ ! -d "$BACKUP_PATH" ]; then
    echo "Creating backup directory: $BACKUP_PATH"
    mkdir -p "$BACKUP_PATH"
fi

# Выводим конфигурацию
echo "Configuration:"
echo "  MINECRAFT_DATA_PATH: $MINECRAFT_DATA_PATH"
echo "  BACKUP_PATH: $BACKUP_PATH"
echo "  BACKUP_INTERVAL_MINUTES: $BACKUP_INTERVAL_MINUTES"
echo "  BACKUP_RETENTION_DAYS: $BACKUP_RETENTION_DAYS"
echo "  RCON_HOST: $RCON_HOST"
echo "  RCON_PORT: $RCON_PORT"

# Запускаем Python скрипт
exec python3 /app/backup_script.py
