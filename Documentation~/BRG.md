# BRG 飘字

测试场景使用 `TextFeatures.unity`。文字数据来自 TMP 字体与材质资源，通过共享字形四边形和 BatchRendererGroup 绘制；除传统对象池对照外，所有飘字只有数据，不创建每条 GameObject、TMP 组件或 Mesh。

默认后端是 Auto：设备支持时优先 BRG，否则自动使用普通 GPU Instancing Draw。回退仍是本方案底层直接绘制，沿用文字排版、整条排序、空间模式和 GPU 动画，不退回 TMP 对象或对象池。测试面板可切换 Auto / BRG / Instancing，并显示实际后端。平台依赖及验证范围见 [PLATFORMS.md](PLATFORMS.md)。

运行时保留字符与排版缓存、HarfBuzz 复杂文字塑形、Unicode 双向处理和断行、字形实例缓冲、Job 准备与索引生成、整条文字排序、共享位置/动画缓冲等优化。优化代码与第三方 Unicode/塑形依赖是正常功能，不属于临时测试脚本。

`Emit` 支持伤害数字；`EmitText` 支持字符串、富文本和姿态；`EmitBatch` 支持批量发射。配置 TMP 字体、相机、容量，调用发射接口即可。传统池化对照见 `TextBaseline.unity`。

压力面板只保留基础帧率/内存统计和测试控制，不再采集逐帧历史、排版细分日志或自动生成诊断文件。Unity Profiler 的正常性能标记仍保留。
