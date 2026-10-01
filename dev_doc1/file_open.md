# 默认打开方式注册与注销

## 界面

菜单栏“为系统做设置”→“默认打开方式设置”打开 `DefaultOpenWithWindow`。窗口提供格式复选框、全选、全不选、“这些格式用本软件打开”、“为这些格式设置打开方式”和“解除本软件在系统中的注册”。前两个操作只处理勾选格式；注销按钮始终处理 `DefaultAppAssociationService.Formats` 的全部扩展名，包括 ZIP、RAR、7z、TAR、GZip、BZip2、XZ、LZ4、Zstandard 的 `.zst`/`.zstd`，不含 ISO、WIM。注销运行时按钮与选择区禁用，窗口不允许在注册表操作中关闭。

## 注册路径

`DefaultAppAssociationService.RegisterHandler(extension, exePath)` 先用 `ArchiveFileIconService.EnsureIcon` 保存持久格式图标，再根据 `BuildRegistrationPlan` 在当前用户的注册表写入：

| 路径 | 内容 |
| --- | --- |
| `HKCU\Software\Classes\TangerineZip.Archive.<格式>` | 描述、图标和 `shell\open\command` |
| `HKCU\Software\Classes\.<格式>\OpenWithProgids` | 本软件 ProgID 值 |
| `HKCU\Software\TangerineZip\DefaultApps\Capabilities` | 应用说明和 `FileAssociations` |
| `HKCU\Software\RegisteredApplications` | `TANGERINE ZIP` → Capabilities 路径 |

第一个按钮随后调用 `SetThisAppDefault`，按当前系统选择 `SftaUserChoice`（经典 `UserChoice`）或 `LatestUserChoice`（`UserChoiceLatest`），并通过 Shell 查询核验实际默认应用。第二个按钮仍是用户主动打开 Windows 官方默认应用设置页的流程；程序不自动操作设置界面。

## 注销调用

窗口的 `UnregisterThisAppButton_Click` 通过 `Task.Run` 在后台线程调用 `DefaultAppUnregistrationService.Unregister(report)`。`Progress<string>` 捕获 WPF 同步上下文，将逐格式日志异步投递到界面，不阻塞注册表工作线程。成功返回本次覆盖的扩展名数量；失败抛出带 `UNRAS` 阶段码的 `StageException`，界面显示本地化错误和已处理日志。按钮不读取复选框状态，不需要管理员权限，作用范围是当前 Windows 账户。

## 异步界面边界

默认打开方式窗口内可能阻塞的调用均由 `Task.Run` 转移到后台：可执行文件路径检查、逐格式候选注册、自动默认关联、Windows 设置入口的 Shell 启动、手动流程的实际默认 ProgID 查询，以及完整注销。每次 `await` 后才访问 WPF 控件，按钮和选择区在操作期间禁用；关闭窗口会等待当前注册表操作完成。全选、全不选、日志显示和主题对话框只修改或读取 WPF 控件，必须在 UI 线程执行，不属于后台 I/O 操作。底层 Windows 注册表 API 本身是同步接口，因此这里的“异步”指不占用 UI 线程，而非中途强制取消注册表事务。

`Unregister` 分为两阶段：

1. **清显式默认选择。** 对每个扩展名读取 `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.<格式>\UserChoiceLatest\ProgId\ProgId` 和旧格式 `UserChoice\ProgId`。仅当值是该格式对应的 `TangerineZip.Archive.*` 时，改名为私有备份。再次读取显式选择；若仍指向本软件或读取失败，恢复已移动的键并停止该格式。确认后删除备份。之前中断留下、名称及 ProgID 都符合本软件规则的私有备份，也会在重试时删除。任何格式失败都会阻止第二阶段。此时 Shell 仍可能把已注册的本软件当作回退默认程序，不据此误判失败。
2. **撤销注册并核验。** 逐项删除本软件的 `OpenWithProgids` 值、属于本软件的直接扩展名默认值、确认描述属于本软件的 ProgID 树、匹配的 Capabilities 文件关联。全部格式完成后移除应用登记和本软件 Capabilities 元数据，最后删除已知格式的 SHA-256 命名图标缓存，通知 Shell。此后再查询全部格式的实际默认 ProgID；若仍为本软件，报告失败。每步可重试；外来值与不认识的注册项保留并报告错误。

这里“默认打开方式设置为空”是删除**本软件拥有的用户默认选择**。Windows 若有其他已注册程序或系统回退关联，Shell 可能立即显示那个程序；程序不能承诺有效 ProgID 一定是空字符串。也不会为其他软件生成新的默认值。

## 失败处理与阶段码

| 阶段码 | 含义 |
| --- | --- |
| `UNRAS0001` | 当前用户身份或互斥锁不可用 |
| `UNRAS0002` | 至少一个默认选择无法清除，未撤销处理程序注册 |
| `UNRAS0003` | 清理后仍发现本软件的显式用户默认选择，已尝试恢复移动的键 |
| `UNRAS0004` | 部分处理程序注册或图标缓存未能撤销，或注销后 Shell 仍报告本软件为默认；可重试 |
| `UNRAS0005` | 清默认失败且回滚旧选择键也失败 |
| `DAPWN0005` | 窗口注销操作边界出现未分类异常 |

Windows 受保护关联可能拒绝注册表改名或删除。此时不会跳过错误继续删除本软件的 ProgID。Microsoft 的[默认应用平台说明](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform)明确指出程序化修改默认应用不属于受支持接口，UCPD 可能阻止注册表写入。该实现对 Windows 10、早期与新版 Windows 11 做格式检测和实际结果核验，但不能保证所有系统构建都允许自动清理。

## 开发验证

Release 编译零警告、零错误。隔离注册表测试覆盖作用域、所有权校验、保留其他程序的 OpenWith 值、直接扩展名回退值、重复执行，以及 UserChoiceLatest 清理和 UserChoice 回滚。针对用户报告的 `UNRAS0002`，已在当前 Windows 账户运行修正版完整注销：10 个受支持扩展名均成功，重复运行也成功；只读复核未见本软件的对应 ProgID 或 Capabilities 文件关联。设计记录见 [file_open_think.md](file_open_think.md)。
