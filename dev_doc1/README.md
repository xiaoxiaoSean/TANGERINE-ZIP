# Tangerine ZIP 开发文档

密码压缩、解压、加密封装与安全清理的详细设计见 [`PASSWORD_ARCHIVES.md`](PASSWORD_ARCHIVES.md)。

## 1. 实现概览

项目以 .NET 10 WinForms 为 UI，归档逻辑集中在 `Services/ArchiveService.cs`。主窗体只负责选择文件、展示条目、导航、覆盖策略、进度及错误提示。所有耗时操作均通过 `Task`/异步流或后台任务执行，UI 线程不会执行压缩、解压或镜像扫描。

发布目标为 Windows x64 自包含单文件。`PublishSingleFile`、`SelfContained` 与 `IncludeNativeLibrariesForSelfExtract` 已写入项目文件；XZ 和 WIM 的原生库由 .NET 单文件宿主解出后按 `NATIVE_DLL_SEARCH_DIRECTORIES` 定位。

## 2. 格式能力矩阵

| 格式 | 列出 | 解压 | 创建 | 实现 |
|---|---:|---:|---:|---|
| ZIP | 是 | 是 | 是 | .NET ZipArchive / Deflate |
| RAR | 是 | 是 | 条件支持 | SharpCompress 解压；官方 `rar.exe` 创建 |
| 7Z | 是 | 是 | 是 | SharpCompress / LZMA2 |
| TAR | 是 | 是 | 是 | .NET System.Formats.Tar / PAX |
| GZ | 是 | 是 | 是 | .NET GZipStream |
| BZ2 | 是 | 是 | 是 | SharpCompress BZip2Stream |
| XZ | 是 | 是 | 是 | Joveler.Compression.XZ / liblzma |
| LZ4 | 是 | 是 | 是 | K4os.Compression.LZ4.Streams |
| ZSTD | 是 | 是 | 是 | SharpCompress ZStandard streams |
| ISO | 是 | 是 | 是 | DiscUtils ISO9660/Joliet |
| WIM | 是 | 是 | 是 | ManagedWimLib / wimlib |

RAR 创建依赖用户按 WinRAR 官方许可取得的 `rar.exe`（官方说明与下载：https://www.rarlab.com/rardos.htm、https://www.rarlab.com/download.htm）。程序启动时以及每次执行 RAR 创建前都会检查应用程序目录；缺失时显示 `RARTL0001`，但不会阻止其他功能。如果应用程序目录存在无后缀标记文件 `DONT_CHECK_RAR_EXE_AT_START`，程序会跳过启动检查和启动警告；实际执行 RAR 创建时仍会强制重新检查。外部进程使用 `ArgumentList` 传参、异步读取输出并解析百分比，取消时终止进程。`rar.exe` 不会被本项目打包或重新分发。

GZ、BZ2、XZ、LZ4、ZSTD 是单文件压缩流，因此创建时只能选择一个普通文件。ZIP、7Z、TAR、ISO、WIM 服务层支持多文件；ISO 使用 Joliet 文件名，WIM 使用 LZMS。

所有列出的格式均可选择 TZIP 密码保护。由于 TAR、GZ、BZ2、XZ、LZ4、ZSTD、ISO、WIM 的标准格式不定义密码，创建密码文件时统一使用 AES-256-GCM 的 `TZIPENC2` 外层封装；未设置密码时仍输出原生标准格式。第三方原生加密 ZIP、RAR、7Z 通过 SharpCompress 密码读取流程处理。

## 3. 核心设计

### ArchiveService

- `ListAsync`：按检测类型列出归档项目；单文件流显示由压缩文件名推导的虚拟条目。
- `ExtractAsync`：统一执行全部或选中条目解压。
- `CreateAsync`：统一创建归档；先写同目录临时文件，成功后原子替换最终文件，失败时清理临时文件。
- `GetSafeTargetPath`：对每个输出路径执行规范化和根目录边界检查，阻止 Zip Slip/path traversal。
- WIM 先解至独立临时目录，再按“全部覆盖/全部跳过”策略合并，保证覆盖语义一致。
- `AnalyzeNestedTarAsync` 完全按文件内容检查内层 TAR。单个内层 TAR 会在打开时自动展示其内容；多个 TAR 保留为可选择条目，一键解压到“外层压缩包名/内层 TAR 名”目录。
- ZIP 写入显式设置 UTF-8 标志，7Z 使用 Unicode 条目名，TAR 使用 PAX Unicode 路径，避免文件名按系统 ANSI 代码页解释。

### 进度

`ArchiveProgress` 包含百分比和当前条目。流格式按已读取输入字节计算；归档格式按条目字节累计；ISO 按文件数据累计；WIM 使用 wimlib 回调。`Progress<T>` 将更新自动封送回 WinForms UI 线程。

### 覆盖策略

开始解压前统一询问：

- “是”：覆盖全部现有文件；
- “否”：跳过全部现有文件；
- “取消”：不开始操作。

保存压缩包时还使用 WinForms 的 `OverwritePrompt`。输出文件不能同时是输入文件（`ARCSV0009`）。

## 4. 本地化与主题

语言中性值为 `en-US`。`LanguageManager.Get` 先查询当前 UI 文化，缺失时回退到 `en-US`，最后才返回资源键。资源已覆盖：

- `LanguageResource.resx`（中性英文）
- `LanguageResource.en-US.resx`
- `LanguageResource.zh-CN.resx`
- `LanguageResource.zh-TW.resx`
- `LanguageResource.zh-HK.resx`
- `LanguageResource.zh-MO.resx`

窗体运行时调用 `DarkTheme.Apply`，递归设置黑色背景和浅色前景，也处理菜单及下拉项。新增的所有用户可见文字均从 `LanguageManager` 获取。

光效由 MSBuild 属性 `ENABLE_LIGHT` 控制。默认值为 `false`，因此默认发布配置不会定义同名预处理符号，也不会实例化光效窗口或计时器。需要光效时使用 `-p:ENABLE_LIGHT=true`。

## 5. StageCode 规范

StageCode 固定为 9 个字符：5 字符 `stageHead` + 4 字符 `stageDetail`。

| Stage head | 模块 |
|---|---|
| `F0001` | 主窗体和 UI 工作流 |
| `F0002` | 文件选择窗体 |
| `ARCSV` | 归档服务 |
| `NESTR` | TAR 嵌套分析与解压 |
| `RARTL` | 外部 RAR 工具 |
| `CTXMN` | Win11 右键菜单安装、移除与回滚服务 |
| `CTXWZ` | 右键菜单设置向导 |
| `CTXCH` | 提权后的证书所有权辅助程序 |

展示统一调用：

```csharp
MessageBox.Show(
    MessageTipGenerator.GenerateTip(
        "F00010006",
        exception.Message)); //F00010006
```

服务层使用 `StageException` 保留原始异常作为 `InnerException`，UI 优先显示其 StageCode。

### 本次修复：Unicode、进度与强制停止

- ZIP 改用 .NET `ZipArchive` 并显式传入 `Encoding.UTF8`，确保本地头和中央目录同时设置 EFS（UTF-8）标志。TAR 改用 .NET `System.Formats.Tar` 的 PAX 格式，使用扩展头保存 Unicode 路径，避免传统 TAR 无字符集声明时被 Windows 工具按本地代码页解释。此修复仅作用于新建压缩包；已有乱码压缩包应从原始文件重新创建。
- 回归检查同时验证 ZIP 本地头的 `0x0800` 标志和独立读取器结果，避免自家读取器强制 UTF-8 掩盖兼容性问题。ZIP、7Z、TAR、ISO、WIM 和五种流格式测试包含中文、日文、韩文、阿拉伯文、俄文、重音字符及 Emoji，并逐字节核对解压内容。流格式不保存原始文件名，测试以压缩包名称推导输出名称。
- 每个 UI 任务具有独立身份。完成或停止后，队列中的旧进度消息不再更新状态。RAR 通过字符流解析含退格符的百分比，不依赖换行；实际退出成功并替换输出前，进度最高为 99%。
- `ArchiveWorker` 使用当前单文件 EXE 的 `--archive-worker` 模式执行归档操作，通过 UTF-8 JSON 管道交换请求、进度、结果和阶段码。无需额外工作程序文件。工作进程继承当前 UI 语言，进度通知限频以减少界面队列积压。
- 执行期间顶部菜单显示“停止工作”；默认选择“否”。选择“是”后终止整个工作进程树（含 `rar.exe`），等待句柄释放后再恢复菜单。关闭任务中的主窗口也先走风险确认流程，完成停止后可再次关闭。
- 强制停止无法执行被终止进程的 `finally`，可能留下同目录的随机临时压缩包、系统临时目录中的暂存文件或不完整解压文件。已经覆写的数据不能自动恢复，确认提示明确说明这些后果。不会自动删除或回滚目标解压目录。
- 新阶段码：`F00010008`（停止确认/请求失败）、`WORKR0001`（工作进程内部失败）、`WORKR0002`（工作进程未正常完成）、`WORKR0003`（停止失败）、`WORKR0004`（启动或管道失败）、`WORKR0005`（异常后的进程清理失败）、`RARTL0006`（RAR 输出与输入冲突）。

定向测试另外覆盖工作进程 Unicode 通信、压缩中强制停止，以及存在官方 `rar.exe` 时的 RAR 往返与进度单调性。RAR 测试需要将工具置于测试可执行文件目录。

当前服务阶段明细：

| StageCode | 含义 |
|---|---|
| `ARCSV0001` | 列出归档失败 |
| `ARCSV0002` | 解压失败 |
| `ARCSV0003` | 请求创建 RAR |
| `ARCSV0004` | 单文件流输入数量错误 |
| `ARCSV0005` | 不支持的输出类型 |
| `ARCSV0006` | 创建归档失败 |
| `ARCSV0007` | 单文件流输入不是普通文件 |
| `ARCSV0008` | 检测到不安全输出路径 |
| `ARCSV0009` | 输出文件与输入文件冲突 |
| `NESTR0001` | 分析内层 TAR 失败 |
| `NESTR0002` | 列出内层 TAR 失败 |
| `NESTR0003` | 一键解压内层 TAR 失败 |
| `NESTR0004` | 内层 TAR 条目不存在 |
| `RARTL0001` | 找不到 `rar.exe` |
| `RARTL0002` | 无法启动 `rar.exe` |
| `RARTL0003` | `rar.exe` 返回非零退出代码 |
| `RARTL0004` | RAR 创建发生其他异常 |
| `RARTL0005` | 取消时无法终止 RAR 进程 |

## 6. 构建、测试与发布

### Windows 11 一级右键菜单

Win11 一级菜单不再使用 `HKCU\Software\Classes\*\shell` 传统动词。项目采用开源项目 [ikas-mc/ContextMenuForWindows11](https://github.com/ikas-mc/ContextMenuForWindows11) 的 `IExplorerCommand` 原生宿主实现，许可证为 LGPL-3.0。对应源码及许可证保留在 `third_party/ContextMenuForWindows11`，便于替换或重新链接；TZIP 的修改仅包括独立 CLSID、默认顶层标题和构建路径。MSIX 使用独立包身份 `TangerineZip.ContextMenu`，不会覆盖用户另行安装的 Custom Context Menu。

主 EXE 内嵌已签名 MSIX、公钥证书及 LGPL-3.0 许可证。建立向导依次执行：平台/资源验证、清理旧版注册表菜单、释放包、请求管理员信任证书、安装包、写入三条本地化命令、验证注册。移除向导会独立尝试清理所有旧菜单路径、命令文件、MSIX、证书所有权和用户设置；一个步骤失败不会阻止其他清理步骤，最终统一显示全部错误。证书辅助程序只接受主程序内嵌证书，并在 `HKLM\SOFTWARE\TangerineZip\ContextMenuCertificates` 记录使用者 SID；预先存在或仍由其他用户使用的证书不会被删除。

向导通过 `ContextMenuProgress` 报告确定百分比和逐阶段日志。用户点击“停止工作”并确认风险后会取消令牌、终止正在运行的 PowerShell/提权进程树，并在建立流程中尽力回滚本次新增的包与证书所有权。取消或回滚失败使用 StageCode 显示，不会静默忽略。Win11 上安装失败时不会创建传统菜单作为假成功回退。

三条命令均由 Explorer 传递一个或多个完整 Unicode 路径：解压与打开仍由 `ContextMenuCommandHandler` 通过 `FileDetector` 判断内容；压缩命令显示现有格式与位置对话框。顶层菜单和命令名称使用执行向导时的 UI 语言，支持 `en-US`、`zh-CN`、`zh-TW`、`zh-HK`、`zh-MO`。

构建原生宿主和签名包：

```powershell
./TANGERINE-ZIP.ContextMenuPackage/Build-ContextMenuNative.ps1
./TANGERINE-ZIP.ContextMenuPackage/Build-ContextMenuPackage.ps1 `
  -ShellExtensionPath ./artifacts/context-menu-native/TangerineZipContextMenuHost.dll `
  -ContextMenuHostPath ./artifacts/context-menu-native/ContextMenuHost.exe `
  -OutputPackagePath ./TANGERINE-ZIP/Embedded/TangerineZipContextMenu.msix `
  -CertificateThumbprint <具有代码签名私钥的证书指纹>
```

清单中的 `Publisher` 必须与签名证书 Subject 完全一致。只将公开 `.cer` 放入 `TANGERINE-ZIP/Embedded`，禁止提交 `.pfx` 或私钥。变更原生源码后，必须先重新生成并签名 MSIX，再发布主程序，否则单文件 EXE 会继续嵌入旧版本。

在仓库根目录执行：

```powershell
dotnet restore TANGERINE-ZIP.slnx --configfile NuGet.Config
dotnet build TANGERINE-ZIP.slnx -c Release --no-restore
dotnet run --project TANGERINE-ZIP.SmokeTests/TANGERINE-ZIP.SmokeTests.csproj -c Debug --no-restore
dotnet publish TANGERINE-ZIP/TANGERINE-ZIP.csproj -c Release --no-restore -o artifacts/single-file
dotnet publish TANGERINE-ZIP/TANGERINE-ZIP.csproj -c Release --no-restore -p:ENABLE_LIGHT=true -o artifacts/single-file-light
```

当前测试是短时定向测试，使用 `testfile/1/Linux教程.pdf` 验证 ZIP 中文条目名、无扩展名 BZ2 中的单 TAR 内容检测/自动展开，以及 ZIP 中两个 TAR 的选择和分目录一键解压。测试数据位于随机临时目录，结束后只删除该次测试创建的目录。

本次发布结果的可运行文件为 `artifacts/single-file-context-menu-final/TANGERINE-ZIP.exe`。PDB 仅用于调试，可不随软件分发；Win11 右键菜单宿主、MSIX 和公开证书均嵌入此 EXE，安装时释放到系统管理的位置。只有创建 RAR 时需要应用程序目录中的可选 `rar.exe`。

## 7. 维护注意事项

- 压缩源文件选择使用复用的 Windows 原生 `OpenFileDialog`。该对话框使用经典 Win32 模式，避免加载 Explorer Shell 扩展和不可用的最近位置，并以本地用户目录作为首次位置。点击菜单只显示文件对话框；归档工作进程在选择输出格式并确认保存后才启动，`rar.exe` 仅在输出格式为 RAR 时启动。

- 增加格式时，应同时更新 `FileDetector`、`ArchiveCapabilities`、`ArchiveService`、保存/打开筛选器、六份资源和定向测试。
- 文件类型判断只能进入 `FileDetector`，且只允许使用内容签名或 TAR 头校验。保存对话框的格式选择通过 `FileDetector.GetTypeFromCreateFilterIndex` 映射，不根据用户输入的后缀推断类型。
- 不要关闭路径边界校验，也不要直接调用库提供的“一键解压到目录”绕过覆盖策略。
- Native NuGet 版本变化后必须重新执行单文件发布和 XZ/WIM 往返测试。
- `PublishTrimmed` 保持为 `false`，以避免 WinForms、资源管理器和反射/PInvoke 库被错误裁剪。
