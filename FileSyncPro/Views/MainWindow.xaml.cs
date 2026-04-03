using System;
using System.Windows;
using FileSyncPro.ViewModels;

namespace FileSyncPro.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            // Scale initial window dimensions to the current screen work area
            var workArea = SystemParameters.WorkArea;
            var targetWidth = Math.Min(1100, workArea.Width * 0.95);
            var targetHeight = Math.Min(1000, workArea.Height * 0.85);

            Width = Math.Max(800, targetWidth);
            Height = Math.Max(600, targetHeight);
            MinWidth = 750;
            MinHeight = 540;
            MaxWidth = workArea.Width;
            MaxHeight = workArea.Height;

            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            InitializeComponent();
        }
    }
}