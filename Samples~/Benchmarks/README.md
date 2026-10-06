# BurstWord

方案直接附带两个可运行的测试场景，无需通过脚本生成：

- `TextFeatures.unity`：BRG 压力测试。无每条文字 GameObject，包含数字、中日韩、复杂文字、富文本、字体材质效果、Emoji 和自动换行；可切换排序、空间跟随和 GPU 动画。
- `TextBaseline.unity`：传统 TextMeshProUGUI 对象池对照。每条文字一个池化 GameObject，独立 Update 动画。

两个场景默认每秒发射 200 条，瞬发数量也为 200。打开场景后 Play，在面板输入每秒发射量并点 **Apply**，或选 1,000 / 5,000 / 10,000 / 20,000 快捷档。两边都有发射量倍增/减半、爆发数量输入、暂停、瞬发、清空重测。F1 隐藏或恢复面板。

两边共用基础统计：平均 FPS、平均/最大帧耗时、采样帧数、Unity 已分配/保留内存、托管内存、上一帧 GC 分配、GC 回收次数，以及存活数量、累计发射与容量丢弃。前三秒为预热，改发射量或清空会重置统计。内存为 Unity 进程中被 Unity 跟踪的总量，不是飘字独占内存。

传统场景的负载是纯数字；比较时将 BRG 也切到“纯数字”。

对象池容量在 Play 前通过 Inspector 调整；不在运行时自动创建更多文字对象。BRG 面板可手动增加容量，此操作会重建并预热。出现 Dropped 说明容量不足，对比时须保持相同发射量、寿命、字号、分辨率与战场条件。

方案附带 Float、Critical、Heal、Wobble 四套动画预设及对应的 AnimationClip，位于 `Animations` 文件夹，可直接选择使用或在动画编辑器中修改，无需生成示例。

常用编辑器入口：

- **Tools → BurstWord → Animation Editor**：独立 AnimationClip 编辑、固定属性关键帧和即时 GPU 预览，也可双击动画预设打开。
- **Tools → BurstWord → Prepare Font Sources**：新增 TMP 字体后准备复杂文字塑形所需的字体源。
- **Tools → BurstWord → Install BRG Rendering**：配置 URP 渲染功能。

多语言、换行、字体材质、Job 优化、三种排序/空间模式与 GPU 动画均保留。临时验证脚本、旧场景、逐帧记录器、详细诊断导出和重复菜单已移除；正常运行不会自动写测试日志或性能报告。必要的错误和缺资源提示仍会保留。

详细说明见 [BRG.md](BRG.md)、[TEXT_FEATURES.md](TEXT_FEATURES.md)、[SPATIAL_RENDERING.md](SPATIAL_RENDERING.md) 与 [GPUAnimation.md](GPUAnimation.md)。
