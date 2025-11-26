using System.Security.Cryptography;
using System.IO;
using System;
using System.Collections.Generic;
using System.Collections;

namespace FileSyncPro.Services
{
    public class IntegrityChecker
    {
        public bool VerifyIntegrity(string sourcePath, string destinationPath)
        {
            try
            {
                // Calculate total size of source directory
                long sourceSize = GetDirectorySize(sourcePath);
                
                // Calculate total size of destination directory
                long destSize = GetDirectorySize(destinationPath);
                
                // Compare sizes
                if (sourceSize != destSize)
                {
                    return false;
                }

                // For additional verification, we could compare file checksums
                // This is a more thorough but slower check
                return VerifyChecksums(sourcePath, destinationPath);
            }
            catch
            {
                return false;
            }
        }

        private bool VerifyChecksums(string sourcePath, string destinationPath)
        {
            try
            {
                var sourceFiles = GetAllFiles(sourcePath);
                var destFiles = GetAllFiles(destinationPath);

                // Check if both directories have the same number of files
                if (sourceFiles.Count != destFiles.Count)
                {
                    return false;
                }

                // Create a mapping of relative paths to checksums for source
                var sourceChecksums = new Dictionary<string, string>();
                foreach (var file in sourceFiles)
                {
                    var relativePath = GetRelativePath(file, sourcePath);
                    sourceChecksums[relativePath] = ComputeChecksum(file);
                }

                // Verify each destination file against source checksum
                foreach (var file in destFiles)
                {
                    var relativePath = GetRelativePath(file, destinationPath);
                    
                    if (!sourceChecksums.ContainsKey(relativePath))
                    {
                        return false;
                    }

                    if (sourceChecksums[relativePath] != ComputeChecksum(file))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private List<string> GetAllFiles(string directory)
        {
            var files = new List<string>();
            var stack = new Stack<string>();
            stack.Push(directory);

            while (stack.Count > 0)
            {
                var currentDir = stack.Pop();
                files.AddRange(Directory.GetFiles(currentDir));

                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    stack.Push(subDir);
                }
            }

            return files;
        }

        private string GetRelativePath(string filePath, string basePath)
        {
            var baseUri = new Uri(basePath + Path.DirectorySeparatorChar);
            var fileUri = new Uri(filePath);
            var relativeUri = baseUri.MakeRelativeUri(fileUri);
            return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
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

        private string ComputeChecksum(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}