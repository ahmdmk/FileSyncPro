using System.Windows;
using System.IO;
using FileSyncPro.Utilities;

namespace FileSyncPro
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Ensure Logs directory exists
            var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            // Initialize logging
            Logger.CleanOldLogs(30);
            Logger.Info("FileSyncPro MVVM application started");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("FileSyncPro application shutting down");
            base.OnExit(e);
        }
    }
}