# GUI 与命令行功能对应

本文记录用户可见的 GUI 操作与公开 CLI 的对应关系。命令行使用英文帮助、状态及错误文本；GUI 仍按所选语言显示。CLI 和 GUI 对归档读写调用同一批服务，并保留相同的 StageCode。界面宽高由现有 WPF 比例布局决定；本次新增功能没有增加或直接指定控件尺寸。

## 归档与文件操作

| GUI 操作 | CLI 操作 | 说明 |
|---|---|---|
| 打开归档、浏览成员、查看详细信息 | `list ARCHIVE`、`list ARCHIVE --json` | JSON 保留成员元数据。 |
| 成员搜索 | `list ARCHIVE --search TEXT` | 对完整成员路径进行不区分大小写的包含匹配；可与 `--json` 合用。 |
| 文件预览 | `extract ARCHIVE DEST --entry PATH` | 命令行将文件交给指定目标目录，终端自身没有图像/PDF 窗口；文本可由终端或其他程序读取。 |
| 解压全部、选中成员、解压到归档旁边 | `extract ARCHIVE DEST [--entry PATH...]` | 终端直接指定目标目录；`--on-conflict` 和 `--on-issue` 代替交互对话框。 |
| 展开嵌套 TAR | `nested-tar ARCHIVE DEST [--entry OUTER_TAR...]` | 省略成员时展开检测到的全部内层 TAR；冲突与安全问题默认中止。 |
| 选择或拖入来源后创建归档 | `compress OUTPUT SOURCE...` | 文件列表直接作为位置参数。ISO、自解压及高级压缩参数见 `command.md`。 |
| 把来源文件添加到已打开归档 | `add ARCHIVE FILE...` | 与 GUI 一样保留原归档备份。 |
| 归档内部复制、剪切/粘贴、删除 | `edit ARCHIVE copy\|move\|delete --entry PATH... [--destination FOLDER]` | `--destination` 是归档内的目标目录，复制和移动必填；服务验证成员、安全路径和源文件身份。 |
| 批量解压、转换 | `batch-extract`、`convert` | 各输入仍有独立输出；既有输出被拒绝。 |
| ZIP 注释 | `comment ARCHIVE [--set TEXT\|--file FILE]` | 不给修改参数则读取。 |
| 完整性检查、修复、哈希 | `test`、`repair`、`hash` | `test` 中坏成员使退出码非零。 |
| 自解压 EXE | `sfx SOURCE OUTPUT.exe` 或 `compress --sfx` | 与 GUI 使用相同转换或压缩服务。 |
| Microsoft Defender 扫描、快照 | `scan`、`snapshot` | 共用服务与阶段码。 |

## 设置与系统集成

| GUI 操作 | CLI 操作 | 说明 |
|---|---|---|
| 临时目录 | `temp get`、`temp set DIRECTORY` | `set` 使用 GUI 的验证与原子配置写入。 |
| 鼠标效果开关、半径、厚度 | `appearance mouse get`、`appearance mouse enabled on\|off`、`appearance mouse radius NUMBER`、`appearance mouse thickness NUMBER` | 使用相同范围与配置文件。 |
| 五种主题颜色 | `appearance color 1..5 get\|set #RRGGBB\|reset` | 颜色 1/2 和 3/4 是对比度配对，可对 1 或 3 使用 `reset-pair`。 |
| 压缩配置 | `profile list\|show\|save\|delete`，`compress --profile NAME` | JSON 输入是 `CompressionOptions` 对象；保存时拒绝密码。 |
| 密码管理器 | `vault list\|save\|check\|delete` | 仍保存在 Windows Credential Manager；不会打印密码。 |
| 注册/删除资源管理器右键菜单 | `integration create-context-menu`、`integration delete-context-menu` | 使用相同的系统服务，安装现代菜单可能显示 Windows 提权提示。 |
| 设置本程序为默认打开方式 | `integration default EXTENSION` | 每次指定一个受支持扩展名，并核验 Windows 生效的关联。 |
| 打开 Windows 页面选择其他默认应用 | `integration choose-default EXTENSION` | 注册本程序为可选处理程序并打开 Windows 设置页，由用户选择最终应用。 |
| 移除本程序的默认关联和注册 | `integration unregister-defaults` | 与 GUI 一样处理所有支持的扩展名。 |

## 非数据性界面动作

GUI 的窗口开关、导航、焦点、列宽、拖放手势、进度条和关于窗口属于呈现或输入方式。CLI 通过参数、标准输出、退出码和 `Ctrl+C` 提供对应工作流；终端不会显示 WPF 窗口。选择其他默认应用仍需在 Windows 设置页交互。`help` 提供命令总览和逐项语法。

## 错误边界

- 参数错误：`CLINE0007`；缺少指定成员：`CLINE0004`；缺少嵌套 TAR：`CLINE0012`；配置不存在或无效：`CLINE0013`、`CLINE0014`；主动取消：`CLINE0015`。
- 归档内改写、嵌套提取、系统关联、外观、配置存储分别保留服务层 `ARCED`、`NESTR`、`DEFAS`/`UNRAS`/`CTXMN`、`COLRS`/`MECFG`、`CPRFL` 阶段码。
- CLI 默认拒绝会覆盖目标的解压冲突，也默认停止安全问题；显式 `--on-conflict` 和 `--on-issue` 用于无人值守脚本。所有异常都写入标准错误，不弹出错误对话框。

完整语法见 [`command.md`](command.md)，阶段码定义见 `TANGERINE-ZIP/Resources/StageList.txt`。
