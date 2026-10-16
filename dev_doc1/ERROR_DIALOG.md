# 异常对话框与 StageCode

## 用户看到的内容

异常对话框依次显示阶段码、异常详情、可能原因和建议操作。建议是排查方向，不把推测写成确定结论。提示文字使用 `LanguageManager`，覆盖英文、简体中文以及繁体中文资源。窗口使用当前主题，内容可滚动；宽高通过 `WpfUi.SizeWindow` 按工作区比例计算，未设置固定像素宽高。

## 调用入口

`Tools/ErrorDialog.cs` 提供静态入口。捕获异常时优先调用 `ErrorDialog.Show(owner, fallbackStageCode, exception)`；已有业务详情但没有异常对象时调用 `Show(owner, stageCode, detail)`。若已有完整的 `MessageTipGenerator.GenerateTip` 结果，可使用 `ShowFormatted`。`ThemedPromptWindow.Inform` 对已有的阶段码提示自动转交专用对话框，避免旧调用点显示两套 UI。普通信息和确认框仍按原方式处理。

`ErrorDialog` 从 `StageException` 读取原始 StageCode，否则使用调用边界的 fallback。`MessageTipGenerator` 验证编号为五个 ASCII 大写字母加四位数字；无效编号显示 `UNKWN0001`。分类先查看异常及其 `InnerException` 的类型，再查看编号前五位。权限、文件缺失、归档数据、内存和 I/O 错误有对应建议；密码、设置、外部工具等模块也有建议；其余使用通用建议。分类不解析本地化异常文本，也不创建新的 StageCode。

## 新增异常处理

1. 在操作边界捕获异常；取消操作仍应单独处理，不显示错误对话框。
2. 复用服务层 `StageException.StageCode`。意外异常使用边界专属的 fallback 编号，并在 `Resources/StageList.txt` 登记新编号。
3. 用 `ErrorDialog.Show` 显示异常对象，以便按异常类型选择排查建议。不要仅传入 `exception.Message`，否则只能按模块分类。
4. 新增建议类别时，同时补齐六套 `UiStrings*.resx` 的原因和操作资源，并保持不带技术猜测的措辞。

应用级未处理异常通过专用对话框显示后退出；主窗口和启动边界使用相同静态入口。异常信息本身可能由系统或第三方库提供，因此资源化的是标题、标签和排查建议，原始异常详情保留以供诊断。
