using FileSyncPro.ViewModels;

namespace FileSyncPro.Models
{
    public class DestinationConfig : ObservableObject
    {
        private string _localPath = string.Empty;

        // Local
        public string LocalPath
        {
            get => _localPath;
            set => SetProperty(ref _localPath, value);
        }

        // SharePoint
        private string _sharePointUrl = string.Empty;
        private string _sharePointUser = string.Empty;
        private string _sharePointPassword = string.Empty;
        private string _sharePointLibrary = string.Empty;
        
        public string SharePointUrl
        {
            get => _sharePointUrl;
            set => SetProperty(ref _sharePointUrl, value);
        }
        
        public string SharePointUser
        {
            get => _sharePointUser;
            set => SetProperty(ref _sharePointUser, value);
        }
        
        public string SharePointPassword
        {
            get => _sharePointPassword;
            set => SetProperty(ref _sharePointPassword, value);
        }
        
        public string SharePointLibrary
        {
            get => _sharePointLibrary;
            set => SetProperty(ref _sharePointLibrary, value);
        }
        
        public string Username { get; set; }
        public string Password { get; set; }

        // SFTP
        private string _sftpHost = string.Empty;
        private int _sftpPort = 22;
        private string _sftpUser = string.Empty;
        private string _sftpPath = "/";
        
        public string SFTPHost
        {
            get => _sftpHost;
            set => SetProperty(ref _sftpHost, value);
        }
        
        public int SFTPPort
        {
            get => _sftpPort;
            set => SetProperty(ref _sftpPort, value);
        }
        
        public string SFTPUser
        {
            get => _sftpUser;
            set => SetProperty(ref _sftpUser, value);
        }
        
        private string _sftpPassword = string.Empty;
        public string SFTPPassword 
        { 
            get => _sftpPassword;
            set => SetProperty(ref _sftpPassword, value);
        }
        
        public string SFTPPath
        {
            get => _sftpPath;
            set => SetProperty(ref _sftpPath, value);
        }
    }
}