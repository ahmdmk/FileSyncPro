#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using FileSyncPro.Models;

namespace FileSyncPro.Utilities
{
    public static class Logger
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        private static readonly object LockObject = new object();

        static Logger()
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }
        }

        private static string GetLogFilePath()
        {
            return Path.Combine(LogDirectory, $"FileSyncPro_{DateTime.Now:yyyyMMdd}.log");
        }

        public static void Log(LogLevel level, string message, Exception? exception = null)
        {
            lock (LockObject)
            {
                try
                {
                    var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{Thread.CurrentThread.ManagedThreadId}] {message}";
                    
                    if (exception != null)
                    {
                        logEntry += $"\nEXCEPTION: {exception.Message}\nSTACK TRACE: {exception.StackTrace}";
                    }

                    using (var writer = new StreamWriter(GetLogFilePath(), true))
                    {
                        writer.WriteLine(logEntry);
                    }

                    System.Diagnostics.Debug.WriteLine(logEntry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
                }
            }
        }

        public static void Info(string message) => Log(LogLevel.INFO, message);
        public static void Warning(string message) => Log(LogLevel.WARNING, message);
        public static void Error(string message, Exception? exception = null) => Log(LogLevel.ERROR, message, exception);
        public static void Debug(string message) => Log(LogLevel.DEBUG, message);
        public static void Success(string message) => Log(LogLevel.SUCCESS, message);

        public static string[] GetRecentLogFiles(int maxFiles = 10)
        {
            try
            {
                var logFiles = Directory.GetFiles(LogDirectory, "FileSyncPro_*.log");
                Array.Sort(logFiles);
                Array.Reverse(logFiles);
                return logFiles.Length > maxFiles ? logFiles.Take(maxFiles).ToArray() : logFiles;
            }
            catch (Exception ex)
            {
                Error("Failed to get recent log files", ex);
                return Array.Empty<string>();
            }
        }

        public static string? ReadLogFile(string filePath)
        {
            try
            {
                return File.Exists(filePath) ? File.ReadAllText(filePath) : "Log file not found.";
            }
            catch (Exception ex)
            {
                Error($"Failed to read log file: {filePath}", ex);
                return $"Error reading log file: {ex.Message}";
            }
        }

        public static void CleanOldLogs(int daysToKeep = 30)
        {
            try
            {
                var logFiles = Directory.GetFiles(LogDirectory, "FileSyncPro_*.log");
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);

                foreach (var logFile in logFiles)
                {
                    var fileInfo = new FileInfo(logFile);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        fileInfo.Delete();
                        Debug($"Deleted old log file: {logFile}");
                    }
                }
            }
            catch (Exception ex)
            {
                Error("Failed to clean old logs", ex);
            }
        }
    }
}