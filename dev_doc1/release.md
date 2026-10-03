# TANGERINE ZIP 发布流程

## v2.0.0 流程回顾

以下内容以仓库提交、标签、发布页和两个项目发布配置为依据。不能从这些结果反推出当时每一条终端命令，所以下面的命令是可复用的等价流程。

1. 从前一标签 `v1.1.0` 对照源码变更，按 **Refactoring → New features → Fixes**（重构 → 新增 → 修复）整理 [`RELEASE_NOTES_v2.0.0.md`](../RELEASE_NOTES_v2.0.0.md)。文件先英文、后中文；说明 `fd`/`sc` 的运行要求和 RAR 创建对 `rar.exe` 的依赖。
2. 在 `TANGERINE-ZIP/TANGERINE-ZIP.csproj` 将 `<Version>` 设为 `2.0.0`；`AssemblyVersion`、`FileVersion`、`InformationalVersion` 均引用它。版本提交为 `4ac38a6`，更新日志提交为 `c030c1a`。
3. 使用项目的 `FolderProfile1` 和 `FolderProfile` 发布 Windows x64 单文件程序。前者为依赖 .NET 10 Desktop Runtime 的 `fd`，后者为自包含的 `sc`。
4. 将两份输出分别命名为 `TANGERINE-ZIP-v2.0.0-fd.exe` 与 `TANGERINE-ZIP-v2.0.0-sc.exe`。GitHub v2.0.0 发布页显示这两个附件；“Assets 4”还包括 GitHub 自动生成的两份源码压缩包。发布标题为 `TANGERINE-ZIP-V2.0.0`。
5. 将源码、版本号和英文先行的双语更新日志提交并推送；创建注释标签 `v2.0.0`，其标签说明为 `TANGERINE-ZIP-V2.0.0`，指向 `c030c1a`；将标签推送后创建同名 GitHub Release，使用更新日志正文并上传两个 EXE。
6. 发布后检查标签、附件名称、发布正文的语言顺序及下载链接。README 的英文版 [`README.md`](../README.md) 和简中版 [`README.zh-CN.md`](../README.zh-CN.md) 也应更新当前版本链接、功能与下载说明；本次 v2.1.0 明确把这一步纳入发布流程。

## 每次发布的可复用顺序

1. 确认 `git status` 干净，比较上一标签至当前工作树的提交与差异；检查六份本地化资源和开发文档。
2. 更新项目 `<Version>`、`RELEASE_NOTES_vX.Y.Z.md`、英文及简中 README。README 顶部保留直达另一语言版本的超链接和项目 logo/橘子/Kiro 素材；同步更新“新版本”链接、功能、下载要求、文档入口。若以后增加其他 README 语言版本，同步搜索并更新全部版本链接。
3. `dotnet build TANGERINE-ZIP/TANGERINE-ZIP.csproj -c Release`，运行与变更相关的验证，执行 `git diff --check`。
4. 分别执行 `dotnet publish TANGERINE-ZIP/TANGERINE-ZIP.csproj -p:PublishProfile=FolderProfile1 -c Release` 和 `-p:PublishProfile=FolderProfile -c Release`。复制 `out/fd/TANGERINE-ZIP.exe`、`out/sc/TANGERINE-ZIP.exe` 为带版本号的发布附件；核对 Windows 文件版本、文件大小及 SHA-256。
5. 提交并推送源码与说明；在该提交上创建并推送注释标签 `vX.Y.Z`。不要在标签创建后再改源码、README 或构建参数，否则附件与标签不再对应。
6. 创建标题 `TANGERINE-ZIP-VX.Y.Z` 的 GitHub Release，正文读取双语更新日志，上传 `TANGERINE-ZIP-vX.Y.Z-fd.exe` 和 `TANGERINE-ZIP-vX.Y.Z-sc.exe`。检查发布页的附件可见性与校验值。

示例（在仓库根目录，发布版本号替换为实际值）：

```powershell
dotnet build TANGERINE-ZIP/TANGERINE-ZIP.csproj -c Release
dotnet publish TANGERINE-ZIP/TANGERINE-ZIP.csproj -p:PublishProfile=FolderProfile1 -c Release
dotnet publish TANGERINE-ZIP/TANGERINE-ZIP.csproj -p:PublishProfile=FolderProfile -c Release
Copy-Item -LiteralPath out/fd/TANGERINE-ZIP.exe -Destination out/TANGERINE-ZIP-vX.Y.Z-fd.exe
Copy-Item -LiteralPath out/sc/TANGERINE-ZIP.exe -Destination out/TANGERINE-ZIP-vX.Y.Z-sc.exe
Get-FileHash out/TANGERINE-ZIP-vX.Y.Z-fd.exe, out/TANGERINE-ZIP-vX.Y.Z-sc.exe -Algorithm SHA256
git add -A
git commit -m "Prepare vX.Y.Z release"
git push origin master
git tag -a vX.Y.Z -m "TANGERINE-ZIP-VX.Y.Z"
git push origin vX.Y.Z
gh release create vX.Y.Z out/TANGERINE-ZIP-vX.Y.Z-fd.exe out/TANGERINE-ZIP-vX.Y.Z-sc.exe --title TANGERINE-ZIP-VX.Y.Z --notes-file RELEASE_NOTES_vX.Y.Z.md
```

`gh release create` 需要已认证的 GitHub 会话；也可以在 GitHub 发布页面用同一标签、标题、正文与附件完成。复制附件不改变原发布目录，避免后续构建覆盖待上传文件。

## v2.1.0 发布记录

- 基线：`v2.0.0`。更新日志：[`RELEASE_NOTES_v2.1.0.md`](../RELEASE_NOTES_v2.1.0.md)，英文在前、中文在后。
- 主程序版本：`2.1.0`。README 语言版本：[`README.md`](../README.md)、[`README.zh-CN.md`](../README.zh-CN.md)，两者的当前版本链接及新增默认打开方式说明已同步。
- 构建目标：Windows x64，`FolderProfile1` → `TANGERINE-ZIP-v2.1.0-fd.exe`；`FolderProfile` → `TANGERINE-ZIP-v2.1.0-sc.exe`。
- Release 编译：零警告、零错误；默认应用探针 `1657` 项通过，注销私有注册表探针通过。两个发布配置成功，EXE 的 FileVersion 与 ProductVersion 均为 `2.1.0`。
- `TANGERINE-ZIP-v2.1.0-fd.exe`：9,518,290 字节；SHA-256 `8FAC1B1CB3D7F240622C65BD0F2CBCFE10F85F9FCC7EEBE70A804B8585B9BCCF`。
- `TANGERINE-ZIP-v2.1.0-sc.exe`：83,795,689 字节；SHA-256 `09681EFEDB1DAFF962A51307CBFA8449E121A87239B63BCBA98F08DC9B7C6D87`。
- 注释标签 `v2.1.0` 指向提交 `28f5c8aba167e7f21fa658e6ee71730dc7fa5a97`；[GitHub Release](https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/tag/v2.1.0) 标题为 `TANGERINE-ZIP-V2.1.0`，已发布为正式版本（非草稿、非预发布）。
- 发布后通过 GitHub Release 元数据核验：附件恰为上述两个 EXE，远端大小与 SHA-256 和本地一致；正文英文在前、中文在后。

## v2.1.1 发布记录

- 基线：`v2.1.0`。修复主菜单“关于 TZIP”未随界面语言切换的问题，六份本地化资源均加入 `AboutTzipMenu`；更新日志为 [`RELEASE_NOTES_v2.1.1.md`](../RELEASE_NOTES_v2.1.1.md)，英文在前、中文在后。
- 主程序版本：`2.1.1`。英文 [`README.md`](../README.md) 和简中 [`README.zh-CN.md`](../README.zh-CN.md) 的当前版本链接均已更新。
- 使用已有依赖执行 Release 编译（`--no-restore`），结果为零警告、零错误；`FolderProfile1` 和 `FolderProfile` 均成功发布。两个 EXE 的 FileVersion、ProductVersion 均为 `2.1.1`。
- `TANGERINE-ZIP-v2.1.1-fd.exe`：9,518,290 字节；SHA-256 `7B9FDBF83E21AA3EABF89D531F2009B38D63E13BBF6FB0F830E3327E179BF1DA`。
- `TANGERINE-ZIP-v2.1.1-sc.exe`：83,794,728 字节；SHA-256 `F3AEBBDC8DD3860A59A233601DFEB28B8BDAA6E6A6FAA0D990BB874D9B247D29`。
- 标签 `v2.1.1` 指向源码提交 `4f25130`；[GitHub Release](https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/tag/v2.1.1) 标题为 `TANGERINE-ZIP-V2.1.1`，已发布为正式版本。远端两个附件的大小和 SHA-256 与本地一致。

## v2.1.5 tanevo 预发布记录

- 基线：`v2.1.1`。更新日志为 [`RELEASE_NOTES_v2.1.5.md`](../RELEASE_NOTES_v2.1.5.md)，英文在前、中文在后；README 的版本链接已指向本次预发布版。
- 全局 `VName` 为 `tanevo`，主菜单使用本地化的“关于 {0} / About {0}”格式。主程序和两个附件的 FileVersion、ProductVersion 均为 `2.1.5`。
- Release 编译零警告、零错误；`FolderProfile1` 与 `FolderProfile` 均发布成功，两个附件的 `help` 命令返回 `0`。
- `TANGERINE-ZIP-v2.1.5-fd.exe`：10,009,810 字节；SHA-256 `10317C8A003741FAF9456B66224F7EDA515B5DF7219B592BF75DF0B2402BB194`。
- `TANGERINE-ZIP-v2.1.5-sc.exe`：84,186,851 字节；SHA-256 `5462AD4328F71F960DDF3C0E0A056C8D932407CBEBE8D095979E3533F2C1998D`。
- 注释标签 `v2.1.5` 指向提交 `7cd4bd41ab79cec0f087d8ed278f473de51c11bc`；[GitHub Release](https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/tag/v2.1.5) 标题为 `TANGERINE-ZIP-V2.1.5`，状态为 pre-release、非草稿。远端两个附件的大小与 SHA-256 均与本地一致。

## v2.1.5.1 tanevo 预发布记录

- 基线：`v2.1.5`。更新日志为 [`RELEASE_NOTES_v2.1.5.1.md`](../RELEASE_NOTES_v2.1.5.1.md)，英文在前、中文在后；英文与简中 README 均指向新预发布版。
- 全局 `VName` 保持 `tanevo`；主程序及两个附件的 FileVersion、ProductVersion 均为 `2.1.5.1`。
- 使用已还原依赖进行 Release 编译（`--no-restore`），结果为零警告、零错误；`FolderProfile1` 和 `FolderProfile` 均发布成功，两个附件的 `help` 命令返回 `0`。普通构建的自动还原因执行环境无法读取用户目录下的 NuGet.Config 而失败，未影响已有依赖下的构建和发布。
- `TANGERINE-ZIP-v2.1.5.1-fd.exe`：16,838,338 字节；SHA-256 `50E97F845BC966322717AC816521D68BFE252E2D955C06C1C07D192B042212DD`。
- `TANGERINE-ZIP-v2.1.5.1-sc.exe`：92,107,206 字节；SHA-256 `C839361A4AE8DB5161D111170BE6E72810820B92E150AAE4F21700E31A13F244`。
- 发布源码提交为 `b61b127`，注释标签 `v2.1.5.1` 指向该提交；[GitHub Release](https://github.com/xiaoxiaoSean/TANGERINE-ZIP/releases/tag/v2.1.5.1) 标题为 `TANGERINE-ZIP-V2.1.5.1`，状态为 pre-release、非草稿。远端两个附件的大小和 GitHub 提供的 SHA-256 digest 均与本地一致。
