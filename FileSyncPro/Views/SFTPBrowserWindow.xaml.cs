using Renci.SshNet;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileSyncPro.Views
{
    public partial class SFTPBrowserWindow : Window
    {
        private readonly SftpClient _client;
        public string SelectedPath { get; private set; } = "/";

        public SFTPBrowserWindow(SftpClient client, string host)
        {
            InitializeComponent();
            _client = client;
            HostLabel.Text = $"Server: {host}";
            LoadRoot();
        }

        private void LoadRoot()
        {
            var rootItem = CreateFolderNode("/", "/");
            FolderTree.Items.Add(rootItem);
            rootItem.IsExpanded = true;
        }

        private TreeViewItem CreateFolderNode(string name, string fullPath)
        {
            var item = new TreeViewItem
            {
                Header = "📁  " + name,
                Tag = fullPath,
                Padding = new Thickness(2)
            };
            // Placeholder so the expand arrow is visible
            item.Items.Add(new TreeViewItem { Header = "Loading...", IsEnabled = false });
            item.Expanded += OnFolderExpanded;
            return item;
        }

        private void OnFolderExpanded(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewItem)sender;

            // Only load if placeholder is still present
            if (item.Items.Count == 1 &&
                item.Items[0] is TreeViewItem first &&
                first.Header?.ToString() == "Loading...")
            {
                item.Items.Clear();
                try
                {
                    var path = (string)item.Tag;
                    var dirs = _client.ListDirectory(path)
                        .Where(f => f.IsDirectory && f.Name != "." && f.Name != "..")
                        .OrderBy(f => f.Name)
                        .ToList();

                    foreach (var dir in dirs)
                        item.Items.Add(CreateFolderNode(dir.Name, dir.FullName));

                    if (dirs.Count == 0)
                    {
                        item.Items.Add(new TreeViewItem
                        {
                            Header = "(no sub-folders)",
                            IsEnabled = false,
                            Foreground = Brushes.Gray
                        });
                    }
                }
                catch (Exception ex)
                {
                    item.Items.Add(new TreeViewItem
                    {
                        Header = $"⚠ {ex.Message}",
                        IsEnabled = false,
                        Foreground = Brushes.Red
                    });
                }
            }

            e.Handled = true;
        }

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem selected && selected.Tag is string path)
            {
                SelectedPath = path;
                SelectedPathBox.Text = path;
            }
        }

        private void Select_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
