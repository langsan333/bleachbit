# BleachBit 回收站占用磁盘容量的逻辑流程

本文档梳理 BleachBit 在 Windows 下**如何得到并显示**回收站占用的磁盘容量，以便其他项目（如 C# RecycleBinHelper）与之保持一致。

---

## 1. 结论（与 SHQueryRecycleBin 无关）

**界面上显示的回收站“容量”不是来自 SHQueryRecycleBin**，而是：

- 对 **get_recycle_bin()** 返回的**每一条路径**调用 **FileUtilities.getsize(path)**；
- 在 **Worker** 中把这些 size **累加**成 `total_bytes`，再显示给用户。

因此，要复现“和原软件一致”的容量，应实现：**容量 = 对“回收站文件列表”中每条路径取“文件大小”并求和**。

---

## 2. 流程概览

```
用户勾选「系统 → 回收站」并点击「预览」
    → Worker.clean_operation('recycle_bin')
    → backends['system'].get_commands('recycle_bin')
    → 对每条 path in Windows.get_recycle_bin(): yield Command.Delete(path)
    → 对每个 Command.Delete(path): Worker.execute(cmd, ...) 即 cmd.execute(False)
    → Command.Delete.execute(False) 内: size = FileUtilities.getsize(self.path); yield ret{'size': size}
    → Worker 收到 ret: self.size += ret['size'], self.total_bytes += ret['size']
    → 界面显示 total_bytes（即“容量”）
```

所以：**容量 = Σ FileUtilities.getsize(path)**，其中 path 遍历 **get_recycle_bin()** 返回的全体路径。

---

## 3. 各环节说明

### 3.1 回收站“命令”从哪来（Cleaner.get_commands）

- **位置**：`bleachbit/Cleaner.py`，`System` 清理器，`option_id == 'recycle_bin'` 分支。
- **逻辑**：
  - 遍历 `Windows.get_recycle_bin()` 的每一个 `path`；
  - 对每个 `path` 执行 `yield Command.Delete(path)`；
  - 最后再 yield 一个 `Command.Function(..., empty_recycle_bin_func, ...)` 用于真正清空回收站（与容量计算无关）。
- **结论**：回收站“容量”对应的数据源就是 **get_recycle_bin()** 的路径集合，每条路径对应一个 **Delete** 命令。

### 3.2 路径列表从哪来（get_recycle_bin）

- **位置**：`bleachbit/Windows.py`，`get_recycle_bin()`。
- **逻辑**：
  - 用 Shell API 取回收站文件夹（CSIDL_BITBUCKET），枚举顶层项；
  - 对每一项用 `GetDisplayNameOf(..., SHGDN_FORPARSING)` 得到实际路径 `path`；
  - 若 `path` 是目录：先 `yield from FileUtilities.children_in_directory(path, True)`（该目录下所有子项，含子目录），再 `yield path`；
  - 若 `path` 是文件：只 `yield path`。
- **结论**：列表 = 回收站内所有“可删除项”的路径（文件 + 目录，目录会先展开子项再包含自身），与 C# 中 **GetFileList()** 应对齐。

### 3.3 单条路径的“大小”从哪来（getsize）

- **位置**：`bleachbit/FileUtilities.py`，`getsize(path)`；Windows 分支。
- **逻辑**：
  1. 使用 **extended_path(path)**（长路径前缀 `\\?\`）后，调用 **win32file.FindFilesW(path)**；
  2. 若 **finddata 非空**：用 `finddata[0][4]`（nFileSizeHigh）与 `finddata[0][5]`（nFileSizeLow）计算  
     `size = (high * (0xFFFFFFFF + 1)) + low`，返回该 size；
  3. 若 **finddata 为空**（例如对目录 FindFilesW 可能不返回文件信息）：退回 **os.path.getsize(path)**。
- **说明**：
  - 对**文件**：通常由 FindFilesW 得到正确大小；
  - 对**目录**：FindFilesW 常无结果，走 os.path.getsize；在 Windows 上对目录可能异常或返回 0，异常在 Command.Delete 里被捕获后 `size = None`，Worker 中只累加 `int`，故目录多数相当于“不贡献”或贡献 0。
- **结论**：单路径大小 = **FindFilesW 得到的大小，或退化为 os.path.getsize**；C# 侧应对“文件”取 Length，对“目录”可视为 0（与 BleachBit 实际效果一致）。

### 3.4 容量如何累加与显示（Worker）

- **位置**：`bleachbit/Worker.py`，`execute(cmd, operation_option)`。
- **逻辑**：
  - 对 `Command.Delete(path).execute(False)` 返回的 `ret`，若 `isinstance(ret['size'], int)` 则  
    `self.size += ret['size']`，`self.total_bytes += ret['size']`；
  - 界面上显示的回收站“可释放”容量即该 **total_bytes**。
- **结论**：**容量 = 所有 Delete 命令返回的 ret['size'] 之和**（仅整数部分；None 或非 int 不累加）。

---

## 4. SHQueryRecycleBin 在 BleachBit 中的用途

- **位置**：`bleachbit/Windows.py`，`empty_recycle_bin(path, really_delete)`。
- **逻辑**：`(bytes_used, num_files) = shell.SHQueryRecycleBin(path)`；若 `really_delete` 为 True 且 `num_files > 0` 则调用 `SHEmptyRecycleBin`；函数返回值是 `bytes_used`。
- **用途**：仅用于“清空回收站”前的查询（以及返回 bytes_used），**不参与**界面“预览”时显示的回收站容量。界面容量完全来自 **get_recycle_bin() + getsize() 的逐项求和**。

---

## 5. 对 C# 实现的约束（与 BleachBit 一致）

| 项目         | 要求 |
|--------------|------|
| 路径列表     | 与 **get_recycle_bin()** 一致：Shell 枚举回收站，目录先展开子项再包含自身；即现有 **GetFileList()**。 |
| 单路径大小   | 与 **FileUtilities.getsize(path)** 一致：优先用 FindFilesW 风格得到的大小（文件），目录可视为 0。 |
| 总容量       | **容量 = Σ 单路径大小**，即对 GetFileList() 中每条路径取“文件大小”后求和。 |
| SHQueryRecycleBin | 可选保留用于“项数/系统级统计”，但**界面主容量应使用“按列表求和”**。 |

---

## 6. 参考代码位置

- 回收站命令与路径来源：`bleachbit/Cleaner.py`（recycle_bin 分支）  
- 路径枚举：`bleachbit/Windows.py`（get_recycle_bin）  
- 单路径大小：`bleachbit/FileUtilities.py`（getsize，extended_path）  
- 容量累加与展示：`bleachbit/Worker.py`（execute），`bleachbit/Command.py`（Delete.execute）
