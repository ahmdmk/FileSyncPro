using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileSyncPro.Utilities
{
    public static class FileHelper
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailable,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        public static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        public static bool IsDirectoryWritable(string dirPath)
        {
            try
            {
                using var fs = File.Create(Path.Combine(dirPath, Path.GetRandomFileName()), 1, FileOptions.DeleteOnClose);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public class SavedSftpCredentials
        {
            public bool RememberSource { get; set; }
            public string SourceHost { get; set; } = string.Empty;
            public int SourcePort { get; set; } = 22;
            public string SourceUser { get; set; } = string.Empty;
            public string SourcePassword { get; set; } = string.Empty;

            public bool RememberDestination { get; set; }
            public string DestinationHost { get; set; } = string.Empty;
            public int DestinationPort { get; set; } = 22;
            public string DestinationUser { get; set; } = string.Empty;
            public string DestinationPassword { get; set; } = string.Empty;
        }

        private static string GetCredentialFilePath()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncPro");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            return Path.Combine(folder, "sftp_credentials.json");
        }

        public static void SaveSftpCredentials(SavedSftpCredentials credentials)
        {
            try
            {
                var path = GetCredentialFilePath();
                var json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignore persistence failures (non-critical)
            }
        }

        public static SavedSftpCredentials? LoadSftpCredentials()
        {
            try
            {
                var path = GetCredentialFilePath();
                if (!File.Exists(path))
                    return null;

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<SavedSftpCredentials>(json);
            }
            catch
            {
                return null;
            }
        }

        public static bool TryGetDestinationAvailableSpace(string destinationPath, out long availableBytes, out string message)
        {
            availableBytes = 0;
            message = string.Empty;

            try
            {
                var root = Path.GetPathRoot(destinationPath);
                if (string.IsNullOrWhiteSpace(root))
                {
                    message = "Unable to evaluate destination root path.";
                    return false;
                }

                if (root.StartsWith("\\\\"))
                {
                    // UNC network path: use Win32 API to avoid DriveInfo limitations.
                    if (GetDiskFreeSpaceEx(destinationPath, out var freeBytes, out _, out _))
                    {
                        availableBytes = (long)Math.Min((ulong)long.MaxValue, freeBytes);
                        return true;
                    }

                    message = "Network destination free space check not supported via DriveInfo; GetDiskFreeSpaceEx failed.";
                    return false;
                }

                var drive = new DriveInfo(root);
                availableBytes = drive.AvailableFreeSpace;
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to read destination free space: {ex.Message}";
                return false;
            }
        }
    }
}