# 自适应布局与禁止命令行闪窗（2026-10-01）

## 控制台闪现的原因与修复

此前项目 `OutputType=Exe`，发布 apphost 使用控制台子系统。Windows 在进入托管 `Program.Main` 之前就可以分配控制台；原来的 `FreeConsole()` 只能事后关闭，无法阻止启动瞬间闪现。这也影响以同一 EXE 启动的文件关联、新实例和提权证书辅助入口。

项目现在明确设置 `OutputType=WinExe`，生成 Windows GUI 子系统的 apphost，从进程创建阶段避免自动分配控制台。删除事后 `FreeConsole()` 及其 P/Invoke。GUI、右键菜单、内部 worker、提权证书辅助程序使用同一个 Windows GUI apphost。

按普通 WPF 项目组织启动：`App.xaml` 的 ApplicationDefinition 自动生成带 STA 的入口，`App.xaml.cs` 处理 Startup 和 DispatcherUnhandledException。原 `Program.cs` 改为 `WpfStartup.cs`，负责既有配置准备和参数分派，不再声明 Main、创建第二个 Application 或手动调用 Application.Run。正常启动明确设置 MainWindow 并 Show，WPF 自身管理事件循环及窗口关闭。配置失败或辅助任务结束通过 Shutdown 保留退出码，诊断仍使用稳定的 PROGM 阶段码。

Startup 事件异步等待 CLI/worker 服务，避免在 WPF dispatcher 上使用 GetAwaiter().GetResult 阻塞其异步延续。GUI 配置及窗口创建仍在 UI 线程进行。

所有直接后台工具调用（7-Zip、RAR、归档 worker、注册右键菜单使用的 PowerShell）都使用 `UseShellExecute=false`、`CreateNoWindow=true` 和重定向输出；打开另一个归档的新实例也加入 `CreateNoWindow=true`。此参数不隐藏 WPF 的正常窗口。需要 UAC 的证书辅助入口通过 `runas` 启动自己的 WinExe，保留已有 `WindowStyle=Hidden`；UAC 是权限确认，不是命令行窗口。Windows 默认应用设置的 `ms-settings:` 使用 Shell 打开官方设置 UI，不能为了隐藏控制台而隐藏该设置窗口。

公开 CLI 接口保留。`CommandLine` 仅在没有重定向的标准输出/错误时尝试 `AttachConsole(ATTACH_PARENT_PROCESS)`，沿用已经存在的调用方终端；绝不调用 `AllocConsole`，也不创建 cmd.exe 或终端窗口。`GetStdHandle` / `GetFileType` 识别文件与管道句柄，避免 AttachConsole 替换脚本传入的重定向输出。没有调用方控制台时 AttachConsole 失败是正常情形，不创建替代窗口。

Windows GUI 程序从不同终端启动时，调用方的等待规则可能与控制台程序不同。脚本需要确定执行结束和退出码时，应显式等待，例如：

```powershell
$process = Start-Process -FilePath './TANGERINE-ZIP.exe' -ArgumentList 'help' `
    -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput './help.txt' -RedirectStandardError './help-errors.txt'
$process.ExitCode
```

以上重定向不会弹出新的命令行窗口。归档工作进程仍通过已有标准输入/输出管道传送 JSON，不附加调用方控制台。

## 控件与布局

- 默认打开方式：说明和九个复选框组成可滚动的比例区域，日志占另一个比例区域；状态说明按内容显示并受动态可用高度约束；操作按钮与选择按钮按内容自适应，窄窗口下换行。新增全选/全不选，设置过程中冻结选择。
- 主窗口：标题、搜索、条目表格各占独立布局行，移除搜索框固定高度和原先用于避让搜索框的固定偏移。条目三列按实际视口宽度比例分配，表头按可用宽度换行，空归档列表也能显示完整标题。
- 归档工具：文件路径和结果区使用比例行；模式/算法使用 Grid 的 Auto/比例列，移除两个 ComboBox 固定宽度。
- 压缩选项与编码选择：窗口初始大小按屏幕工作区比例计算，移除固定像素尺寸和固定最小宽度；压缩选项底部按钮改为 WrapPanel。
- 共用提示：移除固定窗口宽度、滚动区域最大高度和按钮最小宽度，采用工作区比例大小与可换行按钮。长内容保留滚动能力。
- 共用主题：复选标记、下拉箭头、子菜单箭头跟随 FontSize；下拉列表最大高度使用 ComboBox 的动态 MaxDropDownHeight；分隔线以边框绘制而不固定控件高度。

`WpfUi.SizeWindow` 只计算初始窗口大小，使用当前系统工作区比例；没有将控件宽高写死。用户仍可调整窗口，控件由布局容器测量和分配。边距、内边距、线条厚度及矢量图形坐标属于绘制参数，不作为控件固定宽高。

## 注释、异常与验证

英文注释说明 Windows 在 Main 前创建控制台的时机、GUI 子系统、管道句柄保留、新实例无控制台启动，以及设置流程冻结选择的原因。批量选择异常使用 `DAPWN0004`，现有进程启动、退出码、取消及窗口错误仍使用各自服务的异常处理和 StageCode。

- Release 编译成功，0 警告、0 错误。
- 按 `FolderProfile1`（依赖框架）与 `FolderProfile`（自包含）发布成功，两个 EXE 的 PE Subsystem 均为 WindowsGui。
- 两种实际发布 EXE 均验证 `help` 的标准输出/退出码、无效命令的标准错误/退出码、worker 的 JSON 错误输出/退出码，未出现管道输出丢失。
- 两种实际发布 EXE 复制到工作区的独立 GUID 测试目录，配置该目录下的 TEMP_D，验证带空格的 EXE 路径及中文文件名的 ZIP 创建、JSON 列出、解压内容往返。测试成功后清理专属目录，未更改原安装配置。
- 五种 UI 文化、三个可用区域尺寸、两种字号的实际 WPF 布局测量验证，以及全选、全不选、流程锁定验证通过；没有写入真实默认应用选择。
- 实际简中窗口验证全选/全不选的勾选反馈与按钮主题；窗口已关闭。
- 标准 App 入口更新后，验证编译程序集的 EntryPoint 属于 TANGERINE_ZIP.App，原有布局/文件关联/原生 CLI/worker/ZIP 往返验证共通过 1369 项检查。复制发布 EXE 到工作区独立目录，用该目录的配置实际启动主窗口，确认菜单和表头正常，关闭后进程退出；专属目录已清理。
- 未对 Win10/初代 Win11 实机进行新增人工回归；GUI 子系统与无窗口进程创建采用 Windows 通用机制。

## 官方参考

- [Microsoft：Creation of a Console](https://learn.microsoft.com/en-us/windows/console/creation-of-a-console)
- [Microsoft：.NET Desktop SDK properties](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop)
- [Microsoft：ProcessStartInfo.CreateNoWindow](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.createnowindow)
