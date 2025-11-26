#nullable enable
using System;

namespace FileSyncPro.Models
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public enum LogLevel
    {
        DEBUG,
        INFO,
        SUCCESS,
        WARNING,
        ERROR
    }
}