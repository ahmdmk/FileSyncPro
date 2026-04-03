#nullable enable
using FileSyncPro.Models;
using FileSyncPro.Utilities;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using LogLevel = FileSyncPro.Models.LogLevel;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace FileSyncPro.Services
{
    public class SharePointSyncService
    {
        // Microsoft Office - has WAM broker redirect registered
        private const string ClientId = "d3590ed6-52b3-4102-aeff-aad2292ab01c";
        private static HttpClient? _httpClient;
        private static string? _cachedHostname;
        private static IPublicClientApplication? _msalApp;

        public static void ResetAuth()
        {
            _httpClient?.Dispose();
            _httpClient = null;
            _cachedHostname = null;
            _msalApp = null;
        }

        private async Task<HttpClient> GetAuthenticatedClientAsync(string hostname)
        {
            if (_httpClient != null && _cachedHostname == hostname)
                return _httpClient;

            // Get window handle on UI thread for WAM broker
            IntPtr windowHandle = IntPtr.Zero;
            Application.Current.Dispatcher.Invoke(() =>
            {
                windowHandle = new WindowInteropHelper(Application.Current.MainWindow).Handle;
            });

            if (_msalApp == null)
            {
                _msalApp = PublicClientApplicationBuilder
                    .Create(ClientId)
                    .WithAuthority("https://login.microsoftonline.com/organizations")
                    .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
                    .WithParentActivityOrWindow(() => windowHandle)
                    .Build();
            }

            var scopes = new[] { $"https://{hostname}/AllSites.Read" };
            AuthenticationResult result;

            try
            {
                // Try silent auth first (uses cached token or Windows SSO)
                var accounts = await _msalApp.GetAccountsAsync();
                result = await _msalApp.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                    .ExecuteAsync();
            }
            catch (MsalUiRequiredException)
            {
                // Fall back to interactive (WAM popup)
                result = await _msalApp.AcquireTokenInteractive(scopes)
                    .ExecuteAsync();
            }

            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", result.AccessToken);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            _httpClient?.Dispose();
            _httpClient = client;
            _cachedHostname = hostname;

            return _httpClient;
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
            return $"{size:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Encode a server-relative path for use in SharePoint REST API URLs.
        /// Encodes each segment separately to preserve forward slashes.
        /// </summary>
        private string EncodeSharePointPath(string serverRelativePath)
        {
            return string.Join("/", serverRelativePath.Split('/')
                .Select(segment => Uri.EscapeDataString(segment)));
        }

        private string? GetQueryStringParameter(string query, string paramName)
        {
            if (string.IsNullOrEmpty(query)) return null;
            query = query.TrimStart('?');
            foreach (var pair in query.Split('&'))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && kv[0].Equals(paramName, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        private (string hostname, string sitePath, string libraryName, string folderPath) ParseSharePointUrl(string fullUrl)
        {
            var uri = new Uri(fullUrl);
            // Strip MCAS proxy suffix (.mcas.ms) to get the real SharePoint hostname
            var hostname = uri.Host;
            if (hostname.EndsWith(".mcas.ms", StringComparison.OrdinalIgnoreCase))
                hostname = hostname.Substring(0, hostname.Length - ".mcas.ms".Length);

            var path = uri.AbsolutePath.TrimStart('/');
            string libraryName = "";
            string folderPath = "";

            // Check if URL has query string with 'id' parameter (folder path)
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var idParam = GetQueryStringParameter(uri.Query, "id");
                if (!string.IsNullOrEmpty(idParam))
                {
                    var decodedPath = Uri.UnescapeDataString(idParam).TrimStart('/');
                    if (decodedPath.StartsWith("sites/", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = decodedPath.Split('/');
                        if (parts.Length >= 3)
                        {
                            libraryName = parts[2];
                            if (parts.Length > 3)
                                folderPath = string.Join("/", parts.Skip(3));
                        }
                    }
                }
            }

            // Extract just the site path (e.g., "sites/AP_DCC")
            string sitePath = "";
            if (path.StartsWith("sites/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = path.Split('/');
                if (parts.Length >= 2)
                    sitePath = $"{parts[0]}/{parts[1]}";
            }
            else if (path.Contains("/sites/"))
            {
                var sitesIndex = path.IndexOf("/sites/", StringComparison.OrdinalIgnoreCase);
                var afterSites = path.Substring(sitesIndex + 1);
                var parts = afterSites.Split('/');
                if (parts.Length >= 2)
                    sitePath = $"{parts[0]}/{parts[1]}";
            }
            else if (path.Contains("Shared") || path.Contains("Documents") || path.Contains("Forms") || path.Contains("_layouts"))
            {
                sitePath = "";
            }

            return (hostname, sitePath, libraryName, folderPath);
        }

        /// <summary>
        /// Build server-relative URL for the target folder.
        /// </summary>
        private string BuildServerRelativeUrl(string sitePath, string libraryName, string folderPath)
        {
            var url = "/";
            if (!string.IsNullOrWhiteSpace(sitePath))
                url += sitePath + "/";
            if (!string.IsNullOrWhiteSpace(libraryName))
                url += libraryName;
            if (!string.IsNullOrWhiteSpace(folderPath))
                url += "/" + folderPath;
            return url.TrimEnd('/');
        }

        /// <summary>
        /// Recursively calculate total size and file count of a SharePoint folder.
        /// </summary>
        private async Task<(long totalSize, int fileCount)> GetFolderSizeAsync(HttpClient client, string siteUrl, string folderServerRelativeUrl)
        {
            long totalSize = 0;
            int fileCount = 0;

            try
            {
                var encodedPath = EncodeSharePointPath(folderServerRelativeUrl);

                // Get files
                var filesUrl = $"{siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encodedPath}')/Files?$select=Length";
                var filesResponse = await client.GetAsync(filesUrl);
                if (filesResponse.IsSuccessStatusCode)
                {
                    var filesJson = JsonDocument.Parse(await filesResponse.Content.ReadAsStringAsync());
                    foreach (var file in filesJson.RootElement.GetProperty("value").EnumerateArray())
                    {
                        fileCount++;
                        if (file.TryGetProperty("Length", out var lengthProp))
                        {
                            if (long.TryParse(lengthProp.ToString(), out var size))
                                totalSize += size;
                        }
                    }
                }

                // Get subfolders
                var foldersUrl = $"{siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encodedPath}')/Folders?$select=Name,ServerRelativeUrl";
                var foldersResponse = await client.GetAsync(foldersUrl);
                if (foldersResponse.IsSuccessStatusCode)
                {
                    var foldersJson = JsonDocument.Parse(await foldersResponse.Content.ReadAsStringAsync());
                    foreach (var folder in foldersJson.RootElement.GetProperty("value").EnumerateArray())
                    {
                        var folderName = folder.GetProperty("Name").GetString()!;
                        if (folderName == "Forms" || folderName.StartsWith("_"))
                            continue;

                        var subFolderUrl = folder.GetProperty("ServerRelativeUrl").GetString()!;
                        var (subSize, subCount) = await GetFolderSizeAsync(client, siteUrl, subFolderUrl);
                        totalSize += subSize;
                        fileCount += subCount;
                    }
                }
            }
            catch { /* Ignore errors during size calculation */ }

            return (totalSize, fileCount);
        }

        /// <summary>
        /// Download files from SharePoint to local destination using SharePoint REST API.
        /// </summary>
        public async Task DownloadFromSharePointAsync(DestinationConfig config, string localDestinationPath, SyncProgress progress)
        {
            try
            {
                var (hostname, sitePath, libraryName, folderPath) = ParseSharePointUrl(config.SharePointUrl);
                var client = await GetAuthenticatedClientAsync(hostname);
                var siteUrl = $"https://{hostname}/{sitePath}";
                var serverRelativePath = BuildServerRelativeUrl(sitePath, libraryName, folderPath);

                if (!Directory.Exists(localDestinationPath))
                    Directory.CreateDirectory(localDestinationPath);

                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.INFO,
                    Message = $"Downloading from: {serverRelativePath}"
                });

                // Calculate total source size
                progress.CurrentOperation = "Calculating source size...";
                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.INFO,
                    Message = "Calculating source size..."
                });

                var (totalSize, fileCount) = await GetFolderSizeAsync(client, siteUrl, serverRelativePath);

                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.INFO,
                    Message = $"Source: {fileCount} files, Total size: {FormatBytes(totalSize)}"
                });

                // Calculate destination size
                long totalDestinationSize = 0;
                int destFileCount = 0;
                try
                {
                    var destFiles = Directory.GetFiles(localDestinationPath, "*", SearchOption.AllDirectories);
                    destFileCount = destFiles.Length;
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
                }
                catch (Exception ex)
                {
                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.WARNING,
                        Message = $"Failed to calculate destination size: {ex.Message}"
                    });
                }

                if (destFileCount > 0 || totalDestinationSize > 0)
                {
                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.INFO,
                        Message = $"Destination: {destFileCount} files, Total size: {FormatBytes(totalDestinationSize)}"
                    });
                }

                if (FileHelper.TryGetDestinationAvailableSpace(localDestinationPath, out var availableSpace, out var spaceMessage))
                {
                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.INFO,
                        Message = $"Destination available space: {FormatBytes(availableSpace)}"
                    });

                    if (availableSpace < totalSize)
                    {
                        var errorMsg = $"Insufficient destination space. Required: {FormatBytes(totalSize)}, Available: {FormatBytes(availableSpace)}.";
                        progress.LogEntries.Add(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = LogLevel.ERROR,
                            Message = errorMsg
                        });
                        throw new IOException(errorMsg);
                    }
                }
                else
                {
                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.WARNING,
                        Message = $"Destination free-space check skipped: {spaceMessage}"
                    });
                }

                progress.Percentage = 0;
                progress.CurrentOperation = $"Downloading 0 / {fileCount} files...";
                int[] downloadedCount = { 0 };
                long[] downloadedBytes = { 0 };
                var startTime = DateTime.Now;

                await DownloadFolderRecursiveAsync(client, siteUrl, serverRelativePath, localDestinationPath, progress,
                    fileCount, totalSize, downloadedCount, downloadedBytes, startTime);

                progress.Percentage = 100;
                progress.CurrentOperation = "Download complete!";

                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.SUCCESS,
                    Message = $"Download completed! {downloadedCount[0]} / {fileCount} files, {FormatBytes(downloadedBytes[0])} / {FormatBytes(totalSize)}, Duration: {(DateTime.Now - startTime):hh\\:mm\\:ss}"
                });
            }
            catch (Exception ex)
            {
                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.ERROR,
                    Message = $"Failed to download from SharePoint: {ex.Message}"
                });
                throw;
            }
        }

        private async Task DownloadFolderRecursiveAsync(HttpClient client, string siteUrl, string folderServerRelativeUrl, string localFolderPath, SyncProgress progress,
            int totalFiles, long totalSize, int[] downloadedCount, long[] downloadedBytes, DateTime startTime)
        {
            try
            {
                var encodedPath = EncodeSharePointPath(folderServerRelativeUrl);

                // Get files in folder
                var filesUrl = $"{siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encodedPath}')/Files?$select=Name,ServerRelativeUrl,Length";
                var filesResponse = await client.GetAsync(filesUrl);

                if (!filesResponse.IsSuccessStatusCode)
                {
                    var error = await filesResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to list files ({filesResponse.StatusCode}): {error}");
                }

                var filesJson = JsonDocument.Parse(await filesResponse.Content.ReadAsStringAsync());

                foreach (var file in filesJson.RootElement.GetProperty("value").EnumerateArray())
                {
                    var fileName = file.GetProperty("Name").GetString()!;
                    var fileServerRelativeUrl = file.GetProperty("ServerRelativeUrl").GetString()!;
                    long fileSize = 0;
                    if (file.TryGetProperty("Length", out var lengthProp))
                        long.TryParse(lengthProp.ToString(), out fileSize);

                    try
                    {
                        var sizeText = fileSize > 0 ? $" ({FormatBytes(fileSize)})" : "";
                        progress.CurrentOperation = $"Downloading {downloadedCount[0] + 1} / {totalFiles}: {fileName}";

                        progress.LogEntries.Add(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = LogLevel.INFO,
                            Message = $"[{downloadedCount[0] + 1}/{totalFiles}] Downloading: {fileName}{sizeText}"
                        });

                        var encodedFileUrl = EncodeSharePointPath(fileServerRelativeUrl);
                        var downloadUrl = $"{siteUrl}/_api/web/GetFileByServerRelativeUrl('{encodedFileUrl}')/$value";
                        // Use ResponseHeadersRead to stream large files without buffering in memory
                        var fileResponse = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        fileResponse.EnsureSuccessStatusCode();

                        var localFilePath = Path.Combine(localFolderPath, fileName);
                        using (var outputStream = File.Create(localFilePath))
                        using (var fileStream = await fileResponse.Content.ReadAsStreamAsync())
                        {
                            await fileStream.CopyToAsync(outputStream);
                        }

                        downloadedCount[0]++;
                        downloadedBytes[0] += fileSize;

                        // Update progress percentage and status
                        if (totalFiles > 0)
                            progress.Percentage = (double)downloadedCount[0] / totalFiles * 100;

                        var elapsed = DateTime.Now - startTime;
                        var eta = downloadedCount[0] > 0 && downloadedCount[0] < totalFiles
                            ? TimeSpan.FromSeconds(elapsed.TotalSeconds / downloadedCount[0] * (totalFiles - downloadedCount[0]))
                            : TimeSpan.Zero;

                        progress.CurrentOperation = $"{downloadedCount[0]} / {totalFiles} files ({FormatBytes(downloadedBytes[0])} / {FormatBytes(totalSize)}) | ETA: {eta:hh\\:mm\\:ss}";

                        progress.LogEntries.Add(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = LogLevel.SUCCESS,
                            Message = $"[{downloadedCount[0]}/{totalFiles}] Downloaded: {fileName}{sizeText}"
                        });
                    }
                    catch (Exception ex)
                    {
                        downloadedCount[0]++;
                        if (totalFiles > 0)
                            progress.Percentage = (double)downloadedCount[0] / totalFiles * 100;

                        progress.LogEntries.Add(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = LogLevel.ERROR,
                            Message = $"[{downloadedCount[0]}/{totalFiles}] Failed to download {fileName}: {ex.Message}"
                        });
                    }
                }

                // Get subfolders
                var foldersUrl = $"{siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encodedPath}')/Folders?$select=Name,ServerRelativeUrl";
                var foldersResponse = await client.GetAsync(foldersUrl);

                if (foldersResponse.IsSuccessStatusCode)
                {
                    var foldersJson = JsonDocument.Parse(await foldersResponse.Content.ReadAsStringAsync());

                    foreach (var folder in foldersJson.RootElement.GetProperty("value").EnumerateArray())
                    {
                        var folderName = folder.GetProperty("Name").GetString()!;

                        // Skip SharePoint system folders
                        if (folderName == "Forms" || folderName.StartsWith("_"))
                            continue;

                        var subFolderServerRelativeUrl = folder.GetProperty("ServerRelativeUrl").GetString()!;
                        var localSubFolder = Path.Combine(localFolderPath, folderName);
                        Directory.CreateDirectory(localSubFolder);

                        progress.LogEntries.Add(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = LogLevel.INFO,
                            Message = $"Processing folder: {folderName}"
                        });

                        await DownloadFolderRecursiveAsync(client, siteUrl, subFolderServerRelativeUrl, localSubFolder, progress,
                            totalFiles, totalSize, downloadedCount, downloadedBytes, startTime);
                    }
                }
            }
            catch (Exception ex)
            {
                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.ERROR,
                    Message = $"Error processing folder: {ex.Message}"
                });
                throw;
            }
        }

        /// <summary>
        /// Upload files to SharePoint using SharePoint REST API.
        /// </summary>
        public async Task SyncAsync(string sourcePath, DestinationConfig config, SyncProgress progress)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            {
                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.ERROR,
                    Message = "Invalid source path for SharePoint sync."
                });
                return;
            }

            var (hostname, sitePath, libraryName, folderPath) = ParseSharePointUrl(config.SharePointUrl);
            var client = await GetAuthenticatedClientAsync(hostname);
            var siteUrl = $"https://{hostname}/{sitePath}";
            var targetFolder = BuildServerRelativeUrl(sitePath, libraryName, folderPath);

            var files = Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                try
                {
                    var relativePath = Path.GetRelativePath(sourcePath, file).Replace('\\', '/');
                    var fileName = Path.GetFileName(file);
                    var uploadFolder = targetFolder;

                    var relativeDir = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(relativeDir))
                        uploadFolder = $"{targetFolder}/{relativeDir}";

                    var encodedFolder = EncodeSharePointPath(uploadFolder);
                    var encodedFileName = Uri.EscapeDataString(fileName);
                    var uploadUrl = $"{siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encodedFolder}')/Files/add(url='{encodedFileName}',overwrite=true)";

                    using var fileStream = File.OpenRead(file);
                    using var content = new StreamContent(fileStream);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                    var response = await client.PostAsync(uploadUrl, content);
                    response.EnsureSuccessStatusCode();

                    long fileSize = new FileInfo(file).Length;
                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.SUCCESS,
                        Message = $"Uploaded: {relativePath} ({FileHelper.FormatBytes(fileSize)})"
                    });
                }
                catch (Exception ex)
                {
                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.ERROR,
                        Message = $"Failed to upload {Path.GetFileName(file)}: {ex.Message}"
                    });
                }
            }
        }

        /// <summary>
        /// Validate SharePoint connection using SharePoint REST API.
        /// </summary>
        public async Task<SyncResult> ValidateAsync(DestinationConfig config)
        {
            try
            {
                var (hostname, sitePath, libraryName, folderPath) = ParseSharePointUrl(config.SharePointUrl);
                var client = await GetAuthenticatedClientAsync(hostname);
                var siteUrl = $"https://{hostname}/{sitePath}";

                var response = await client.GetAsync($"{siteUrl}/_api/web?$select=Title,Url");
                response.EnsureSuccessStatusCode();

                var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var siteTitle = json.RootElement.GetProperty("Title").GetString();

                var message = $"Connected to: {siteTitle}\nSite: {siteUrl}";
                if (!string.IsNullOrWhiteSpace(libraryName))
                    message += $"\nLibrary: {libraryName}";
                if (!string.IsNullOrWhiteSpace(folderPath))
                    message += $"\nFolder: {folderPath}";

                return new SyncResult { IsSuccess = true, Message = message };
            }
            catch (Exception ex)
            {
                var (hostname, sitePath, _, _) = ParseSharePointUrl(config.SharePointUrl);

                return new SyncResult
                {
                    IsSuccess = false,
                    Message = $"Failed to connect to SharePoint.\n" +
                              $"Site: https://{hostname}/{sitePath}\n" +
                              $"Error: {ex.Message}"
                };
            }
        }
    }
}
