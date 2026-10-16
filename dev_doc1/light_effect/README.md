# 鼠标邻近光效：封装、原理与调用

本文对应当前代码中的 **鼠标邻近浅色笔画增粗效果**。它不是独立光源或发光控件：鼠标靠近时，窗口内容中的白色、浅灰色笔画会向四周略微扩张，距离越远扩张越小。下文以当前实现为准；旧文档中提到的 `ENABLE_LIGHT` 编译开关不适用于这套效果，当前项目文件和调用代码没有用该开关包围它。

## 1. 封装边界与文件

| 层次 | 当前文件 | 职责 |
| --- | --- | --- |
| 窗口接入与生命周期 | `TANGERINE-ZIP/MouseWhiteThickening.cs` | `MouseWhiteThickening.Attach` 为一个窗口创建效果实例，跟踪鼠标和窗口尺寸，响应设置变更，窗口关闭时解除订阅 |
| WPF 着色器包装 | 同上，`MouseWhiteThickenEffect` | 把 WPF 视觉输入和四个参数映射到像素着色器的采样器、常量寄存器 |
| 像素算法源文件 | `TANGERINE-ZIP/Shaders/MouseWhiteThicken.fx` | 定义浅色覆盖率、邻域采样和随距离衰减的笔画扩张 |
| 运行时着色器 | `TANGERINE-ZIP/Shaders/MouseWhiteThicken.ps` | `.fx` 编译得到的 Pixel Shader 2.0 字节码，作为 WPF `Resource` 编入程序集 |
| 配置与广播 | `TANGERINE-ZIP/Services/MouseEffectSettings.cs` | 读取、验证、保存开关和参数；变更后向已打开窗口广播 |
| 用户入口 | `TANGERINE-ZIP/SettingsWindow.xaml(.cs)` | 开关、范围和粗细滑块，合并拖动期间的密集写入 |

现有封装的调用面很小：窗口只需调用 `MouseWhiteThickening.Attach(window, fallbackRadius)`；启动流程先初始化 `MouseEffectSettings`，设置页调用它的异步写入方法。着色器采样、事件订阅和清理都留在封装内部。以上 C# 类型目前均为 `internal`，供同一程序集使用；它们不是可直接引用的独立 NuGet 包。

## 2. 渲染原理

### 2.1 输入和坐标

`Attach` 要求 `window.Content` 是 `FrameworkElement`，并把一个 `MouseWhiteThickenEffect` 放在该元素的 `Effect` 属性上。WPF 把这个元素的已渲染内容交给像素着色器的 `s0` 采样器。着色器输出只影响该视觉内容的像素，不改变控件布局、文本值或鼠标命中测试；系统标题栏、Windows 文件对话框和不属于此内容视觉树的弹出层不在作用范围内。

`PreviewMouseMove` 监听窗口范围内的鼠标移动，即使事件已被子控件处理也能收到。鼠标位置用 `args.GetPosition(content)` 转为内容坐标，再除以内容的 `ActualWidth`、`ActualHeight` 得到归一化坐标 `cursor`。内容尺寸作为 `viewport` 传给着色器。鼠标移出内容、窗口失活或鼠标离开窗口时，把 `cursor` 设为 `(-100, -100)`；这样作用圆落在画面外，无需销毁效果。

着色器对每个像素的 UV 坐标计算：

```text
distance  = length((uv - cursor) * viewport)
proximity = clamp(1 - distance / radius, 0, 1)
offset    = thickness * proximity / viewport
```

`radius` 和 `thickness` 均按 WPF 内容坐标使用，界面以 DIP 表示。中心处偏移最大，半径边界处趋于零，圆外不扩张。`viewport` 的两个分量还分别把横向、纵向偏移转换到 UV 空间，避免窗口长宽不同时效果被拉伸。

### 2.2 只扩张浅色像素

源像素和上、下、左、右四个偏移位置各采样一次。每个采样的浅色覆盖率定义为：

```text
coverage = clamp((min(R, G, B) - 0.38) * 1.75, 0, 1)
```

取 RGB 最小通道意味着三个通道都比较亮才会被认作浅色；例如橙色强调元素的蓝通道较低，通常不会被扩张。四个邻点覆盖率取最大值，再减去原像素已有的覆盖率，得到只需补充的部分：

```text
added = max(0, max(neighborCoverages) - originalCoverage)
result.rgb = original.rgb + (1 - original.rgb) * added
result.a   = original.a
```

因此效果是局部的浅色笔画增粗，原像素的透明度保持不变。它不会复制邻点的 RGB 图像，避免在文字和图标边缘出现一份错位的副本。着色器不生成模糊光晕，也不修改深色背景的颜色。

### 2.3 WPF 参数映射

`MouseWhiteThickenEffect` 的 `Input` 用 `RegisterPixelShaderSamplerProperty` 绑定到 `s0`；其余依赖属性用 `PixelShaderConstantCallback` 对应着色器寄存器：

| C# 属性 | HLSL 变量 | 寄存器 | 含义 |
| --- | --- | --- | --- |
| `Cursor` | `cursor` | `c0` | 内容内归一化鼠标坐标；画面外哨兵值表示无效果 |
| `Viewport` | `viewport` | `c1` | 内容宽高 |
| `Radius` | `radius` | `c2` | 作用半径 |
| `Thickness` | `thickness` | `c3` | 中心处最大采样偏移 |

着色器字节码由共享的 `PixelShader` 对象加载；**每个窗口仍有自己的 `MouseWhiteThickenEffect` 实例**，所以窗口之间的鼠标坐标和尺寸互不干扰。鼠标移动只更新常量，不重新创建效果或窗口。

## 3. 调用方式

### 3.1 启动时加载配置

正常 WPF 启动流程在创建第一个窗口前调用：

```csharp
MouseEffectSettings.Initialize();           // 读取开关标记
MouseEffectSettings.InitializeParameters(); // 读取或创建半径、粗细文件
```

实际启动流程位于 `WpfStartup.cs`，首次使用流程位于 `FirstRunWindow.xaml.cs`。它们在后台执行文件 I/O；对于非法数值，启动流程会提示用户是否删除相应文件并重建默认值。**不要在新增窗口的构造函数中重复执行初始化**。

### 3.2 给窗口附加效果

在每个窗口的 `InitializeComponent()` 之后调用一次：

```csharp
using System.Windows;

namespace TANGERINE_ZIP;

internal sealed partial class ExampleWindow : Window
{
    private const double MouseWhiteThickenRadius = 125.0;

    internal ExampleWindow()
    {
        InitializeComponent();
        MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius);
    }
}
```

`fallbackRadius` 必须是有限的正数。正常启动完成后，保存的全局半径覆盖它；若参数尚未初始化，该值作为窗口的回退半径。当前窗口将半径常量放在类的第一个字段，新增窗口可沿用此约定。**一个窗口只调用一次 `Attach`**，因为调用会登记鼠标、尺寸、设置变更和关闭事件。

附加时如果内容不是 `FrameworkElement`、半径无效或发生其他错误，封装显示 `MWFXT` 阶段码，并让窗口保留原始画面。`MWFXT0001` 是内容缺失，`MWFXT0002` 是半径无效，`MWFXT0003` 是其他附加错误；`MWFXT0004` 和 `MWFXT0005` 分别用于即时切换与参数更新失败。

### 3.3 命令行调用

项目的 CLI 也使用同一个设置服务。示例：

```text
TANGERINE-ZIP.exe appearance mouse get
TANGERINE-ZIP.exe appearance mouse enabled on
TANGERINE-ZIP.exe appearance mouse enabled off
TANGERINE-ZIP.exe appearance mouse radius 180
TANGERINE-ZIP.exe appearance mouse thickness 1.10
```

`get` 输出当前开关、半径和粗细；写入命令先加载并校验配置，再保存指定值。数字使用 `.` 作为小数点。

## 4. 参数调整方式

### 4.1 参数含义和范围

| 参数 | 视觉作用 | 默认值 | 有效范围 | 持久化文件 |
| --- | --- | ---: | ---: | --- |
| 开关 | 控制是否把效果附加到窗口内容 | 开 | 开 / 关 | `NO_MOUSE_EFFECT`，存在表示关 |
| 半径 `Radius` | 鼠标周围的作用范围；越大影响区域越大 | 140 DIP | 40–360 DIP | `MOUSE_EFFECT_CONFIG1` |
| 粗细 `Thickness` | 鼠标中心处的最大采样偏移；越大浅色笔画越粗 | 0.95 DIP | 0.20–1.25 DIP | `MOUSE_EFFECT_CONFIG2` |

半径边界处扩张量始终降为零。粗细上限为 1.25 DIP，超过可能使笔画出现重影；服务会拒绝越界值、`NaN` 和无穷大。

### 4.2 在设置页调整

打开主窗口的“设置”页，使用“鼠标效果”开关切换效果，拖动“光晕范围”滑块调整半径，拖动“描边粗细”滑块调整强度。滑块事件会合并为约 120 ms 一次的保存请求；保存成功后所有已打开窗口立即更新。三个“恢复默认”按钮分别恢复开关、半径和粗细。若保存失败，控件恢复上一次成功保存的数值并显示错误。

### 4.3 在代码中调整

在 WPF UI 线程调用并 `await` 保存方法：

```csharp
await MouseEffectSettings.SetEnabledAsync(true);
await MouseEffectSettings.SetRadiusAsync(140);
await MouseEffectSettings.SetThicknessAsync(0.95);
```

方法先在后台写入并核对配置，成功后更新内存状态并触发 `Changed` 或 `ParametersChanged`。已附加窗口收到事件后只切换 `content.Effect` 或修改自身着色器常量；不需要手动再次 `Attach`。调用者负责捕获保存异常并恢复界面状态；`SettingsWindow` 已实现滑块事件合并、错误提示和失败回退。若从非 UI 线程调用，调用方必须自行封送到 WPF Dispatcher，因为广播会触及窗口视觉对象。

窗口关闭时，`Attach` 注册的关闭回调会移除鼠标、尺寸和设置事件处理器，并清除 `content.Effect`，避免设置服务继续持有已关闭窗口。

### 4.4 直接修改配置文件

退出程序后，也可在 EXE 同目录编辑 `MOUSE_EFFECT_CONFIG1`、`MOUSE_EFFECT_CONFIG2`，每个文件只写一个范围内的数字，例如 `180` 或 `1.10`，并用 `.` 作为小数点。若要关闭效果，创建无后缀的 `NO_MOUSE_EFFECT` 文件；删除它则开启。程序在下一次启动时读取这些文件。运行中手动改文件不会触发窗口更新，应使用设置页、CLI 或上述异步方法调整。

## 5. 持久化和失败行为

配置文件都位于 `AppContext.BaseDirectory`，即程序 EXE 所在目录。`NO_MOUSE_EFFECT` **存在表示关，不存在表示开**；`MOUSE_EFFECT_CONFIG1` 保存半径，`MOUSE_EFFECT_CONFIG2` 保存粗细。后两个文件都只有一个按不变区域性书写的数字，不带扩展名。缺失时启动会尝试创建默认值；无效值不会静默覆盖，而由启动流程逐个提示处理。

保存数值时，服务在同目录创建临时文件，再移动到目标路径并重读核对；保存开关时也会检查标记文件的最终状态。只有保存成功才广播给窗口。因此 EXE 目录需要可写权限，尤其在关闭效果、首次创建配置或调节参数时。更多错误码与恢复细节见 [`../SETTINGS_MOUSE_EFFECT.md`](../SETTINGS_MOUSE_EFFECT.md)。

## 6. 构建、修改和移植

当前项目文件把 `Shaders/MouseWhiteThicken.ps` 标为 WPF `Resource`；`MouseWhiteThickenEffect` 用 pack URI `/TANGERINE-ZIP;component/Shaders/MouseWhiteThicken.ps` 加载。普通构建会将其装入主程序集，单文件发布也无需旁置 `.ps`。

修改像素算法时，编辑 `.fx` 后必须重新编译 `.ps`，否则运行的仍是旧字节码。Windows SDK `fxc.exe` 命令示例：

```text
fxc.exe /T ps_2_0 /E main /Fo MouseWhiteThicken.ps MouseWhiteThicken.fx
```

在 `TANGERINE-ZIP/Shaders` 目录执行，然后重新构建项目。只修改 C# 包装或设置服务时不需要重编译 `.ps`。把封装移到别的 WPF 程序时，需要一同迁移包装类、`.ps` 资源和配置服务，调整 pack URI 中的程序集名与资源路径；当前包装还依赖本项目的 `LanguageManager`、`StageException`、`ErrorDialog`。若希望做成独立库，应把这些错误呈现依赖改为由宿主注入，并明确 UI 线程与配置存储接口。

## 7. 快速核验

1. 启动程序并移动鼠标经过浅色文字：中心附近笔画略增粗，离开半径后恢复；橙色图标不应明显变粗。
2. 打开两个窗口，分别移动鼠标：每个窗口按自己的指针位置更新；关闭其中一个窗口后，另一个仍可正常调整。
3. 在设置页关闭、开启效果并调节两个滑块：已打开窗口即时变化；重启后状态与数值保持一致。
4. 若改了 `.fx`，重新生成 `.ps` 并构建后再验证效果，避免把旧字节码误认为新算法。
