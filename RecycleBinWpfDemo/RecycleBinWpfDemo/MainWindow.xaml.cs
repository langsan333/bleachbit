using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RecycleBinWpfDemo
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnRefreshCapacity_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RecycleBinCapacityInfo info = RecycleBinHelper.GetCapacityInfo();
                string sizeStr = FormatBytes(info.BytesUsed);
                TxtCapacity.Text = $"占用: {sizeStr}  |  项数: {info.ItemCount}";
            }
            catch (Exception ex)
            {
                TxtCapacity.Text = "获取失败: " + ex.Message;
            }
        }

        private void BtnRefreshList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                List<string> paths = RecycleBinHelper.GetFileList();
                ListPaths.Items.Clear();
                foreach (string p in paths)
                    ListPaths.Items.Add(p);
                TxtListHint.Text = $"共 {paths.Count} 项。点击“刷新列表”可重新获取。";
            }
            catch (Exception ex)
            {
                ListPaths.Items.Clear();
                ListPaths.Items.Add("获取失败: " + ex.Message);
                TxtListHint.Text = "获取失败";
            }
        }

        private void BtnEmpty_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要清空回收站吗？此操作不可恢复。",
                "确认清空",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                bool ok = RecycleBinHelper.EmptyRecycleBin();
                if (ok)
                {
                    MessageBox.Show("回收站已清空。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    BtnRefreshCapacity_Click(sender, e);
                    BtnRefreshList_Click(sender, e);
                }
                else
                    MessageBox.Show("清空失败，请检查权限或重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("清空失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}
