using System;

namespace FileSyncPro.Models
{
    public class SyncResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public int FilesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public TimeSpan Duration { get; set; }
    }
}