# BleachBit 浏览器缓存收集与清理逻辑（Edge / Chrome / Firefox，Windows）

本文档基于 BleachBit 的 CleanerML 与 Action 实现，整理 **Edge、Chrome、Firefox** 在 **Windows** 下“缓存”选项的**路径定义、变量解析、枚举方式**，以及**清理**时如何执行删除。其他项目（如 C# BrowserCache）可按此逻辑实现“获取缓存路径列表 + 统计大小 + 清理”。

---

## 1. 总体流程（与 Edge 缓存文档一致）

1. **变量**：每个浏览器在 CleanerML 中定义 `base`、`profile` 等变量，Windows 只取 `os="windows"` 的 `<value>`；部分变量带 `search="glob"` 需先展开再使用。
2. **路径模板**：`<option id="cache">` 下每条 `<action command="delete" search="..." path="...">` 的 path 中可用 `$$变量名$$`；先做变量替换，再对字符串做环境变量展开（如 `%LocalAppData%`），最后 `normpath`。
3. **枚举**：按 `search` 类型对“已展开的路径”做枚举：
   - `file`：单路径，存在则列表含该项；
   - `glob`：通配符匹配；
   - `walk.all`：对目录递归，产出其下所有文件与子目录；
   - `walk.files`：对目录递归，只产出文件。
4. **清理**：对枚举得到的每条路径执行删除（文件删文件，目录先删内容再删自身）；BleachBit 中由 `Command.Delete(path)` + `FileUtilities.delete()` 完成。

容量统计与回收站一致：对每条路径取“文件大小”并求和（目录视为 0 或递归求和），此处不重复。

---

## 2. Microsoft Edge（Windows）缓存

### 2.1 变量（Windows）

| 变量     | 取值（环境变量未展开） |
|----------|------------------------|
| base     | `%LocalAppData%\Microsoft\Edge\User Data` |
| profile  | `%LocalAppData%\Microsoft\Edge\User Data\Default` |

### 2.2 Cache 规则表（仅 Windows、command=delete）

| search     | 路径模板 |
|------------|----------|
| file       | `$$profile$$/Network Persistent State` |
| walk.all   | `$$base$$/ShaderCache` |
| walk.all   | `$$profile$$/File System` |
| walk.all   | `$$profile$$/Service Worker` |
| walk.all   | `$$profile$$/Storage/ext/*/*def/GPUCache` |
| walk.files | `$$profile$$/GPUCache/` |
| glob       | `$$base$$/B*.tmp` |
| walk.all   | `$$profile$$/Default/Application Cache/` |
| walk.files | `$$profile$$/Cache/` |
| walk.files | `$$profile$$/Media Cache/` |

来源：`cleaners/microsoft_edge.xml` `<option id="cache">`，仅保留 `command="delete"` 且无 `os` 或 `os="windows"` 的 action。

---

## 3. Google Chrome（Windows）缓存

### 3.1 变量（Windows）

| 变量     | 取值（环境变量未展开） |
|----------|------------------------|
| base     | `%LocalAppData%\Google\Chrome\User Data` |
| profile  | `%LocalAppData%\Google\Chrome\User Data\Default` |

### 3.2 Cache 规则表（仅 Windows、command=delete，不含 json）

| search     | 路径模板 |
|------------|----------|
| file       | `$$base$$/Safe Browsing Channel IDs-journal` |
| file       | `$$profile$$/Network Persistent State` |
| file       | `$$profile$$/Network/Network Persistent State` |
| walk.all   | `$$base$$/ShaderCache` |
| walk.all   | `$$profile$$/File System` |
| walk.all   | `$$profile$$/Pepper Data/Shockwave Flash/CacheWritableAdobeRoot/` |
| walk.all   | `$$profile$$/Service Worker` |
| walk.all   | `$$profile$$/Storage/ext/*/*def/GPUCache` |
| walk.files | `$$profile$$/GPUCache/` |
| glob       | `$$base$$/B*.tmp` |
| walk.all   | `$$profile$$/Default/Application Cache/` |
| walk.files | `$$profile$$/Cache/` |
| walk.files | `$$profile$$/Code Cache/` |
| walk.files | `$$profile$$/Media Cache/` |

来源：`cleaners/google_chrome.xml` `<option id="cache">`，仅保留 `command="delete"` 的 action（不含 `command="json"`）。

---

## 4. Firefox（Windows）缓存

### 4.1 变量（Windows）

| 变量     | 取值（环境变量未展开） | 说明 |
|----------|------------------------|------|
| base     | `%AppData%\Mozilla\Firefox` | 单值 |
| profile  | `%AppData%\Mozilla\Firefox\Profiles\*` | **search="glob"**，需先展开为多条 profile 路径再参与替换 |

### 4.2 Cache 规则表（仅 Windows、command=delete）

| search   | 路径模板 | 说明 |
|----------|----------|------|
| walk.all | `%LocalAppData%\Mozilla\Firefox\Profiles\*\cache2` | 无变量，先展开环境变量再 glob 再 walk.all |
| walk.all | `%LocalAppData%\Mozilla\Firefox\Profiles\*\jumpListCache` | 同上 |
| walk.all | `%LocalAppData%\Mozilla\Firefox\Profiles\*\OfflineCache` | 同上 |
| file     | `$$profile$$/netpredictions.sqlite` | profile 为 glob 展开后的每条路径 |

实现时：前三条可先 `ExpandEnvironmentVariables` 再对路径做 glob 匹配目录，再对每个目录 walk.all；第四条需先得到“profile 列表”（对 `%AppData%\Mozilla\Firefox\Profiles\*` 做 glob），再对每个 profile 替换 `$$profile$$` 得到单文件路径，file 枚举。

来源：`cleaners/firefox.xml` `<option id="cache">`，仅保留 Windows 相关路径（含 `%LocalAppData%` 或 `$$profile$$`）。

---

## 5. 对 C# BrowserCache 的约束

| 项目       | 要求 |
|------------|------|
| 变量解析   | Windows 下仅用 `os="windows"` 的变量值；`%LocalAppData%`、`%AppData%` 用 `Environment.ExpandEnvironmentVariables` 展开；Firefox 的 profile 用 glob 展开为列表。 |
| 路径模板   | `$$base$$`、`$$profile$$` 替换为已展开的变量值（profile 为多值时对每条做笛卡尔积或逐条替换）；再用 `Path.Combine` / 替换 `/` 为 `\`、`normpath`。 |
| 枚举       | file：存在则加入；glob：`Directory.GetFiles`/`Directory.GetDirectories` 或手动通配；walk.all：自底向上遍历目录下所有文件与子目录；walk.files：仅文件。 |
| 容量       | 对得到的路径列表，文件取 `FileInfo.Length`，目录取 0（或递归），求和。 |
| 清理       | 对每条路径：文件则 `File.Delete`，目录则先删内容再 `Directory.Delete`；注意顺序（先子后父）。 |

---

## 6. 参考来源

- 变量与规则：`cleaners/microsoft_edge.xml`、`cleaners/google_chrome.xml`、`cleaners/firefox.xml`（各 `<option id="cache">`）。
- 变量解析与枚举：`bleachbit/CleanerML.py`、`bleachbit/Action.py`（expand_multi_var、_set_paths、_get_paths）、`bleachbit/FileUtilities.py`（children_in_directory）。
- 删除：`bleachbit/Command.py`（Delete.execute）、`bleachbit/FileUtilities.py`（delete）。

以上即为 Edge、Chrome、Firefox 在 Windows 下缓存收集与清理的逻辑要点，可供 C# BrowserCache 及 WPF 演示实现时对齐行为。
