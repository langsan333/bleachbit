/*
 * Windows Recycle Bin - 信息收集与清空
 *
 * 逻辑移植自 BleachBit (https://www.bleachbit.org)
 * - 文件列表：Shell 枚举回收站项，对目录递归子项（与 get_recycle_bin + children_in_directory 一致）
 * - 容量信息：SHQueryRecycleBin
 * - 清空：SHEmptyRecycleBin（无声音、无确认、无进度窗）
 *
 * 目标框架：.NET Framework 4.7.2
 * 依赖：无额外引用；GetFileList 通过 Shell.Application ProgID 晚期绑定。
 */

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace YourNamespace
{
    /// <summary>
    /// 回收站容量信息（与 SHQueryRecycleBin 返回一致）
    /// </summary>
    public sealed class RecycleBinCapacityInfo
    {
        public long BytesUsed { get; set; }
        public long ItemCount { get; set; }
    }

    /// <summary>
    /// Windows 回收站：获取文件列表、容量信息、清空。
    /// 逻辑对应 BleachBit 的 get_recycle_bin / empty_recycle_bin / SHQueryRecycleBin。
    /// </summary>
    public static class RecycleBinHelper
    {
        #region P/Invoke (shell32)

        private const int SHERB_NOCONFIRMATION = 0x00000001;
        private const int SHERB_NOPROGRESSUI = 0x00000002;
        private const int SHERB_NOSOUND = 0x00000004;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string pszRootPath, uint dwFlags);

        #endregion

        /// <summary>
        /// 获取回收站中所有文件/目录的路径列表。
        /// 行为与 BleachBit get_recycle_bin 一致：顶层项若为目录，先列出其下所有子项（文件+目录），再列该项本身。
        /// </summary>
        /// <returns>所有项的完整路径列表（含 $Recycle.Bin 下的实际路径）</returns>
        public static List<string> GetFileList()
        {
            var list = new List<string>();
            try
            {
                // Shell.Application, Namespace(10) = CSIDL_BITBUCKET (回收站)
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                    return list;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic recycleBin = shell.NameSpace(10);
                if (recycleBin == null)
                    return list;

                dynamic items = recycleBin.Items();
                if (items == null)
                    return list;

                foreach (dynamic item in items)
                {
                    CollectRecycleBinItemPaths(item, list);
                }
            }
            catch (Exception)
            {
                // 无 COM 权限或未安装 Shell 时可能失败，返回空列表
            }

            return list;
        }

        /// <summary>
        /// 递归收集单项路径：若是目录则先收集其下所有子项（文件+目录），再加入自身路径（与 BleachBit children_in_directory + yield path 一致）。
        /// </summary>
        private static void CollectRecycleBinItemPaths(dynamic item, List<string> list)
        {
            if (item == null) return;

            string path = null;
            try
            {
                path = item.Path;
            }
            catch
            {
                return;
            }

            if (string.IsNullOrEmpty(path)) return;

            bool isFolder = false;
            try
            {
                isFolder = item.IsFolder;
            }
            catch { }

            if (isFolder)
            {
                try
                {
                    dynamic folder = item.GetFolder();
                    if (folder != null)
                    {
                        dynamic childItems = folder.Items();
                        if (childItems != null)
                        {
                            foreach (dynamic child in childItems)
                            {
                                CollectRecycleBinItemPaths(child, list);
                            }
                        }
                    }
                }
                catch { }
            }

            list.Add(path);
        }

        /// <summary>
        /// 获取回收站容量信息（总字节数、项数）。
        /// 对应 BleachBit empty_recycle_bin(path, really_delete=False) 使用的 SHQueryRecycleBin。
        /// </summary>
        /// <param name="driveRoot">指定盘符根路径（如 "C:\"）可只查该盘；传 null 或空表示所有回收站。</param>
        /// <returns>容量信息；若调用失败则返回 BytesUsed=0, ItemCount=0。</returns>
        public static RecycleBinCapacityInfo GetCapacityInfo(string driveRoot = null)
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
            string root = string.IsNullOrEmpty(driveRoot) ? null : driveRoot;
            int hr = SHQueryRecycleBinW(root, ref info);
            if (hr != 0)
                return new RecycleBinCapacityInfo { BytesUsed = 0, ItemCount = 0 };
            return new RecycleBinCapacityInfo
            {
                BytesUsed = info.i64Size,
                ItemCount = info.i64NumItems
            };
        }

        /// <summary>
        /// 清空回收站（无确认、无进度窗、无声音）。
        /// 对应 BleachBit empty_recycle_bin(path, really_delete=True) 及 SHEmptyRecycleBin 的 flags。
        /// </summary>
        /// <param name="driveRoot">指定盘符根路径（如 "C:\"）只清该盘；传 null 或空表示清空所有回收站。</param>
        /// <returns>是否成功；若回收站已空仍返回 true。</returns>
        public static bool EmptyRecycleBin(string driveRoot = null)
        {
            uint flags = SHERB_NOSOUND | SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI;
            string root = string.IsNullOrEmpty(driveRoot) ? null : driveRoot;
            int hr = SHEmptyRecycleBinW(IntPtr.Zero, root, flags);
            return hr == 0;
        }
    }
}

/*
 * 使用示例：
 *
 * // 1. 获取回收站中所有文件/目录路径列表
 * var paths = RecycleBinHelper.GetFileList();
 * foreach (var p in paths) Console.WriteLine(p);
 *
 * // 2. 获取容量信息（所有盘）
 * var info = RecycleBinHelper.GetCapacityInfo();
 * Console.WriteLine($"占用: {info.BytesUsed} 字节, 项数: {info.ItemCount}");
 *
 * // 仅查 C 盘
 * var infoC = RecycleBinHelper.GetCapacityInfo("C:\\");
 *
 * // 3. 清空回收站
 * bool ok = RecycleBinHelper.EmptyRecycleBin();       // 清空所有
 * bool okC = RecycleBinHelper.EmptyRecycleBin("C:\\"); // 仅清 C 盘
 *
 * 项目配置（.NET Framework 4.7.2）：
 * - GetFileList() 使用 Shell.Application COM，无需额外引用即可通过 ProgID 晚期绑定。
 * - 若需强类型，可添加 COM 引用 "Microsoft Shell Controls And Automation" 后改用 Shell32 命名空间中的类型。
 */
