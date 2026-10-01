# 拖入文件创建压缩包

## 原理

未打开压缩包时，`MainWindow_Drop` 接收 Windows `FileDrop` 中的文件和目录路径，去重后保存在 `_pendingCreatePaths`，并显示顶部“创建一个新压缩文件”菜单。此时只保存源路径，不立即读取或压缩文件。再次拖入会替换待创建列表。

点击菜单后，`CreateArchiveFromPathsAsync` 调用系统 `SaveFileDialog`。用户在此选择输出目录、文件名和压缩格式，再通过原有 `CompressionOptionsWindow` 确认格式选项。真正的压缩交给 `ArchiveWorkerClient.CreateAsync`；`RunOperationAsync` 显示逐项进度并提供“停止工作”取消入口。输出文件缺失会报告 `MAINW0012`，其他菜单错误用 `MAINW0027`。

## 调用方式

1. 启动软件，不打开压缩包。
2. 从资源管理器拖入一个或多个文件、文件夹。
3. 点击顶部“创建一个新压缩文件”，在保存窗口设置名称、位置与格式。
4. 确认压缩选项；需要中止时点击“停止工作”。

原“压缩 → 选择文件”菜单也复用同一个保存与压缩方法。若已打开压缩包，拖入仍按现有“卸载并打开”流程处理。
