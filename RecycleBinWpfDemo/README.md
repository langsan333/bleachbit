# RecycleBinWpfDemo

基于 WPF（.NET Framework 4.7.2）的演示项目，包含：

**1. 回收站（RecycleBinHelper）** — 演示并验证：

1. **GetCapacityInfo** — 获取回收站占用字节数与项数  
2. **GetFileList** — 获取回收站中所有文件/目录路径列表  
3. **EmptyRecycleBin** — 清空回收站（无确认、无进度窗、无声音）

**2. 浏览器缓存（BrowserCache）** — 演示并验证 Edge / Chrome / Firefox 缓存：
- **GetCachePaths(browser)** — 获取该浏览器缓存相关的所有路径列表（与 BleachBit Cache 选项一致）
- **GetCacheSize(browser)** — 获取占用字节数（对路径列表逐项取文件大小并求和）
- **CleanCache(browser)** — 清理该浏览器缓存（先删文件，再递归删目录）

逻辑移植自 [BleachBit](https://www.bleachbit.org) 的 Windows 回收站与浏览器 CleanerML（见 `doc/recycle-bin-capacity-flow.md`、`doc/browser-cache-flow.md`）。

## 环境要求

- Windows
- .NET Framework 4.7.2（或已安装对应运行时）
- Visual Studio 2017+ 或 MSBuild 15+（用于编译）

## 打开与编译

1. 用 Visual Studio 打开 `RecycleBinWpfDemo.sln`
2. 选择配置（Debug/Release），生成解决方案
3. 运行 `RecycleBinWpfDemo` 项目（F5 或 Ctrl+F5）

或使用命令行：

```bat
cd RecycleBinWpfDemo
msbuild RecycleBinWpfDemo.sln /p:Configuration=Debug
.\RecycleBinWpfDemo\bin\Debug\RecycleBinWpfDemo.exe
```

## 界面说明

**主窗口（回收站）**
- **容量信息**：点击「刷新容量」调用 `GetCapacityInfo()`，显示占用大小（B/KB/MB/GB）与项数  
- **文件列表**：点击「刷新列表」调用 `GetFileList()`，在列表中显示所有路径  
- **清空回收站**：点击「清空回收站」会先弹出确认框，确认后调用 `EmptyRecycleBin()`  
- **浏览器缓存演示**：点击「浏览器缓存演示」打开浏览器缓存演示窗口  

**浏览器缓存窗口**
- 选择 Edge / Chrome / Firefox，点击「获取缓存路径」调用 `GetCachePaths(browser)`  
- 点击「获取占用大小」调用 `GetCacheSize(browser)`  
- 点击「清理该浏览器缓存」会先确认，再调用 `CleanCache(browser)`

## 项目结构

```
RecycleBinWpfDemo/
├── RecycleBinWpfDemo.sln
├── README.md
└── RecycleBinWpfDemo/
    ├── RecycleBinWpfDemo.csproj
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / MainWindow.xaml.cs
    ├── RecycleBinHelper.cs    # 回收站信息收集与清空（BleachBit 逻辑）
    ├── BrowserCache.cs        # 浏览器缓存路径/容量/清理（Edge/Chrome/Firefox，BleachBit 逻辑）
    ├── BrowserCacheWindow.xaml / .xaml.cs  # 浏览器缓存演示窗口
    └── Properties/
        └── AssemblyInfo.cs
```
