#nullable enable
using System.ComponentModel;
using System;

namespace FileSyncPro.Models
{
    public class SftpConnection
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public class SharePointConnection
    {
        public string SiteUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Library { get; set; } = string.Empty;
    }

    public class LocalPathConnection
    {
        public string Path { get; set; } = string.Empty;
    }

    public class TransferLog
    {
        public DateTime Timestamp { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public TransferStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public enum TransferStatus
    {
        Success,
        Failed,
        InProgress
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}