# Windows 11 UserChoiceLatest 自动关联（2026-10-01）

## 范围与来源

`DefaultAppAssociationService.SetThisAppDefault` 先注册本软件 ProgID，再检查当前用户是否启用 `UserChoiceLatest`：已有该子键，或 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\SystemProtectedUserData\<SID>\AnyoneRead\AppDefaults\HashVersion=1` 时走 `LatestUserChoice`；否则走经典 SFTA。所有写入仍针对当前用户，第一按钮不会操作 Windows 设置界面，只有实际 Shell 默认 ProgID 等于本软件才计为成功。第二按钮保留用户主动选择其他程序的官方设置入口。

哈希算法来自 [cssxn/UserChoiceLatestHash](https://github.com/cssxn/UserChoiceLatestHash)，按 MIT 许可保存于 [`third_party/UserChoiceLatestHash`](../third_party/UserChoiceLatestHash/README.md)。其固定种子和查表算法只经过有限环境验证，不能视为 Microsoft 支持的接口。`TzipHashExport.cpp` 提供只计算哈希的 native DLL；主程序将 DLL 作为资源嵌入单文件发布，按 SHA-256 文件名提取到当前用户临时目录并校验内容后加载。native 组件本身不改注册表，也不启动命令行窗口。

## 写入与恢复

1. 读取机器 ID、当前用户 SID 和已有 `UserChoiceLatest\ProgId` 的实际最后写入时间；若已有哈希不能重算吻合，停止，绝不覆盖未知格式。
2. 在目标扩展名的 FileExts 键下创建带 GUID 的私有待提交键，写入目标 ProgID，按其嵌套键时间计算新哈希并回读核验。
3. 将旧 `UserChoiceLatest` 临时改名为带 GUID 的备份，再将完整待提交键改名为 `UserChoiceLatest`。这个顺序避免先修改现有键中的 ProgID、再短暂留下错误哈希。
4. 通知 Shell 并查询实际生效的 ProgID；仅在匹配时计为成功。否则将新键移开，尝试把旧键改名恢复。回滚也失败时保留两个异常。进程若恰在两次改名之间退出，下次进入同一扩展名时会尝试恢复唯一备份。

不修改 UCPD 驱动、实验特性、系统策略、其他应用的 ProgID 或 HKLM 关联。`RegRenameKey` 是否能替换所有已受保护键仍须按系统版本实测；失败会报告阶段码，不会伪装为成功。普通 UAC 提权不能替代有效哈希或保证绕过 UCPD，因此目前没有无条件提权分支。

## 本机验证与边界

- 源码编译的独立哈希校验器与这台 Windows 11 Home 的 ZIP、RAR、PDF、TXT、JPG 五个现有 `UserChoiceLatest` 哈希一致，只读验证；7z/GZip/TAR 尚无现有 Latest 键。
- 独立原型在随机生成、事先确认不存在的测试扩展名上成功写入新哈希，Windows 实际默认 ProgID 变为测试程序；程序随后删除该扩展名及其两个私有 ProgID，清理均成功。这个试验不涉及真实压缩格式。
- 用与主程序共享的 `LatestUserChoice.cs` 在另一个随机扩展名上注册两个有效私有 ProgID：首次设置为 A、将已有有效默认自动替换为 B，Shell 查询均正确。随后强制模拟生效检查失败，返回 `LUCAS0004` 且自动回滚后仍为 B。测试项全部清理。这个测试没有覆盖真实 ZIP/RAR 的系统保护行为。
- 主程序嵌入的 native DLL 可加载，公开示例的预期哈希 `JOBZ2dl4dKM=` 与生成结果一致；主程序构造规范化输入的结果也一致。Release 编译为 0 警告、0 错误。
- 真实 ZIP/RAR 的已有受保护键、多个 Win11 更新版本及 UCPD 拦截仍未完成实机验证。已验证显式生效失败时的回滚；进程中断或注册表访问被拒时的回滚尚未验证。当前实现不能宣称“新 Win11 所有版本都已保证自动成功”。

## 阶段码

`LUCAS0001`：机器 ID、哈希输入或 native codec 不可用。
`LUCAS0002`：已有新哈希无法验证或当前系统未启用新格式。
`LUCAS0003`：准备或改名失败，已尝试恢复。
`LUCAS0004`：Windows 实际默认未变为目标 ProgID，已尝试恢复。
`LUCAS0005`：恢复旧键也失败，保留两项异常。
`LUCAS0006`：当前用户关联互斥锁失败或超时。

详见 [`Resources/StageList.txt`](../TANGERINE-ZIP/Resources/StageList.txt)。微软[默认应用平台文档](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform)仍要求通过系统 UI 设置默认应用；这里的注册表机制并非受支持的公开 API。
