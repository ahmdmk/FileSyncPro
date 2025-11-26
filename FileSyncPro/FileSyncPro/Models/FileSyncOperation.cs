using System;
using System.Collections.Generic;

namespace FileSyncPro.Models
{
    public class FileSyncOperation
    {
        public SourceType SourceType { get; set; } = SourceType.Local;
        public string SourcePath { get; set; } = string.Empty;
        public DestinationConfig SourceConfig { get; set; } = new DestinationConfig(); // Reuse for SharePoint source

        public DestinationType DestinationType { get; set; }
        public DestinationConfig DestinationConfig { get; set; } = new DestinationConfig();
        public List<string> FilesToProcess { get; set; } = new List<string>();
        public SyncProgress Progress { get; set; } = new SyncProgress();
    }

    public enum SourceType
    {
        Local,
        SharePoint
    }

    public enum DestinationType
    {
        Local,
        SharePoint,
        SFTP
    }
}