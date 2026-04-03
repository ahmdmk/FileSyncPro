#nullable enable
using Renci.SshNet;
using FileSyncPro.Models;
using FileSyncPro.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace FileSyncPro.Services
{
    public class SFTPSyncService
    {
        public async Task DownloadFromSFTPAsync(DestinationConfig sourceConfig, string localDestPath, string? finalLocalDestinationPath, SyncProgress progress)
        {
            using var client = new SftpClient(sourceConfig.SFTPHost, sourceConfig.SFTPPort, sourceConfig.SFTPUser, sourceConfig.SFTPPassword);
            try
            {
                await Task.Run(() => client.Connect());

                // Evaluate remote source size before download
                var (remoteTotalSize, remoteFileCount) = GetRemoteFolderSize(client, sourceConfig.SFTPPath);
                await LogAsync(progress, $"Remote source size: {FileHelper.FormatBytes(remoteTotalSize)} ({remoteFileCount} files)", LogLevel.INFO);

                // Calculate destination size against final destination (local path box) when provided
                var destinationPathToCheck = string.IsNullOrWhiteSpace(finalLocalDestinationPath) ? localDestPath : finalLocalDestinationPath;
                long totalDestinationSize = 0;
                try
                {
                    var destFiles = Directory.GetFiles(destinationPathToCheck, "*", SearchOption.AllDirectories);
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
                    await LogAsync(progress, $"Failed to calculate destination size ({destinationPathToCheck}): {ex.Message}", LogLevel.WARNING);
                }

                Directory.CreateDirectory(localDestPath);

                if (TryGetDestinationAvailableSpace(destinationPathToCheck, out var availableSpace, out var spaceMessage))
                {
                    await LogAsync(progress, $"Destination available space: {FileHelper.FormatBytes(availableSpace)}", LogLevel.INFO);

                    if (availableSpace < remoteTotalSize)
                    {
                        var errorMsg = $"Insufficient local destination space. Required: {FileHelper.FormatBytes(remoteTotalSize)}, Available: {FileHelper.FormatBytes(availableSpace)}.";
                        await LogAsync(progress, errorMsg, LogLevel.ERROR);
                        throw new IOException(errorMsg);
                    }
                }
                else
                {
                    await LogAsync(progress, $"Destination space check not available: {spaceMessage}", LogLevel.WARNING);
                }

                await LogAsync(progress, $"Downloading from SFTP: {sourceConfig.SFTPHost}{sourceConfig.SFTPPath}", LogLevel.INFO);

                await Task.Run(() => DownloadDirectory(client, sourceConfig.SFTPPath, localDestPath, progress));

                await LogAsync(progress, "SFTP download completed", LogLevel.SUCCESS);
            }
            catch (Exception ex)
            {
                await LogAsync(progress, $"SFTP download failed: {ex.Message}", LogLevel.ERROR);
                await LogAsync(progress, ex.ToString(), LogLevel.ERROR);
                throw; // Re-throw to propagate failure to outer sync logic
            }
            finally
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
        }

        private void DownloadDirectory(SftpClient client, string remotePath, string localPath, SyncProgress progress)
        {
            try
            {
                foreach (var entry in client.ListDirectory(remotePath))
                {
                    if (entry.Name == "." || entry.Name == "..") continue;

                    var localEntryPath = Path.Combine(localPath, entry.Name);
                    if (entry.IsDirectory)
                    {
                        Directory.CreateDirectory(localEntryPath);
                        DownloadDirectory(client, entry.FullName, localEntryPath, progress);
                    }
                    else
                    {
                        int attempts = 0;
                        bool success = false;
                        while (attempts < 3 && !success)
                        {
                            attempts++;
                            try
                            {
                                using var fileStream = File.Create(localEntryPath);
                                client.DownloadFile(entry.FullName, fileStream);
                                success = true;

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    progress.LogEntries.Add(new LogEntry
                                    {
                                        Timestamp = DateTime.Now,
                                        Level = LogLevel.INFO,
                                        Message = $"{DateTime.Now:HH:mm:ss} [INFO] Downloaded: {entry.Name} ({FileHelper.FormatBytes(entry.Attributes.Size)})"
                                    });
                                });
                            }
                            catch (Exception ex)
                            {
                                if (attempts >= 3)
                                {
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        progress.LogEntries.Add(new LogEntry
                                        {
                                            Timestamp = DateTime.Now,
                                            Level = LogLevel.ERROR,
                                            Message = $"{DateTime.Now:HH:mm:ss} [ERROR] Failed to download {entry.Name} after {attempts} attempts: {ex.Message}"
                                        });
                                    });
                                }
                                else
                                {
                                    System.Threading.Thread.Sleep(500);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.ERROR,
                        Message = $"{DateTime.Now:HH:mm:ss} [ERROR] Failed to list directory {remotePath}: {ex.Message}"
                    });
                });
            }
        }

        private (long totalSize, int totalFiles) GetRemoteFolderSize(SftpClient client, string remotePath)
        {
            long size = 0;
            int count = 0;
            try
            {
                foreach (var entry in client.ListDirectory(remotePath))
                {
                    if (entry.Name == "." || entry.Name == "..")
                        continue;

                    if (entry.IsDirectory)
                    {
                        var (subSize, subCount) = GetRemoteFolderSize(client, entry.FullName);
                        size += subSize;
                        count += subCount;
                    }
                    else
                    {
                        size += entry.Attributes.Size;
                        count++;
                    }
                }
            }
            catch
            {
                // ignore errors for size enumeration, we'll still proceed with best-effort
            }
            return (size, count);
        }

        private bool TryGetDestinationAvailableSpace(string destinationPath, out long availableBytes, out string message)
        {
            return FileHelper.TryGetDestinationAvailableSpace(destinationPath, out availableBytes, out message);
        }

        public async Task SyncAsync(string sourcePath, DestinationConfig config, SyncProgress progress)
        {
            using var client = new SftpClient(config.SFTPHost, config.SFTPPort, config.SFTPUser, config.SFTPPassword);
            await Task.Run(() => client.Connect());

            var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
            var remoteBasePath = config.SFTPPath;

            foreach (var file in files)
            {
                var relativePath = file.Substring(sourcePath.Length + 1).Replace("\\", "/");
                var remotePath = Path.Combine(remoteBasePath, relativePath).Replace("\\", "/");
                var remoteDir = Path.GetDirectoryName(remotePath)!.Replace("\\", "/");

                if (!client.Exists(remoteDir))
                {
                    client.CreateDirectory(remoteDir);
                }

                using var fileStream = File.OpenRead(file);
                client.UploadFile(fileStream, remotePath);
                await LogAsync(progress, $"Uploaded to SFTP: {relativePath} ({FileHelper.FormatBytes(fileStream.Length)})", LogLevel.INFO);
            }

            client.Disconnect();
        }

        public async Task<SyncResult> ValidateAsync(DestinationConfig config)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(config.SFTPHost) ||
                    string.IsNullOrWhiteSpace(config.SFTPUser) ||
                    string.IsNullOrWhiteSpace(config.SFTPPassword))
                {
                    return new SyncResult { IsSuccess = false, Message = "SFTP credentials are incomplete" };
                }

                using var client = new SftpClient(config.SFTPHost, config.SFTPPort, config.SFTPUser, config.SFTPPassword);
                await Task.Run(() => client.Connect());
                
                if (!client.IsConnected)
                {
                    return new SyncResult { IsSuccess = false, Message = "Failed to connect to SFTP server" };
                }

                var remotePath = config.SFTPPath;
                if (!client.Exists(remotePath))
                {
                    await LogAsync(new SyncProgress(), $"Remote path {remotePath} does not exist. It will be created during sync.", LogLevel.WARNING);
                }

                client.Disconnect();
                return new SyncResult { IsSuccess = true, Message = "SFTP connection validated successfully" };
            }
            catch (Exception ex)
            {
                return new SyncResult { IsSuccess = false, Message = $"SFTP validation failed: {ex.Message}" };
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