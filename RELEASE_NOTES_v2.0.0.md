# TANGERINE-ZIP-V2.0.0

Changes since v1.1.0.

## English

### Refactoring

- Migrated the main window, settings, dialogs, file picker, and context menu workflow from Windows Forms to WPF. Replaced the old controls and resource files with a shared WPF theme and localized UI strings.
- Reworked archive operations around dedicated services and a separate worker process, with clearer progress reporting, cancellation, and stage codes.

### New features

- Added text and image previews for supported archive entries, including zoom, rotation, panning, and Save As for images. Added extraction directly to the archive's parent folder.
- Added archive integrity testing, corruption detection and readable-file recovery, hash display and verification, and filename encoding selection.
- Added configurable compression options for ZIP, 7z, and RAR, including advanced parameters, split volumes, and memory limits.
- Added search within archives, entry details, and single-entry extraction. Added copy, cut, paste, and delete for eligible ZIP, 7z, and TAR archives, with backups when an archive is changed.
- Added `help`, `compress`, `extract`, and `list` command-line commands while keeping the graphical interface as the default when no command is supplied.
- Added customizable appearance colors, mouse effect settings, and a configurable temporary directory.
- Improved Windows 11 Explorer context menu integration and drag-and-drop handling.

### Fixes

- Improved extraction error handling, unsafe-path checks, resource preflight checks, and cleanup of temporary files and embedded tools.
- Fixed stale archive search results and several preview, mouse effect, localization, and context menu issues.
- Made archive edits use a temporary file and backup so a failed write does not replace the original archive.

`fd` is framework-dependent and requires the .NET 10 Desktop Runtime; `sc` is self-contained. Both downloads target Windows x64. Creating RAR archives still requires `rar.exe` beside the application.

## 中文

相对 v1.1.0 的更新。

### 重构

- 将主窗口、设置、对话框、文件选择器和右键菜单流程从 WinForms 迁移到 WPF；以统一的 WPF 主题和本地化界面资源替换旧控件与资源文件。
- 将归档操作整理为独立服务和工作进程，改进进度反馈、取消操作与阶段码。

### 新增

- 新增压缩包内文字和图片预览，图片支持缩放、旋转、拖动和另存为；新增直接解压到压缩包所在文件夹。
- 新增完整性测试、损坏探测与可读文件恢复、Hash 显示与校验、文件名编码切换。
- 新增 ZIP、7z、RAR 压缩配置，包括高级参数、分卷和内存限制。
- 新增压缩包内搜索、成员详细信息、单成员解压；符合条件的 ZIP、7z、TAR 压缩包支持复制、剪切、粘贴和删除成员，修改时保留备份。
- 新增 `help`、`compress`、`extract`、`list` 命令行命令；不带命令时仍打开图形界面。
- 新增自定义主题配色、鼠标效果设置和临时目录配置。
- 改进 Windows 11 资源管理器右键菜单集成与拖放打开流程。

### 修复

- 改进解压失败处理、不安全路径检查、资源余量预检，以及临时文件和内嵌工具的清理。
- 修复过期搜索结果覆盖新结果，以及部分预览、鼠标效果、本地化和右键菜单问题。
- 压缩包内编辑改用临时文件和备份，写入失败时不替换原压缩包。

`fd` 为依赖框架版，需要安装 .NET 10 Desktop Runtime；`sc` 为自包含版。两个附件均适用于 Windows x64。创建 RAR 压缩包仍需将 `rar.exe` 放在程序同目录。
