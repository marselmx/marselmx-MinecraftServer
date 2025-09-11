namespace Manager.Models
{
    public class BackupInfo
    {
        public string FileName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
        public double SizeMB => Math.Round(SizeBytes / (1024.0 * 1024.0), 2);
    }

    public class BackupStats
    {
        public int TotalBackups { get; set; }
        public double TotalSizeMB { get; set; }
        public DateTime? OldestBackup { get; set; }
        public DateTime? NewestBackup { get; set; }
        public List<BackupInfo> Backups { get; set; } = new();
    }

    public class BackupEvent
    {
        public string Event { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class CreateBackupRequest
    {
        public string? Description { get; set; }
        public bool DisableAutosave { get; set; } = true;
    }

    public class RestoreBackupRequest
    {
        public string BackupFileName { get; set; } = string.Empty;
        public bool StopServer { get; set; } = true;
        public bool CreateBackupBeforeRestore { get; set; } = true;
    }

    public class BackupResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
    }
}
