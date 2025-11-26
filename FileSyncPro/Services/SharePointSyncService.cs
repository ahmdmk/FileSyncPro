using FileSyncPro.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Azure.Identity;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace FileSyncPro.Services
{
    public class SharePointSyncService
    {
        // Public Azure AD app for Microsoft Graph - works for any Microsoft 365 user
        private const string ClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e"; // Microsoft Graph PowerShell
        private static GraphServiceClient? _graphClient;

        /// <summary>
        /// Get authenticated Graph client using interactive browser authentication.
        /// Works with ANY user who has SharePoint access - no custom Azure app registration required.
        /// </summary>
        private async Task<GraphServiceClient> GetGraphClientAsync()
        {
            if (_graphClient != null)
                return _graphClient;

            var scopes = new[] { "https://graph.microsoft.com/.default" };

            var options = new InteractiveBrowserCredentialOptions
            {
                ClientId = ClientId,
                TenantId = "organizations", // Works with any organization
                RedirectUri = new Uri("http://localhost")
            };

            var interactiveCredential = new InteractiveBrowserCredential(options);
            _graphClient = new GraphServiceClient(interactiveCredential, scopes);

            return _graphClient;
        }

        /// <summary>
        /// Synchronize files from sourcePath to SharePoint using Microsoft Graph.
        /// Preserves folder structure from the source.
        /// </summary>
        public async Task SyncAsync(string sourcePath, DestinationConfig config, SyncProgress progress)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            {
                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.ERROR,
                    Message = "❌ Invalid source path for SharePoint sync."
                });
                return;
            }

            var files = System.IO.Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                try
                {
                    // Calculate relative path to preserve folder structure
                    var relativePath = Path.GetRelativePath(sourcePath, file);

                    await UploadFileAsync(config, file, relativePath);

                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.SUCCESS,
                        Message = $"✅ Uploaded: {relativePath}"
                    });
                }
                catch (Exception ex)
                {
                    progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.ERROR,
                        Message = $"❌ Failed to upload {Path.GetFileName(file)}: {ex.Message}"
                    });
                }
            }
        }

        /// <summary>
        /// Helper method to parse query string
        /// </summary>
        private string GetQueryStringParameter(string query, string paramName)
        {
            if (string.IsNullOrEmpty(query))
                return null;

            query = query.TrimStart('?');
            var pairs = query.Split('&');

            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=');
                if (keyValue.Length == 2 && keyValue[0].Equals(paramName, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(keyValue[1]);
                }
            }

            return null;
        }

        /// <summary>
        /// Helper method to extract site URL, library, and folder from full SharePoint path
        /// </summary>
        private (string hostname, string sitePath, string libraryName, string folderPath) ParseSharePointUrl(string fullUrl)
        {
            var uri = new Uri(fullUrl);
            var hostname = uri.Host;
            var path = uri.AbsolutePath.TrimStart('/');
            string libraryName = "";
            string folderPath = "";

            // Check if URL has query string with 'id' parameter (folder path)
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var idParam = GetQueryStringParameter(uri.Query, "id");

                if (!string.IsNullOrEmpty(idParam))
                {
                    // idParam example: /sites/OSS-EnggApps-DEV/Shared Documents/KT Videos
                    // Extract library and folder from this path
                    var decodedPath = Uri.UnescapeDataString(idParam).TrimStart('/');

                    // Find the site portion
                    if (decodedPath.StartsWith("sites/", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = decodedPath.Split('/');
                        if (parts.Length >= 4) // sites/sitename/library/folder...
                        {
                            libraryName = parts[2]; // "Shared Documents"
                            if (parts.Length > 3)
                            {
                                // Join remaining parts as folder path
                                folderPath = string.Join("/", parts.Skip(3));
                            }
                        }
                    }
                }
            }

            // Extract just the site path (e.g., "sites/OSS-EnggApps-DEV")
            string sitePath = path;
            if (path.StartsWith("sites/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = path.Split('/');
                if (parts.Length >= 2)
                {
                    sitePath = $"{parts[0]}/{parts[1]}";
                }
            }
            else if (path.Contains("/sites/"))
            {
                var sitesIndex = path.IndexOf("/sites/", StringComparison.OrdinalIgnoreCase);
                var afterSites = path.Substring(sitesIndex + 1);
                var parts = afterSites.Split('/');
                if (parts.Length >= 2)
                {
                    sitePath = $"{parts[0]}/{parts[1]}";
                }
            }
            else if (path.Contains("Shared") || path.Contains("Documents") || path.Contains("Forms") || path.Contains("_layouts"))
            {
                sitePath = "";
            }

            return (hostname, sitePath, libraryName, folderPath);
        }

        /// <summary>
        /// Upload a single file to SharePoint document library using Microsoft Graph.
        /// </summary>
        /// <param name="config">Destination configuration</param>
        /// <param name="localFilePath">Full local file path</param>
        /// <param name="relativePath">Relative path from source to preserve folder structure (optional)</param>
        public async Task UploadFileAsync(DestinationConfig config, string localFilePath, string relativePath = null)
        {
            try
            {
                var graphClient = await GetGraphClientAsync();

                // Parse SharePoint URL to get site path, library, and folder
                var (hostname, sitePath, parsedLibraryName, folderPath) = ParseSharePointUrl(config.SharePointUrl);

                // Get the site ID using the correct format
                string siteAddress;
                if (string.IsNullOrWhiteSpace(sitePath))
                {
                    siteAddress = $"{hostname}:/";
                }
                else
                {
                    siteAddress = $"{hostname}:/{sitePath}";
                }

                var site = await graphClient.Sites[siteAddress].GetAsync();

                if (site == null || string.IsNullOrEmpty(site.Id))
                {
                    throw new Exception("Could not find SharePoint site");
                }

                // Use parsed library name if available, otherwise use config or default
                string libraryName;
                if (!string.IsNullOrWhiteSpace(parsedLibraryName))
                {
                    libraryName = parsedLibraryName;
                }
                else if (!string.IsNullOrWhiteSpace(config.SharePointLibrary))
                {
                    libraryName = config.SharePointLibrary;
                }
                else
                {
                    libraryName = "Documents";
                }

                var drives = await graphClient.Sites[site.Id].Drives.GetAsync();

                // Try to find the drive by name with common variations
                Microsoft.Graph.Models.Drive targetDrive = null;

                // Common name mappings for SharePoint libraries
                var libraryNameVariations = new List<string> { libraryName };

                if (libraryName.Equals("Shared Documents", StringComparison.OrdinalIgnoreCase))
                {
                    libraryNameVariations.Add("Documents");
                }
                else if (libraryName.Equals("Documents", StringComparison.OrdinalIgnoreCase))
                {
                    libraryNameVariations.Add("Shared Documents");
                }

                foreach (var nameVariation in libraryNameVariations)
                {
                    targetDrive = drives?.Value?.FirstOrDefault(d =>
                        d.Name?.Equals(nameVariation, StringComparison.OrdinalIgnoreCase) == true);

                    if (targetDrive != null)
                    {
                        break;
                    }
                }

                if (targetDrive == null)
                {
                    // List available drives for debugging
                    var availableDrives = drives?.Value != null
                        ? string.Join(", ", drives.Value.Select(d => $"'{d.Name}'"))
                        : "none";
                    throw new Exception($"Document library '{libraryName}' not found. Available libraries: {availableDrives}");
                }

                // Upload the file
                using var fileStream = System.IO.File.OpenRead(localFilePath);

                // Build the upload path with folder structure preservation
                string uploadPath;
                if (!string.IsNullOrWhiteSpace(relativePath))
                {
                    // Use relative path to preserve folder structure, normalize path separators
                    var normalizedRelativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');

                    if (!string.IsNullOrWhiteSpace(folderPath))
                    {
                        // Combine SharePoint folder with relative path
                        uploadPath = $"{folderPath}/{normalizedRelativePath}";
                    }
                    else
                    {
                        uploadPath = normalizedRelativePath;
                    }
                }
                else
                {
                    // Fall back to just filename if no relative path provided
                    var fileName = Path.GetFileName(localFilePath);
                    uploadPath = string.IsNullOrWhiteSpace(folderPath)
                        ? fileName
                        : $"{folderPath}/{fileName}";
                }

                // Upload using Microsoft Graph API v5
                var driveItem = await graphClient.Drives[targetDrive.Id]
                    .Root
                    .ItemWithPath(uploadPath)
                    .Content
                    .PutAsync(fileStream);
            }
            catch (Exception ex)
            {
                throw new Exception($"Upload failed for {Path.GetFileName(localFilePath)}: {ex.Message}");
            }
        }

        /// <summary>
        /// Download files from SharePoint to local destination.
        /// </summary>
        public async Task DownloadFromSharePointAsync(DestinationConfig config, string localDestinationPath, SyncProgress progress)
        {
            try
            {
                var graphClient = await GetGraphClientAsync();

                // Parse SharePoint URL to get site path, library, and folder
                var (hostname, sitePath, parsedLibraryName, folderPath) = ParseSharePointUrl(config.SharePointUrl);

                // Get the site ID using the correct format
                string siteAddress;
                if (string.IsNullOrWhiteSpace(sitePath))
                {
                    siteAddress = $"{hostname}:/";
                }
                else
                {
                    siteAddress = $"{hostname}:/{sitePath}";
                }

                var site = await graphClient.Sites[siteAddress].GetAsync();

                if (site == null || string.IsNullOrEmpty(site.Id))
                {
                    throw new Exception("Could not find SharePoint site");
                }

                // Use parsed library name if available, otherwise use config or default
                string libraryName;
                if (!string.IsNullOrWhiteSpace(parsedLibraryName))
                {
                    libraryName = parsedLibraryName;
                }
                else if (!string.IsNullOrWhiteSpace(config.SharePointLibrary))
                {
                    libraryName = config.SharePointLibrary;
                }
                else
                {
                    libraryName = "Documents";
                }

                var drives = await graphClient.Sites[site.Id].Drives.GetAsync();

                // Try to find the drive by name with common variations
                Microsoft.Graph.Models.Drive targetDrive = null;

                // Common name mappings for SharePoint libraries
                var libraryNameVariations = new List<string> { libraryName };

                if (libraryName.Equals("Shared Documents", StringComparison.OrdinalIgnoreCase))
                {
                    libraryNameVariations.Add("Documents");
                }
                else if (libraryName.Equals("Documents", StringComparison.OrdinalIgnoreCase))
                {
                    libraryNameVariations.Add("Shared Documents");
                }

                foreach (var nameVariation in libraryNameVariations)
                {
                    targetDrive = drives?.Value?.FirstOrDefault(d =>
                        d.Name?.Equals(nameVariation, StringComparison.OrdinalIgnoreCase) == true);

                    if (targetDrive != null)
                    {
                        break;
                    }
                }

                if (targetDrive == null)
                {
                    var availableDrives = drives?.Value != null
                        ? string.Join(", ", drives.Value.Select(d => $"'{d.Name}'"))
                        : "none";
                    throw new Exception($"Document library '{libraryName}' not found. Available libraries: {availableDrives}");
                }

                // Create local destination directory if it doesn't exist
                if (!Directory.Exists(localDestinationPath))
                {
                    Directory.CreateDirectory(localDestinationPath);
                }

                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.INFO,
                    Message = $"Starting download from SharePoint library: {libraryName}"
                });

                // Download files recursively
                await DownloadFolderRecursiveAsync(graphClient, targetDrive.Id, folderPath, localDestinationPath, progress);

                progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.SUCCESS,
                    Message = "Download from SharePoint completed successfully!"
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

        /// <summary>
        /// Recursively download files from a SharePoint folder.
        /// </summary>
        private async Task DownloadFolderRecursiveAsync(GraphServiceClient graphClient, string driveId, string sharePointFolderPath, string localFolderPath, SyncProgress progress)
        {
            try
            {
                // Get items in the current folder
                Microsoft.Graph.Models.DriveItemCollectionResponse items;
                if (string.IsNullOrWhiteSpace(sharePointFolderPath))
                {
                    // Get root items
                    var rootItem = await graphClient.Drives[driveId].Root.GetAsync();
                    items = await graphClient.Drives[driveId].Items[rootItem.Id].Children.GetAsync();
                }
                else
                {
                    // Get items in specific folder
                    var folderItem = await graphClient.Drives[driveId].Root.ItemWithPath(sharePointFolderPath).GetAsync();
                    items = await graphClient.Drives[driveId].Items[folderItem.Id].Children.GetAsync();
                }

                if (items?.Value == null)
                    return;

                foreach (var item in items.Value)
                {
                    if (item.Folder != null)
                    {
                        // It's a folder - create local folder and recurse
                        var localSubFolderPath = Path.Combine(localFolderPath, item.Name);
                        Directory.CreateDirectory(localSubFolderPath);

                        var subFolderPath = string.IsNullOrWhiteSpace(sharePointFolderPath)
                            ? item.Name
                            : $"{sharePointFolderPath}/{item.Name}";

                        progress.LogEntries.Add(new LogEntry
                        {
                            Timestamp = DateTime.Now,
                            Level = LogLevel.INFO,
                            Message = $"📁 Processing folder: {item.Name}"
                        });

                        await DownloadFolderRecursiveAsync(graphClient, driveId, subFolderPath, localSubFolderPath, progress);
                    }
                    else if (item.File != null)
                    {
                        // It's a file - download it
                        var localFilePath = Path.Combine(localFolderPath, item.Name);

                        try
                        {
                            var fileStream = await graphClient.Drives[driveId].Items[item.Id].Content.GetAsync();

                            if (fileStream != null)
                            {
                                using (var outputStream = System.IO.File.Create(localFilePath))
                                {
                                    await fileStream.CopyToAsync(outputStream);
                                }

                                progress.LogEntries.Add(new LogEntry
                                {
                                    Timestamp = DateTime.Now,
                                    Level = LogLevel.SUCCESS,
                                    Message = $"✅ Downloaded: {item.Name}"
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            progress.LogEntries.Add(new LogEntry
                            {
                                Timestamp = DateTime.Now,
                                Level = LogLevel.ERROR,
                                Message = $"❌ Failed to download {item.Name}: {ex.Message}"
                            });
                        }
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
        /// Validate SharePoint destination using Microsoft Graph.
        /// </summary>
        public async Task<SyncResult> ValidateAsync(DestinationConfig config)
        {
            try
            {
                var graphClient = await GetGraphClientAsync();

                // Parse SharePoint URL
                var (hostname, sitePath, parsedLibraryName, folderPath) = ParseSharePointUrl(config.SharePointUrl);

                // Get the site ID using the correct format
                string siteAddress;
                if (string.IsNullOrWhiteSpace(sitePath))
                {
                    siteAddress = $"{hostname}:/";
                }
                else
                {
                    siteAddress = $"{hostname}:/{sitePath}";
                }

                // Try to access the site
                var site = await graphClient.Sites[siteAddress].GetAsync();

                if (site == null)
                {
                    return new SyncResult
                    {
                        IsSuccess = false,
                        Message = "❌ Could not access SharePoint site."
                    };
                }

                // Build success message with details
                var message = $"✅ Connected successfully to: {site.DisplayName ?? "SharePoint Site"}\nSite: {siteAddress}";
                if (!string.IsNullOrWhiteSpace(parsedLibraryName))
                {
                    message += $"\nLibrary: {parsedLibraryName}";
                }
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    message += $"\nFolder: {folderPath}";
                }

                return new SyncResult
                {
                    IsSuccess = true,
                    Message = message
                };
            }
            catch (Exception ex)
            {
                var (hostname, sitePath, parsedLibraryName, folderPath) = ParseSharePointUrl(config.SharePointUrl);
                var siteAddress = string.IsNullOrWhiteSpace(sitePath) ? $"{hostname}:/" : $"{hostname}:/{sitePath}";

                return new SyncResult
                {
                    IsSuccess = false,
                    Message = $"❌ Failed to connect to SharePoint.\n" +
                              $"Parsed site: {siteAddress}\n" +
                              $"Library: {parsedLibraryName ?? "Not specified"}\n" +
                              $"Folder: {folderPath ?? "Root"}\n" +
                              $"Error: {ex.Message}"
                };
            }
        }
    }
}
