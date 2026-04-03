using System.IO.Compression;
using FileSyncPro.Models;
using FileSyncPro.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FileSyncPro.Services
{
    public class FileSyncService
    {
        private readonly LocalSyncService _localSyncService;
        private readonly SharePointSyncService _sharePointSyncService;
        private readonly SFTPSyncService _sftpSyncService;

        public FileSyncService()
        {
            _localSyncService = new LocalSyncService();
            _sharePointSyncService = new SharePointSyncService();
            _sftpSyncService = new SFTPSyncService();
        }

        public async Task AnalyzeFilesAsync(FileSyncOperation operation, IProgress<ProgressReport> progress)
        {
            try
            {
                operation.Progress.IsRunning = true;
                progress.Report(new ProgressReport { CurrentOperation = "Analyzing files...", Percentage = 0 });

                var zipFiles = Directory.GetFiles(operation.SourcePath, "*.zip");
                await LogAsync(operation.Progress, $"Found {zipFiles.Length} ZIP files", LogLevel.INFO);

                long totalSize = 0;
                long totalFiles = 0;
                int processedFiles = 0;

                foreach (var zipFile in zipFiles)
                {
                    using var archive = ZipFile.OpenRead(zipFile);
                    totalFiles += archive.Entries.Count;

                    long currentZipSize = 0;
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Length < 0) 
                            continue;

                        if (long.MaxValue - currentZipSize < entry.Length)
                            throw new OverflowException("Zip archive total size exceeds Int64 limit");

                        currentZipSize += entry.Length;
                    }

                    if (long.MaxValue - totalSize < currentZipSize)
                        throw new OverflowException("Cumulative ZIP total size exceeds Int64 limit");

                    totalSize += currentZipSize;

                    await LogAsync(operation.Progress, 
                        $"{Path.GetFileName(zipFile)}: {archive.Entries.Count} files, {FileHelper.FormatBytes(currentZipSize)}", 
                        LogLevel.DEBUG);
                    processedFiles++;
                    progress.Report(new ProgressReport { Percentage = (double)processedFiles / zipFiles.Length * 100 });
                }

                await LogAsync(operation.Progress, 
                    $"Analysis complete: {totalFiles} files, {FileHelper.FormatBytes(totalSize)}", 
                    LogLevel.SUCCESS);
            }
            catch (Exception ex)
            {
                await LogAsync(operation.Progress, $"Analysis failed: {ex.Message}\n{ex}", LogLevel.ERROR);
                Logger.Error("File analysis failed", ex);
            }
            finally
            {
                operation.Progress.IsRunning = false;
                progress.Report(new ProgressReport { CurrentOperation = "Analysis completed", Percentage = 100 });
                MessageBox.Show("Analysis completed!", "FileSyncPro", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public async Task SyncFilesAsync(FileSyncOperation operation, IProgress<ProgressReport> progress)
        {
            bool syncSuccess = false;

            try
            {
                operation.Progress.IsRunning = true;
                progress.Report(new ProgressReport { CurrentOperation = "Starting synchronization...", Percentage = 0 });

                // Handle SharePoint to Local sync
                if (operation.SourceType == SourceType.SharePoint && operation.DestinationType == DestinationType.Local)
                {
                    await LogAsync(operation.Progress, "Downloading from SharePoint to Local...", LogLevel.INFO);
                    await _sharePointSyncService.DownloadFromSharePointAsync(
                        operation.SourceConfig,
                        operation.DestinationConfig.LocalPath,
                        operation.Progress);

                    await LogAsync(operation.Progress, "Synchronization completed successfully!", LogLevel.SUCCESS);
                    syncSuccess = true;
                    return;
                }

                // Handle SFTP source
                if (operation.SourceType == SourceType.SFTP)
                {
                    // Use temp directory on destination drive if destination is local, otherwise system temp
                    string tempBasePath = operation.DestinationType == DestinationType.Local ? operation.DestinationConfig.LocalPath : Path.GetTempPath();
                    var tempSftpPath = Path.Combine(tempBasePath, $"FileSyncPro_SFTP_{Guid.NewGuid()}");
                    try
                    {
                        await LogAsync(operation.Progress, "Downloading files from SFTP source...", LogLevel.INFO);

                        // When destination is Local, use the selected local destination path for space checks.
                        var destinationPathForSpaceCheck = operation.DestinationType == DestinationType.Local ? operation.DestinationConfig.LocalPath : null;
                        await _sftpSyncService.DownloadFromSFTPAsync(operation.SourceConfig, tempSftpPath, destinationPathForSpaceCheck, operation.Progress);

                        progress.Report(new ProgressReport { CurrentOperation = "Syncing to destination...", Percentage = 50 });

                        switch (operation.DestinationType)
                        {
                            case DestinationType.Local:
                                await _localSyncService.SyncAsync(tempSftpPath, operation.DestinationConfig, operation.Progress);
                                break;
                            case DestinationType.SharePoint:
                                await _sharePointSyncService.SyncAsync(tempSftpPath, operation.DestinationConfig, operation.Progress);
                                break;
                            case DestinationType.SFTP:
                                await _sftpSyncService.SyncAsync(tempSftpPath, operation.DestinationConfig, operation.Progress);
                                break;
                        }

                        await LogAsync(operation.Progress, "Synchronization completed successfully!", LogLevel.SUCCESS);
                        syncSuccess = true;
                    }
                    finally
                    {
                        if (Directory.Exists(tempSftpPath))
                        {
                            Directory.Delete(tempSftpPath, true);
                            await LogAsync(operation.Progress, "Cleaned up temporary files", LogLevel.DEBUG);
                        }
                    }
                    return;
                }

                var zipFiles = Directory.GetFiles(operation.SourcePath, "*.zip");
                var tempExtractPath = Path.Combine(Path.GetTempPath(), $"FileSyncPro_{Guid.NewGuid()}");
                bool useTempDirectory = zipFiles.Length > 0;

                try
                {
                    if (useTempDirectory)
                    {
                        // If ZIP files exist, extract them to temp directory
                        Directory.CreateDirectory(tempExtractPath);
                        await LogAsync(operation.Progress, $"Created temp directory: {tempExtractPath}", LogLevel.DEBUG);

                        var totalOperations = zipFiles.Length;
                        var completedOperations = 0;

                        foreach (var zipFile in zipFiles)
                        {
                            progress.Report(new ProgressReport { CurrentOperation = $"Processing {Path.GetFileName(zipFile)}" });

                            var extractPath = Path.Combine(tempExtractPath, Path.GetFileNameWithoutExtension(zipFile));
                            ZipFile.ExtractToDirectory(zipFile, extractPath);
                            await LogAsync(operation.Progress, $"Extracted: {Path.GetFileName(zipFile)}", LogLevel.INFO);

                            switch (operation.DestinationType)
                            {
                                case DestinationType.Local:
                                    await _localSyncService.SyncAsync(extractPath, operation.DestinationConfig, operation.Progress);
                                    break;
                                case DestinationType.SharePoint:
                                    await _sharePointSyncService.SyncAsync(extractPath, operation.DestinationConfig, operation.Progress);
                                    break;
                                case DestinationType.SFTP:
                                    await _sftpSyncService.SyncAsync(extractPath, operation.DestinationConfig, operation.Progress);
                                    break;
                            }

                            completedOperations++;
                            progress.Report(new ProgressReport { Percentage = (double)completedOperations / totalOperations * 100 });
                        }
                    }
                    else
                    {
                        // No ZIP files - copy folders and contents directly
                        await LogAsync(operation.Progress, "No ZIP files found. Syncing folders and contents directly...", LogLevel.INFO);

                        progress.Report(new ProgressReport { CurrentOperation = "Syncing folders and files..." });

                        switch (operation.DestinationType)
                        {
                            case DestinationType.Local:
                                await _localSyncService.SyncAsync(operation.SourcePath, operation.DestinationConfig, operation.Progress);
                                break;
                            case DestinationType.SharePoint:
                                await _sharePointSyncService.SyncAsync(operation.SourcePath, operation.DestinationConfig, operation.Progress);
                                break;
                            case DestinationType.SFTP:
                                await _sftpSyncService.SyncAsync(operation.SourcePath, operation.DestinationConfig, operation.Progress);
                                break;
                        }

                        progress.Report(new ProgressReport { Percentage = 100 });
                    }

                    await LogAsync(operation.Progress, "Synchronization completed successfully!", LogLevel.SUCCESS);
                    syncSuccess = true;
                }
                finally
                {
                    // Cleanup temp directory only if we created one
                    if (useTempDirectory && Directory.Exists(tempExtractPath))
                    {
                        Directory.Delete(tempExtractPath, true);
                        await LogAsync(operation.Progress, "Cleaned up temporary files", LogLevel.DEBUG);
                    }
                }
            }
            catch (Exception ex)
            {
                await LogAsync(operation.Progress, $"Synchronization failed: {ex.Message}\n{ex}", LogLevel.ERROR);
                Logger.Error("Synchronization failed", ex);
            }
            finally
            {
                operation.Progress.IsRunning = false;
                progress.Report(new ProgressReport { CurrentOperation = syncSuccess ? "Synchronization completed" : "Synchronization failed", Percentage = 100 });

                if (syncSuccess)
                {
                    MessageBox.Show("Synchronization completed successfully!", "FileSyncPro", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Synchronization failed. Please check logs.", "FileSyncPro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public async Task<SyncResult> ValidateDestinationAsync(FileSyncOperation operation, IProgress<ProgressReport> progress)
        {
            try
            {
                operation.Progress.IsRunning = true;
                progress.Report(new ProgressReport { CurrentOperation = "Validating destination...", Percentage = 0 });

                var result = operation.DestinationType switch
                {
                    DestinationType.Local => await _localSyncService.ValidateAsync(operation.DestinationConfig),
                    DestinationType.SharePoint => await _sharePointSyncService.ValidateAsync(operation.DestinationConfig),
                    DestinationType.SFTP => await _sftpSyncService.ValidateAsync(operation.DestinationConfig),
                    _ => new SyncResult { IsSuccess = false, Message = "Unknown destination type" }
                };

                await LogAsync(operation.Progress, result.Message, 
                    result.IsSuccess ? LogLevel.SUCCESS : LogLevel.ERROR);
                
                progress.Report(new ProgressReport { Percentage = 100 });

                return result;
            }
            finally
            {
                operation.Progress.IsRunning = false;
                progress.Report(new ProgressReport { CurrentOperation = "Validation completed", Percentage = 100 });
                MessageBox.Show("Validation completed!", "FileSyncPro", MessageBoxButton.OK, MessageBoxImage.Information);
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

            // Also log to file
            switch (level)
            {
                case LogLevel.DEBUG: Logger.Debug(message); break;
                case LogLevel.INFO: Logger.Info(message); break;
                case LogLevel.SUCCESS: Logger.Success(message); break;
                case LogLevel.WARNING: Logger.Warning(message); break;
                case LogLevel.ERROR: Logger.Error(message); break;
            }
        }
    }
}