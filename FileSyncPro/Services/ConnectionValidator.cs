#nullable enable
using FileSyncPro.Models;
using System.IO;
using System.Net;
using System;
using System.Threading.Tasks;

namespace FileSyncPro.Services
{
    public class ConnectionValidator
    {
        public async Task<ValidationResult> ValidateSftpConnection(SftpConnection connection)
        {
            try
            {
                using (var sftp = new Renci.SshNet.SftpClient(connection.Host, connection.Port, connection.Username, connection.Password))
                {
                    sftp.Connect();
                    
                    // Check if path exists
                    if (!sftp.Exists(connection.Path))
                    {
                        return new ValidationResult { IsValid = false, ErrorMessage = $"SFTP path does not exist: {connection.Path}" };
                    }
                    
                    sftp.Disconnect();
                    return new ValidationResult { IsValid = true };
                }
            }
            catch (Exception ex)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = $"SFTP connection failed: {ex.Message}" };
            }
        }

        public async Task<ValidationResult> ValidateSharePointConnection(SharePointConnection connection)
        {
            try
            {
                // For SharePoint Online, we would typically use Microsoft Graph API with app-only permissions
                // This simplified version only validates the URL format
                
                if (string.IsNullOrWhiteSpace(connection.SiteUrl))
                {
                    return new ValidationResult { IsValid = false, ErrorMessage = "SharePoint site URL is required" };
                }
                
                if (!Uri.TryCreate(connection.SiteUrl, UriKind.Absolute, out _))
                {
                    return new ValidationResult { IsValid = false, ErrorMessage = "Invalid SharePoint site URL format" };
                }
                
                // Simulate successful validation
                await Task.Delay(100);
                return new ValidationResult { IsValid = true, ErrorMessage = "SharePoint connection validated (simulated). Actual implementation would use Microsoft Graph API." };
            }
            catch (Exception ex)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = $"SharePoint connection validation failed: {ex.Message}" };
            }
        }

        public Task<ValidationResult> ValidateLocalPath(LocalPathConnection connection)
        {
            try
            {
                if (!Directory.Exists(connection.Path))
                {
                    return Task.FromResult(new ValidationResult { IsValid = false, ErrorMessage = $"Local path does not exist: {connection.Path}" });
                }
                
                return Task.FromResult(new ValidationResult { IsValid = true });
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ValidationResult { IsValid = false, ErrorMessage = $"Local path validation failed: {ex.Message}" });
            }
        }

        public ValidationResult ValidateDestinationWriteAccess(string path)
        {
            try
            {
                // Test if we can write to the directory
                var testFile = Path.Combine(path, ".write_test");
                
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                
                return new ValidationResult { IsValid = true };
            }
            catch (Exception ex)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = $"No write access to destination: {ex.Message}" };
            }
        }

        public ValidationResult CheckStorageSpace(string sourcePath, string destinationPath)
        {
            try
            {
                // Calculate source size
                long sourceSize = GetDirectorySize(sourcePath);
                
                // Get available space on destination drive
                var destDrive = new DriveInfo(new DirectoryInfo(destinationPath).Root.FullName);
                long availableSpace = destDrive.AvailableFreeSpace;
                
                if (availableSpace < sourceSize)
                {
                    return new ValidationResult 
                    { 
                        IsValid = false, 
                        ErrorMessage = $"Insufficient storage space. Required: {FormatBytes(sourceSize)}, Available: {FormatBytes(availableSpace)}" 
                    };
                }
                
                return new ValidationResult { IsValid = true };
            }
            catch (Exception ex)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = $"Storage space check failed: {ex.Message}" };
            }
        }

        private long GetDirectorySize(string directoryPath)
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            long size = 0;
            
            // Add file sizes
            foreach (var fileInfo in dirInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                size += fileInfo.Length;
            }
            
            return size;
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
    }
}