# 默认打开方式注销：设计决策与实现记录

> 本文记录可复核的需求分析、设计取舍和验证结果，不包含模型的私有逐步思维记录。实现和接口说明见 [file_open.md](file_open.md)。

## 需求拆解

用户要求在“默认打开方式设置”窗口加入“解除本软件在系统中的注册”按钮。按钮执行时不依赖当前勾选状态，处理本软件支持的全部压缩扩展名。顺序必须是：先清除本软件的默认打开选择，再撤销候选程序注册。若先删除 ProgID，仍指向该 ProgID 的系统默认选择会变成悬空关联。

本软件原有注册计划写入当前用户下的五类条目：独立的 `TangerineZip.Archive.<扩展名>` ProgID、对应扩展名的 `OpenWithProgids` 值、Capabilities 描述和 `FileAssociations` 值、`RegisteredApplications` 值，以及本地应用数据中的格式图标缓存。自动设为默认时，旧版系统写 `UserChoice`，启用新格式的 Windows 11 写 `UserChoiceLatest`。注销必须覆盖这两种格式。

## 关键决定

1. **只清理本软件拥有的选择。** 读取选择键中的 ProgID，只有精确匹配 `TangerineZip.Archive.<扩展名>` 才移动并删除。别的程序的选择即使位于同一扩展名下也不触碰。
2. **先整批清默认，再整批撤注册。** 任一扩展名清除失败，就停止第二阶段，保留所有处理程序注册。已清除的默认值不会重设，重复点击可继续未完成的部分。
3. **分清显式选择与候选回退。** 使用 `RegRenameKey` 将本软件的 `UserChoice` 或 `UserChoiceLatest` 暂移到随机私有备份名，再读取这两种显式选择是否仍指向本软件；若仍存在或读取失败，按逆序恢复。删除备份后才进入注销阶段。已注册的 ProgID 可能在没有显式选择时仍被 Shell 当作默认回退，所以 Shell 的实际默认结果必须在撤销注册后核验。
4. **“清空”指清除本软件的用户选择，而不是写入空 ProgID。** Windows 可能显示其他程序或系统回退程序。Windows 的哈希保护不支持把一个空字符串当成有效默认应用；因此只保证本软件不再是实际默认，并且不更改其他程序的选择。
5. **注销条目按登记计划反向处理。** 只删除自己的 `OpenWithProgids` 值、归本软件所有的 ProgID 树、匹配的 Capabilities 文件关联和 `RegisteredApplications` 值。遇到不认识的 Capabilities 关联或被别人改写的 ProgID，报告错误而不删未知数据。
6. **同一把当前用户互斥锁。** 注册、自动设置和注销共享 `Local\TangerineZip.DefaultApps.<SID>`，避免本程序多个实例在这些步骤中互相覆盖。
7. **保留局部失败信息。** 每个扩展名独立处理并记录；默认值阶段任一失败则不进入注销阶段。注销阶段允许其他扩展名继续清理，最终汇总异常，重试保持幂等。阶段码见 `UNRAS0001`–`UNRAS0005`。
8. **只清本软件生成的图标缓存。** 在全部注册条目清除后，删除当前用户 `LocalAppData\TangerineZip\DefaultApps\Icons` 中已知格式名加 64 位十六进制摘要的 `.ico` 文件；不递归删除目录或其他文件。

## 验证与限制

- Release 编译为零警告、零错误。
- 在随机生成的私有注册表根下验证：外来 ProgID 不会被删除；自己的 ProgID、OpenWith 值和应用登记会清理；别的程序的 OpenWith 值会保留；重复调用安全。
- 同一隔离根下验证 `UserChoiceLatest` 的移走与删除，以及模拟显式选择清理后仍存在时 `UserChoice` 的回滚。测试键均已删除。
- 实际错误报告显示 `.gz`、`.lz4`、`.zstd` 在清除显式选择后仍由 Shell 返回本软件：旧流程因此停在 `UNRAS0002`。这些格式可能由当前候选注册回退解析。修正为先检查显式选择已清、撤销候选注册、最后核验 Shell；旧版本在注销前核验 Shell 的顺序不适用于此情形。
- 在当前 Windows 账户运行修正版完整注销，10 个受支持扩展名全部通过；第二次运行也通过，确认幂等。只读复核未见这 10 个格式的本软件 ProgID 或 Capabilities 文件关联。
- 未拿用户真实 ZIP/RAR 默认设置做破坏性测试。Windows 的 UCPD 或组织策略可能拒绝对受保护键改名或删除；程序会报告失败并保留处理程序注册，不承诺所有 Windows 10/11 构建都允许自动清理。
- [Microsoft 默认应用平台文档](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform)说明注册表方式不是受支持的修改默认应用接口，且可能受 UCPD 保护。这里的注销沿用项目现有非官方关联机制，因此必须核验 Shell 实际结果。
