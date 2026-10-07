# 排序、遮挡与三维空间

打开 `TextFeatures.unity`，Play 后在现有持续压力面板切换两排按钮。第一排选择排序，第二排选择空间；下方“目标移动 / 旋转 / 缩放”让原有战场单位运动，便于观察跟随。世界模式还可用“世界文字大小 ×2/÷2”调整实际尺寸。发射量、容量、文字类型按钮仍然有效。默认是 **最前显示 + 固定发射位置**。

## 三种排序模式

| Inspector / API | 表现 |
| --- | --- |
| `AlwaysInFront`，默认 | 场景透明物体画完后显示，不受场景深度遮挡。飘字自身按整条文字从远到近排列。 |
| `OpaqueOcclusion` | 同样在透明物体之后显示，但逐片元与“不透明物体绘制后的深度快照”比较。可被不透明物体完整或部分遮挡；透明物体即使开启深度写入，也不改变这份快照。 |
| `SceneTransparent` | 使用正常 URP 透明队列和深度测试，与普通透明 Renderer 一起排序；不透明深度正常遮挡，前方半透明物体正常混色。 |

排序以**整条飘字的锚点/动画后的世界位置**为单位，不独立按每个字符的位置排序。一条内所有字形使用同一排序位置。默认与不透明遮挡模式按相机前向深度排序；完整场景模式跟随相机/Graphics Settings 的普通透明排序方式。相同排序距离使用发射顺序作为内部次序，后发射的在前；字体、效果预设和图集不会直接决定内部次序。

完整场景模式是普通 TMP 三维透明文字的排序能力：透明对象作为整体比较位置，仍服从场景透明队列设置。不是逐像素透明求解；两块相交的透明平面、不同 Render Queue 的人为优先级，以及另一个自定义 Renderer Feature 在更晚阶段画出的内容，都遵守 URP 本身的渲染规则。

默认和不透明遮挡模式需要 `BrgTextRendererFeature`。接入时执行 **Tools → BurstWord → Install BRG Rendering**，检查相机实际使用的 Renderer Data 包含启用的 **BurstWord ordered text**。安装器会保留 Feature 的子资源映射和绘制所需的 shader 变体；管理器 Inspector 可检查当前相机及质量级别的管线配置。完整场景模式直接走普通透明通道。当前实现适配 Unity 2022.3 及后续 Unity 6 的 URP；Unity 6 提供 Render Graph 路径。不兼容 BRG 的设备自动退回底层 Instancing Draw，依然不创建每条文字对象。Built-in RP 和 HDRP 尚未实现。

## 三种空间模式

| Inspector / API | 位置、旋转与尺寸 |
| --- | --- |
| `ScreenSnapshot`，默认 | 发射时取得世界位置，之后不再引用目标 Transform。始终朝向镜头，沿用原来的屏幕字号。 |
| `ScreenFollow` | 持续更新目标位置，始终朝向镜头，保持屏幕字号；目标旋转和缩放不会旋转或缩放字形。 |
| `WorldFollow` | 使用目标完整的 `localToWorldMatrix` 和传入的局部偏移、旋转、缩放。文字的朝向、尺寸、移动跟随目标，受到相机透视影响；不强制朝向镜头。保留父级非均匀缩放产生的完整矩阵。 |

屏幕模式不随目标距离变大变小。**UI scaling** 支持 Canvas Scaler 的 Constant Pixel Size、Scale With Screen Size、Constant Physical Size，以及 Match Width Or Height / Expand / Shrink。默认保持 `referenceResolution = 1920×1080`、Match 0.5；Match 为 0 时匹配宽度、1 时匹配高度。也可指定 `scalingCanvas`，直接读取已有屏幕空间根 Canvas 的实际 `scaleFactor`。不创建 Canvas、TMP 组件或每条文字对象。

手动缩放以相机完整显示输出或完整 Render Texture 尺寸为基准；分屏的相机视口用于像素到投影坐标的转换，不再把字体额外缩小。参考分辨率、匹配模式、缩放因子和已有 Canvas 的缩放变化会实时作用于存活的屏幕文字及其屏幕动画位移，不需要重新排版或发射。

世界模式不受 UI 缩放设置影响，通过 `worldUnitsPerLayoutUnit` 把排版单位转换为 Unity 世界单位，默认 0.01。字号、世界单位比例、寿命和空间模式在发射时捕获，修改它们影响后续发射。

`risePixels`、`horizontalDrift` 的 GPU 动画仍然保留。屏幕模式在镜头平面运动；世界模式按文字局部 X/Y 轴和世界单位比例运动，因此运动方向也跟随目标旋转与缩放。

## Transform 跟随

```csharp
using BurstWord.BRG;
using UnityEngine;

// textRenderer 是场景中的共享管理器；victim 是已经存在的角色 Transform。
textRenderer.sortingMode = BrgDamageTextRenderer.SortingMode.OpaqueOcclusion;
textRenderer.spaceMode = BrgDamageTextRenderer.SpaceMode.WorldFollow;

var handle = textRenderer.EmitText(
    victim, "<b>暴击 12345</b>", Color.yellow,
    offset: new Vector3(0, 1.8f, 0),
    rotation: Quaternion.identity,
    scale: Vector3.one);
```

偏移、旋转和缩放是目标的局部值。矩阵为 `target.localToWorldMatrix * TRS(offset, rotation, scale)`；偏移不会再被这次传入的文字局部旋转/缩放重复变换。目标丢失或被销毁时文字冻结在最后一次有效姿态，继续播放至到期。传入空目标不发射。

## 手动更新姿态

```csharp
textRenderer.spaceMode = BrgDamageTextRenderer.SpaceMode.WorldFollow;
var pose = new BrgDamageTextRenderer.TextPose(
    hitPosition, hitRotation, Vector3.one,
    offset: new Vector3(0, 0.5f, 0));
var handle = textRenderer.EmitText(pose, "伤害 12345", Color.white);

// 在你的数据源变化时更新，无需创建 Transform。
pose.position = newPosition;
pose.rotation = newRotation;
pose.scale = newScale;
textRenderer.TryUpdatePose(handle, pose);

// 可查询存活或提前释放。过期、Clear、禁用重建及槽位重用后，旧句柄不会操作新文字。
bool alive = handle.IsAlive;
textRenderer.TryRelease(handle);
```

无目标的姿态矩阵是 `TRS(position + offset, rotation, scale)`。`TryUpdatePose` 也支持 `ScreenFollow`；更新一个原本跟随 Transform 的句柄会改为手动姿态来源。`ScreenSnapshot` 拒绝姿态更新。原有 `EmitText(Vector3, ...)` 和整数 `Emit(...)` 继续有效；需要后续更新时使用返回句柄的新重载。

## 性能实现

所有模式只保存标签与字形数据，不创建每条文字的 GameObject、Transform、TMP 组件或 Mesh。GPU 仍然负责上浮、漂移和淡出。

每条文字共享一个姿态地址，所有字形引用它。只平移的有效数据为 16 字节/条；世界旋转或缩放变化最多更新原点、X 轴、Y 轴三个 float4（48 字节/条）。上传区间会合并小空洞，实际上传量可能包含这些槽位；稠密更新直接上传区间，稀疏更新再整理索引，避免每帧对大量移动标签排序上传地址。字形数据仍为 96 字节/字形，跟随时不重排版，也不重传全部字形。

跟随标签按现有目标组织链表。每次更新只读取一次目标矩阵；目标未变时整组跳过，避免反复扫描数千条文字。变化时只计算世界原点及所需的两个方向，不做完整的 4×4 矩阵乘法。最后一条关联文字释放后移除目标记录，目标销毁后安全冻结所有关联文字。只遍历活跃标签，到期后归还槽位。

不同字体图集、彩色 Emoji 和效果参数通过共享资源表绘制。一页最多绑定 16 张原始图集纹理，多个字体资源自带的材质效果共享该页；保留原图集分辨率和 Alpha8/RGBA 格式，不建立巨大 RGBA 纹理数组，也不复制或缩放原始字体图集。超出一页时追加页，按已排好的文字顺序输出连续页段，保持跨页前后顺序。页面上的实例缓冲区仍使用环形槽位和增量上传。

默认和不透明遮挡模式可以把同页的排序实例放进一条 BRG 命令；当前压力场景全部文字资源在同页，八种工作负载均可使用一条命令。相机与存活文字的位置不变时，只排序新文字、剔除到期项并合并，保留原有顺序。大量动态标签使用稳定基数排序，深度为主键、64 位发射序号为次键；只处理发生变化的键字节。可见实例索引按此顺序输出，不移动 GPU 字形数据。这里是 BRG 命令数；底层 API 的实例数限制可能再拆分绘制，不能把它当成整个场景的 Draw Call 总数。

完整场景模式要让场景透明 Renderer 插入文字之间，提交整条标签对应的排序位置和命令，通常批次更多；不能承诺它与默认模式有相同帧率。这份开销来自选择的排序能力，不通过丢字、降低发射速率或改变效果掩盖。

管理器只为 `worldCamera` 提交，多个相机需要各自配置管理器；同一相机的飘字建议集中交给一个管理器，内部排序覆盖该管理器所有标签。没有多管理器之间的全局合并排序。

## 性能测试记录



2026-10-04，Windows x64 Mono Development Player，D3D11、RTX 3070 Laptop、1920×1080，一轮采样结果如下。200 个原有目标持续平移、旋转和缩放，稳定约 7500 条文字、66000 个字形，各组合 Dropped 均为 0。

| 排序 | 固定发射位置 ms/帧 | 固定大小跟随 ms/帧 | 完整世界跟随 ms/帧 |
| --- | ---: | ---: | ---: |
| 最前显示 | 5.23 | 10.73 | 12.10 |
| 不透明遮挡 | 6.13 | 11.43 | 12.54 |
| 完整场景透明排序 | 12.65 | 15.56 | 14.19 |

前两种排序各为 1 条 BRG 命令；完整透明排序约为 7500 条命令，整个场景实际 Draw Calls 约 7700，而前两种约 219～220。后者包含战场等全部绘制，不能拿它当成字体专属的 Draw Call 数。完整场景排序确实有明显成本。

按目标组织跟随、稠密姿态上传和动态标签基数排序后，“最前显示 + 固定大小跟随”的这一探针总帧时间由第一轮的 18.49 降至 10.73 ms，“不透明遮挡 + 固定大小跟随”由 20.43 降至 11.43 ms。两轮各只采样一次，目标运动相位、像素覆盖和硬件状态可能波动；这说明消除了明显开销，不构成实际游戏帧率保证。GPU 时间计数器未返回有效样本，表格是包含强制渲染与 GPU 等待的总帧时间。

最终 Editor 验证覆盖九种组合的实际像素、透明物体插入近/远两条文字之间、超过 16 张图集、2000 条初始标签的增量释放/发射/槽位重用和基数排序。每秒 20000 条的八类持续压力、到期回收、无额外对象、环形上传、排版缓存、换行及复杂文字回归通过。Player 静态验证为 8 条文字、228 个字形、1 条 BRG 命令，缺字/缺 Sprite/缺塑形源均为 0。

Unity 的排序位置接口说明：[BatchDrawCommand.sortingPosition](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Rendering.BatchDrawCommand-sortingPosition.html)。本实现给同一条文字所有实例填写相同位置，保留整条文字排序语义。
