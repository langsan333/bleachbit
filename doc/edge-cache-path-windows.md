# Windows 下获取 Edge 缓存路径与文件列表 — 技术说明

本文档基于 BleachBit 的实现，说明在 Windows 上如何**解析变量**、**展开路径**、**按规则枚举**，最终得到 Microsoft Edge 的缓存根路径和所有缓存文件路径列表。其他项目若需实现“获取 Edge 缓存路径 + 列出所有缓存文件”，可直接复用下述逻辑与路径表。

---

## 1. 目标与范围

- **目标**：在 Windows 上得到 Edge 的“缓存”所对应的**根路径**，以及**所有可清理的缓存文件/目录路径列表**（与 BleachBit 的“Cache”选项一致）。
- **范围**：仅讨论路径解析与枚举逻辑，不涉及删除、粉碎、白名单等后续操作。

---

## 2. 核心概念

### 2.1 两层“变量”

1. **CleanerML 变量（如 `base`、`profile`）**  
   在配置里定义，取值与 **OS 过滤** 有关；在 Windows 上只使用 `os="windows"` 的取值。
2. **系统环境变量（如 `%LocalAppData%`）**  
   在得到 CleanerML 变量的**字符串值**之后，再对该字符串做一次环境变量展开。

### 2.2 路径模板与占位符

- 占位符格式：`$$变量名$$`，例如 `$$profile$$`、`$$base$$`。
- 若一条路径中用到多个变量，会对各变量的取值做**笛卡尔积**，生成多条路径（Edge 缓存里 `base`/`profile` 各只有一个 Windows 取值，因此通常只得到一条）。
- 模板中可使用 `/` 或 `\`；在 Windows 上最终会通过 `os.path.normpath` 统一为反斜杠。

### 2.3 四种“搜索类型”（如何从一条路径得到多条路径）

| 类型 | 含义 | 产出 |
|------|------|------|
| `file` | 单个文件或目录 | 若路径存在，则列表仅含该路径（1 个） |
| `glob` | 通配符匹配 | 对路径做 `glob` 匹配，列表为所有匹配项 |
| `walk.all` | 目录下所有内容（含子目录） | 先对路径做 `glob`，再对每个匹配目录做**自底向上**遍历，得到该目录下所有文件和子目录路径（不含该目录本身） |
| `walk.files` | 目录下仅文件 | 同上，但只收集文件，不收集子目录路径 |

---

## 3. Windows 下“缓存”相关的路径定义（与 BleachBit 一致）

以下变量在 **Windows** 上的定义（环境变量未展开前）：

```text
base   = "%LocalAppData%\Microsoft\Edge\User Data"
profile = "%LocalAppData%\Microsoft\Edge\User Data\Default"
```

环境变量展开后（示例，具体以本机为准）：

```text
%LocalAppData% → C:\Users\<UserName>\AppData\Local
```

因此：

```text
base   → C:\Users\<UserName>\AppData\Local\Microsoft\Edge\User Data
profile → C:\Users\<UserName>\AppData\Local\Microsoft\Edge\User Data\Default
```

“缓存”由下面这些**规则**组成（每条规则包含：搜索类型 + 路径模板）。路径模板中 `$$base$$` / `$$profile$$` 需先替换为上面两个变量的值，再对环境变量展开、再 `normpath`。

### 3.1 规则表（Cache 选项）

| # | search 类型 | 路径模板（替换前） |
|---|-------------|---------------------|
| 1 | file | `$$profile$$/Network Persistent State` |
| 2 | walk.all | `$$base$$/ShaderCache` |
| 3 | walk.all | `$$profile$$/File System` |
| 4 | walk.all | `$$profile$$/Service Worker` |
| 5 | walk.all | `$$profile$$/Storage/ext/*/*def/GPUCache` |
| 6 | walk.files | `$$profile$$/GPUCache/` |
| 7 | glob | `$$base$$\B*.tmp` |
| 8 | walk.all | `$$profile$$\Default\Application Cache\` |
| 9 | walk.files | `$$profile$$\Cache\` |
| 10 | walk.files | `$$profile$$\Media Cache\` |

说明：

- 模板中的 `/` 与 `\` 在实现里可混用；展开并 `normpath` 后统一为 `\`。
- `walk.*` 的路径若为目录，会递归其下内容；`walk.all` 会得到子目录和文件，`walk.files` 只得到文件。
- `glob` 的 `*` 匹配任意字符；路径 5 中的 `*` 用于匹配多级目录名。

---

## 4. 逻辑分解：如何从“规则”得到“路径列表”

### 4.1 步骤 1：OS 过滤（只保留 Windows）

仅当“当前平台是 Windows”时，使用上述变量与规则。BleachBit 的判定方式：

```python
# 伪代码 / 与 General.os_match 等价
def os_match(os_str: str, platform: str = sys.platform) -> bool:
    if not os_str:
        return True
    if platform == "win32":
        return os_str == "windows"
    # linux, darwin, bsd 等略
    return False
```

- 变量 `<value os="windows">`：仅当 `os_match("windows")` 为真时加入。
- 规则 `<action ... os="windows">`：仅当为 Windows 时才参与“缓存”路径枚举。

### 4.2 步骤 2：解析变量（CleanerML 变量 → 字符串列表）

对每个 `<var name="...">`，收集所有满足 OS 的 `<value>` 文本，组成列表（多数情况下每个变量只有一个 Windows 值）：

```python
# 伪代码：解析 Windows 下的 base / profile
def get_windows_vars():
    return {
        "base": [os.path.expandvars(r"%LocalAppData%\Microsoft\Edge\User Data")],
        "profile": [os.path.expandvars(r"%LocalAppData%\Microsoft\Edge\User Data\Default")],
    }
```

注意：BleachBit 在解析 XML 时，**先**取 `<value>` 的原始字符串（如 `%LocalAppData%\...`），**再**在“使用该变量展开路径模板”时调用 `os.path.expandvars`。因此若你要硬编码实现，可直接对上述字符串做 `expandvars` 得到一条路径；若支持多值，则变量值为列表。

### 4.3 步骤 3：路径模板展开（$$var$$ + 环境变量 + normpath）

将路径模板中的 `$$变量名$$` 替换为变量值，再展开环境变量并规范化路径：

```python
from itertools import product
import os

def expand_multi_var(template: str, variables: dict) -> list[str]:
    """
    将 template 中的 $$key$$ 替换为 variables[key] 的取值。
    variables 的 value 为 list；多个变量时做笛卡尔积，得到多条路径。
    """
    if not variables or "$$" not in template:
        return [template]

    used = [k for k in variables if f"$${k}$$" in template]
    if not used:
        return [template]

    result = []
    for combo in product(*(variables[k] for k in used)):
        s = template
        for k, v in zip(used, combo):
            s = s.replace(f"$${k}$$", v)
        result.append(s)
    return result

def resolve_path(path_template: str, variables: dict) -> list[str]:
    """单条路径模板 → 多条绝对路径（已展开环境变量、normpath）。"""
    expanded = expand_multi_var(path_template, variables)
    out = []
    for s in expanded:
        s = os.path.expanduser(os.path.expandvars(s))
        if os.name == "nt" and s:
            s = os.path.normpath(s)
        out.append(s)
    return out
```

Windows 下 Edge 的 `base`/`profile` 各一值，因此对每条规则，`resolve_path` 通常得到**一个**绝对路径（或 glob 模式）。

### 4.4 步骤 4：按 search 类型枚举路径

下面用“单条已展开路径”（或 glob 模式）`path` 和 `search` 类型，得到“要清理的路径”列表。与 BleachBit 行为一致：

```python
import glob
import os

def walk_bottom_up(top):
    """自底向上遍历目录：先子目录和文件，再父目录。使用 os.walk(top, topdown=False)。"""
    for dirpath, dirnames, filenames in os.walk(top, topdown=False):
        for d in dirnames:
            yield os.path.join(dirpath, d)
        for f in filenames:
            yield os.path.join(dirpath, f)

def children_in_directory(top, include_dirs: bool):
    """返回目录 top 下的所有文件；若 include_dirs 为 True，还包含子目录。"""
    for dirpath, dirnames, filenames in os.walk(top, topdown=False):
        if include_dirs:
            for d in dirnames:
                yield os.path.join(dirpath, d)
        for f in filenames:
            yield os.path.join(dirpath, f)

def enumerate_paths(resolved_path: str, search: str) -> list[str]:
    """
    resolved_path: 已展开变量与环境变量、normpath 后的单条路径（或含 * 的 glob 模式）。
    search: "file" | "glob" | "walk.all" | "walk.files"
    返回：该规则下所有要操作的路径列表（文件或目录）。
    """
    result = []

    if search == "file":
        if os.path.lexists(resolved_path):
            result.append(resolved_path)
        return result

    if search == "glob":
        for p in glob.iglob(resolved_path):
            result.append(p)
        return result

    if search == "walk.all":
        for expanded in glob.iglob(resolved_path):
            if os.path.isdir(expanded):
                for p in children_in_directory(expanded, include_dirs=True):
                    result.append(p)
        return result

    if search == "walk.files":
        for expanded in glob.iglob(resolved_path):
            if os.path.isdir(expanded):
                for p in children_in_directory(expanded, include_dirs=False):
                    result.append(p)
        return result

    raise ValueError(f"Unknown search type: {search}")
```

要点：

- **file**：不递归，只判断该路径是否存在。
- **glob**：`resolved_path` 可含 `*`（如 `...\B*.tmp`），`iglob` 返回所有匹配项。
- **walk.all**：先 `iglob(resolved_path)`（若为目录则通常只有自身），再对该目录递归，产出其下所有文件和子目录（不含顶层目录自身）。
- **walk.files**：同上，但只产出文件。

若需与 BleachBit 完全一致，目录遍历顺序应使用 `topdown=False`（自底向上），以便“先文件后目录”。

---

## 5. 完整流程：从配置到“所有缓存路径列表”

### 5.1 输入

- 变量表（Windows）：`base`, `profile`（见 3.1）。
- 规则表：上表 3.1 的 10 条 (search, 路径模板)。

### 5.2 算法（伪代码）

```text
FUNCTION get_edge_cache_paths_windows():
  vars = get_windows_vars()   // 含 base, profile，值已 expandvars
  all_paths = []

  FOR EACH (search_type, path_template) IN cache_rules:
    resolved_list = resolve_path(path_template, vars)   // 通常 1 条
    FOR EACH resolved IN resolved_list:
      paths = enumerate_paths(resolved, search_type)
      all_paths.extend(paths)

  RETURN all_paths
```

### 5.3 规则表常量（可直接用于编码）

```python
EDGE_CACHE_RULES_WINDOWS = [
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
]
```

注意：XML 中写的是 `\Default\Application Cache\`，在 Python 里用 `/` 或 `\` 均可，`normpath` 会统一。

---

## 6. 获取“Edge 缓存根路径”的简便方法

若只需要“缓存所在根目录”，不需要枚举所有文件，可只做变量解析，不做 walk/glob：

- **profile（默认配置档）**：  
  `%LocalAppData%\Microsoft\Edge\User Data\Default`  
  对应“缓存”的父目录多为该路径下的子目录（如 `Cache`、`GPUCache`、`Media Cache` 等）。
- **base**：  
  `%LocalAppData%\Microsoft\Edge\User Data`  
  用于 ShaderCache、`B*.tmp` 等。

示例（Python）：

```python
import os

def get_edge_cache_base_paths():
    local_app_data = os.environ.get("LocalAppData", "")
    if not local_app_data:
        return None, None
    base = os.path.normpath(os.path.join(local_app_data, "Microsoft", "Edge", "User Data"))
    profile = os.path.normpath(os.path.join(base, "Default"))
    return base, profile
```

其他项目若只需“根路径”，可直接用上述两个目录；若需要“与 BleachBit 一致的完整缓存文件列表”，则按第 5 节实现 `get_edge_cache_paths_windows()` 即可。

---

## 7. 边界与注意事项

1. **多用户/多 Profile**  
   当前 BleachBit 只使用 `Default` profile；若 Edge 使用其他 profile（如 `Profile 1`），需在变量中增加对应路径并扩展规则。
2. **路径不存在**  
   `file` 若不存在则不会加入列表；`walk.*` 和 `glob` 若目录或匹配为空，也不会加入。实现时无需特别处理“路径不存在”的错误，只要不把不存在的路径加入结果即可。
3. **长路径（Windows）**  
   若需处理超长路径，可对最终路径加 BleachBit 的 `extended_path()` 逻辑（前缀 `\\?\`）；枚举与删除时使用该前缀。
4. **权限**  
   枚举时若遇无权限目录，可跳过或记录；与“获取路径列表”的解耦可保持本文档只关心路径解析与枚举逻辑。

---

## 8. 参考来源

- 变量与规则：`cleaners/microsoft_edge.xml`（`<option id="cache">`）。
- 变量解析与 OS 过滤：`bleachbit/CleanerML.py`（`handle_cleaner_var`、`os_match`）。
- 路径展开：`bleachbit/Action.py`（`expand_multi_var`、`_set_paths`）。
- 枚举：`bleachbit/Action.py`（`_get_paths` 中 `get_file` / `get_walk_all` / `get_walk_files`、`glob.iglob`），`bleachbit/FileUtilities.py`（`children_in_directory`、`walk`）。
- OS 匹配：`bleachbit/General.py`（`os_match`）。

以上即为 Windows 下获取 Edge 缓存路径与所有缓存文件列表的完整逻辑与实现要点，可供其他项目直接按步骤编码实现。

---

## 9. 参考实现（Python，可运行）

以下代码不依赖 BleachBit，仅用标准库 + `glob`，可直接在其他项目中复用或改写。

```python
"""
Windows 下获取 Microsoft Edge 缓存路径及所有缓存文件列表。
参考：BleachBit cleaners/microsoft_edge.xml <option id="cache">。
"""
import glob
import os
from itertools import product


def get_windows_vars():
    """Windows 下 Edge 的 base / profile（已展开环境变量）。"""
    local = os.environ.get("LocalAppData", "")
    if not local:
        return {}
    base = os.path.normpath(os.path.join(local, "Microsoft", "Edge", "User Data"))
    profile = os.path.normpath(os.path.join(base, "Default"))
    return {"base": [base], "profile": [profile]}


def expand_multi_var(template: str, variables: dict) -> list:
    """将 template 中的 $$key$$ 替换为 variables[key]，支持多值笛卡尔积。"""
    if not variables or "$$" not in template:
        return [template]
    used = [k for k in variables if f"$${k}$$" in template]
    if not used:
        return [template]
    result = []
    for combo in product(*(variables[k] for k in used)):
        s = template
        for k, v in zip(used, combo):
            s = s.replace(f"$${k}$$", str(v))
        result.append(s)
    return result


def resolve_path(path_template: str, variables: dict) -> list:
    """单条路径模板 → 多条绝对路径（expandvars + normpath）。"""
    expanded = expand_multi_var(path_template, variables)
    out = []
    for s in expanded:
        s = os.path.expanduser(os.path.expandvars(s))
        if os.name == "nt" and s:
            s = os.path.normpath(s)
        out.append(s)
    return out


def children_in_directory(top: str, include_dirs: bool):
    """自底向上遍历目录，产出文件；若 include_dirs 则也产出子目录。"""
    for dirpath, dirnames, filenames in os.walk(top, topdown=False):
        if include_dirs:
            for d in dirnames:
                yield os.path.join(dirpath, d)
        for f in filenames:
            yield os.path.join(dirpath, f)


def enumerate_paths(resolved_path: str, search: str) -> list:
    """根据 search 类型枚举要操作的路径列表。"""
    result = []
    if search == "file":
        if os.path.lexists(resolved_path):
            result.append(resolved_path)
        return result
    if search == "glob":
        result.extend(glob.iglob(resolved_path))
        return result
    if search == "walk.all":
        for expanded in glob.iglob(resolved_path):
            if os.path.isdir(expanded):
                result.extend(children_in_directory(expanded, True))
        return result
    if search == "walk.files":
        for expanded in glob.iglob(resolved_path):
            if os.path.isdir(expanded):
                result.extend(children_in_directory(expanded, False))
        return result
    raise ValueError(f"Unknown search type: {search}")


# 与 BleachBit microsoft_edge.xml cache 选项一致（Windows 部分）
EDGE_CACHE_RULES_WINDOWS = [
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
]


def get_edge_cache_paths_windows() -> list:
    """返回 Windows 下 Edge 缓存相关的所有路径列表（文件 + 目录）。"""
    variables = get_windows_vars()
    if not variables:
        return []
    all_paths = []
    for search_type, path_template in EDGE_CACHE_RULES_WINDOWS:
        for resolved in resolve_path(path_template, variables):
            all_paths.extend(enumerate_paths(resolved, search_type))
    return all_paths


# 仅获取根路径（不枚举文件）
def get_edge_cache_base_paths():
    """返回 (base, profile) 或 (None, None)。"""
    v = get_windows_vars()
    if not v:
        return None, None
    return v["base"][0], v["profile"][0]
```

使用示例：

```python
if __name__ == "__main__":
    import sys
    if os.name != "nt":
        print("此逻辑仅适用于 Windows")
        sys.exit(1)
    base, profile = get_edge_cache_base_paths()
    print("base:", base)
    print("profile:", profile)
    paths = get_edge_cache_paths_windows()
    print("缓存路径数量:", len(paths))
    for p in paths[:20]:
        print(" ", p)
```
