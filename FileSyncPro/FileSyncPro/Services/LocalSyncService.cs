#nullable enable
using FileSyncPro.Models;
using FileSyncPro.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace FileSyncPro.Services
{
    public class LocalSyncService
    {
        public async Task SyncAsync(string sourcePath, DestinationConfig config, SyncProgress progress)
        {
            var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                var relativePath = file.Substring(sourcePath.Length + 1);
                var destFile = Path.Combine(config.LocalPath, relativePath);
                var destDir = Path.GetDirectoryName(destFile);

                if (!Directory.Exists(destDir))
                {
                    await Task.Run(() => Directory.CreateDirectory(destDir!));
                }

                await Task.Run(() => File.Copy(file, destFile, true));
                await LogAsync(progress, $"Copied: {relativePath}", LogLevel.INFO);
            }
        }

        public async Task<SyncResult> ValidateAsync(DestinationConfig config)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.LocalPath))
                {
                    return new SyncResult { IsSuccess = false, Message = "Local path is required" };
                }

                if (!Directory.Exists(config.LocalPath))
                {
                    try
                    {
                        Directory.CreateDirectory(config.LocalPath);
                        return new SyncResult { IsSuccess = true, Message = "Local directory created successfully" };
                    }
                    catch (Exception ex)
                    {
                        return new SyncResult { IsSuccess = false, Message = $"Failed to create directory: {ex.Message}" };
                    }
                }

                // UNC paths (e.g. \\server\share\...) are not supported by DriveInfo
                var root = Path.GetPathRoot(config.LocalPath)!;
                if (root.StartsWith(@"\\"))
                {
                    return new SyncResult
                    {
                        IsSuccess = true,
                        Message = $"Network destination validated: {config.LocalPath}"
                    };
                }

                var drive = new DriveInfo(root);
                var availableSpace = drive.AvailableFreeSpace;

                return new SyncResult
                {
                    IsSuccess = true,
                    Message = $"Local destination validated. Available space: {FileHelper.FormatBytes(availableSpace)}"
                };
            }
            catch (Exception ex)
            {
                return new SyncResult { IsSuccess = false, Message = $"Validation failed: {ex.Message}" };
            }
        }

        private async Task LogAsync(SyncProgress progress, string message, LogLevel level)
        {
            var logEntry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = $"{DateTime.Now:HH:mm:ss} [{level}] {message}"
            };

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                progress.LogEntries.Add(logEntry);
            });
        }
    }
}