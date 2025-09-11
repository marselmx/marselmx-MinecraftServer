using CoreRCON;
using Manager.Models;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;

namespace Manager.Services
{
    public class BackupService
    {
        private readonly ILogger<BackupService> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _backupPath;
        private readonly string _dataPath;
        private readonly string _rconHost;
        private readonly int _rconPort;
        private readonly string _rconPassword;
        private readonly List<BackupEvent> _recentEvents = new();

        public BackupService(ILogger<BackupService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _backupPath = configuration["BACKUP_PATH"] ?? "/app/backups";
            _dataPath = configuration["DATA_PATH"] ?? "/app/data";
            _rconHost = configuration["MINECRAFT_RCON_HOST"] ?? "minecraft-server";
            _rconPort = int.Parse(configuration["MINECRAFT_RCON_PORT"] ?? "25575");
            _rconPassword = configuration["MINECRAFT_RCON_PASSWORD"] ?? "changeme123";

            // Создаем директории если их нет
            Directory.CreateDirectory(_backupPath);
            Directory.CreateDirectory(_dataPath);
        }

        public void AddEvent(BackupEvent backupEvent)
        {
            _recentEvents.Add(backupEvent);
            // Оставляем только последние 100 событий
            if (_recentEvents.Count > 100)
            {
                _recentEvents.RemoveAt(0);
            }
        }

        public List<BackupEvent> GetRecentEvents()
        {
            return _recentEvents.OrderByDescending(e => e.Timestamp).Take(50).ToList();
        }

        public async Task<string?> SendRconCommandAsync(string command)
        {
            try
            {
                // Пытаемся парсить как IP адрес, если не получается - используем как hostname
                IPAddress? ipAddress = null;
                if (!IPAddress.TryParse(_rconHost, out ipAddress))
                {
                    // Резолвим hostname в IP
                    var hostEntry = await Dns.GetHostEntryAsync(_rconHost);
                    ipAddress = hostEntry.AddressList.FirstOrDefault();
                    if (ipAddress == null)
                    {
                        throw new Exception($"Could not resolve hostname: {_rconHost}");
                    }
                }

                using var rcon = new RCON(ipAddress, (ushort)_rconPort, _rconPassword);
                await rcon.ConnectAsync();
                var response = await rcon.SendCommandAsync(command);
                _logger.LogInformation($"RCON command '{command}' executed: {response}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to execute RCON command '{command}'");
                return null;
            }
        }

        public async Task<BackupStats> GetBackupStatsAsync()
        {
            try
            {
                var backupDir = new DirectoryInfo(_backupPath);
                var backupFiles = backupDir.GetFiles("minecraft_backup_*.tar.gz")
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                var backups = backupFiles.Select(file => new BackupInfo
                {
                    FileName = file.Name,
                    CreatedAt = file.CreationTime,
                    SizeBytes = file.Length
                }).ToList();

                var stats = new BackupStats
                {
                    TotalBackups = backups.Count,
                    TotalSizeMB = Math.Round(backups.Sum(b => b.SizeMB), 2),
                    OldestBackup = backups.LastOrDefault()?.CreatedAt,
                    NewestBackup = backups.FirstOrDefault()?.CreatedAt,
                    Backups = backups
                };

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get backup statistics");
                throw;
            }
        }

        public async Task<BackupResponse> CreateBackupAsync(CreateBackupRequest request)
        {
            var backupStartTime = DateTime.Now;
            var backupFileName = $"minecraft_backup_{backupStartTime:yyyyMMdd_HHmmss}";
            if (!string.IsNullOrEmpty(request.Description))
            {
                backupFileName += $"_{request.Description.Replace(" ", "_")}";
            }
            backupFileName += ".tar.gz";

            var backupFilePath = Path.Combine(_backupPath, backupFileName);

            try
            {
                _logger.LogInformation($"Starting manual backup: {backupFileName}");
                AddEvent(new BackupEvent
                {
                    Event = "backup_started",
                    Message = $"Starting manual backup {backupFileName}",
                    Timestamp = DateTime.Now
                });

                // Отключаем автосохранение если требуется
                if (request.DisableAutosave)
                {
                    await SendRconCommandAsync("save-off");
                    await SendRconCommandAsync("save-all flush");
                    await Task.Delay(5000); // Ждем завершения сохранения
                }

                // Создаем архив
                using (var fileStream = File.Create(backupFilePath))
                using (var gzipStream = new GZipStream(fileStream, CompressionMode.Compress))
                using (var tarWriter = new TarWriter(gzipStream))
                {
                    // Добавляем world директорию
                    var worldPath = Path.Combine(_dataPath, "world");
                    if (Directory.Exists(worldPath))
                    {
                        await AddDirectoryToTarAsync(tarWriter, worldPath, "world");
                    }

                    // Добавляем важные файлы
                    var importantFiles = new[]
                    {
                        "server.properties",
                        "whitelist.json",
                        "banned-players.json",
                        "banned-ips.json",
                        "ops.json"
                    };

                    foreach (var fileName in importantFiles)
                    {
                        var filePath = Path.Combine(_dataPath, fileName);
                        if (File.Exists(filePath))
                        {
                            await tarWriter.WriteEntryAsync(filePath, fileName);
                        }
                    }
                }

                // Включаем автосохранение обратно
                if (request.DisableAutosave)
                {
                    await SendRconCommandAsync("save-on");
                }

                var backupInfo = new FileInfo(backupFilePath);
                var duration = DateTime.Now - backupStartTime;

                _logger.LogInformation($"Backup completed: {backupFileName}, Size: {backupInfo.Length / (1024.0 * 1024.0):F2} MB");
                
                AddEvent(new BackupEvent
                {
                    Event = "backup_completed",
                    Message = $"Manual backup {backupFileName} completed successfully. Size: {backupInfo.Length / (1024.0 * 1024.0):F2} MB, Duration: {duration.TotalSeconds:F1}s",
                    Timestamp = DateTime.Now
                });

                return new BackupResponse
                {
                    Success = true,
                    Message = "Backup created successfully",
                    Data = new BackupInfo
                    {
                        FileName = backupFileName,
                        CreatedAt = backupInfo.CreationTime,
                        SizeBytes = backupInfo.Length
                    }
                };
            }
            catch (Exception ex)
            {
                // Включаем автосохранение в случае ошибки
                if (request.DisableAutosave)
                {
                    await SendRconCommandAsync("save-on");
                }

                _logger.LogError(ex, $"Backup creation failed: {backupFileName}");
                AddEvent(new BackupEvent
                {
                    Event = "backup_failed",
                    Message = $"Backup creation failed: {ex.Message}",
                    Timestamp = DateTime.Now
                });

                return new BackupResponse
                {
                    Success = false,
                    Message = $"Backup creation failed: {ex.Message}"
                };
            }
        }

        private async Task AddDirectoryToTarAsync(TarWriter tarWriter, string directoryPath, string entryName)
        {
            var directory = new DirectoryInfo(directoryPath);
            
            foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(directoryPath, file.FullName);
                var entryPath = Path.Combine(entryName, relativePath).Replace('\\', '/');
                
                await tarWriter.WriteEntryAsync(file.FullName, entryPath);
            }
        }

        public async Task<BackupResponse> RestoreBackupAsync(RestoreBackupRequest request)
        {
            var backupFilePath = Path.Combine(_backupPath, request.BackupFileName);
            
            if (!File.Exists(backupFilePath))
            {
                return new BackupResponse
                {
                    Success = false,
                    Message = "Backup file not found"
                };
            }

            try
            {
                _logger.LogInformation($"Starting backup restore: {request.BackupFileName}");
                AddEvent(new BackupEvent
                {
                    Event = "restore_started",
                    Message = $"Starting restore from {request.BackupFileName}",
                    Timestamp = DateTime.Now
                });

                // Создаем backup перед восстановлением если требуется
                if (request.CreateBackupBeforeRestore)
                {
                    var preRestoreBackup = await CreateBackupAsync(new CreateBackupRequest
                    {
                        Description = "pre_restore",
                        DisableAutosave = true
                    });
                    
                    if (!preRestoreBackup.Success)
                    {
                        return new BackupResponse
                        {
                            Success = false,
                            Message = $"Failed to create pre-restore backup: {preRestoreBackup.Message}"
                        };
                    }
                }

                // Останавливаем сервер если требуется
                if (request.StopServer)
                {
                    await SendRconCommandAsync("save-all flush");
                    await SendRconCommandAsync("stop");
                    await Task.Delay(10000); // Ждем остановки сервера
                }

                // Удаляем текущий мир
                var worldPath = Path.Combine(_dataPath, "world");
                if (Directory.Exists(worldPath))
                {
                    Directory.Delete(worldPath, true);
                }

                // Извлекаем backup
                using (var fileStream = File.OpenRead(backupFilePath))
                using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                using (var tarReader = new TarReader(gzipStream))
                {
                    TarEntry? entry;
                    while ((entry = await tarReader.GetNextEntryAsync()) != null)
                    {
                        if (entry.EntryType == TarEntryType.RegularFile)
                        {
                            var extractPath = Path.Combine(_dataPath, entry.Name);
                            var extractDir = Path.GetDirectoryName(extractPath);
                            
                            if (!string.IsNullOrEmpty(extractDir))
                            {
                                Directory.CreateDirectory(extractDir);
                            }

                            await entry.ExtractToFileAsync(extractPath, true);
                        }
                        else if (entry.EntryType == TarEntryType.Directory)
                        {
                            var extractPath = Path.Combine(_dataPath, entry.Name);
                            Directory.CreateDirectory(extractPath);
                        }
                    }
                }

                _logger.LogInformation($"Backup restore completed: {request.BackupFileName}");
                AddEvent(new BackupEvent
                {
                    Event = "restore_completed",
                    Message = $"Restore from {request.BackupFileName} completed successfully",
                    Timestamp = DateTime.Now
                });

                return new BackupResponse
                {
                    Success = true,
                    Message = "Backup restored successfully. Please restart the Minecraft server."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Backup restore failed: {request.BackupFileName}");
                AddEvent(new BackupEvent
                {
                    Event = "restore_failed",
                    Message = $"Restore failed: {ex.Message}",
                    Timestamp = DateTime.Now
                });

                return new BackupResponse
                {
                    Success = false,
                    Message = $"Backup restore failed: {ex.Message}"
                };
            }
        }

        public async Task<BackupResponse> DeleteBackupAsync(string backupFileName)
        {
            var backupFilePath = Path.Combine(_backupPath, backupFileName);
            
            if (!File.Exists(backupFilePath))
            {
                return new BackupResponse
                {
                    Success = false,
                    Message = "Backup file not found"
                };
            }

            try
            {
                var fileInfo = new FileInfo(backupFilePath);
                var sizeMB = fileInfo.Length / (1024.0 * 1024.0);
                
                File.Delete(backupFilePath);
                
                _logger.LogInformation($"Backup deleted: {backupFileName}");
                AddEvent(new BackupEvent
                {
                    Event = "backup_deleted",
                    Message = $"Backup {backupFileName} deleted successfully. Freed {sizeMB:F2} MB",
                    Timestamp = DateTime.Now
                });

                return new BackupResponse
                {
                    Success = true,
                    Message = $"Backup deleted successfully. Freed {sizeMB:F2} MB"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to delete backup: {backupFileName}");
                return new BackupResponse
                {
                    Success = false,
                    Message = $"Failed to delete backup: {ex.Message}"
                };
            }
        }
    }
}
