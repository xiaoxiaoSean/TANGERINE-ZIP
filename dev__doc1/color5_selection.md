# COLOR5：压缩包选中项背景色

## 原理

`AppearanceSettings.FileNames` 增加无后缀名的 `COLOR5`。它与 `COLOR1` 至 `COLOR4` 一样存放在程序同目录，只接受 UTF-8 编码的七字符 `#RRGGBB`；缺失时创建默认值 `#FF8C00`。读取、写入临时文件后替换、回读核验、异常恢复和实时资源广播都复用现有颜色配置流程。无效内容对应阶段码 `COLRS0018`，启动时可按原颜色错误处理路径删除并重建。

`ApplyBrushes` 将 COLOR5 发布为 `ArchiveSelectionBrush`。`Theme.xaml` 的 `ListViewItem` 选中触发器引用此动态资源，配置变更后已选中项目立即刷新。COLOR5 是独立的选中背景，不参与文字/窗口和进度条的两组配对对比度检查。

## 调用方式

在“设置 → 外观”中找到“压缩包选中项背景色（COLOR5）”，可选择颜色或恢复默认值。也可以在程序关闭后编辑同目录的 `COLOR5`，内容必须严格是 `#RRGGBB`。重新打开程序后生效。
