using FileSyncPro.Models;
using System.Collections.ObjectModel;
using System.IO;
using System;
using System.Collections.Generic;

namespace FileSyncPro.Services
{
    public class LogManager
    {
        private readonly string _logFilePath;
        private readonly object _lock = new object();
        public ObservableCollection<TransferLog> Logs { get; private set; }

        public LogManager()
        {
            Logs = new ObservableCollection<TransferLog>();
            _logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "FileSyncPro", "logs.txt");
            
            // Create directory if it doesn't exist
            var logDir = Path.GetDirectoryName(_logFilePath);
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
        }

        public void AddLog(TransferLog log)
        {
            lock (_lock)
            {
                Logs.Add(log);
                WriteToLogFile(log);
            }
        }

        private void WriteToLogFile(TransferLog log)
        {
            string logEntry = $"{log.Timestamp:yyyy-MM-dd HH:mm:ss} | " +
                             $"{log.FileName} | " +
                             $"{log.Source} -> {log.Destination} | " +
                             $"{log.Status}" +
                             (log.ErrorMessage != null ? $" | Error: {log.ErrorMessage}" : "") + 
                             Environment.NewLine;

            File.AppendAllText(_logFilePath, logEntry);
        }

        public void ClearLogs()
        {
            lock (_lock)
            {
                Logs.Clear();
                if (File.Exists(_logFilePath))
                {
                    File.Delete(_logFilePath);
                }
            }
        }

        public List<TransferLog> GetLogs()
        {
            lock (_lock)
            {
                return new List<TransferLog>(Logs);
            }
        }

        public void ExportLogs(string exportPath)
        {
            lock (_lock)
            {
                File.WriteAllText(exportPath, "");
                
                foreach (var log in Logs)
                {
                    string logEntry = $"{log.Timestamp:yyyy-MM-dd HH:mm:ss} | " +
                                     $"{log.FileName} | " +
                                     $"{log.Source} -> {log.Destination} | " +
                                     $"{log.Status}" +
                                     (log.ErrorMessage != null ? $" | Error: {log.ErrorMessage}" : "") + 
                                     Environment.NewLine;
                    
                    File.AppendAllText(exportPath, logEntry);
                }
            }
        }
    }
}