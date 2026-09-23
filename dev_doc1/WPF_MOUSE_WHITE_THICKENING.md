# WPF 鼠标邻近白色笔画增粗效果

## 范围与行为

项目定义的 9 个 WPF `Window`（包括设置窗口）均在 `InitializeComponent()` 后调用 `MouseWhiteThickening.Attach`。每个窗口代码类中的第一个字段为 `MouseWhiteThickenRadius`，单位是设备无关像素（DIP）；直接修改该字段即可单独调整该窗口的作用半径。设置页可即时关闭或开启效果，持久化规则见 [SETTINGS_MOUSE_EFFECT.md](SETTINGS_MOUSE_EFFECT.md)。

效果在鼠标中心附近对近白色、浅灰色笔画增加最多约 0.95 DIP 的宽度；新增宽度随着与鼠标中心的距离连续减小，在作用半径边缘降为零。深色背景和橙色状态指示不会被扩张。它只改变窗口内容的渲染结果，不改变控件的布局、文字数据或命中测试，也不影响 Windows 自带的文件选择对话框与系统标题栏。

## 实现

- `MouseWhiteThickening.cs` 为窗口内容附加一个 WPF `ShaderEffect`，维护窗口尺寸、归一化鼠标坐标和窗口自己的半径。窗口关闭时移除事件处理器与效果。
- `Shaders/MouseWhiteThicken.fx` 是着色器源文件；`Shaders/MouseWhiteThicken.ps` 是编译后的 Pixel Shader 2.0 字节码，以 WPF `Resource` 包入主程序集。无需随单文件程序另发着色器文件。
- 着色器采样当前像素及上下左右四个邻近像素，使用 RGB 三通道的最低亮度得到连续的白色覆盖率，因此橙色指示不会被误认作白色。采样偏移随鼠标距离从约 0.95 DIP 递减至零，且只增加原像素缺少的白色覆盖率；不再把偏移位置的 RGB 图像复制回来，从而避免固定偏移产生的重影。半径和鼠标位置通过着色器常量更新，移动鼠标不会重新创建效果实例。
- 如果附加效果失败，会以 `MWFXT` 阶段码显示错误并保留原始窗口内容，不阻断压缩或解压。`MWFXT0001` 表示窗口内容缺失，`MWFXT0002` 表示半径无效，`MWFXT0003` 表示其他附加失败。

## 构建与维护

正常构建和 `dotnet publish` 会把已编译的 `.ps` 资源放入程序集。若修改 `.fx`，需使用 Windows SDK 的 `fxc.exe /T ps_2_0 /E main /Fo MouseWhiteThicken.ps MouseWhiteThicken.fx` 重新生成 `.ps`，然后重新构建。单文件发布配置保留在主项目文件中。

新增 WPF 窗口时，把半径常量作为该 `Window` 类的第一个字段，在每个构造函数调用 `InitializeComponent()` 后调用 `MouseWhiteThickening.Attach(this, MouseWhiteThickenRadius)`；勿对同一窗口重复附加。
