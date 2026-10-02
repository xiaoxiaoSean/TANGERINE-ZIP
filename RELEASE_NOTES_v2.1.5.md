# TANGERINE-ZIP-V2.1.5 — tanevo (pre-release)

Changes since v2.1.1. This is a pre-release for Windows x64.

## English

### Refactoring

- Added staged archive operations and StageCode errors for archive updates, batch jobs, snapshots, ZIP comments, password storage, and Defender scans.
- Centralized the release codename in `GlobalConfig.VName`; the About menu now formats its translated label with `tanevo`.

### New features

- Added browsing, extraction, and integrity checks for ARJ, ACE, ARC, LZW, and LZip; writing these legacy formats is not supported.
- Added backup-protected add or replace operations for ordinary ZIP, 7z, and RAR archives, plus batch extraction and conversion to ZIP, 7z, or TAR.
- Added 7z self-extracting EXE output, solid compression, RAR recovery records, exclusion patterns, and saved compression profiles.
- Added ZIP comments, Windows Credential Manager password storage, verified archive snapshots, Microsoft Defender scanning, and command-line access to these operations.
- Added archive drag workflows, first-run setup, and COLOR5 appearance settings.

### Fixes

- Integrity testing now returns a nonzero command-line exit code for damaged members and uses translated error messages.
- Improved handling of missing inputs, dropped sources, encrypted or split update targets, and failed external tools.

`fd` requires the .NET 10 Desktop Runtime; `sc` is self-contained. Both downloads target Windows x64. RAR creation and updates require an independently obtained official `rar.exe` beside the application. Defender scanning requires an available system component and can require administrator privileges.

## 中文

相对 v2.1.1 的更新。本版本是面向 Windows x64 的预发布版。

### 重构

- 为压缩包更新、批量任务、快照、ZIP 注释、密码保存及 Defender 扫描加入分阶段处理和 StageCode 异常。
- 将版本代号集中到 `GlobalConfig.VName`；“关于”菜单使用本地化格式显示 `tanevo`。

### 新功能

- 支持浏览、解压和完整性检查 ARJ、ACE、ARC、LZW、LZip；暂不支持写入这些旧格式。
- 支持给普通 ZIP、7z、RAR 添加或替换文件并保留原包备份；支持批量解压和转换为 ZIP、7z、TAR。
- 支持创建 7z 自解压 EXE，并加入固实压缩、RAR 恢复记录、排除规则和压缩配置方案。
- 加入 ZIP 注释、Windows 凭据管理器密码保存、校验过的压缩包快照、Microsoft Defender 扫描，以及相应命令行入口。
- 加入压缩包拖放操作、首次运行设置和 COLOR5 外观设置。

### 修复

- 完整性检查发现损坏成员时，命令行现在返回非零退出码并显示本地化错误。
- 改进缺失输入、拖放来源、加密或分卷更新目标以及外部工具失败时的异常处理。

`fd` 需要 .NET 10 Desktop Runtime；`sc` 为自包含版本。两个附件均适用于 Windows x64。创建和更新 RAR 需要自行取得官方 `rar.exe` 并放在程序同目录。Defender 扫描需要系统提供该组件，部分环境还需要管理员权限。
