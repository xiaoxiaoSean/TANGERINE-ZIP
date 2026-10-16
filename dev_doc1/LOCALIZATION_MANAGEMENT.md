# 本土化管理类与资源概览

## 核心入口

| 类或文件 | 职责 |
| --- | --- |
| [`LanguageManager`](../TANGERINE-ZIP/LanguageManager/LanguageManager.cs) | 全局静态资源访问入口。`Get(key)` 使用当前线程的 `CultureInfo.CurrentUICulture` 从 `TANGERINE_ZIP.LanguageManager.UiStrings` 读取字符串；未找到时尝试 `en-US`，最后返回键名。资源由执行程序集加载。 |
| [`UiStrings*.resx`](../TANGERINE-ZIP/LanguageManager/) | 保存界面和错误提示的键值文本。`UiStrings.resx` 是默认资源，另有 `en-US`、`zh-CN`、`zh-TW`、`zh-HK`、`zh-MO` 区域资源。 |
| [`TANGERINE-ZIP.csproj`](../TANGERINE-ZIP/TANGERINE-ZIP.csproj) | 将中性语言设为 `en-US`，并指定发布的卫星资源语言为上述五种区域语言。 |

调用方式为 `LanguageManager.Get("Confirm")`；带参数的文案由调用方使用 `string.Format(LanguageManager.Get("..."), 参数)`。`ResourceManager.GetString` 会先执行 .NET 自身的区域及中性资源回退。当前代码随后还显式查询 `en-US`，最后返回键名。因此区域资源缺键时，通常会先使用默认资源中的英文文本。

## 与语言选择相关的类

| 类 | 本土化行为 |
| --- | --- |
| [`WpfStartup`](../TANGERINE-ZIP/WpfStartup.cs) | 区分 GUI、CLI 和归档工作进程入口。GUI 通常沿用系统 UI 区域；当 CLI 启动的工作进程带有 `TZIP_CLI_ENGLISH=1` 时，将当前文化与 UI 文化设为 `en-US`。 |
| [`CommandLine`](../TANGERINE-ZIP/Services/CommandLine.cs) | 执行命令前将 `CurrentCulture` 和 `CurrentUICulture` 设为 `en-US`，并设置 `TZIP_CLI_ENGLISH=1`，使命令输出及子进程诊断保持英文。 |
| [`ArchiveWorker` / `WorkerRequest`](../TANGERINE-ZIP/Services/ArchiveWorker.cs) | 独立归档进程通过请求中的 `Culture` 字段接收 UI 区域，并在处理前设置 `CurrentUICulture`；创建请求时使用调用方当前 UI 区域名。 |

项目中没有独立的语言设置类，也没有在设置窗口中切换界面语言的机制。GUI 文案一般在窗口构造或操作执行时读取；已创建的控件不会因进程文化后来改变而自动刷新。

## 主要使用方

- [`MainWindow`](../TANGERINE-ZIP/MainWindow.xaml.cs) 和 [`SettingsWindow`](../TANGERINE-ZIP/SettingsWindow.xaml.cs)：菜单、标签、状态、文件对话框及设置界面文案。
- 各窗口的 `*.xaml.cs`：在构造时或操作过程中设置标题、按钮、提示等，例如密码、批量处理、首次使用及系统集成窗口。
- [`ArchiveService`](../TANGERINE-ZIP/Services/ArchiveService.cs)、[`ResourcePreflight`](../TANGERINE-ZIP/Services/ResourcePreflight.cs)：归档及资源检查的错误说明。
- [`MessageTipGenerator`](../TANGERINE-ZIP/Tools/MessageTipGenerator.cs)：按阶段码拼装本土化错误原因和建议。

这些类是资源的使用方，集中管理和回退逻辑仍在 `LanguageManager`。

## 当前资源状态与维护方法

按当前工作区的 `.resx` 文件统计，默认资源和 `en-US` 各有 727 个键，四份中文区域资源各有 714 个键。新增文案时应在默认资源及所需区域资源中使用同一个键，并检查 `string.Format` 占位符在各语言中是否一致。缺失的区域键会触发资源回退，可能在中文界面显示英文。

排查文案时先搜索 `LanguageManager.Get("键名")` 的调用，再到 `LanguageManager/UiStrings*.resx` 核对翻译；动态拼接的键（例如 `"Format_" + 类型`）还需检查对应的整组键。GUI 与 CLI 的语言行为不同，验证时分别从窗口和命令行入口启动。

## 文件夹结构

下面只列语言管理目录、资源文件及其项目配置文件。`UiStrings.resx` 与各区域文件必须使用同一个资源基名，并放在同一目录。

```text
TANGERINE-ZIP/
├── TANGERINE-ZIP.csproj                 # 中性语言、卫星资源发布设置
└── LanguageManager/
    ├── LanguageManager.cs              # 统一取词入口
    ├── UiStrings.resx                   # 默认资源，英文
    ├── UiStrings.en-US.resx             # 英文区域资源
    ├── UiStrings.zh-CN.resx             # 简体中文
    ├── UiStrings.zh-TW.resx             # 繁体中文，台湾
    ├── UiStrings.zh-HK.resx             # 繁体中文，香港
    └── UiStrings.zh-MO.resx             # 繁体中文，澳门
```

## 核心代码怎么写

### 1. 统一取词类

项目中的 `LanguageManager.cs` 是一个没有命名空间声明的全局静态类。资源基名由项目根命名空间、目录和资源文件名组成；复制到别的项目时，必须改成目标程序集实际生成的资源名。

```csharp
using System.Resources;
using System.Reflection;
using System.Globalization;

public static class LanguageManager
{
    private static readonly ResourceManager rm =
        new ResourceManager(
            "TANGERINE_ZIP.LanguageManager.UiStrings",
            Assembly.GetExecutingAssembly());

    public static string Get(string key)
    {
        return rm.GetString(key, CultureInfo.CurrentUICulture)
            ?? rm.GetString(key, CultureInfo.GetCultureInfo("en-US"))
            ?? key;
    }
}
```

`CurrentUICulture` 控制资源语言；`CurrentCulture` 主要影响数字、日期等格式。`Assembly.GetExecutingAssembly()` 指向定义该类的程序集。若把资源放入单独类库，应核对资源实际位于哪个程序集，并使用该程序集创建 `ResourceManager`。

### 2. 资源文件与项目设置

`.resx` 是 XML 键值表。同一个键在不同文件中给出不同译文，例如：

```xml
<!-- UiStrings.resx / UiStrings.en-US.resx -->
<data name="Confirm" xml:space="preserve"><value>Confirm</value></data>

<!-- UiStrings.zh-CN.resx -->
<data name="Confirm" xml:space="preserve"><value>确认</value></data>
```

项目文件中的相关配置为：

```xml
<RootNamespace>TANGERINE_ZIP</RootNamespace>
<NeutralLanguage>en-US</NeutralLanguage>
<SatelliteResourceLanguages>en-US;zh-CN;zh-TW;zh-HK;zh-MO</SatelliteResourceLanguages>
```

默认 `.resx` 编入主程序集；区域 `.resx` 通常生成对应文化的卫星资源程序集。`SatelliteResourceLanguages` 约束发布时保留的语言。资源文件由 .NET SDK 按常规 `.resx` 规则包含，本项目没有为这些文件单独写 `EmbeddedResource Include`。

### 3. 窗口和业务代码调用

主窗口在 `Loaded` 事件中调用 `ApplyLocalizedText()`，集中设置当前控件文字；其他窗口多在构造函数里设置。操作过程中产生的状态或错误则在使用时取词。

```csharp
private void ApplyLocalizedText()
{
    operationStatusText.Text = LanguageManager.Get("readytext");
    openArchiveMenuItem.Header = LanguageManager.Get("openText");
    aboutMenuItem.Header = string.Format(
        LanguageManager.Get("AboutTzipMenu"), GlobalConfig.VName);
}
```

这里 `AboutTzipMenu` 的英文值为 `About {0}`，简体中文值为 `关于 {0}`。占位符属于翻译模板，各语言可以调整词序，但应保留相同的参数编号。服务层也直接调用 `LanguageManager.Get`，例如 `ResourcePreflight` 抛错时通过资源键取得磁盘空间不足说明。`MessageTipGenerator` 会组合多个资源键生成完整错误提示。

### 4. GUI、CLI 和子进程语言传递

GUI 没有自定义语言选择逻辑，通常使用操作系统提供的当前 UI 区域。CLI 则在 `CommandLine.RunAsync` 开始时执行：

```csharp
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
Environment.SetEnvironmentVariable("TZIP_CLI_ENGLISH", "1");
```

归档操作运行在另一进程。父进程创建 `WorkerRequest` 时写入 `CultureInfo.CurrentUICulture.Name`；工作进程反序列化后执行：

```csharp
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(request.Culture);
```

这样 GUI 启动的工作进程能使用 GUI 的语言。CLI 启动工作进程时，`WpfStartup` 检查 `TZIP_CLI_ENGLISH`，并把文化设为英文；`CommandLine` 设置的英文文化也会通过请求传给工作进程。这里的环境变量和 `WorkerRequest.Culture` 是本项目的进程协议，单进程项目不需要照搬。

## 在其他 .NET / WPF 项目中复用

1. 在目标项目建立 `LanguageManager/`，放入 `UiStrings.resx`、所需的 `UiStrings.<区域名>.resx` 和取词类。先确定默认语言，再保持资源基名、目录与代码中的 `ResourceManager` 名称一致。
2. 在目标 `.csproj` 设置 `NeutralLanguage`；若有单文件发布或裁剪卫星资源的配置，再列出需要发布的区域语言。不要直接复制本项目的 `RootNamespace`。
3. 把用户可见字符串改成稳定资源键；窗口标题、按钮、菜单、状态、对话框、业务异常都通过统一入口取词。保留翻译所需的整个句子，避免用多个片段拼接语序。
4. 选定语言来源：沿用系统 UI 区域时无需额外设置；若要支持应用内语言切换，需要另加语言设置、文化传播和已打开窗口的刷新机制。当前项目没有这些功能。
5. 如果有后台独立进程，把文化名作为请求字段显式传递，并在子进程处理请求前设置 `CurrentUICulture`。如果命令行输出要固定语言，也在 CLI 入口明确设置文化。
6. 用目标项目实际运行验证：各语言窗口文字、缺失键回退、格式化占位符、CLI 输出，以及发布包里的卫星资源。若总是返回键名，优先检查资源基名和程序集；若只在发布后失效，检查卫星资源是否随包保留。

本实现适合启动时确定语言、以代码设置 WPF 文案的应用。若目标项目使用 XAML 动态绑定、运行时切换语言或多程序集资源，应在此基础上增加可通知 UI 更新的资源服务，不应把 `LanguageManager.Get` 直接复制后期待控件自动刷新。
