# Minecraft Server Backup System

Система автоматического резервного копирования для Minecraft сервера с REST API управлением.

## Что было улучшено в docker-compose.yml

### 1. Исправления портов
- **Проблема**: Manager использовал порт 8080 внутри контейнера, но в compose был указан 5000
- **Решение**: Изменено на `"5000:8080"` - внешний порт 5000 перенаправляется на внутренний 8080
- **Почему лучше**: Корректное проксирование портов, приложение работает на своем стандартном порту

### 2. Добавлена сеть
- **Проблема**: Сервисы использовали default network
- **Решение**: Создана отдельная сеть `minecraft-network`
- **Почему лучше**: Изоляция сервисов, лучший контроль над сетевым взаимодействием

### 3. Именованные volumes
- **Проблема**: Использовались bind mounts (`./data:/data`)
- **Решение**: Созданы именованные volumes `minecraft-data` и `backup-data`
- **Почему лучше**: Лучшая производительность, автоматическое управление Docker'ом, безопасность

### 4. Health checks
- **Проблема**: Отсутствие проверок здоровья сервисов
- **Решение**: Добавлены healthcheck для всех сервисов
- **Почему лучше**: Корректная последовательность запуска, автоматический перезапуск при сбоях

### 5. Улучшенные depends_on
- **Проблема**: Простая зависимость без проверки готовности
- **Решение**: `condition: service_healthy`
- **Почему лучше**: Сервисы запускаются только после готовности зависимостей

### 6. Безопасность
- **Проблема**: Слабый пароль RCON по умолчанию
- **Решение**: Изменен пароль на более сложный
- **Почему лучше**: Повышенная безопасность

### 7. Дополнительные настройки Minecraft
- **Добавлено**: Множество настроек для оптимальной работы сервера
- **Почему лучше**: Более стабильная работа, лучшая производительность

## Backup Service

### Возможности
- ✅ **Автоматическое резервное копирование** каждые N минут (настраивается)
- ✅ **Остановка автосохранения** через RCON перед бэкапом
- ✅ **Автоматическая очистка** старых бэкапов по времени жизни
- ✅ **REST API** для управления бэкапами
- ✅ **Уведомления** Manager API о статусе операций
- ✅ **Загрузка/скачивание** бэкапов через API
- ✅ **Восстановление** из бэкапов с предварительным бэкапом

### Переменные окружения
```bash
BACKUP_INTERVAL_MINUTES=30      # Интервал создания бэкапов
BACKUP_RETENTION_DAYS=7         # Сколько дней хранить бэкапы
RCON_HOST=minecraft-server      # Хост RCON
RCON_PORT=25575                 # Порт RCON
RCON_PASSWORD=changeme123       # Пароль RCON
```

### Что сохраняется в бэкап
- Папка `world/` (весь мир)
- `server.properties`
- `whitelist.json`
- `banned-players.json`
- `banned-ips.json`
- `ops.json`

## REST API Endpoints

### Получить статистику бэкапов
```bash
GET /api/backup/stats
```

**Ответ:**
```json
{
  "totalBackups": 5,
  "totalSizeMB": 245.67,
  "oldestBackup": "2024-01-01T12:00:00",
  "newestBackup": "2024-01-01T18:00:00",
  "backups": [
    {
      "fileName": "minecraft_backup_20240101_180000.tar.gz",
      "createdAt": "2024-01-01T18:00:00",
      "sizeBytes": 51380224,
      "sizeMB": 49.01
    }
  ]
}
```

### Создать бэкап
```bash
POST /api/backup/create
Content-Type: application/json

{
  "description": "manual_backup",
  "disableAutosave": true
}
```

### Восстановить из бэкапа
```bash
POST /api/backup/restore
Content-Type: application/json

{
  "backupFileName": "minecraft_backup_20240101_180000.tar.gz",
  "stopServer": true,
  "createBackupBeforeRestore": true
}
```

### Скачать бэкап
```bash
GET /api/backup/download/{backupFileName}
```

### Загрузить бэкап
```bash
POST /api/backup/upload
Content-Type: multipart/form-data

# Загрузить .tar.gz файл
```

### Удалить бэкап
```bash
DELETE /api/backup/{backupFileName}
```

### Получить события
```bash
GET /api/backup/events
```

### Отправить RCON команду
```bash
POST /api/backup/rcon
Content-Type: application/json

{
  "command": "list"
}
```

## Запуск системы

1. **Сборка и запуск:**
```bash
docker-compose up --build -d
```

2. **Проверка логов backup сервиса:**
```bash
docker-compose logs -f backup
```

3. **Проверка API:**
```bash
curl http://localhost:5000/api/backup/stats
```

## Мониторинг

### Health checks
- **Manager**: `http://localhost:5000/health`
- **Minecraft**: встроенный `mc-health`
- **Backup**: проверка процесса backup_script

### Логи
```bash
# Все сервисы
docker-compose logs -f

# Только backup
docker-compose logs -f backup

# Только manager
docker-compose logs -f manager
```

## Безопасность

1. **Изменен пароль RCON** с `changeme` на `changeme123`
2. **Именованные volumes** вместо bind mounts
3. **Изолированная сеть** для сервисов
4. **Health checks** для контроля состояния
5. **CORS настроен** для безопасного API доступа

## Производительность

1. **Сжатие gzip** для бэкапов (экономия места)
2. **Остановка автосохранения** во время бэкапа (консистентность)
3. **Автоматическая очистка** старых бэкапов (управление дисковым пространством)
4. **Именованные volumes** (лучшая производительность I/O)
5. **Настроенные параметры Minecraft** для оптимальной работы

## Troubleshooting

### Backup сервис не может подключиться к RCON
```bash
# Проверить, что Minecraft сервер запущен
docker-compose ps

# Проверить логи Minecraft
docker-compose logs minecraft

# Проверить RCON настройки
docker-compose exec minecraft rcon-cli list
```

### Не хватает места для бэкапов
```bash
# Проверить размер бэкапов
curl http://localhost:5000/api/backup/stats

# Уменьшить время хранения
# Изменить BACKUP_RETENTION_DAYS в docker-compose.yml
```

### API недоступен
```bash
# Проверить health check
curl http://localhost:5000/health

# Проверить логи manager
docker-compose logs manager
```
