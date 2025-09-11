FROM alpine:3.18

# Устанавливаем необходимые пакеты
RUN apk add --no-cache \
    bash \
    curl \
    tar \
    gzip \
    python3 \
    py3-pip \
    tzdata \
    && ln -sf python3 /usr/bin/python

# Устанавливаем Python библиотеки для RCON
RUN pip3 install --no-cache-dir mcrcon requests schedule

# Создаем рабочую директорию
WORKDIR /app

# Копируем скрипты
COPY backup_script.py /app/
COPY entrypoint.sh /app/
COPY requirements.txt /app/

# Устанавливаем права на выполнение
RUN chmod +x /app/entrypoint.sh

# Создаем директории для бэкапов
RUN mkdir -p /backups /minecraft-data

# Устанавливаем часовой пояс (по умолчанию UTC)
ENV TZ=UTC

ENTRYPOINT ["/app/entrypoint.sh"]
