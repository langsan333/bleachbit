# RecycleBinWpfDemo

基于 WPF（.NET Framework 4.7.2）的回收站功能演示项目，用于演示并验证 `RecycleBinHelper` 类的三项功能：

1. **GetCapacityInfo** — 获取回收站占用字节数与项数  
2. **GetFileList** — 获取回收站中所有文件/目录路径列表  
3. **EmptyRecycleBin** — 清空回收站（无确认、无进度窗、无声音）

逻辑移植自 [BleachBit](https://www.bleachbit.org) 的 Windows 回收站实现。

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

- **容量信息**：点击「刷新容量」调用 `GetCapacityInfo()`，显示占用大小（B/KB/MB/GB）与项数  
- **文件列表**：点击「刷新列表」调用 `GetFileList()`，在列表中显示所有路径  
- **清空回收站**：点击「清空回收站」会先弹出确认框，确认后调用 `EmptyRecycleBin()`

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
    └── Properties/
        └── AssemblyInfo.cs
```
