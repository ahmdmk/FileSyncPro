#nullable enable
using Renci.SshNet;
using FileSyncPro.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace FileSyncPro.Services
{
    public class SFTPSyncService
    {
        public async Task DownloadFromSFTPAsync(DestinationConfig sourceConfig, string localDestPath, SyncProgress progress)
        {
            using var client = new SftpClient(sourceConfig.SFTPHost, sourceConfig.SFTPPort, sourceConfig.SFTPUser, sourceConfig.SFTPPassword);
            await Task.Run(() => client.Connect());

            Directory.CreateDirectory(localDestPath);
            await LogAsync(progress, $"Downloading from SFTP: {sourceConfig.SFTPHost}{sourceConfig.SFTPPath}", LogLevel.INFO);

            await Task.Run(() => DownloadDirectory(client, sourceConfig.SFTPPath, localDestPath, progress));

            client.Disconnect();
            await LogAsync(progress, "SFTP download completed", LogLevel.SUCCESS);
        }

        private void DownloadDirectory(SftpClient client, string remotePath, string localPath, SyncProgress progress)
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
                    using var fileStream = File.Create(localEntryPath);
                    client.DownloadFile(entry.FullName, fileStream);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        progress.LogEntries.Add(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = LogLevel.INFO,
                            Message = $"{DateTime.Now:HH:mm:ss} [INFO] Downloaded: {entry.Name}"
                        });
                    });
                }
            }
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
                await LogAsync(progress, $"Uploaded to SFTP: {relativePath}", LogLevel.INFO);
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