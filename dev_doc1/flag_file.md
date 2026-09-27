# 标记文件与无后缀配置文件

以下文件均位于程序所在目录（`AppContext.BaseDirectory`），文件名须完全一致。目录位置指实际运行的 EXE 所在目录；开发构建与正式发布各自使用自己的目录。

## 按存在状态生效的标记文件

| 文件名 | 存在时 | 不存在时 | 内容要求 |
| --- | --- | --- | --- |
| `NO_MOUSE_EFFECT` | 关闭鼠标光效 | 开启鼠标光效（默认） | 内容不参与判断；设置页通过创建或删除文件切换。若该路径是目录，启动会报错。 |
| `ADVANCED_MENU_ON` | **仅当文件大小为 0 字节**时，压缩配置自动展开高级选项，并隐藏“高级选项”勾选框 | 普通视图先显示密码选项和高级选项勾选框 | 必须是空文件；非空文件不触发。只对支持高级配置的格式显示高级参数。 |
| `DONT_CHECK_RAR_EXE_AT_START` | `RarToolService.ShouldCheckAtStartup` 返回 `false` | 该属性返回 `true` | 内容不参与判断。**当前启动流程没有调用此属性**，因此目前放置该文件不会改变启动行为；执行 RAR 压缩时始终检查同目录的 `rar.exe`。 |

## 存储设置值的无后缀文件

这些文件不是“空文件即启用”的开关。程序会读取内容，格式错误时显示对应阶段码；请通过设置界面修改。

| 文件名 | 保存的内容 | 缺失时 |
| --- | --- | --- |
| `TEMP_D` | UTF-8 编码的临时目录绝对路径。程序在该目录下使用 `TangerineZipWorkspace` 存放可清理的暂存内容。 | 启动时要求选择临时目录。 |
| `MOUSE_EFFECT_CONFIG1` | 鼠标光晕半径；不变区域性数字，范围 40–360 DIP，默认 140。 | 启动时创建并写入默认值。 |
| `MOUSE_EFFECT_CONFIG2` | 鼠标中心描边扩张量；不变区域性数字，范围 0.20–1.25 DIP，默认 0.95。 | 启动时创建并写入默认值。 |
| `COLOR1` | 文字及列表文字颜色，`#RRGGBB`；默认 `#FFFFFF`。 | 启动时创建默认值。 |
| `COLOR2` | WPF 窗口背景颜色，`#RRGGBB`；默认 `#000000`。 | 启动时创建默认值。 |
| `COLOR3` | 进度条已完成部分的颜色，`#RRGGBB`；默认 `#FFA500`。 | 启动时创建默认值。 |
| `COLOR4` | 进度条背景颜色，`#RRGGBB`；默认 `#FFFFFF`。 | 启动时创建默认值。 |

`COLOR1`/`COLOR2` 与 `COLOR3`/`COLOR4` 分别接受对比度检查；数值有效但配色过近时，程序会要求重新选择。`TEMP_D` 路径必须可访问、可写且有足够空间。程序确认没有其他实例时，在启动阶段清理其专用临时工作目录，不清空用户选择目录中的其他文件。

## 旧版文件与非标记文件

- 旧版资源管理器菜单曾使用 `LocalState/custom_commands/TZIP-mode.txt`。当前版本不创建、不读取它；更换或移除菜单时只做遗留清理。
- `rar.exe` 是创建 RAR 所需的外部工具，不是标记文件；是否能创建 RAR 取决于执行时能否找到它。
- `.tmp`、`.bak`、`.001`、`.part1.rar` 和 `TangerineZipWorkspace` 分别是临时输出、备份、分卷文件与专用目录，不是功能开关。

本表按当前代码核对：`MouseEffectSettings`、`CompressionOptionsWindow`、`RarToolService`、`TempDirectorySettings`、`AppearanceSettings` 和 `ContextMenuRegistrationService`。历史文档中关于 RAR 启动检查的描述与当前调用路径不一致，应以本表注明的当前行为为准。
