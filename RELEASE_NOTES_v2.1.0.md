# TANGERINE-ZIP-V2.1.0

Changes since v2.0.0.

## English

### Refactoring

- Switched startup to a standard WPF `App.xaml` and Windows GUI executable, so opening the application does not allocate a command prompt window.
- Updated dialogs and the default-app window to use responsive layouts. Blocking registry, disk, and Shell work in the default-app workflow runs away from the UI thread.

### New features

- Added **System settings → Default apps** for ZIP, RAR, 7z, TAR, GZip, BZip2, XZ, LZ4, and Zstandard. Select all/none, register this app, or choose another app through Windows Settings. ISO and WIM are excluded.
- Added automatic current-user default association attempts for supported archive formats, using the classic UserChoice format or UserChoiceLatest as detected. Each result is checked against the effective Windows association. Windows can reject unsupported or protected changes; the app reports failures and attempts rollback.
- Added format-specific orange and white archive icons, and an action to clear this app's explicit default choices before removing its handler registrations and cached icons.
- Expanded the English and Simplified Chinese READMEs with direct language links, the project artwork, and current release guidance. Added detailed developer documentation for default associations and release steps.

### Fixes

- Fixed unregistration when Windows still selected this app as a fallback before its candidate registration was removed. Effective defaults are now verified after deregistration; retrying the action is safe.
- Improved default-app localization, stage codes, error recovery, and selection controls. The default-app window stays responsive during registration, verification, Settings handoff, and removal.

`fd` is framework-dependent and requires the .NET 10 Desktop Runtime; `sc` is self-contained. Both downloads target Windows x64. Creating RAR archives still requires `rar.exe` beside the application. Automatic default-app changes use undocumented Windows behavior and may be blocked on some Windows 10/11 builds.

## 中文

相对 v2.0.0 的更新。

### 重构

- 将启动入口改为标准 WPF `App.xaml` 和 Windows 图形程序，打开软件时不再分配命令行窗口。
- 调整对话框和默认应用窗口的自适应布局；默认应用流程中的注册表、磁盘和 Shell 耗时操作移出 UI 线程。

### 新增

- 新增**为系统做设置 → 默认打开方式设置**，支持 ZIP、RAR、7z、TAR、GZip、BZip2、XZ、LZ4 和 Zstandard；可全选或全不选、注册本软件，或通过 Windows 设置选择其他程序。ISO、WIM 不参与。
- 新增为当前用户自动设置受支持压缩格式默认程序的尝试：按系统实际格式选择经典 UserChoice 或 UserChoiceLatest，并逐项核验 Windows 的生效结果。若系统拒绝受保护或不支持的写入，程序报告失败并尝试回滚。
- 新增带橙白配色和格式文字的文件图标，以及先清除本软件的显式默认选择、再撤销处理程序注册和缓存图标的操作。
- 补全英文和简体中文 README 的语言直达链接、项目素材及当前版本说明；增加默认关联与发布流程开发文档。

### 修复

- 修复注销前 Windows 将本软件作为候选程序回退默认值，导致注销提前停止的问题。现在撤销注册后再核验实际默认程序，重复运行也安全。
- 改进默认应用界面的本地化、阶段码、异常恢复和选择控件；注册、核验、设置入口及注销过程中界面保持响应。

`fd` 为依赖框架版，需要安装 .NET 10 Desktop Runtime；`sc` 为自包含版。两个附件均适用于 Windows x64。创建 RAR 压缩包仍需将 `rar.exe` 放在程序同目录。自动修改默认应用依赖 Windows 未公开的行为，在部分 Windows 10/11 版本上可能被系统阻止。
