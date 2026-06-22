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
using System.Collections.Generic;
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

        // Access tokens expire after ~1 hour. A long-running sync outlives the token, after
        // which every request returns 401 Unauthorized. This silently re-acquires a fresh
        // token (MSAL uses its cached refresh token — no UI) and updates the shared client's
        // Authorization header in place. Returns false if a silent refresh isn't possible.
        private async Task<bool> TryRefreshTokenAsync()
        {
            try
            {
                if (_msalApp == null || _httpClient == null || _cachedHostname == null)
                    return false;

                var scopes = new[] { $"https://{_cachedHostname}/AllSites.Read" };
                var accounts = await _msalApp.GetAccountsAsync();
                var result = await _msalApp.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                    .ExecuteAsync();

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", result.AccessToken);
                return true;
            }
            catch
            {
                return false;
            }
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

        // Extract the library root server-relative URL from any path within it.
        // e.g. /sites/AP_DCC/Shared Documents/Folder/Sub → /sites/AP_DCC/Shared Documents
        private string ExtractLibraryRootUrl(string serverRelativePath)
        {
            var parts = serverRelativePath.TrimStart('/').Split('/');
            int libraryIndex = parts[0].Equals("sites", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            if (parts.Length > libraryIndex)
                return "/" + string.Join("/", parts.Take(libraryIndex + 1));
            return "/" + string.Join("/", parts);
        }

        // Enumerate the DIRECT children (files + subfolders) of one folder.
        //
        // We use RenderListDataAsStream — the same endpoint the SharePoint web UI uses to
        // browse large libraries — instead of any of these throttling alternatives:
        //   * GetList('…')/items?$filter=FileDirRef eq '…'  → filters a non-indexed column,
        //     trips the list view threshold once the LIBRARY exceeds 5,000 items.
        //   * GetFolderByServerRelativeUrl('…')/Files        → trips the threshold once a
        //     SINGLE FOLDER exceeds 5,000 items (its paging is not index-backed).
        //
        // RenderListDataAsStream is immune because the CAML view below:
        //   * scopes to the folder via FolderServerRelativeUrl (only direct children),
        //   * orders by ID, which is ALWAYS indexed, and
        //   * pages with <RowLimit Paged='TRUE'>, following NextHref each round.
        // This bypasses the threshold even for folders holding tens of thousands of items.
        private async Task<List<Dictionary<string, string>>> RenderFolderRowsAsync(
            HttpClient client, string siteUrl, string libraryRootUrl, string folderServerRelativeUrl)
        {
            var rows = new List<Dictionary<string, string>>();
            var encodedListUrl = EncodeSharePointPath(libraryRootUrl);
            var baseUrl = $"{siteUrl}/_api/web/GetList('{encodedListUrl}')/RenderListDataAsStream";
            const string viewXml =
                "<View><Query><OrderBy><FieldRef Name='ID' Ascending='TRUE'/></OrderBy></Query>" +
                "<ViewFields><FieldRef Name='FileLeafRef'/><FieldRef Name='FileRef'/>" +
                "<FieldRef Name='FSObjType'/><FieldRef Name='File_x0020_Size'/></ViewFields>" +
                "<RowLimit Paged='TRUE'>5000</RowLimit></View>";

            string? nextHref = null;
            do
            {
                // RenderOptions 2 = ListData (returns Row[] and NextHref for paging).
                var payload = new
                {
                    parameters = new
                    {
                        RenderOptions = 2,
                        ViewXml = viewXml,
                        FolderServerRelativeUrl = folderServerRelativeUrl
                    }
                };
                var url = baseUrl + (nextHref ?? "");
                var body = JsonSerializer.Serialize(payload);

                HttpResponseMessage response;
                bool refreshed = false;
                while (true)
                {
                    // Fresh content each attempt — a StringContent can only be sent once.
                    using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                    response = await client.PostAsync(url, content);
                    // On token expiry, refresh once and retry the same request.
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        && !refreshed && await TryRefreshTokenAsync())
                    {
                        refreshed = true;
                        response.Dispose();
                        continue;
                    }
                    break;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    response.Dispose();
                    throw new Exception($"Failed to list files ({response.StatusCode}): {error}");
                }

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                if (root.TryGetProperty("Row", out var rowArr) && rowArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in rowArr.EnumerateArray())
                    {
                        var dict = new Dictionary<string, string>();
                        foreach (var prop in item.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.String)
                                dict[prop.Name] = prop.Value.GetString() ?? "";
                        }
                        rows.Add(dict);
                    }
                }
                nextHref = root.TryGetProperty("NextHref", out var nh) && nh.ValueKind == JsonValueKind.String
                    ? nh.GetString() : null;
            } while (!string.IsNullOrEmpty(nextHref));

            return rows;
        }

        // List files directly inside a folder. FSObjType "0" = file.
        private async Task<List<(string Name, string ServerRelativeUrl, long Size)>> ListFilesInFolderAsync(
            HttpClient client, string siteUrl, string libraryRootUrl, string folderServerRelativeUrl)
        {
            var results = new List<(string Name, string ServerRelativeUrl, long Size)>();
            foreach (var row in await RenderFolderRowsAsync(client, siteUrl, libraryRootUrl, folderServerRelativeUrl))
            {
                if (!row.TryGetValue("FSObjType", out var type) || type != "0") continue;
                var name = row.TryGetValue("FileLeafRef", out var n) ? n : "";
                var url = row.TryGetValue("FileRef", out var u) ? u : "";
                long size = 0;
                if (row.TryGetValue("File_x0020_Size", out var s))
                    long.TryParse(s, out size);
                if (!string.IsNullOrEmpty(name))
                    results.Add((name, url, size));
            }
            return results;
        }

        // List direct subfolders of a folder. FSObjType "1" = folder.
        private async Task<List<(string Name, string ServerRelativeUrl)>> ListSubfoldersInFolderAsync(
            HttpClient client, string siteUrl, string libraryRootUrl, string folderServerRelativeUrl)
        {
            var results = new List<(string Name, string ServerRelativeUrl)>();
            foreach (var row in await RenderFolderRowsAsync(client, siteUrl, libraryRootUrl, folderServerRelativeUrl))
            {
                if (!row.TryGetValue("FSObjType", out var type) || type != "1") continue;
                var name = row.TryGetValue("FileLeafRef", out var n) ? n : "";
                var url = row.TryGetValue("FileRef", out var u) ? u : "";
                if (!string.IsNullOrEmpty(name) && name != "Forms" && !name.StartsWith("_"))
                    results.Add((name, url));
            }
            return results;
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
                var libraryRootUrl = ExtractLibraryRootUrl(folderServerRelativeUrl);

                var files = await ListFilesInFolderAsync(client, siteUrl, libraryRootUrl, folderServerRelativeUrl);
                foreach (var (_, _, size) in files)
                {
                    fileCount++;
                    totalSize += size;
                }

                var subfolders = await ListSubfoldersInFolderAsync(client, siteUrl, libraryRootUrl, folderServerRelativeUrl);
                foreach (var (_, subFolderUrl) in subfolders)
                {
                    var (subSize, subCount) = await GetFolderSizeAsync(client, siteUrl, subFolderUrl);
                    totalSize += subSize;
                    fileCount += subCount;
                }
            }
            catch { /* Ignore errors during size calculation */ }

            return (totalSize, fileCount);
        }

        /// <summary>
        /// Download files from SharePoint to local destination using SharePoint REST API.
        /// </summary>
        public async Task<List<FailedSyncItem>> DownloadFromSharePointAsync(DestinationConfig config, string localDestinationPath, SyncProgress progress)
        {
            var failures = new List<FailedSyncItem>();
            try
            {
                var (hostname, sitePath, libraryName, folderPath) = ParseSharePointUrl(config.SharePointUrl);
                var client = await GetAuthenticatedClientAsync(hostname);
                var siteUrl = $"https://{hostname}/{sitePath}";
                var serverRelativePath = BuildServerRelativeUrl(sitePath, libraryName, folderPath);

                if (!Directory.Exists(localDestinationPath))
                    Directory.CreateDirectory(localDestinationPath);

                await LogAsync(progress, $"Downloading from: {serverRelativePath}", LogLevel.INFO);

                // Calculate total source size
                progress.CurrentOperation = "Calculating source size...";
                await LogAsync(progress, "Calculating source size...", LogLevel.INFO);

                var (totalSize, fileCount) = await GetFolderSizeAsync(client, siteUrl, serverRelativePath);

                await LogAsync(progress, $"Source: {fileCount} files, Total size: {FileHelper.FormatBytes(totalSize)}", LogLevel.INFO);

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
                    await LogAsync(progress, $"Failed to calculate destination size: {ex.Message}", LogLevel.WARNING);
                }

                if (destFileCount > 0 || totalDestinationSize > 0)
                {
                    await LogAsync(progress, $"Destination: {destFileCount} files, Total size: {FileHelper.FormatBytes(totalDestinationSize)}", LogLevel.INFO);
                }

                if (FileHelper.TryGetDestinationAvailableSpace(localDestinationPath, out var availableSpace, out var spaceMessage))
                {
                    await LogAsync(progress, $"Destination available space: {FileHelper.FormatBytes(availableSpace)}", LogLevel.INFO);

                    if (availableSpace < totalSize)
                    {
                        var errorMsg = $"Insufficient destination space. Required: {FileHelper.FormatBytes(totalSize)}, Available: {FileHelper.FormatBytes(availableSpace)}.";
                        await LogAsync(progress, errorMsg, LogLevel.ERROR);
                        throw new IOException(errorMsg);
                    }
                }
                else
                {
                    await LogAsync(progress, $"Destination free-space check skipped: {spaceMessage}", LogLevel.WARNING);
                }

                progress.Percentage = 0;
                progress.CurrentOperation = $"Downloading 0 / {fileCount} files...";
                int[] downloadedCount = { 0 };
                long[] downloadedBytes = { 0 };
                var startTime = DateTime.Now;

                failures = await DownloadFolderRecursiveAsync(client, siteUrl, serverRelativePath, localDestinationPath, progress,
                    fileCount, totalSize, downloadedCount, downloadedBytes, startTime);

                progress.Percentage = 100;
                progress.CurrentOperation = "Download complete!";

                await LogAsync(progress, $"Download completed! {downloadedCount[0]} / {fileCount} files, {FileHelper.FormatBytes(downloadedBytes[0])} / {FileHelper.FormatBytes(totalSize)}, Duration: {(DateTime.Now - startTime):hh\\:mm\\:ss}", LogLevel.SUCCESS);
                return failures;
            }
            catch (Exception ex)
            {
                await LogAsync(progress, $"Failed to download from SharePoint: {ex.Message}", LogLevel.ERROR);
                throw;
            }
        }

        private async Task<List<FailedSyncItem>> DownloadFolderRecursiveAsync(HttpClient client, string siteUrl, string folderServerRelativeUrl, string localFolderPath, SyncProgress progress,
            int totalFiles, long totalSize, int[] downloadedCount, long[] downloadedBytes, DateTime startTime)
        {
            var failures = new List<FailedSyncItem>();
            try
            {
                var libraryRootUrl = ExtractLibraryRootUrl(folderServerRelativeUrl);

                var files = await ListFilesInFolderAsync(client, siteUrl, libraryRootUrl, folderServerRelativeUrl);

                foreach (var (fileName, fileServerRelativeUrl, fileSize) in files)
                {
                    var localFilePath = Path.Combine(localFolderPath, fileName);
                    try
                    {
                        var sizeText = fileSize > 0 ? $" ({FileHelper.FormatBytes(fileSize)})" : "";

                        if (File.Exists(localFilePath))
                        {
                            downloadedCount[0]++;
                            downloadedBytes[0] += fileSize;
                            if (totalFiles > 0)
                                progress.Percentage = (double)downloadedCount[0] / totalFiles * 100;
                            await LogAsync(progress, $"[{downloadedCount[0]}/{totalFiles}] Skipped (already exists): {fileName}{sizeText}", LogLevel.INFO);
                            continue;
                        }

                        progress.CurrentOperation = $"Downloading {downloadedCount[0] + 1} / {totalFiles}: {fileName}";
                        await LogAsync(progress, $"[{downloadedCount[0] + 1}/{totalFiles}] Downloading: {fileName}{sizeText}", LogLevel.INFO);

                        var encodedFileUrl = EncodeSharePointPath(fileServerRelativeUrl);
                        var downloadUrl = $"{siteUrl}/_api/web/GetFileByServerRelativeUrl('{encodedFileUrl}')/$value";
                        // Use ResponseHeadersRead to stream large files without buffering in memory
                        var fileResponse = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        // Token may have expired mid-sync (~1h lifetime): refresh once and retry.
                        if (fileResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized
                            && await TryRefreshTokenAsync())
                        {
                            fileResponse.Dispose();
                            fileResponse = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        }
                        fileResponse.EnsureSuccessStatusCode();

                        using (var outputStream = File.Create(localFilePath))
                        using (var fileStream = await fileResponse.Content.ReadAsStreamAsync())
                        {
                            await fileStream.CopyToAsync(outputStream);
                        }

                        downloadedCount[0]++;
                        downloadedBytes[0] += fileSize;
                        if (totalFiles > 0)
                            progress.Percentage = (double)downloadedCount[0] / totalFiles * 100;

                        var elapsed = DateTime.Now - startTime;
                        var eta = downloadedCount[0] > 0 && downloadedCount[0] < totalFiles
                            ? TimeSpan.FromSeconds(elapsed.TotalSeconds / downloadedCount[0] * (totalFiles - downloadedCount[0]))
                            : TimeSpan.Zero;
                        progress.CurrentOperation = $"{downloadedCount[0]} / {totalFiles} files ({FileHelper.FormatBytes(downloadedBytes[0])} / {FileHelper.FormatBytes(totalSize)}) | ETA: {eta:hh\\:mm\\:ss}";
                        await LogAsync(progress, $"[{downloadedCount[0]}/{totalFiles}] Downloaded: {fileName}{sizeText}", LogLevel.SUCCESS);
                    }
                    catch (Exception ex)
                    {
                        downloadedCount[0]++;
                        if (totalFiles > 0)
                            progress.Percentage = (double)downloadedCount[0] / totalFiles * 100;
                        await LogAsync(progress, $"[{downloadedCount[0]}/{totalFiles}] Failed to download {fileName}: {ex.Message}", LogLevel.ERROR);
                        failures.Add(new FailedSyncItem
                        {
                            FilePath = localFilePath,
                            RelativePath = fileServerRelativeUrl,
                            ErrorMessage = ex.Message
                        });
                    }
                }

                var subfolders = await ListSubfoldersInFolderAsync(client, siteUrl, libraryRootUrl, folderServerRelativeUrl);
                foreach (var (folderName, subFolderServerRelativeUrl) in subfolders)
                {
                    var localSubFolder = Path.Combine(localFolderPath, folderName);
                    Directory.CreateDirectory(localSubFolder);
                    await LogAsync(progress, $"Processing folder: {folderName}", LogLevel.INFO);
                    failures.AddRange(await DownloadFolderRecursiveAsync(client, siteUrl, subFolderServerRelativeUrl, localSubFolder, progress,
                        totalFiles, totalSize, downloadedCount, downloadedBytes, startTime));
                }
            }
            catch (Exception ex)
            {
                await LogAsync(progress, $"Error processing folder: {ex.Message}", LogLevel.ERROR);
                throw;
            }
            return failures;
        }

        /// <summary>
        /// Upload files to SharePoint using SharePoint REST API.
        /// </summary>
        public async Task<List<FailedSyncItem>> SyncAsync(string sourcePath, DestinationConfig config, SyncProgress progress)
        {
            var failures = new List<FailedSyncItem>();
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            {
                await LogAsync(progress, "Invalid source path for SharePoint sync.", LogLevel.ERROR);
                return failures;
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
                    await UploadFileAsync(config, file, relativePath, progress); // Pass progress for logging
                }
                catch (Exception ex)
                {
                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.ERROR,
                        Message = $"Failed to upload {Path.GetFileName(file)}: {ex.Message}"
                    });
                    failures.Add(new FailedSyncItem
                    {
                        FilePath = file,
                        RelativePath = Path.GetRelativePath(sourcePath, file),
                        ErrorMessage = ex.Message
                    });
                }
            }
            return failures;
        }

        public async Task UploadFileAsync(DestinationConfig config, string localFilePath, string relativePath, SyncProgress? progress = null)
        {
            var (hostname, sitePath, libraryName, folderPath) = ParseSharePointUrl(config.SharePointUrl);
            var client = await GetAuthenticatedClientAsync(hostname);
            var siteUrl = $"https://{hostname}/{sitePath}";
            var targetFolder = BuildServerRelativeUrl(sitePath, libraryName, folderPath);

            var fileName = Path.GetFileName(localFilePath);
            var uploadFolder = targetFolder;

            var normalizedRelativePath = relativePath.Replace('\\', '/');
            var relativeDir = Path.GetDirectoryName(normalizedRelativePath)?.Replace('\\', '/');
            
            if (!string.IsNullOrWhiteSpace(relativeDir))
                uploadFolder = $"{targetFolder}/{relativeDir}";

            var encodedFolder = EncodeSharePointPath(uploadFolder);
            var encodedFileName = Uri.EscapeDataString(fileName);
            var uploadUrl = $"{siteUrl}/_api/web/GetFolderByServerRelativeUrl('{encodedFolder}')/Files/add(url='{encodedFileName}',overwrite=true)";

            using var fileStream = File.OpenRead(localFilePath);
            using var content = new StreamContent(fileStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var response = await client.PostAsync(uploadUrl, content);
            response.EnsureSuccessStatusCode(); // Throws if not success

            if (progress != null)
                await LogAsync(progress, $"Uploaded: {relativePath} ({FileHelper.FormatBytes(new FileInfo(localFilePath).Length)})", LogLevel.SUCCESS);
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
