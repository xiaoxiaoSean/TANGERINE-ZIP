# TANGERINE-ZIP-V2.1.5.1 — tanevo (pre-release)

Changes since v2.1.5. This pre-release targets Windows x64.

## English

### Refactoring

- Added a dedicated ISO creation dialog with separate filesystem and El Torito BIOS boot settings. All new controls use proportional layout, and their labels and validation messages are available in the six UI resource sets.
- Routed archive opening and creation progress through the worker, with StageCode errors for the new ISO and legacy-format paths.

### New features

- Create ARJ, classic ARC Packed, Unix compress (`.Z`/LZW), and ACE archives. ARJ and ARC use compressed methods; ACE currently writes interoperable stored entries and therefore does not reduce file size. Classic ARC accepts only flat ASCII names of at most 12 characters.
- Configure ISO volume and manufacturer IDs, Joliet, identical-file sharing, and one El Torito BIOS boot image. Boot options include no, floppy, or hard-disk emulation, load segment, and ISOLINUX boot information table updates. UEFI and hybrid USB images are outside the current writer's capabilities.
- Create a 7z-based self-extracting EXE from a readable archive through the always-visible **Archive tools** menu. The dialog can select a file or use the archive currently open in the main window.

### Fixes

- Corrected archive creation and integrity checks for the new legacy formats, including ARC CRC-16, ACE CRC handling, and ARC member size reporting.
- ISO integrity testing now reads image contents and reports the files checked. Self-extracting EXE creation now converts the selected archive to 7z content before attaching the SFX module.

`fd` requires the .NET 10 Desktop Runtime; `sc` is self-contained. RAR creation still requires an independently obtained official `rar.exe` beside the application. The new Hawkynt NuGet packages are licensed under LGPL-3.0-or-later; see [`third_party/Hawkynt/README.md`](third_party/Hawkynt/README.md).

## 中文

相对 v2.1.5 的更新。本预发布版面向 Windows x64。

### 重构

- ISO 创建改用独立设置窗口，分别配置文件系统与 El Torito BIOS 启动选项。新控件采用比例布局，标签及校验消息已加入六份界面语言资源。
- 压缩包打开和创建进度通过工作进程传递；新增 ISO 和旧格式路径按 StageCode 报告异常。

### 新功能

- 支持创建 ARJ、经典 ARC Packed、Unix compress（`.Z`/LZW）及 ACE 压缩包。ARJ、ARC 使用压缩方法；ACE 目前只写入可互操作的存储条目，不会缩小体积。经典 ARC 只接受最长 12 个字符、没有目录层级的 ASCII 文件名。
- ISO 可设置卷标识、制造商标识、Joliet、相同文件数据复用，以及一个 El Torito BIOS 启动映像。启动设置包括无仿真、软盘或硬盘仿真、加载段及 ISOLINUX 启动信息表。当前写入器不支持 UEFI 或 USB 混合启动镜像。
- 可从始终可见的“压缩包工具”菜单创建基于 7z 的自解压 EXE。窗口可选择压缩文件，也可使用主窗口当前打开的文件。

### 修复

- 修正新旧格式创建及完整性检查，包括 ARC CRC-16、ACE CRC 和 ARC 成员大小显示。
- ISO 完整性检查现在会读取镜像内容并报告检查的文件。创建自解压 EXE 时，会先将选中的压缩包转换为 7z 内容再附加 SFX 模块。

`fd` 需要 .NET 10 Desktop Runtime；`sc` 为自包含版。RAR 创建仍需自行取得官方 `rar.exe` 并放在程序同目录。新增的 Hawkynt NuGet 包使用 LGPL-3.0-or-later 许可，详见 [`third_party/Hawkynt/README.md`](third_party/Hawkynt/README.md)。
