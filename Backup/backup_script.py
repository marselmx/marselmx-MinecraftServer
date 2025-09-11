#!/usr/bin/env python3
"""
Minecraft Server Backup Script
Автоматически создает резервные копии мира Minecraft с интеграцией RCON
"""

import os
import sys
import time
import shutil
import tarfile
import schedule
import logging
import requests
from datetime import datetime, timedelta
from pathlib import Path
from mcrcon import MCRcon

# Настройка логирования
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler(sys.stdout),
        logging.FileHandler('/app/backup.log')
    ]
)

logger = logging.getLogger(__name__)

class MinecraftBackup:
    def __init__(self):
        # Получаем настройки из переменных окружения
        self.minecraft_data_path = os.getenv('MINECRAFT_DATA_PATH', '/minecraft-data')
        self.backup_path = os.getenv('BACKUP_PATH', '/backups')
        self.backup_interval_minutes = int(os.getenv('BACKUP_INTERVAL_MINUTES', '30'))
        self.backup_retention_days = int(os.getenv('BACKUP_RETENTION_DAYS', '7'))
        
        # RCON настройки
        self.rcon_host = os.getenv('RCON_HOST', 'minecraft-server')
        self.rcon_port = int(os.getenv('RCON_PORT', '25575'))
        self.rcon_password = os.getenv('RCON_PASSWORD', 'changeme123')
        
        # Manager API настройки
        self.manager_api_url = os.getenv('MANAGER_API_URL', 'http://minecraft-manager:8080')
        
        # Создаем директории если их нет
        Path(self.backup_path).mkdir(parents=True, exist_ok=True)
        
        logger.info(f"Backup service initialized:")
        logger.info(f"  - Data path: {self.minecraft_data_path}")
        logger.info(f"  - Backup path: {self.backup_path}")
        logger.info(f"  - Interval: {self.backup_interval_minutes} minutes")
        logger.info(f"  - Retention: {self.backup_retention_days} days")

    def send_rcon_command(self, command):
        """Отправляет команду через RCON"""
        try:
            with MCRcon(self.rcon_host, self.rcon_password, port=self.rcon_port) as mcr:
                response = mcr.command(command)
                logger.info(f"RCON command '{command}' executed: {response}")
                return response
        except Exception as e:
            logger.error(f"Failed to execute RCON command '{command}': {e}")
            return None

    def notify_manager(self, event_type, message):
        """Уведомляет Manager API о событиях бэкапа"""
        try:
            payload = {
                'event': event_type,
                'message': message,
                'timestamp': datetime.now().isoformat()
            }
            response = requests.post(
                f"{self.manager_api_url}/api/backup/events",
                json=payload,
                timeout=10
            )
            if response.status_code == 200:
                logger.info(f"Notified manager about {event_type}")
            else:
                logger.warning(f"Failed to notify manager: {response.status_code}")
        except Exception as e:
            logger.error(f"Failed to notify manager: {e}")

    def disable_autosave(self):
        """Отключает автосохранение мира"""
        logger.info("Disabling world autosave...")
        self.send_rcon_command("save-off")
        # Принудительно сохраняем мир перед бэкапом
        self.send_rcon_command("save-all flush")
        time.sleep(5)  # Ждем завершения сохранения

    def enable_autosave(self):
        """Включает автосохранение мира"""
        logger.info("Enabling world autosave...")
        self.send_rcon_command("save-on")

    def create_backup(self):
        """Создает резервную копию мира"""
        backup_start_time = datetime.now()
        backup_filename = f"minecraft_backup_{backup_start_time.strftime('%Y%m%d_%H%M%S')}.tar.gz"
        backup_filepath = os.path.join(self.backup_path, backup_filename)
        
        try:
            logger.info(f"Starting backup: {backup_filename}")
            self.notify_manager('backup_started', f"Starting backup {backup_filename}")
            
            # Отключаем автосохранение
            self.disable_autosave()
            
            # Создаем архив
            with tarfile.open(backup_filepath, 'w:gz') as tar:
                # Добавляем world директорию если она существует
                world_path = os.path.join(self.minecraft_data_path, 'world')
                if os.path.exists(world_path):
                    tar.add(world_path, arcname='world')
                    logger.info("Added world directory to backup")
                
                # Добавляем другие важные файлы
                important_files = [
                    'server.properties',
                    'whitelist.json',
                    'banned-players.json',
                    'banned-ips.json',
                    'ops.json'
                ]
                
                for file in important_files:
                    file_path = os.path.join(self.minecraft_data_path, file)
                    if os.path.exists(file_path):
                        tar.add(file_path, arcname=file)
                        logger.info(f"Added {file} to backup")
            
            # Включаем автосохранение обратно
            self.enable_autosave()
            
            # Проверяем размер созданного архива
            backup_size = os.path.getsize(backup_filepath)
            backup_size_mb = backup_size / (1024 * 1024)
            
            backup_duration = datetime.now() - backup_start_time
            
            logger.info(f"Backup completed successfully!")
            logger.info(f"  - File: {backup_filename}")
            logger.info(f"  - Size: {backup_size_mb:.2f} MB")
            logger.info(f"  - Duration: {backup_duration.total_seconds():.2f} seconds")
            
            self.notify_manager('backup_completed', 
                              f"Backup {backup_filename} completed successfully. Size: {backup_size_mb:.2f} MB")
            
            return backup_filepath
            
        except Exception as e:
            # Включаем автосохранение в случае ошибки
            self.enable_autosave()
            logger.error(f"Backup failed: {e}")
            self.notify_manager('backup_failed', f"Backup failed: {str(e)}")
            return None

    def cleanup_old_backups(self):
        """Удаляет старые резервные копии"""
        try:
            cutoff_date = datetime.now() - timedelta(days=self.backup_retention_days)
            backup_dir = Path(self.backup_path)
            
            deleted_count = 0
            freed_space = 0
            
            for backup_file in backup_dir.glob("minecraft_backup_*.tar.gz"):
                file_stat = backup_file.stat()
                file_date = datetime.fromtimestamp(file_stat.st_mtime)
                
                if file_date < cutoff_date:
                    file_size = file_stat.st_size
                    backup_file.unlink()
                    deleted_count += 1
                    freed_space += file_size
                    logger.info(f"Deleted old backup: {backup_file.name}")
            
            if deleted_count > 0:
                freed_space_mb = freed_space / (1024 * 1024)
                logger.info(f"Cleanup completed: {deleted_count} files deleted, {freed_space_mb:.2f} MB freed")
                self.notify_manager('cleanup_completed', 
                                  f"Deleted {deleted_count} old backups, freed {freed_space_mb:.2f} MB")
            else:
                logger.info("No old backups to clean up")
                
        except Exception as e:
            logger.error(f"Cleanup failed: {e}")
            self.notify_manager('cleanup_failed', f"Cleanup failed: {str(e)}")

    def get_backup_stats(self):
        """Возвращает статистику по бэкапам"""
        try:
            backup_dir = Path(self.backup_path)
            backups = list(backup_dir.glob("minecraft_backup_*.tar.gz"))
            
            total_size = sum(backup.stat().st_size for backup in backups)
            total_size_mb = total_size / (1024 * 1024)
            
            stats = {
                'total_backups': len(backups),
                'total_size_mb': round(total_size_mb, 2),
                'oldest_backup': None,
                'newest_backup': None
            }
            
            if backups:
                backup_dates = [(backup, datetime.fromtimestamp(backup.stat().st_mtime)) 
                               for backup in backups]
                backup_dates.sort(key=lambda x: x[1])
                
                stats['oldest_backup'] = backup_dates[0][1].isoformat()
                stats['newest_backup'] = backup_dates[-1][1].isoformat()
            
            return stats
            
        except Exception as e:
            logger.error(f"Failed to get backup stats: {e}")
            return None

    def run_backup_job(self):
        """Выполняет полный цикл бэкапа"""
        logger.info("=" * 50)
        logger.info("Starting backup job")
        
        # Создаем бэкап
        backup_file = self.create_backup()
        
        if backup_file:
            # Очищаем старые бэкапы
            self.cleanup_old_backups()
            
            # Выводим статистику
            stats = self.get_backup_stats()
            if stats:
                logger.info(f"Backup statistics: {stats['total_backups']} backups, "
                           f"{stats['total_size_mb']} MB total")
        
        logger.info("Backup job completed")
        logger.info("=" * 50)

    def start_scheduler(self):
        """Запускает планировщик бэкапов"""
        logger.info(f"Starting backup scheduler (every {self.backup_interval_minutes} minutes)")
        
        # Планируем регулярные бэкапы
        schedule.every(self.backup_interval_minutes).minutes.do(self.run_backup_job)
        
        # Создаем первый бэкап сразу при запуске (через 30 секунд)
        schedule.every(30).seconds.do(self.run_backup_job).tag('initial')
        
        while True:
            try:
                schedule.run_pending()
                time.sleep(60)  # Проверяем каждую минуту
            except KeyboardInterrupt:
                logger.info("Backup service stopped by user")
                break
            except Exception as e:
                logger.error(f"Scheduler error: {e}")
                time.sleep(60)  # Ждем минуту перед повторной попыткой

def main():
    """Главная функция"""
    logger.info("Minecraft Backup Service starting...")
    
    # Ждем немного чтобы minecraft сервер успел запуститься
    logger.info("Waiting for Minecraft server to be ready...")
    time.sleep(60)
    
    backup_service = MinecraftBackup()
    
    # Проверяем подключение к RCON
    try:
        response = backup_service.send_rcon_command("list")
        if response is not None:
            logger.info("RCON connection successful")
        else:
            logger.warning("RCON connection failed, backups will continue without save-off/save-on")
    except Exception as e:
        logger.warning(f"RCON test failed: {e}")
    
    # Запускаем планировщик
    backup_service.start_scheduler()

if __name__ == "__main__":
    main()
