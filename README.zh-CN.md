<p align="right"><a href="README.md">English</a> · 简体中文</p>

<p align="center">
  <img src="TANGERINE-ZIP/Resources/TZIP.png" alt="TANGERINE ZIP 软件标志" width="110">
  &nbsp;&nbsp;&nbsp;
  <img src="README-assets/tangerine.png" alt="橘子形象" width="86">
  &nbsp;&nbsp;&nbsp;
  <img src="TANGERINE-ZIP/Resources/Kiro.png" alt="TANGERINE ZIP 的 Kiro 形象" width="110">
</p>

<h1 align="center">TANGERINE ZIP</h1>

<p align="center">支持图形界面和命令行的 Windows 压缩包管理工具。</p>

<p align="center">
  <a href="https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/tag/v2.1.5">下载 tanevo v2.1.5 预发布版</a> ·
  <a href="RELEASE_NOTES_v2.1.5.md">查看 v2.1.5 更新日志</a> ·
  <a href="dev_doc1/README.md">开发文档</a>
</p>

## 功能

- 创建、浏览和解压 ZIP、7z、TAR、GZ、BZ2、XZ、LZ4、ZSTD、ISO、WIM 压缩包；浏览和解压 RAR。创建 RAR 需要将官方 `rar.exe` 放在程序同目录。ARJ、ACE、ARC、LZW 和 LZip 支持只读浏览与解压。
- 在支持的压缩包内预览文字和图片、搜索成员、查看详细信息，按需解压单个成员或整个压缩包。
- 执行完整性测试、显示和校验 Hash、从损坏的压缩包中恢复可读文件，并切换文件名编码以处理乱码。
- 配置 ZIP、7z、RAR 的压缩参数、分卷、固实压缩、排除规则、RAR 恢复记录和压缩配置方案；创建 7z 自解压 EXE。在符合条件的 ZIP、7z、TAR 中编辑成员并保留原包备份，也可向普通 ZIP、7z、RAR 压缩包添加或替换文件（RAR 需要 `rar.exe`）。
- 批量解压与格式转换、编辑 ZIP 注释、用 Windows 凭据管理器保存密码、创建经过校验且可设置保留数量的压缩包快照；在系统提供 Microsoft Defender 时扫描当前压缩包。
- 使用 Windows 11 资源管理器右键菜单、自定义主题配色和鼠标效果，以及命令行工具。运行 `help` 查看命令，运行 `help <命令>` 查看参数。
- 在**为系统做设置 → 默认打开方式设置**中注册压缩格式、尝试为当前用户自动设置默认程序，或清除本软件的默认关联与注册。受保护的 Windows 版本可能阻止自动修改关联。

部分操作受压缩格式和压缩包内容限制。加密包和分卷包不能原地更新；转换输出密码只支持 ZIP 和 7z。Microsoft Defender 扫描需要系统安装并启用该组件，扫描失败不会显示为安全。

## 下载与首次启动

从 [tanevo v2.1.5 预发布版](https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/tag/v2.1.5)下载一个 Windows x64 可执行文件：

| 文件后缀 | 版本 | 运行要求 |
| --- | --- | --- |
| `-sc.exe` | 自包含版 | 无需另外安装 .NET。 |
| `-fd.exe` | 依赖框架版 | 需要安装 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。 |

运行程序，首次启动时按提示选择可写的临时目录。之后可以在**设置 → 更多**中修改。程序设置保存在可执行文件同目录，因此建议将程序放在可写位置。若要创建 RAR 压缩包，请按其独立许可取得 `rar.exe` 并放在 `TANGERINE-ZIP.exe` 同目录；其他格式不需要它。

## 命令行

同一个可执行文件不带参数时打开图形界面；在终端中也可调用：

```powershell
.\TANGERINE-ZIP.exe help
.\TANGERINE-ZIP.exe compress "C:\Backup\photos.zip" "C:\Photos"
.\TANGERINE-ZIP.exe list "C:\Backup\photos.zip"
.\TANGERINE-ZIP.exe extract "C:\Backup\photos.zip" "C:\Restored"
```

使用 `compress`、`list` 或 `extract` 前，先通过图形界面设置临时目录。完整参数和退出行为见[命令行手册](dev_doc1/command.md)。

## 从源码构建

在 Windows 上安装 .NET 10 SDK，然后运行：

```powershell
dotnet build TANGERINE-ZIP/TANGERINE-ZIP.csproj -c Release
```

项目包含两个发布配置：`FolderProfile` 用于自包含版，`FolderProfile1` 用于依赖框架版。配置中的 `PublishDir` 是本机绝对路径；在其他电脑上发布时，请修改该路径或覆盖 `PublishDir`。

## 文档与许可

- [v2.1.5 预发布更新日志](RELEASE_NOTES_v2.1.5.md)
- [v2.1.1 更新日志](RELEASE_NOTES_v2.1.1.md)
- [v2.1.0 更新日志](RELEASE_NOTES_v2.1.0.md)
- [v2.0.0 更新日志](RELEASE_NOTES_v2.0.0.md)
- [开发文档和格式说明](dev_doc1/README.md)
- [命令行手册](dev_doc1/command.md)
- [MIT 许可证](LICENSE)
