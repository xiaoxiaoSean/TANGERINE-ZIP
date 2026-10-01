# 首次使用与系统集成引导

## 原理

`WpfStartup.HasAnyConfigurationFile` 在所有初始化写入之前检查 `COLOR1` 至 `COLOR5`、`TEMP_D`、鼠标效果参数文件，以及 `NO_MOUSE_EFFECT`、`ADVANCED_MENU_ON`、`DONT_CHECK_RAR_EXE_AT_START` 标记。只要存在其中任一文件，就进入既有配置读取及报错恢复路径；全部不存在时才进入首次使用流程。

`FirstRunWindow` 的宽高按 Windows 工作区的比例计算，内部使用 `Grid` 的相对列宽。用户不同意创建文件时，窗口说明独立文件夹及日后通过系统设置便携使用的方式，程序随即退出。同意后，鼠标参数文件在后台创建，五个颜色文件异步读取或创建，进度实时显示；随后系统目录选择器要求用户选择可写的临时目录，`TempDirectorySettings.SetAsync` 保存并核验 `TEMP_D`。取消目录选择会停留在引导页，可重新选择。

主窗口显示后，程序询问是否立即设置右键菜单与压缩格式默认打开方式。选择“是”打开 `FirstRunSetupWindow`；窗口显示已有的 UAC 需求及默认应用权限说明。“一键设置两项”先调用 `ContextMenuRegistrationService.CreateAsync`，再逐个调用 `DefaultAppAssociationService.SetThisAppDefault`。每一步显示进度；失败逐项记录并继续处理其余扩展名。用户可退出设置流程，运行中按钮则请求取消。此系统集成步骤完全可选，日后仍可使用顶部“为系统做设置”菜单。

## 调用方式

将可执行文件放入不含上述配置文件的目录并启动，按首次使用窗口的提示操作。若目录已有任一配置文件，程序会按现有启动路径处理，不重新显示首次使用引导。系统关联修改受 Windows 当前版本的保护机制约束，失败会在引导进度中逐项呈现。
