using Manager.Models;
using Manager.Services;
using Microsoft.AspNetCore.Mvc;

namespace Manager.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BackupController : ControllerBase
    {
        private readonly BackupService _backupService;
        private readonly ILogger<BackupController> _logger;

        public BackupController(BackupService backupService, ILogger<BackupController> logger)
        {
            _backupService = backupService;
            _logger = logger;
        }

        /// <summary>
        /// Получить статистику по бэкапам
        /// </summary>
        [HttpGet("stats")]
        public async Task<ActionResult<BackupStats>> GetBackupStats()
        {
            try
            {
                var stats = await _backupService.GetBackupStatsAsync();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get backup stats");
                return StatusCode(500, new { error = "Failed to get backup statistics" });
            }
        }

        /// <summary>
        /// Получить список последних событий бэкапа
        /// </summary>
        [HttpGet("events")]
        public ActionResult<List<BackupEvent>> GetRecentEvents()
        {
            var events = _backupService.GetRecentEvents();
            return Ok(events);
        }

        /// <summary>
        /// Принять уведомление о событии бэкапа (от backup сервиса)
        /// </summary>
        [HttpPost("events")]
        public ActionResult ReceiveBackupEvent([FromBody] BackupEvent backupEvent)
        {
            try
            {
                _backupService.AddEvent(backupEvent);
                _logger.LogInformation($"Received backup event: {backupEvent.Event} - {backupEvent.Message}");
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process backup event");
                return StatusCode(500, new { error = "Failed to process backup event" });
            }
        }

        /// <summary>
        /// Создать новый бэкап
        /// </summary>
        [HttpPost("create")]
        public async Task<ActionResult<BackupResponse>> CreateBackup([FromBody] CreateBackupRequest request)
        {
            try
            {
                var result = await _backupService.CreateBackupAsync(request);
                return result.Success ? Ok(result) : BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create backup");
                return StatusCode(500, new BackupResponse 
                { 
                    Success = false, 
                    Message = "Internal server error during backup creation" 
                });
            }
        }

        /// <summary>
        /// Восстановить мир из бэкапа
        /// </summary>
        [HttpPost("restore")]
        public async Task<ActionResult<BackupResponse>> RestoreBackup([FromBody] RestoreBackupRequest request)
        {
            try
            {
                var result = await _backupService.RestoreBackupAsync(request);
                return result.Success ? Ok(result) : BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore backup");
                return StatusCode(500, new BackupResponse 
                { 
                    Success = false, 
                    Message = "Internal server error during backup restore" 
                });
            }
        }

        /// <summary>
        /// Удалить бэкап
        /// </summary>
        [HttpDelete("{backupFileName}")]
        public async Task<ActionResult<BackupResponse>> DeleteBackup(string backupFileName)
        {
            try
            {
                var result = await _backupService.DeleteBackupAsync(backupFileName);
                return result.Success ? Ok(result) : BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete backup");
                return StatusCode(500, new BackupResponse 
                { 
                    Success = false, 
                    Message = "Internal server error during backup deletion" 
                });
            }
        }

        /// <summary>
        /// Скачать бэкап файл
        /// </summary>
        [HttpGet("download/{backupFileName}")]
        public ActionResult DownloadBackup(string backupFileName)
        {
            try
            {
                var backupPath = Environment.GetEnvironmentVariable("BACKUP_PATH") ?? "/app/backups";
                var backupFilePath = Path.Combine(backupPath, backupFileName);

                if (!System.IO.File.Exists(backupFilePath))
                {
                    return NotFound(new { error = "Backup file not found" });
                }

                var fileBytes = System.IO.File.ReadAllBytes(backupFilePath);
                return File(fileBytes, "application/gzip", backupFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to download backup: {backupFileName}");
                return StatusCode(500, new { error = "Failed to download backup file" });
            }
        }

        /// <summary>
        /// Загрузить бэкап файл
        /// </summary>
        [HttpPost("upload")]
        public async Task<ActionResult<BackupResponse>> UploadBackup(IFormFile backupFile)
        {
            try
            {
                if (backupFile == null || backupFile.Length == 0)
                {
                    return BadRequest(new BackupResponse 
                    { 
                        Success = false, 
                        Message = "No file provided" 
                    });
                }

                if (!backupFile.FileName.EndsWith(".tar.gz"))
                {
                    return BadRequest(new BackupResponse 
                    { 
                        Success = false, 
                        Message = "Only .tar.gz files are allowed" 
                    });
                }

                var backupPath = Environment.GetEnvironmentVariable("BACKUP_PATH") ?? "/app/backups";
                var fileName = $"uploaded_{DateTime.Now:yyyyMMdd_HHmmss}_{backupFile.FileName}";
                var filePath = Path.Combine(backupPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await backupFile.CopyToAsync(stream);
                }

                _logger.LogInformation($"Backup uploaded: {fileName}");
                _backupService.AddEvent(new BackupEvent
                {
                    Event = "backup_uploaded",
                    Message = $"Backup {fileName} uploaded successfully. Size: {backupFile.Length / (1024.0 * 1024.0):F2} MB",
                    Timestamp = DateTime.Now
                });

                return Ok(new BackupResponse
                {
                    Success = true,
                    Message = "Backup uploaded successfully",
                    Data = new BackupInfo
                    {
                        FileName = fileName,
                        CreatedAt = DateTime.Now,
                        SizeBytes = backupFile.Length
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload backup");
                return StatusCode(500, new BackupResponse 
                { 
                    Success = false, 
                    Message = "Internal server error during backup upload" 
                });
            }
        }

        /// <summary>
        /// Отправить RCON команду на сервер Minecraft
        /// </summary>
        [HttpPost("rcon")]
        public async Task<ActionResult> SendRconCommand([FromBody] RconCommandRequest request)
        {
            try
            {
                var response = await _backupService.SendRconCommandAsync(request.Command);
                return Ok(new { command = request.Command, response = response });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send RCON command: {request.Command}");
                return StatusCode(500, new { error = "Failed to send RCON command" });
            }
        }
    }

    public class RconCommandRequest
    {
        public string Command { get; set; } = string.Empty;
    }
}
