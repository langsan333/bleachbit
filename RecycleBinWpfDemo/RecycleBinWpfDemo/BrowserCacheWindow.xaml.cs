using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RecycleBinWpfDemo
{
    public partial class BrowserCacheWindow : Window
    {
        public BrowserCacheWindow()
        {
            InitializeComponent();
        }

        private BrowserKind GetSelectedBrowser()
        {
            switch (ComboBrowser?.SelectedIndex ?? 0)
            {
                case 0: return BrowserKind.Edge;
                case 1: return BrowserKind.Chrome;
                case 2: return BrowserKind.Firefox;
                default: return BrowserKind.Edge;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int u = 0;
            double n = bytes;
            while (n >= 1024 && u < units.Length - 1)
            {
                n /= 1024;
                u++;
            }
            return $"{n:F2} {units[u]}";
        }

        private void BtnGetPaths_Click(object sender, RoutedEventArgs e)
        {
            var browser = GetSelectedBrowser();
            try
            {
                List<string> paths = BrowserCache.GetCachePaths(browser);
                ListPaths.Items.Clear();
                foreach (var p in paths)
                    ListPaths.Items.Add(p);
                TxtSize.Text = $"共 {paths.Count} 项。点击“获取占用大小”可查看占用字节数。";
            }
            catch (System.Exception ex)
            {
                ListPaths.Items.Clear();
                ListPaths.Items.Add("获取失败: " + ex.Message);
                TxtSize.Text = "获取失败";
            }
        }

        private void BtnGetSize_Click(object sender, RoutedEventArgs e)
        {
            var browser = GetSelectedBrowser();
            try
            {
                long size = BrowserCache.GetCacheSize(browser);
                TxtSize.Text = $"占用: {FormatBytes(size)}";
            }
            catch (System.Exception ex)
            {
                TxtSize.Text = "获取失败: " + ex.Message;
            }
        }

        private void BtnClean_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要清理该浏览器的缓存吗？浏览器若正在运行，建议先关闭。",
                "确认清理",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            var browser = GetSelectedBrowser();
            try
            {
                BrowserCache.CleanCache(browser);
                MessageBox.Show("缓存清理完成。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                BtnGetSize_Click(sender, e);
                BtnGetPaths_Click(sender, e);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("清理失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
