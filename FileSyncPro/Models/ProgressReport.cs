namespace FileSyncPro.Models
{
    public class ProgressReport
    {
        public double Percentage { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
    }
}