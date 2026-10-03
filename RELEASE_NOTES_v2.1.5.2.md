# TANGERINE-ZIP-V2.1.5.2 — tanevo (pre-release)

Changes since v2.1.5.1. This pre-release targets Windows x64.

## English

### Refactoring

- Reused the GUI archive, settings, and Windows integration services from the command line. CLI requests and their archive worker now use English output regardless of the Windows or GUI language.
- Documented the GUI-to-CLI feature map, command syntax, and StageCode boundaries in `dev_doc1`.

### New features

- Added CLI commands to copy, move, and delete members inside eligible ZIP, 7z, and TAR archives, extract nested TAR members, and search archive member paths.
- Added commands for temporary directory and appearance settings, compression profile management, Explorer context menus, and default-app associations. Saved profiles can be used with `compress --profile`.

### Fixes

- An archive-internal edit destination such as `--destination folder` now creates `folder/member` rather than concatenating the folder name with the member name.
- CLI cancellation and newly added failure paths report registered StageCodes. Noninteractive extraction continues to abort on conflicts and safety issues unless an explicit policy is supplied.

`fd` requires the .NET 10 Desktop Runtime; `sc` is self-contained. RAR creation still requires an independently obtained official `rar.exe` beside the application. The Hawkynt NuGet packages are licensed under LGPL-3.0-or-later; see [`third_party/Hawkynt/README.md`](third_party/Hawkynt/README.md).

## 中文

相对 v2.1.5.1 的更新。本预发布版面向 Windows x64。

### 重构

- 命令行复用 GUI 的归档、设置和 Windows 系统集成服务。命令行及其归档工作进程不受 Windows 或 GUI 语言影响，统一输出英文。
- 在 `dev_doc1` 中补充 GUI 与 CLI 功能对应、命令语法和 StageCode 边界。

### 新功能

- 命令行支持在符合条件的 ZIP、7z、TAR 内复制、移动和删除成员，展开嵌套 TAR，并按成员路径搜索。
- 新增临时目录、外观设置、压缩配置、资源管理器右键菜单及默认打开方式命令。已保存的压缩配置可通过 `compress --profile` 使用。

### 修复

- 归档内编辑的 `--destination folder` 现在生成 `folder/成员名`，不会再把目录名直接拼接到成员名前。
- 命令行取消及新增错误路径报告已登记的 StageCode。无人值守解压遇到文件冲突或安全问题时仍默认终止，除非显式指定处理策略。

`fd` 需要 .NET 10 Desktop Runtime；`sc` 为自包含版。RAR 创建仍需自行取得官方 `rar.exe` 并放在程序同目录。Hawkynt NuGet 包使用 LGPL-3.0-or-later 许可，详见 [`third_party/Hawkynt/README.md`](third_party/Hawkynt/README.md)。
