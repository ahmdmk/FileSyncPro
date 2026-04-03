using FileSyncPro.Commands;
using FileSyncPro.Models;
using FileSyncPro.Services;
using FileSyncPro.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace FileSyncPro.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly FileSyncService _fileSyncService;
        private string _selectedSourceType = "Local";
        private string _selectedDestinationType = "Local";
        private string _sourcePath = string.Empty;
        private bool _isOperationRunning;
        private string _currentUserName = string.Empty;
        private string _currentDate = string.Empty;

        private bool _rememberSourceSftpCredentials;
        private bool _rememberDestinationSftpCredentials;

        public bool RememberSourceSftpCredentials
        {
            get => _rememberSourceSftpCredentials;
            set => SetProperty(ref _rememberSourceSftpCredentials, value);
        }

        public bool RememberDestinationSftpCredentials
        {
            get => _rememberDestinationSftpCredentials;
            set => SetProperty(ref _rememberDestinationSftpCredentials, value);
        }

        public MainViewModel()
        {
            // Check domain access first
            if (!CheckDomainAccess())
            {
                MessageBox.Show("Access Denied\n\nThis application is only accessible to PETROFAC domain users.",
                    "Access Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            _fileSyncService = new FileSyncService();
            Progress = new SyncProgress();
            DestinationConfig = new DestinationConfig();
            SourceConfig = new DestinationConfig
            {
                SFTPHost = "saeunprdftp01.blob.core.windows.net",
                SFTPPort = 22
            };

            // Listen for changes in SourceConfig SharePointUrl
            SourceConfig.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DestinationConfig.SharePointUrl))
                    OnPropertyChanged(nameof(ZipFilesInfo));
            };

            // Set current user and date
            CurrentUserName = GetCurrentUserName();
            CurrentDate = DateTime.Now.ToString("dddd, MMMM dd, yyyy");

            // Commands
            ProcessCommand = new AsyncRelayCommand(ProcessFilesAsync, CanExecuteOperationAsync);
            BrowseSourceCommand = new RelayCommand(BrowseSource);
            BrowseLocalCommand = new RelayCommand(BrowseLocal);
            BrowseSFTPSourceCommand = new RelayCommand(BrowseSFTPSource);
            ClearLogCommand = new RelayCommand(ClearLog);
            ExportLogCommand = new RelayCommand(ExportLog);
            EmailLogCommand = new RelayCommand(EmailLog);
            ResetCommand = new RelayCommand(Reset);

            LoadSftpCredentials();

            Progress.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SyncProgress.IsRunning))
                    IsOperationRunning = Progress.IsRunning;
            };

            // Welcome log entry
            Progress.LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = LogLevel.INFO,
                Message = $"Welcome {CurrentUserName}! Application started successfully."
            });
        }

        private bool CheckDomainAccess()
        {
            try
            {
                // Get current user's domain
                string domain = Environment.UserDomainName;

                // Check if user is part of DSPETROFAC domain
                return domain.Equals("DSPETROFAC", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private string GetCurrentUserName()
        {
            try
            {
                // Get the current user name
                string userName = Environment.UserName;

                // Try to get the full name from Active Directory if available
                try
                {
                    var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    if (identity != null && !string.IsNullOrEmpty(identity.Name))
                    {
                        // Extract just the username part (remove domain prefix)
                        string[] parts = identity.Name.Split('\\');
                        if (parts.Length > 1)
                            userName = parts[1];
                    }
                }
                catch
                {
                    // Fall back to Environment.UserName
                }

                return userName;
            }
            catch
            {
                return "User";
            }
        }

        private void LoadSftpCredentials()
        {
            var saved = FileHelper.LoadSftpCredentials();
            if (saved == null)
                return;

            RememberSourceSftpCredentials = saved.RememberSource;
            RememberDestinationSftpCredentials = saved.RememberDestination;

            if (saved.RememberSource)
            {
                SourceConfig.SFTPHost = saved.SourceHost;
                SourceConfig.SFTPPort = saved.SourcePort;
                SourceConfig.SFTPUser = saved.SourceUser;
                SourceConfig.SFTPPassword = saved.SourcePassword;
            }

            if (saved.RememberDestination)
            {
                DestinationConfig.SFTPHost = saved.DestinationHost;
                DestinationConfig.SFTPPort = saved.DestinationPort;
                DestinationConfig.SFTPUser = saved.DestinationUser;
                DestinationConfig.SFTPPassword = saved.DestinationPassword;
            }
        }

        private void SaveSftpCredentials()
        {
            var saved = new FileHelper.SavedSftpCredentials
            {
                RememberSource = RememberSourceSftpCredentials,
                SourceHost = RememberSourceSftpCredentials ? SourceConfig.SFTPHost : string.Empty,
                SourcePort = RememberSourceSftpCredentials ? SourceConfig.SFTPPort : 22,
                SourceUser = RememberSourceSftpCredentials ? SourceConfig.SFTPUser : string.Empty,
                SourcePassword = RememberSourceSftpCredentials ? SourceConfig.SFTPPassword : string.Empty,

                RememberDestination = RememberDestinationSftpCredentials,
                DestinationHost = RememberDestinationSftpCredentials ? DestinationConfig.SFTPHost : string.Empty,
                DestinationPort = RememberDestinationSftpCredentials ? DestinationConfig.SFTPPort : 22,
                DestinationUser = RememberDestinationSftpCredentials ? DestinationConfig.SFTPUser : string.Empty,
                DestinationPassword = RememberDestinationSftpCredentials ? DestinationConfig.SFTPPassword : string.Empty,
            };
            FileHelper.SaveSftpCredentials(saved);
        }

        public DestinationConfig DestinationConfig { get; }
        public DestinationConfig SourceConfig { get; } = new DestinationConfig();
        public SyncProgress Progress { get; }

        public string SelectedSourceType
        {
            get => _selectedSourceType;
            set
            {
                SetProperty(ref _selectedSourceType, value);
                OnPropertyChanged(nameof(IsSourceLocal));
                OnPropertyChanged(nameof(IsSourceSharePoint));
                OnPropertyChanged(nameof(IsSourceSFTP));
                OnPropertyChanged(nameof(ZipFilesInfo));
                OnPropertyChanged(nameof(HasSourceValidationError));
            }
        }

        public bool IsSourceLocal => SelectedSourceType == "Local";
        public bool IsSourceSharePoint => SelectedSourceType == "SharePoint";
        public bool IsSourceSFTP => SelectedSourceType == "SFTP";

        public string SelectedDestinationType
        {
            get => _selectedDestinationType;
            set
            {
                SetProperty(ref _selectedDestinationType, value);
                OnPropertyChanged(nameof(ZipFilesInfo)); // Update message when destination type changes
                OnPropertyChanged(nameof(HasSourceValidationError)); // Update error state
            }
        }

        public string SourcePath
        {
            get => _sourcePath;
            set
            {
                SetProperty(ref _sourcePath, value);
                OnPropertyChanged(nameof(HasZipFiles));
                OnPropertyChanged(nameof(ZipFilesInfo));
                OnPropertyChanged(nameof(HasSourceValidationError));
            }
        }

        public bool HasZipFiles
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SourcePath) || !System.IO.Directory.Exists(SourcePath))
                    return false;

                try
                {
                    // Case-insensitive search for .zip files
                    var zipFiles = System.IO.Directory.GetFiles(SourcePath, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                        .Where(file => file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    return zipFiles.Length > 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool HasSourceValidationError
        {
            get
            {
                // If source is SharePoint or SFTP, no ZIP validation needed
                if (SelectedSourceType == "SharePoint" || SelectedSourceType == "SFTP")
                    return false;

                // For local source: No error if we have ZIP files
                if (HasZipFiles)
                    return false;

                // No error for SharePoint destination even without ZIP files
                if (SelectedDestinationType == "SharePoint")
                    return false;

                // Error for other destination types without ZIP files
                return true;
            }
        }

        public string ZipFilesInfo
        {
            get
            {
                // If source is SharePoint, show SharePoint connection info
                if (SelectedSourceType == "SharePoint")
                {
                    if (string.IsNullOrWhiteSpace(SourceConfig.SharePointUrl))
                        return "Please enter SharePoint URL";
                    return "SharePoint source configured";
                }

                // If source is SFTP, show SFTP connection info
                if (SelectedSourceType == "SFTP")
                {
                    if (string.IsNullOrWhiteSpace(SourceConfig.SFTPHost))
                        return "Please enter SFTP host";
                    return $"SFTP source configured: {SourceConfig.SFTPHost}";
                }

                // For local source
                if (string.IsNullOrWhiteSpace(SourcePath) || !System.IO.Directory.Exists(SourcePath))
                    return "No folder selected";

                try
                {
                    var zipFiles = System.IO.Directory.GetFiles(SourcePath, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                        .Where(file => file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    if (zipFiles.Length > 0)
                    {
                        return $"Found {zipFiles.Length} ZIP file{(zipFiles.Length > 1 ? "s" : "")}";
                    }
                    else if (SelectedDestinationType == "SharePoint")
                    {
                        // For SharePoint destination, show folder/file count instead
                        var allFiles = System.IO.Directory.GetFiles(SourcePath, "*.*", System.IO.SearchOption.AllDirectories);
                        var folders = System.IO.Directory.GetDirectories(SourcePath, "*", System.IO.SearchOption.AllDirectories);
                        return $"Ready to sync: {folders.Length} folder{(folders.Length != 1 ? "s" : "")}, {allFiles.Length} file{(allFiles.Length != 1 ? "s" : "")}";
                    }
                    else
                    {
                        return "No ZIP files found - required for this destination type";
                    }
                }
                catch
                {
                    return "Error reading folder";
                }
            }
        }

        public bool IsOperationRunning
        {
            get => _isOperationRunning;
            set
            {
                SetProperty(ref _isOperationRunning, value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string CurrentUserName
        {
            get => _currentUserName;
            set => SetProperty(ref _currentUserName, value);
        }

        public string CurrentDate
        {
            get => _currentDate;
            set => SetProperty(ref _currentDate, value);
        }

        public ICommand ProcessCommand { get; }
        public ICommand BrowseSourceCommand { get; }
        public ICommand BrowseLocalCommand { get; }
        public ICommand BrowseSFTPSourceCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand ExportLogCommand { get; }
        public ICommand EmailLogCommand { get; }
        public ICommand ResetCommand { get; }

        // --- Async processing ---
        private async Task ProcessFilesAsync()
        {
            try
            {
                // Perform client-side validation first
                var validationError = ValidateInputs();
                if (!string.IsNullOrEmpty(validationError))
                {
                    MessageBox.Show(validationError, "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);

                    Progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.ERROR,
                        Message = $"Validation failed: {validationError}"
                    });
                    return;
                }

                IsOperationRunning = true;

                Progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.INFO,
                    Message = $"Starting file synchronization to {SelectedDestinationType}..."
                });

                var operation = CreateSyncOperation();

                var validationResult = await _fileSyncService.ValidateDestinationAsync(operation, new Progress<ProgressReport>());
                if (validationResult.IsSuccess)
                {
                    Progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.SUCCESS,
                        Message = validationResult.Message
                    });

                    SaveSftpCredentials();
                    await _fileSyncService.SyncFilesAsync(operation, new Progress<ProgressReport>());
                }
                else
                {
                    MessageBox.Show($"Destination validation failed:\n\n{validationResult.Message}", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);

                    Progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.ERROR,
                        Message = $"Validation failed: {validationResult.Message}"
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during processing:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.ERROR,
                    Message = $"Process error: {ex.Message}"
                });
            }
            finally
            {
                IsOperationRunning = false;

                // Auto-email log after sync completes or fails
                EmailLog();
            }
        }

        private string ValidateInputs()
        {
            // Validate source based on type
            if (SelectedSourceType == "SharePoint")
            {
                // Validate SharePoint source
                if (string.IsNullOrWhiteSpace(SourceConfig.SharePointUrl))
                {
                    return "Please enter a SharePoint site URL for the source.";
                }

                if (!Uri.TryCreate(SourceConfig.SharePointUrl, UriKind.Absolute, out var uri))
                {
                    return "Invalid SharePoint URL format for the source.";
                }

                if (!uri.Host.Contains("sharepoint.com", StringComparison.OrdinalIgnoreCase))
                {
                    return "The source URL does not appear to be a valid SharePoint URL.";
                }
            }
            else if (SelectedSourceType == "SFTP")
            {
                if (string.IsNullOrWhiteSpace(SourceConfig.SFTPHost))
                    return "Please enter the source SFTP host address.";
                if (SourceConfig.SFTPPort <= 0 || SourceConfig.SFTPPort > 65535)
                    return "Please enter a valid source SFTP port number (1-65535).";
                if (string.IsNullOrWhiteSpace(SourceConfig.SFTPUser))
                    return "Please enter the source SFTP username.";
                if (string.IsNullOrWhiteSpace(SourceConfig.SFTPPassword))
                    return "Please enter the source SFTP password.";
                if (string.IsNullOrWhiteSpace(SourceConfig.SFTPPath))
                    return "Please enter the source SFTP remote path.";
            }
            else
            {
                // Validate local source path
                if (string.IsNullOrWhiteSpace(SourcePath))
                {
                    return "Please select a source folder.";
                }

                if (!System.IO.Directory.Exists(SourcePath))
                {
                    return "The selected source folder does not exist.";
                }

                // For SharePoint destination, ZIP files are optional - can sync folders directly
                // For other destinations, ZIP files are still required
                if (!HasZipFiles && SelectedDestinationType != "SharePoint")
                {
                    return "The selected source folder does not contain any ZIP files.";
                }
            }

            // Validate destination based on type
            switch (SelectedDestinationType)
            {
                case "Local":
                    if (string.IsNullOrWhiteSpace(DestinationConfig.LocalPath))
                    {
                        return "Please specify a local destination folder.";
                    }
                    break;

                case "SharePoint":
                    if (string.IsNullOrWhiteSpace(DestinationConfig.SharePointUrl))
                    {
                        return "Please enter a SharePoint site URL.";
                    }

                    if (!Uri.TryCreate(DestinationConfig.SharePointUrl, UriKind.Absolute, out var uri))
                    {
                        return "Invalid SharePoint URL format. Please enter a valid URL.";
                    }

                    if (!uri.Host.Contains("sharepoint.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return "The URL does not appear to be a valid SharePoint URL.";
                    }
                    break;

                case "SFTP":
                    if (string.IsNullOrWhiteSpace(DestinationConfig.SFTPHost))
                    {
                        return "Please enter the SFTP host address.";
                    }

                    if (DestinationConfig.SFTPPort <= 0 || DestinationConfig.SFTPPort > 65535)
                    {
                        return "Please enter a valid SFTP port number (1-65535).";
                    }

                    if (string.IsNullOrWhiteSpace(DestinationConfig.SFTPUser))
                    {
                        return "Please enter the SFTP username.";
                    }

                    if (string.IsNullOrWhiteSpace(DestinationConfig.SFTPPassword))
                    {
                        return "Please enter the SFTP password.";
                    }

                    if (string.IsNullOrWhiteSpace(DestinationConfig.SFTPPath))
                    {
                        return "Please enter the remote SFTP path.";
                    }
                    break;
            }

            return null; // No validation errors
        }

        private Task<bool> CanExecuteOperationAsync()
        {
            // If source is SharePoint or SFTP, allow execution
            if (SelectedSourceType == "SharePoint" || SelectedSourceType == "SFTP")
            {
                return Task.FromResult(!IsOperationRunning);
            }

            // For local source: For SharePoint destination, allow execution even without ZIP files (can sync folders directly)
            // For other destinations, ZIP files are required
            bool hasRequiredFiles = HasZipFiles || SelectedDestinationType == "SharePoint";
            return Task.FromResult(!IsOperationRunning && hasRequiredFiles);
        }

        // --- Helpers ---
        private FileSyncOperation CreateSyncOperation()
        {
            var sourceType = SelectedSourceType switch
            {
                "Local" => SourceType.Local,
                "SharePoint" => SourceType.SharePoint,
                "SFTP" => SourceType.SFTP,
                _ => SourceType.Local
            };

            var destinationType = SelectedDestinationType switch
            {
                "Local" => DestinationType.Local,
                "SharePoint" => DestinationType.SharePoint,
                "SFTP" => DestinationType.SFTP,
                _ => DestinationType.Local
            };

            return new FileSyncOperation
            {
                SourceType = sourceType,
                SourcePath = SourcePath,
                SourceConfig = SourceConfig,
                DestinationType = destinationType,
                DestinationConfig = DestinationConfig,
                Progress = Progress
            };
        }

        private void BrowseSource()
        {
            try
            {
                var dialog = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog
                {
                    IsFolderPicker = true,
                    Title = "Select Source Folder (ZIP files)"
                };

                if (dialog.ShowDialog() == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok)
                    SourcePath = dialog.FileName;

                Progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.INFO,
                    Message = $"Selected source folder: {SourcePath}"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing source folder: {ex.Message}", "FileSyncPro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseSFTPSource()
        {
            if (string.IsNullOrWhiteSpace(SourceConfig.SFTPHost) ||
                string.IsNullOrWhiteSpace(SourceConfig.SFTPUser) ||
                string.IsNullOrWhiteSpace(SourceConfig.SFTPPassword))
            {
                MessageBox.Show("Please fill in SFTP Host, Username, and Password before browsing.",
                    "SFTP Browse", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                var client = new Renci.SshNet.SftpClient(
                    SourceConfig.SFTPHost, SourceConfig.SFTPPort,
                    SourceConfig.SFTPUser, SourceConfig.SFTPPassword);
                client.Connect();

                System.Windows.Input.Mouse.OverrideCursor = null;

                var browser = new Views.SFTPBrowserWindow(client, SourceConfig.SFTPHost)
                {
                    Owner = Application.Current.MainWindow
                };

                if (browser.ShowDialog() == true)
                {
                    SourceConfig.SFTPPath = browser.SelectedPath;
                    Progress.LogEntries.Add(new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = LogLevel.INFO,
                        Message = $"Selected SFTP source path: {browser.SelectedPath}"
                    });
                }

                client.Disconnect();
                client.Dispose();
            }
            catch (Exception ex)
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
                MessageBox.Show($"Failed to connect to SFTP server:\n\n{ex.Message}",
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseLocal()
        {
            try
            {
                var dialog = new Microsoft.WindowsAPICodePack.Dialogs.CommonOpenFileDialog
                {
                    IsFolderPicker = true,
                    Title = "Select Local Destination Folder"
                };

                if (dialog.ShowDialog() == Microsoft.WindowsAPICodePack.Dialogs.CommonFileDialogResult.Ok)
                    DestinationConfig.LocalPath = dialog.FileName;

                Progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.INFO,
                    Message = $"Selected local destination: {DestinationConfig.LocalPath}"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing local folder: {ex.Message}", "FileSyncPro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearLog()
        {
            Progress.LogEntries.Clear();
            Progress.LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = LogLevel.INFO,
                Message = "Log cleared"
            });
        }

        private void ExportLog()
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = $"FileSyncPro_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveDialog.ShowDialog() != true) return;

                var logContent = string.Join("\n", Progress.LogEntries.Select(entry =>
                    $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} [{entry.Level}] {entry.Message}"));

                System.IO.File.WriteAllText(saveDialog.FileName, logContent);

                Progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.SUCCESS,
                    Message = $"Log exported to: {saveDialog.FileName}"
                });

                MessageBox.Show($"Log exported successfully to: {saveDialog.FileName}", "FileSyncPro",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.ERROR,
                    Message = $"Error exporting log: {ex.Message}"
                });
                MessageBox.Show($"Error exporting log: {ex.Message}", "FileSyncPro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EmailLog()
        {
            // Email sending is disabled per user request. Logs are still available in the UI and can be exported manually.
            Progress.LogEntries.Add(new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = LogLevel.INFO,
                Message = "Email log feature is disabled. Use Export Log to save logs locally."
            });
        }

        private void Reset()
        {
            try
            {
                var result = MessageBox.Show("Are you sure you want to reset all fields?\n\nThis will clear all inputs and logs.",
                    "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                // Clear source
                SelectedSourceType = "Local";
                SourcePath = string.Empty;
                SourceConfig.SharePointUrl = string.Empty;
                SourceConfig.SFTPHost = "saeunprdftp01.blob.core.windows.net";
                SourceConfig.SFTPPort = 22;
                SourceConfig.SFTPUser = string.Empty;
                SourceConfig.SFTPPassword = string.Empty;
                SourceConfig.SFTPPath = string.Empty;

                // Reset destination type to Local
                SelectedDestinationType = "Local";

                // Clear destination config
                DestinationConfig.LocalPath = string.Empty;
                DestinationConfig.SharePointUrl = string.Empty;
                DestinationConfig.SharePointLibrary = string.Empty;
                DestinationConfig.SharePointUser = string.Empty;
                DestinationConfig.SharePointPassword = string.Empty;
                DestinationConfig.SFTPHost = string.Empty;
                DestinationConfig.SFTPPort = 22;
                DestinationConfig.SFTPUser = string.Empty;
                DestinationConfig.SFTPPassword = string.Empty;
                DestinationConfig.SFTPPath = string.Empty;

                // Clear progress
                Progress.Percentage = 0;
                Progress.CurrentOperation = string.Empty;

                // Clear logs and add reset message
                Progress.LogEntries.Clear();
                Progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.INFO,
                    Message = "Application reset - all fields cleared"
                });

                Progress.LogEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = LogLevel.INFO,
                    Message = $"Welcome back {CurrentUserName}!"
                });

                // Force UI update
                OnPropertyChanged(nameof(SourcePath));
                OnPropertyChanged(nameof(HasZipFiles));
                OnPropertyChanged(nameof(ZipFilesInfo));
                OnPropertyChanged(nameof(SelectedDestinationType));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during reset: {ex.Message}", "Reset Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
