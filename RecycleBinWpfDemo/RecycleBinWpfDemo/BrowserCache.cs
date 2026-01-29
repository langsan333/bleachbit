/*
 * Windows 浏览器缓存收集与清理（Edge / Chrome / Firefox）
 *
 * 逻辑移植自 BleachBit (https://www.bleachbit.org)，见 doc/browser-cache-flow.md。
 * - 路径：变量解析 + 环境变量展开 + search 枚举（file/glob/walk.all/walk.files）
 * - 容量：对路径列表逐项取文件大小并求和
 * - 清理：按顺序删除（先文件，再目录由深到浅）
 *
 * 目标框架：.NET Framework 4.7.2
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RecycleBinWpfDemo
{
    public enum BrowserKind
    {
        Edge,
        Chrome,
        Firefox
    }

    /// <summary>
    /// 浏览器缓存：获取路径列表、容量、清理（与 BleachBit Cache 选项一致）。
    /// </summary>
    public static class BrowserCache
    {
        private static string ExpandEnv(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Environment.ExpandEnvironmentVariables(s);
        }

        private static string NormPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            path = path.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(path);
        }

        /// <summary>将模板中的 $$key$$ 替换为 variables[key]，多值时做笛卡尔积。</summary>
        private static List<string> ExpandMultiVar(string template, Dictionary<string, List<string>> variables)
        {
            if (variables == null || variables.Count == 0 || string.IsNullOrEmpty(template) || !template.Contains("$$"))
                return new List<string> { template };

            var used = new List<string>();
            foreach (var k in variables.Keys)
                if (template.IndexOf("$$" + k + "$$", StringComparison.Ordinal) >= 0)
                    used.Add(k);
            if (used.Count == 0) return new List<string> { template };

            var result = new List<string>();
            var valueLists = used.Select(k => variables[k]).ToArray();
            foreach (var combo in Cartesian(valueLists))
            {
                var s = template;
                for (int i = 0; i < used.Count; i++)
                    s = s.Replace("$$" + used[i] + "$$", combo[i]);
                result.Add(s);
            }
            return result;
        }

        private static IEnumerable<string[]> Cartesian(IList<List<string>> valueLists)
        {
            if (valueLists == null || valueLists.Count == 0 || valueLists.Any(list => list == null || list.Count == 0))
                yield break;
            var indices = new int[valueLists.Count];
            while (true)
            {
                yield return valueLists.Select((list, i) => list[indices[i]]).ToArray();
                int j = valueLists.Count - 1;
                while (j >= 0 && indices[j] == valueLists[j].Count - 1) { indices[j] = 0; j--; }
                if (j < 0) break;
                indices[j]++;
            }
        }

        /// <summary>单条路径模板 → 多条绝对路径（变量替换 + 环境变量展开 + normpath）。</summary>
        private static List<string> ResolvePath(string pathTemplate, Dictionary<string, List<string>> variables)
        {
            var expanded = ExpandMultiVar(pathTemplate, variables);
            var outList = new List<string>();
            foreach (var s in expanded)
            {
                var t = ExpandEnv(s);
                if (string.IsNullOrEmpty(t)) continue;
                try { outList.Add(NormPath(t)); } catch { }
            }
            return outList;
        }

        /// <summary>自底向上遍历目录，产出文件；若 includeDirs 则也产出子目录。</summary>
        private static void ChildrenInDirectory(string top, bool includeDirs, List<string> result)
        {
            if (!Directory.Exists(top)) return;
            try
            {
                foreach (var f in Directory.GetFiles(top))
                    result.Add(f);
                foreach (var d in Directory.GetDirectories(top))
                {
                    ChildrenInDirectory(d, includeDirs, result);
                    if (includeDirs) result.Add(d);
                }
            }
            catch { }
        }

        /// <summary>展开路径中的通配符 *，返回所有匹配的完整路径（仅一层 *）。</summary>
        private static List<string> ExpandGlob(string pathWithStar)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(pathWithStar) || pathWithStar.IndexOf('*') < 0)
            {
                if (!string.IsNullOrEmpty(pathWithStar)) result.Add(pathWithStar);
                return result;
            }
            var idx = pathWithStar.IndexOf('*');
            var prefix = pathWithStar.Substring(0, idx);
            var afterStar = pathWithStar.Substring(idx + 1);
            var sep = Path.DirectorySeparatorChar;
            var nextSep = afterStar.IndexOf(sep);
            string pattern, suffix;
            if (nextSep >= 0)
            {
                pattern = afterStar.Substring(0, nextSep);
                suffix = afterStar.Substring(nextSep);
            }
            else
            {
                pattern = afterStar;
                suffix = "";
            }
            var dir = Path.GetDirectoryName(prefix.TrimEnd(sep));
            if (string.IsNullOrEmpty(dir)) dir = ".";
            try
            {
                if (!Directory.Exists(dir)) return result;
                if (!string.IsNullOrEmpty(suffix))
                {
                    foreach (var d in Directory.GetDirectories(dir, pattern))
                        result.AddRange(ExpandGlob(d + suffix));
                }
                else
                {
                    result.AddRange(Directory.GetFiles(dir, pattern));
                    result.AddRange(Directory.GetDirectories(dir, pattern));
                }
            }
            catch { }
            return result;
        }

        /// <summary>根据 search 类型枚举要操作的路径列表。</summary>
        private static List<string> EnumeratePaths(string resolvedPath, string search)
        {
            var result = new List<string>();
            if (search == "file")
            {
                if (File.Exists(resolvedPath) || Directory.Exists(resolvedPath))
                    result.Add(resolvedPath);
                return result;
            }
            if (search == "glob")
            {
                try
                {
                    if (resolvedPath.IndexOf('*') >= 0)
                    {
                        var expanded = ExpandGlob(resolvedPath);
                        result.AddRange(expanded);
                        return result;
                    }
                    var dir = Path.GetDirectoryName(resolvedPath);
                    var pattern = Path.GetFileName(resolvedPath);
                    if (string.IsNullOrEmpty(dir)) dir = ".";
                    if (!Directory.Exists(dir)) return result;
                    result.AddRange(Directory.GetFiles(dir, pattern));
                    result.AddRange(Directory.GetDirectories(dir, pattern));
                }
                catch { }
                return result;
            }
            if (search == "walk.all")
            {
                try
                {
                    var toWalk = new List<string>();
                    if (resolvedPath.IndexOf('*') >= 0)
                        toWalk.AddRange(ExpandGlob(resolvedPath));
                    else
                    {
                        var dir = Path.GetDirectoryName(resolvedPath);
                        var pattern = Path.GetFileName(resolvedPath);
                        if (string.IsNullOrEmpty(dir)) dir = ".";
                        if (Directory.Exists(dir))
                            toWalk.AddRange(Directory.GetDirectories(dir, pattern));
                    }
                    foreach (var d in toWalk)
                    {
                        if (Directory.Exists(d))
                            ChildrenInDirectory(d, true, result);
                    }
                }
                catch { }
                return result;
            }
            if (search == "walk.files")
            {
                try
                {
                    var toWalk = new List<string>();
                    if (resolvedPath.IndexOf('*') >= 0)
                        toWalk.AddRange(ExpandGlob(resolvedPath));
                    else
                    {
                        var dir = Path.GetDirectoryName(resolvedPath);
                        var pattern = Path.GetFileName(resolvedPath);
                        if (string.IsNullOrEmpty(dir)) dir = ".";
                        if (Directory.Exists(dir))
                            toWalk.AddRange(Directory.GetDirectories(dir, pattern));
                    }
                    foreach (var d in toWalk)
                    {
                        if (Directory.Exists(d))
                            ChildrenInDirectory(d, false, result);
                    }
                }
                catch { }
                return result;
            }
            return result;
        }

        private static Dictionary<string, List<string>> GetEdgeVars()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) return null;
            var basePath = Path.Combine(local, "Microsoft", "Edge", "User Data");
            var profilePath = Path.Combine(basePath, "Default");
            return new Dictionary<string, List<string>>
            {
                { "base", new List<string> { basePath } },
                { "profile", new List<string> { profilePath } }
            };
        }

        private static readonly (string Search, string Path)[] EdgeCacheRules =
        {
            ("file", "$$profile$$/Network Persistent State"),
            ("walk.all", "$$base$$/ShaderCache"),
            ("walk.all", "$$profile$$/File System"),
            ("walk.all", "$$profile$$/Service Worker"),
            ("walk.all", "$$profile$$/Storage/ext/*/*def/GPUCache"),
            ("walk.files", "$$profile$$/GPUCache/"),
            ("glob", "$$base$$/B*.tmp"),
            ("walk.all", "$$profile$$/Default/Application Cache/"),
            ("walk.files", "$$profile$$/Cache/"),
            ("walk.files", "$$profile$$/Media Cache/"),
        };

        private static Dictionary<string, List<string>> GetChromeVars()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) return null;
            var basePath = Path.Combine(local, "Google", "Chrome", "User Data");
            var profilePath = Path.Combine(basePath, "Default");
            return new Dictionary<string, List<string>>
            {
                { "base", new List<string> { basePath } },
                { "profile", new List<string> { profilePath } }
            };
        }

        private static readonly (string Search, string Path)[] ChromeCacheRules =
        {
            ("file", "$$base$$/Safe Browsing Channel IDs-journal"),
            ("file", "$$profile$$/Network Persistent State"),
            ("file", "$$profile$$/Network/Network Persistent State"),
            ("walk.all", "$$base$$/ShaderCache"),
            ("walk.all", "$$profile$$/File System"),
            ("walk.all", "$$profile$$/Pepper Data/Shockwave Flash/CacheWritableAdobeRoot/"),
            ("walk.all", "$$profile$$/Service Worker"),
            ("walk.all", "$$profile$$/Storage/ext/*/*def/GPUCache"),
            ("walk.files", "$$profile$$/GPUCache/"),
            ("glob", "$$base$$/B*.tmp"),
            ("walk.all", "$$profile$$/Default/Application Cache/"),
            ("walk.files", "$$profile$$/Cache/"),
            ("walk.files", "$$profile$$/Code Cache/"),
            ("walk.files", "$$profile$$/Media Cache/"),
        };

        private static Dictionary<string, List<string>> GetFirefoxVars()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(appData)) return null;
            var basePath = Path.Combine(appData, "Mozilla", "Firefox");
            var profilesDir = Path.Combine(basePath, "Profiles");
            var profileList = new List<string>();
            if (Directory.Exists(profilesDir))
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(profilesDir))
                        profileList.Add(d);
                }
                catch { }
            }
            return new Dictionary<string, List<string>>
            {
                { "base", new List<string> { basePath } },
                { "profile", profileList.Count > 0 ? profileList : new List<string> { Path.Combine(profilesDir, "*") } }
            };
        }

        private static List<string> GetFirefoxCachePathsNoVar()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) return new List<string>();
            var baseProfiles = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
            if (!Directory.Exists(baseProfiles)) return new List<string>();
            var result = new List<string>();
            try
            {
                foreach (var profileDir in Directory.GetDirectories(baseProfiles))
                {
                    foreach (var subName in new[] { "cache2", "jumpListCache", "OfflineCache" })
                    {
                        var subPath = Path.Combine(profileDir, subName);
                        if (Directory.Exists(subPath))
                            ChildrenInDirectory(subPath, true, result);
                    }
                }
            }
            catch { }
            return result;
        }

        private static readonly (string Search, string Path)[] FirefoxCacheRulesWithProfile =
        {
            ("file", "$$profile$$/netpredictions.sqlite"),
        };

        /// <summary>获取指定浏览器缓存相关的所有路径列表（与 BleachBit Cache 选项一致）。</summary>
        public static List<string> GetCachePaths(BrowserKind browser)
        {
            var allPaths = new List<string>();
            Dictionary<string, List<string>> vars = null;
            (string Search, string Path)[] rules = null;

            switch (browser)
            {
                case BrowserKind.Edge:
                    vars = GetEdgeVars();
                    rules = EdgeCacheRules;
                    break;
                case BrowserKind.Chrome:
                    vars = GetChromeVars();
                    rules = ChromeCacheRules;
                    break;
                case BrowserKind.Firefox:
                    allPaths.AddRange(GetFirefoxCachePathsNoVar());
                    vars = GetFirefoxVars();
                    rules = FirefoxCacheRulesWithProfile;
                    break;
                default:
                    return allPaths;
            }

            if (vars != null && rules != null)
            {
                foreach (var (search, pathTemplate) in rules)
                {
                    foreach (var resolved in ResolvePath(pathTemplate, vars))
                        allPaths.AddRange(EnumeratePaths(resolved, search));
                }
            }

            return allPaths.Distinct().ToList();
        }

        private static long GetPathSize(string path)
        {
            try
            {
                if (File.Exists(path)) return new FileInfo(path).Length;
                return 0;
            }
            catch { return 0; }
        }

        /// <summary>获取指定浏览器缓存占用字节数（对路径列表逐项取文件大小并求和）。</summary>
        public static long GetCacheSize(BrowserKind browser)
        {
            var paths = GetCachePaths(browser);
            long sum = 0;
            foreach (var p in paths) sum += GetPathSize(p);
            return sum;
        }

        /// <summary>清理指定浏览器缓存（先删文件，再删目录，目录按深度从深到浅递归删除）。</summary>
        public static void CleanCache(BrowserKind browser)
        {
            var paths = GetCachePaths(browser);
            var files = paths.Where(p => File.Exists(p)).ToList();
            var dirs = paths.Where(p => Directory.Exists(p)).OrderByDescending(p => p.Length).ToList();
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { }
            }
            foreach (var d in dirs)
            {
                try
                {
                    if (Directory.Exists(d))
                        Directory.Delete(d, true);
                }
                catch { }
            }
        }
    }
}
