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

            long totalSourceSize = 0;
            foreach (var file in files)
            {
                try
                {
                    totalSourceSize += new FileInfo(file).Length;
                }
                catch
                {
                    // Ignore files that disappear or cannot be accessed during size calculation.
                }
            }

            await LogAsync(progress, $"Source total size: {FileHelper.FormatBytes(totalSourceSize)} ({files.Length} files)", LogLevel.INFO);

            // Calculate destination size
            long totalDestinationSize = 0;
            try
            {
                var destFiles = Directory.GetFiles(config.LocalPath, "*", SearchOption.AllDirectories);
                foreach (var file in destFiles)
                {
                    try
                    {
                        totalDestinationSize += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Ignore files that disappear or cannot be accessed during size calculation.
                    }
                }
                await LogAsync(progress, $"Destination total size: {FileHelper.FormatBytes(totalDestinationSize)} ({destFiles.Length} files)", LogLevel.INFO);
            }
            catch (Exception ex)
            {
                await LogAsync(progress, $"Failed to calculate destination size: {ex.Message}", LogLevel.WARNING);
            }

            if (TryGetDestinationAvailableSpace(config.LocalPath, out var availableSpace, out var spaceMessage))
            {
                long totalDriveSize = 0;
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(config.LocalPath)!);
                    totalDriveSize = drive.TotalSize;
                }
                catch { }

                if (totalDriveSize > 0)
                {
                    await LogAsync(progress, $"Destination drive size: {FileHelper.FormatBytes(totalDriveSize)}, available: {FileHelper.FormatBytes(availableSpace)}", LogLevel.INFO);
                }
                else
                {
                    await LogAsync(progress, $"Destination available space: {FileHelper.FormatBytes(availableSpace)}", LogLevel.INFO);
                }

                if (availableSpace < totalSourceSize)
                {
                    var errorMsg = $"Insufficient disk space on destination drive. Required: {FileHelper.FormatBytes(totalSourceSize)}, Available: {FileHelper.FormatBytes(availableSpace)}.";
                    await LogAsync(progress, errorMsg, LogLevel.ERROR);
                    MessageBox.Show(errorMsg, "FileSyncPro", MessageBoxButton.OK, MessageBoxImage.Error);
                    throw new IOException(errorMsg);
                }

                await LogAsync(progress, $"Destination free space check passed. Required: {FileHelper.FormatBytes(totalSourceSize)}, Available: {FileHelper.FormatBytes(availableSpace)}.", LogLevel.INFO);
            }
            else
            {
                await LogAsync(progress, $"Destination free space check unavailable: {spaceMessage}. Proceeding without check.", LogLevel.WARNING);
            }

            foreach (var file in files)
            {
                var relativePath = file.Substring(sourcePath.Length + 1);
                var destFile = Path.Combine(config.LocalPath, relativePath);
                var destDir = Path.GetDirectoryName(destFile);

                try
                {
                    if (!Directory.Exists(destDir))
                    {
                        await Task.Run(() => Directory.CreateDirectory(destDir!));
                    }

                    long fileSize = new FileInfo(file).Length;
                    await Task.Run(() => File.Copy(file, destFile, true));
                    await LogAsync(progress, $"Copied: {relativePath} ({FileHelper.FormatBytes(fileSize)})", LogLevel.SUCCESS);
                }
                catch (Exception ex)
                {
                    string sizeInfo = "";
                    try { var sz = new FileInfo(file).Length; sizeInfo = $" ({FileHelper.FormatBytes(sz)})"; } catch { }
                    await LogAsync(progress, $"Failed to copy {relativePath}{sizeInfo}: {ex.Message}", LogLevel.ERROR);
                }
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

        private bool TryGetDestinationAvailableSpace(string destinationPath, out long availableBytes, out string message)
        {
            return FileHelper.TryGetDestinationAvailableSpace(destinationPath, out availableBytes, out message);
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