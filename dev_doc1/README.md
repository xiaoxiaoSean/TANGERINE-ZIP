# Tangerine ZIP 开发文档

## 1. 实现概览

项目以 .NET 10 WinForms 为 UI，归档逻辑集中在 `Services/ArchiveService.cs`。主窗体只负责选择文件、展示条目、导航、覆盖策略、进度及错误提示。所有耗时操作均通过 `Task`/异步流或后台任务执行，UI 线程不会执行压缩、解压或镜像扫描。

发布目标为 Windows x64 自包含单文件。`PublishSingleFile`、`SelfContained` 与 `IncludeNativeLibrariesForSelfExtract` 已写入项目文件；XZ 和 WIM 的原生库由 .NET 单文件宿主解出后按 `NATIVE_DLL_SEARCH_DIRECTORIES` 定位。

## 2. 格式能力矩阵

| 格式 | 列出 | 解压 | 创建 | 实现 |
|---|---:|---:|---:|---|
| ZIP | 是 | 是 | 是 | SharpCompress / Deflate |
| RAR | 是 | 是 | 否 | SharpCompress；RAR 写入受专有许可限制 |
| 7Z | 是 | 是 | 是 | SharpCompress / LZMA2 |
| TAR | 是 | 是 | 是 | SharpCompress |
| GZ | 是 | 是 | 是 | .NET GZipStream |
| BZ2 | 是 | 是 | 是 | SharpCompress BZip2Stream |
| XZ | 是 | 是 | 是 | Joveler.Compression.XZ / liblzma |
| LZ4 | 是 | 是 | 是 | K4os.Compression.LZ4.Streams |
| ZSTD | 是 | 是 | 是 | SharpCompress ZStandard streams |
| ISO | 是 | 是 | 是 | DiscUtils ISO9660/Joliet |
| WIM | 是 | 是 | 是 | ManagedWimLib / wimlib |

RAR 是唯一无法创建的格式。RAR 压缩算法和写入格式为专有技术，没有可合法嵌入并免安装运行的第三方 NuGet 写入器。程序会以 `ARCSV0003` 明确提示，不会生成伪 RAR 文件。RAR 4/5 解压仍受支持。

GZ、BZ2、XZ、LZ4、ZSTD 是单文件压缩流，因此创建时只能选择一个普通文件。ZIP、7Z、TAR、ISO、WIM 服务层支持多文件；ISO 使用 Joliet 文件名，WIM 使用 LZMS。

## 3. 核心设计

### ArchiveService

- `ListAsync`：按检测类型列出归档项目；单文件流显示由压缩文件名推导的虚拟条目。
- `ExtractAsync`：统一执行全部或选中条目解压。
- `CreateAsync`：统一创建归档；先写同目录临时文件，成功后原子替换最终文件，失败时清理临时文件。
- `GetSafeTargetPath`：对每个输出路径执行规范化和根目录边界检查，阻止 Zip Slip/path traversal。
- WIM 先解至独立临时目录，再按“全部覆盖/全部跳过”策略合并，保证覆盖语义一致。

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

## 5. StageCode 规范

StageCode 固定为 9 个字符：5 字符 `stageHead` + 4 字符 `stageDetail`。

| Stage head | 模块 |
|---|---|
| `F0001` | 主窗体和 UI 工作流 |
| `F0002` | 文件选择窗体 |
| `ARCSV` | 归档服务 |

展示统一调用：

```csharp
MessageBox.Show(
    MessageTipGenerator.GenerateTip(
        "F00010006",
        exception.Message)); //F00010006
```

服务层使用 `StageException` 保留原始异常作为 `InnerException`，UI 优先显示其 StageCode。

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

## 6. 构建、测试与发布

在仓库根目录执行：

```powershell
dotnet restore TANGERINE-ZIP.slnx --configfile NuGet.Config
dotnet build TANGERINE-ZIP.slnx -c Release --no-restore
dotnet run --project TANGERINE-ZIP.SmokeTests/TANGERINE-ZIP.SmokeTests.csproj -c Debug --no-restore
dotnet publish TANGERINE-ZIP/TANGERINE-ZIP.csproj -c Release --no-restore -o artifacts/single-file
```

冒烟测试会对 ZIP、7Z、TAR、GZ、BZ2、XZ、LZ4、ZSTD、ISO、WIM 分别执行创建、格式检测、列出、解压和内容比对，并验证 RAR 创建保护。测试数据位于随机临时目录，结束后只删除该次测试创建的目录。

发布结果的可运行文件为 `artifacts/single-file/TANGERINE-ZIP.exe`。PDB 仅用于调试，可不随软件分发；程序运行不依赖旁置 DLL 或已安装的 .NET Runtime。

## 7. 维护注意事项

- 增加格式时，应同时更新 `FileDetector`、`ArchiveCapabilities`、`ArchiveService`、保存/打开筛选器、六份资源和冒烟测试。
- 不要把扩展名当成唯一安全依据；当前优先检查签名，仅对没有强制签名的格式作扩展名回退。
- 不要关闭路径边界校验，也不要直接调用库提供的“一键解压到目录”绕过覆盖策略。
- Native NuGet 版本变化后必须重新执行单文件发布和 XZ/WIM 往返测试。
- `PublishTrimmed` 保持为 `false`，以避免 WinForms、资源管理器和反射/PInvoke 库被错误裁剪。
