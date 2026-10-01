# 从压缩包拖出文件与文件夹

## 原理

`archiveEntriesList` 使用扩展多选。按下所选行并移动超过 Windows 拖动阈值后，`DragSelectedEntriesAsync` 对所选键建立快照，核对压缩包身份和临时空间，把文件异步解压到私有的 `TangerineZipDrag` 临时子目录。普通压缩包用 `ArchiveWorkerClient.ExtractAsync`；自动展开的嵌套 TAR 用 `ExtractNestedTarsAsync`。路径遍历直接停止，其他提取问题交由现有 `ExtractionPrompt` 处理。

解压通过 `RunOperationAsync` 实时更新主窗口进度条，用户可以点击“停止工作”取消。完成后验证每个暂存路径仍位于私有目录内，再通过 WPF `DataObject.SetFileDropList` 与 `DragDrop.DoDragDrop` 将真实文件路径交给资源管理器或其他接受文件拖放的程序。只提供 Copy 效果，因此不会从压缩包删除源项。暂存文件保留到窗口关闭，以照顾延迟读取拖放数据的目标程序；下次启动时现有临时工作区清理流程处理残留。

## 调用方式

打开压缩包，按 Ctrl/Shift 多选项目，然后按住鼠标拖往资源管理器等目标。解压期间保持按住鼠标；若提前松开，临时文件会清理且不会启动外部拖放。异常阶段码为 `MAINW0028` 至 `MAINW0030`。
