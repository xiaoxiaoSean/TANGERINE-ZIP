# 经典 SFTA 自动默认关联试用（2026-10-01）

## 产品行为

“这些格式用本软件打开”调用 `DefaultAppAssociationService.SetThisAppDefault`，在后台线程依次注册候选程序/图标、尝试自动设置当前账户的默认关联，并核验实际 Shell ProgID。成功后直接继续下一后缀，不要求逐项确认。已有正确关联只刷新本软件登记和格式图标。当前用户启用新哈希格式时改用 [UserChoiceLatest 实现](USERCHOICE_LATEST.md)；本页仅描述经典 SFTA 路径。第一按钮失败时显示错误与重试/跳过/停止，不要求用户逐一到系统设置中完成。

异常显示原 StageCode，提供重试/跳过/停止。全选、全不选及九个复选框在运行期间禁用。第二个按钮“为这些格式设置打开方式”仍逐项打开官方设置，用户可选择任意程序，再返回点击下一格式。自动流程不启动命令行程序。十个扩展名及 ISO/WIM 排除范围见 [DEFAULT_OPEN_WITH.md](DEFAULT_OPEN_WITH.md)。

## 来源、算法和许可

使用 [DanysysTeam/SFTA](https://github.com/DanysysTeam/SFTA) 的 `SFTA.pb` 1.3.1，作者 Danyfirex / Dany3j，哈希算法上游署名 LMongrain。将 Hash1、Hash2、GenerateHash、CreateProgIdHash 及文件关联流程移植至 `Services/SftaUserChoice.cs`，没有运行 SFTA.exe、Hash.a、PowerShell 或 cmd。上游 GitHub 没有发布二进制 Release。

保留原 MIT 版权与许可：[third_party/SFTA/LICENSE](../third_party/SFTA/LICENSE)，来源及下载内容 SHA256 见 [third_party/SFTA/README.md](../third_party/SFTA/README.md)。许可作为 `TANGERINE_ZIP.SFTA.MIT.txt` 内嵌，覆盖两种单文件发布。

哈希输入由扩展名、当前进程账户 SID、ProgID、UTC 分钟级 FILETIME 和 Shell32 的 User Experience 字符串组成，转小写后使用包含 NUL 的 UTF-16LE。MD5 和两路 32 位混合运算沿用上游常数，使用无符号移位和 unchecked 溢出；两路结果异或后的 8 字节 Base64 为 Hash。Shell32 字符串从有大小上限的系统 DLL 读取，要求终止 GUID，不使用猜测值。

## 写入、核验和恢复

只在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\<extension>` 操作。产品入口只接受十个压缩后缀，目标固定为本软件 ProgID；ISO/WIM 不参与。没有任意注册表编辑 CLI。

1. 检测 UserChoiceLatest，并按已有 UserChoice 的实际最后写入时间校验旧 Hash。新存储、未知 Hash 或 Win10 1703（15063）之前的系统直接停止，不覆盖默认选择。
2. 使用带 SID 的命名互斥锁与候选注册串行化本软件实例操作，等待上限 5 秒。
3. 删除当前扩展名下单个 UserChoice 子键，不递归删除 FileExts 或父键。接近分钟边界时只在后台线程等待不足 1.1 秒；写入 ProgID / Hash 后按实际键时间核对，至多三次。
4. 通知 Shell 后查询实际默认关联，至多三次、间隔 100 毫秒。只有生效 ProgID 确实匹配才计为成功。注册表回读不能替代结果核验。
5. 写入后失败，原来没有 UserChoice 则删除本次新键；原来有有效旧关联则保留其他值及类型，为旧 ProgID 按新时间重新生成 Hash。直接恢复旧 Hash 会因键时间改变而失效。

发现另一进程已写入不同 ProgID 时不覆盖该选择。回滚也失败则保留操作与回滚的两项异常，明确提示检查系统关联。恢复的是旧应用及值数据，不保证旧键时间戳/安全描述符完全相同；强制终止进程不保证完成回滚。

所有写入作用于当前账户，无需 UAC；权限、组织策略或系统保护失败不会触发提权，不修改 UCPD 驱动、服务或系统策略。默认关联是按用户保存的；管理员权限既不能保证绕过 UserChoiceLatest/UCPD，也可能使提升后的进程操作另一个账户。因此提权不能兑现“所有版本无需确认自动设置”。六套资源覆盖自动说明、权限提示、进度和错误。英文注释覆盖上游算法、整数溢出、时间、回滚、兼容检测和异步 UI 队列。

## Win10 / Win11 与本机结果

SFTA 使用未公开的 Windows 行为。Win10、初代 Win11 和后续 Win11 按实际存储、Hash 与生效结果检测，不能只按版本号判断成功。微软[默认应用平台文档](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform)规定通过系统界面确认默认应用，不支持应用无交互自行修改，UCPD 也可阻止直接写入。因此不能保证所有 Win10/Win11 版本自动完成。新的 UserChoiceLatest 还含机器绑定数据；开源 [UserChoiceLatestHash](https://github.com/cssxn/UserChoiceLatestHash) 说明其固定种子/表只适用于特定环境，不能作为跨版本通用写入器。

此外，本项目目标为 `net10.0-windows`、`win-x64`。可运行系统范围受 [.NET 10 Windows 支持矩阵](https://learn.microsoft.com/en-us/dotnet/core/install/windows) 和 CPU 架构约束；“Windows 10/11 所有版本”也超出当前发布目标。对可运行的系统，官方设置页面提供可完成的交互路径，仍受组织策略限制。

**本机为 Windows 11 build 26200：ZIP 与 RAR 已有 UserChoiceLatest，现由新实现处理，不能宣称这两个格式已自动切换成功。** 新实现的只读哈希验证和一次性测试扩展名写入结果见 [USERCHOICE_LATEST.md](USERCHOICE_LATEST.md)。上游相关反馈见 [UserChoiceLatest 问题](https://github.com/DanysysTeam/PS-SFTA/issues/37)。

本次定向验证：

- 移植算法与 142 个已有 Windows UserChoice Hash 一致，只读比较。
- 实际调用 `.zip` / `.rar` 的保护检查触发 SFTAS0002，原 ProgID / Hash 保持不变。
- 随机、事先确认不存在的测试扩展名写入和 Hash 时间核验通过，但原生 AssocQueryString / QueryCurrentDefault 没有读到目标应用；其初始 Classes 默认值也未解析。因此此次没有验证自动切换成功，也不能单据此推断全部压缩后缀均不支持。
- 未生效试验触发 SFTAS0004，删除本次新建的 UserChoice；FileExts、Classes、RegisteredApplications 和 Capabilities 的私有测试登记已清理。真实压缩格式的默认应用没有修改。
- Win10 / 初代 Win11 实机切换、旧有效默认的完整恢复、强制退出和多进程竞争尚未实测。
- 主项目 Release 编译成功，0 警告、0 错误；`FolderProfile1` 与 `FolderProfile` 两个发布配置均成功，最新文件为 `out/fd/TANGERINE-ZIP.exe` 与 `out/sc/TANGERINE-ZIP.exe`。
- 现有定向验证程序通过 1513 项检查，覆盖六套语言资源、图标、自适应布局/选择快捷键、两种发布文件的 GUI 子系统及 CLI/worker 重定向、ZIP 创建/列出/解压往返。此结果不代表 SFTA 或 UserChoiceLatest 自动切换真实压缩格式成功，且没有更改真实压缩格式的默认应用。

## StageCode

| 代码 | 含义 |
| --- | --- |
| SFTAS0001 | SID 或 Shell32 User Experience 字符串不可用 |
| SFTAS0002 | 过旧系统、UserChoiceLatest 或未知旧 Hash，未覆盖默认选择 |
| SFTAS0003 | 自动关联准备/写入失败，修改后尝试回滚 |
| SFTAS0004 | Windows 未接受目标 ProgID，尝试回滚 |
| SFTAS0005 | 回滚也失败，保留两项异常并提示检查系统关联 |
| SFTAS0006 | 自动关联互斥锁等待失败或超时 |

这些代码已登记至 `Resources/StageList.txt`；跨服务保留已有 StageException。第一按钮显示诊断并提供重试/跳过/停止，不自动操作系统设置界面。原窗口、候选登记、图标服务仍使用 DAPWN / DEFAS 阶段码。
