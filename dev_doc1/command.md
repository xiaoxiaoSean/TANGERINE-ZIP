# TANGERINE ZIP 命令行操作手册

本文对应当前仓库中的 `TANGERINE-ZIP.exe`。同一个程序同时支持图形界面和命令行：**无参数**时打开主窗口；只传入**一个已经存在的文件路径**时在主窗口打开该文件；第一个参数是 `help`、`compress`、`extract` 或 `list` 时在终端执行命令，不打开主窗口。资源管理器右键菜单和内部归档工作进程使用保留参数，不属于公开命令行接口。

下文以 PowerShell 为例。在 exe 所在目录执行时，可以写 `./TANGERINE-ZIP.exe`；在其他目录调用时，建议写完整路径并使用调用运算符 `&`：

```powershell
& "C:\Program Files\TANGERINE ZIP\TANGERINE-ZIP.exe" help
```

`<...>` 代表必填值，`[...]` 代表可选项，末尾的 `...` 代表可以重复。示例中的尖括号只是说明符，实际输入时不要保留。含空格的路径要加引号。命令名不区分大小写；长参数名称（例如 `--on-conflict`）区分大小写，按本文小写形式输入。选项可以放在位置参数之间；同一选项通常只能出现一次，`--entry` 例外。使用单独的 `--` 可以结束选项解析，此后的参数均按路径处理，因此它后面不能再写选项。

## 使用前准备

`compress`、`extract` 和 `list` 都会读取 **exe 同目录**下的无后缀 `TEMP_D`。该文件保存临时目录的绝对路径。先启动一次图形界面按提示设置，之后也可以在“设置 → 更多”修改。命令行不会弹出目录选择窗口；若 `TEMP_D` 缺失、目录不可达、不可写或空间不足，会向标准错误输出报告错误和 StageCode，并提示回到图形界面设置。`help` 无需设置临时目录。

命令行与图形界面共用归档工作进程。创建和解压前会检查目标磁盘、内存及临时目录的可用空间；解压期间也会复核空间。临时工作文件由程序管理，输出压缩包与解压结果则位于你指定的位置。

## 快速开始

```powershell
./TANGERINE-ZIP.exe help
./TANGERINE-ZIP.exe compress "D:\Backup\photos.zip" "C:\Photos"
./TANGERINE-ZIP.exe list "D:\Backup\photos.zip"
./TANGERINE-ZIP.exe extract "D:\Backup\photos.zip" "D:\Restored"
```

创建后可先运行 `list` 确认成员路径，再决定是否使用 `--entry` 只解压一部分。创建命令拒绝已存在的输出路径；解压命令遇到已存在的目标文件时，默认终止任务。

## `help`：查看用法

```text
TANGERINE-ZIP.exe help [command]
TANGERINE-ZIP.exe --help
TANGERINE-ZIP.exe -h
```

`command` 可为 `compress`、`extract` 或 `list`。不填写或填写其他名称时显示总览；`help` 不进行归档读写，不要求 `TEMP_D`。例如：

```powershell
./TANGERINE-ZIP.exe help compress
./TANGERINE-ZIP.exe help extract
./TANGERINE-ZIP.exe help list
```

## `compress`：创建压缩包

```text
TANGERINE-ZIP.exe compress <output> <source>... [options]
```

`output` 是新压缩包路径；至少提供一个 `source`，来源可以是普通文件或目录。相对路径按终端的当前工作目录解析。输出路径、同名目录或 `output + ".001"` 已存在时命令拒绝执行。RAR 分卷还会检查同名 `.part1.rar`。压缩时使用现有的后台工作进程和写入前资源检查。

### 格式与来源

| 输出后缀 / `--format` 值 | 格式 | 来源要求 | 密码 |
|---|---|---|---|
| `.zip` / `zip` | ZIP | 一个或多个文件、目录 | 支持 |
| `.7z` / `7z` | 7z | 一个或多个文件、目录 | 支持 |
| `.rar` / `rar` | RAR | 一个或多个文件、目录；exe 同目录需有官方 `rar.exe` | 支持 |
| `.tar` / `tar` | TAR | 一个或多个文件、目录 | 不支持 |
| `.gz` / `gz` 或 `gzip` | GZip | 恰好一个普通文件 | 不支持 |
| `.bz2` / `bz2` 或 `bzip2` | BZip2 | 恰好一个普通文件 | 不支持 |
| `.xz` / `xz` | XZ | 恰好一个普通文件 | 不支持 |
| `.lz4` / `lz4` | LZ4 | 恰好一个普通文件 | 不支持 |
| `.zst` / `zst` 或 `zstd` | Zstandard | 恰好一个普通文件 | 不支持 |
| `.iso` / `iso` | ISO | 一个或多个文件、目录 | 不支持 |
| `.wim` / `wim` | WIM | 一个或多个文件、目录 | 不支持 |

省略 `--format` 时，只根据**最后一个后缀**判断格式。例如 `archive.tar.gz` 会按 GZip 创建，仍只能输入一个普通文件；命令不会自动先创建 TAR。无后缀或使用自定义后缀时，可显式指定 `--format`。`--format` 指定的格式与输出后缀不一致时，以 `--format` 为准，建议让后缀与实际格式一致，方便其他软件识别。

### 压缩参数

| 参数 | 取值与默认值 | 作用和边界 |
|---|---|---|
| `--format VALUE` | 上表列出的值；默认由输出后缀推断 | 选择输出格式。 |
| `--password TEXT` | 可选 | ZIP、7z、RAR 的密码。当前只接受可打印 ASCII 字符；密码会出现在主进程命令行中。 |
| `--password-env NAME` | 可选 | 从环境变量 `NAME` 读取密码；与 `--password` 互斥。变量缺失或为空会报错。密码仍可能出现在实际压缩工具的子进程参数中。 |
| `--level N` | ZIP/7z：`0–9`；RAR：`0–5`；默认 `5` | 压缩等级。更高值通常消耗更多 CPU 时间，实际压缩率取决于数据。 |
| `--method NAME` | `Default`、`Deflate`、`LZMA2`；默认 `Default` | ZIP 不接受 `LZMA2`；7z 不接受 `Deflate`。RAR 使用自身算法，此参数不会切换 RAR 算法。 |
| `--dictionary N` | `1–1024` MiB；默认 `16` | 7z 和 RAR 写入器使用；ZIP 写入器不使用该值。更大的字典可能显著增加压缩和解压内存需求。 |
| `--threads N` | `0–128`；默认 `0`（自动） | 调整支持该设置的 ZIP、7z、RAR 写入工具的并行度。高值会增加 CPU 和内存占用。 |
| `--memory-limit N` | `0–1048576` MiB；默认 `0`（不设工具内存上限） | 对使用外部工具的 ZIP、7z、RAR 压缩进程施加内存上限；过低会使任务失败。资源预检仍独立进行。 |
| `--volume N` | `0–1048576` MiB；默认 `0`（不分卷） | 由 ZIP、7z、RAR 写入工具生成分卷；需要保留全部卷才能恢复。 |

只要提供 `--level`、`--method`、`--dictionary`、`--threads`、`--memory-limit` 或 `--volume` 之一，就启用该任务的高级写入方式；这些参数仅允许用于 ZIP、7z、RAR。未提供高级参数时使用格式的默认写入方式。大字典、较多线程和较高等级可能增加耗时或使低配置设备解压缓慢；改变默认算法、分卷或加密方式可能降低旧版解压软件的兼容性。

示例：

```powershell
# 把整个目录放进 ZIP
./TANGERINE-ZIP.exe compress "D:\Backup\project.zip" "C:\Work\Project"

# 多来源、明确选择格式与等级
./TANGERINE-ZIP.exe compress "D:\Backup\bundle.7z" "C:\Work\readme.txt" "C:\Work\Images" --format 7z --level 7 --threads 4

# 密码取自本次 PowerShell 会话的环境变量
$env:TZIP_PASSWORD = "example-password"
./TANGERINE-ZIP.exe compress "D:\Backup\private.zip" "C:\Work\private.txt" --password-env TZIP_PASSWORD

# 将单个文件压成 GZip；不会自动打包目录
./TANGERINE-ZIP.exe compress "D:\Backup\database.gz" "C:\Data\database.sql"
```

RAR 创建依赖用户自行取得并放在 exe 同目录的官方 `rar.exe`。`DONT_CHECK_RAR_EXE_AT_START` 只能关闭 GUI 启动时的提示，不能免除执行 RAR 创建时对 `rar.exe` 的检查。

## `list`：列出成员

```text
TANGERINE-ZIP.exe list <archive> [--password TEXT | --password-env NAME] [--encoding NAME] [--json]
```

`archive` 必须是现有且受支持的压缩包文件。读取成员时使用文件内容检测格式，而不是只看文件名后缀。`--password` 和 `--password-env` 与 `compress` 的规则相同。`--encoding` 指定成员**文件名**的解码方式，适合处理某些旧压缩包的乱码名称；例如 `--encoding gb18030`。编码名称无效时命令报错；该选项不转换文件内容。

默认输出到标准输出，首行为表头，之后每个成员一行，四列由制表符分隔：

| 列 | 含义 |
|---|---|
| 路径 | 压缩包内的成员路径；目录通常以 `/` 结尾。成员路径里的反斜杠、制表符、回车和换行会写成转义文本。 |
| 原始字节数 | 原始大小；格式不能可靠提供时为 `-`。 |
| 压缩后字节数 | 成员压缩大小；无法可靠获取时为 `-`。 |
| 压缩方式 | 例如 `Deflate` 或 `LZMA2`；无法获取时为 `-`。 |

这四列中的数字是**字节数**，不自动换算 MiB。控制台文字和表头按当前 UI 语言本地化；适合程序读取时建议使用 `--json`。

`--json` 输出一行 JSON 数组，每个元素包含下列属性：

| 属性 | 类型 | 含义 |
|---|---|---|
| `Key` | 字符串 | 成员在归档内的完整路径。 |
| `IsDirectory` | 布尔值 | 是否目录。 |
| `Size` | 整数 | 原始字节数；`SizeKnown=false` 时 `0` 只是占位值。 |
| `CompressedSize` | 整数或 `null` | 压缩后字节数。 |
| `CompressionMethod` | 字符串或 `null` | 压缩方式。 |
| `IsEncrypted` | 布尔值或 `null` | 是否加密；`null` 表示无法获取。 |
| `LastModifiedTime` | 日期时间或 `null` | 归档提供的修改时间。 |
| `Crc` | 整数或 `null` | 归档提供的 CRC 值。 |
| `SizeKnown` | 布尔值 | `Size` 是否为可靠的成员原始大小。 |

`null` 与 `SizeKnown=false` 表示元数据不可用，不应当当作 0 或未加密。ISO、WIM、单文件压缩流等格式能提供的成员元数据不同。`list` 只列出当前归档的成员；例如对 `.tar.gz` 列表不会自动展开 GZip 内的 TAR。`--json` 适合与 PowerShell 的 `ConvertFrom-Json` 配合：

```powershell
./TANGERINE-ZIP.exe list "D:\Backup\photos.zip"
./TANGERINE-ZIP.exe list "D:\Backup\photos.zip" --json | ConvertFrom-Json
./TANGERINE-ZIP.exe list "D:\Old\archive.zip" --encoding gb18030
```

## `extract`：解压

```text
TANGERINE-ZIP.exe extract <archive> <destination> [options]
```

`archive` 是现有且受支持的压缩包；`destination` 是目标目录，可以尚未创建。开始写入前先列出归档、检查所选成员及目标磁盘和内存余量。默认解压全部成员。命令行不弹出交互式冲突或危险文件对话框，必须用参数预先确定处理方式。

| 参数 | 取值与默认值 | 行为 |
|---|---|---|
| `--password TEXT` / `--password-env NAME` | 可选，二选一 | 解密密码；规则同 `compress`。 |
| `--encoding NAME` | 可选 | 按指定编码解码成员文件名，不修改文件内容。 |
| `--entry PATH` | 可重复；默认不筛选 | 只解压指定成员。`PATH` 必须与 `list` 中的 `Key` 完全一致，包含成员目录层级；选中目录时包含其后代。 |
| `--on-conflict abort\|overwrite\|skip` | 默认 `abort` | 目标文件已存在时，分别终止任务、覆盖、跳过。 |
| `--on-issue abort\|skip` | 默认 `abort` | 归档路径穿透、可疑巨大解压体积或单成员读取失败时，分别终止任务、跳过该成员。 |

`--on-conflict overwrite` 会覆盖已有的目标文件，请确认目标目录；`skip` 会保留已有文件。`--on-issue skip` 可以让其他可读成员继续解压，但命令最终返回 `0` 时也可能有成员被跳过；当前命令行**不输出逐成员跳过清单**，需要完整性保证时应另行检查结果。无论设置什么选项，路径穿透成员都不会按归档里的危险路径写出；命令行没有将危险成员重定向到其他目录的参数。

解压任务不是整个归档的事务。若中途取消或失败，之前成功写出的成员可能仍在目标目录；正在写入的单成员使用临时文件处理。`abort` 的目标冲突通常以取消状态结束。单文件压缩流按压缩文件名生成一个输出文件；`.tar.gz` 不会在命令行中自动执行 GUI 的内层 TAR 展开流程，若需要展开 TAR，可先解出 TAR，再对 TAR 调用一次 `extract`。

示例：

```powershell
# 全部解压；若遇到已有目标文件，默认终止
./TANGERINE-ZIP.exe extract "D:\Backup\photos.zip" "D:\Restored"

# 只解压两个指定成员，跳过已存在的目标文件
./TANGERINE-ZIP.exe extract "D:\Backup\photos.zip" "D:\Restored" --entry "2026/a.jpg" --entry "2026/b.jpg" --on-conflict skip

# 已知目标目录可覆盖时，允许覆盖；危险或失败成员仍默认终止
./TANGERINE-ZIP.exe extract "D:\Backup\photos.zip" "D:\Restored" --on-conflict overwrite
```

## 标准输出、错误输出与退出码

帮助、列表和成功提示写入**标准输出**；失败信息写入**标准错误输出**，格式通常为 `[StageCode] 错误说明`。输出使用 UTF-8，提示语言取决于程序运行时的 UI 文化。`Ctrl+C` 会请求取消工作进程和当前任务。

| 退出码 | 含义 |
|---:|---|
| `0` | 命令完成。使用 `skip` 时仍可能跳过冲突或问题成员。 |
| `1` | 参数错误、输入或格式错误、空间不足、密码错误、压缩或解压失败等。 |
| `2` | 操作被取消；默认冲突策略 `abort` 遇到已有目标文件时也可能返回此码。 |

常见命令行阶段码包括：`CLINE0002`（输出已存在）、`CLINE0003`（格式不支持密码）、`CLINE0004`（指定成员不存在）、`CLINE0005`（输入不是受支持归档）、`CLINE0006`（密码环境变量不可用）、`CLINE0007`（参数无效）、`CLINE0008`（密码包含不支持的字符）。临时目录相关错误使用 `TMPDR` 前缀；工作进程和归档服务可能返回各自的阶段码。完整阶段码登记见项目的 `TANGERINE-ZIP/Resources/StageList.txt`。

PowerShell 中可以检查退出码，并把错误输出单独保存：

```powershell
./TANGERINE-ZIP.exe list "D:\Backup\photos.zip" --json > "D:\Backup\members.json" 2> "D:\Backup\list-errors.txt"
if ($LASTEXITCODE -ne 0) {
    Write-Error "TANGERINE ZIP 命令失败，退出码：$LASTEXITCODE"
}
```

## 参数与安全注意事项

- `--password` 会把密码放进命令行参数；`--password-env` 可避免把密码直接写在主命令中，但某些格式的外部写入工具仍需通过子进程参数接收密码。同一用户权限下运行的其他进程可能看到这些参数。两种方式都只接受可打印 ASCII 密码。
- 对不支持密码的格式传入密码会报错；该程序不会把 TAR、ISO 或单文件流伪装成可加密格式。
- `--entry` 的值是**归档内部路径**，不是磁盘路径。先用 `list --json` 查看 `Key`，尤其适用于名称包含空格或非 ASCII 字符的成员。
- `--encoding` 只影响压缩包内文件名的解码。一般 Unicode 归档无需指定；错误编码可能使 `--entry` 无法匹配。
- `--format` 只用于创建；`list` 和 `extract` 根据输入文件内容检测类型。自定义后缀的输出可以指定格式，但建议使用常见后缀。
- 分卷压缩需要保留所有卷。ZIP/7z 分卷通常从 `<output>.001` 开始；RAR 使用 `.part1.rar` 等名称。打开或解压时应从第一卷开始。
- 若是脚本自动化，建议先把输出写到新的目录，使用默认的 `abort` 冲突策略，并检查退出码及输出文件。需要接受跳过的场景才显式使用 `skip`。
