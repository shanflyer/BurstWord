# BurstWord

High-throughput damage text for Unity, using TMP resources, BRG and GPU instancing.

BurstWord 是面向大规模战斗的高性能飘字插件，适用于 **Unity 2022.3 及后续版本（包括 Unity 6）的 URP 项目**。每条飘字只保存数据，通过 **BatchRendererGroup（BRG）** 绘制；设备无法使用 BRG 时，自动回退到底层 **GPU Instancing Draw**。

运行时不为每条飘字创建 GameObject、TMP 文字组件或 Mesh。字形共用一个自动生成的四边形，字体和图片字符直接使用 TMP 资源，位移、旋转、缩放、淡出及自定义视觉效果由 GPU 执行。

## 功能

- TMP 字体、原生 fallback 字体链、Sprite 图片字体，以及文字与 Emoji 混排。
- 富文本、字偶调整、双向文本、自动换行、横竖九种对齐方式。
- 读取字体自身材质的文字颜色、描边、阴影和发光参数。
- 固定屏幕字号、位置跟随、完整三维姿态与镜头透视；屏幕缩放对齐 Canvas Scaler。
- 整条飘字从远到近排序，支持最前显示、不透明遮挡和完整场景透明排序。
- 独立动画编辑器，使用 AnimationClip、关键帧、曲线和操作柄制作 GPU 动画。
- 自定义 Shader 列表、逐条参数、一键生成模板，以及共享 HLSL 扩展接口。
- 批量发射、排版与资源缓存、Burst/Job 准备路径和增量上传。
- 可选文字塑形委托，使用者自行接入实现或插件；不捆绑 HarfBuzz 或原生塑形库。

## 安装与首次接入

1. 使用 **Unity 2022.3 或更新版本的 URP 项目**。Built-in RP 和 HDRP 尚未实现。
2. 打开 **Window → Package Manager → + → Add package from git URL**，输入：

   ```text
   https://github.com/shanflyer/BurstWord.git
   ```

3. 若项目还没有 TMP 基础资源，执行 **Window → TextMeshPro → Import TMP Essential Resources**。已有 TMP 字体资源可以直接使用。
4. 执行 **Tools → BurstWord → Install BRG Rendering**。打开游戏相机实际使用的 URP Renderer Data，确认 **Renderer Features → BurstWord ordered text** 存在且启用。
5. 在场景创建一个空物体，添加 **Brg Damage Text Renderer**。设置 **Camera**、**Fonts → Default Text Font [0]**、字号和 **Maximum Live Labels**。
6. 在战斗代码中持有该管理器的引用，调用发射接口。

**接入不需要导入 Samples。** 字体字段接收 `TMP_FontAsset`，不是直接填入 `.ttf` / `.otf`。可以用项目现有字体，也可以从字体文件创建 TMP Font Asset。

安装器配置渲染 Feature，并保留所需的 Shader / Instancing 变体。多个管线或相机时，检查 **Project Settings → Graphics / Quality** 的 URP 配置及相机的 Renderer 选择；更换 Renderer Data 后重新执行安装。管理器 **Setup** 会检查当前相机配置，缺失时显示安装和定位 Renderer Data 的按钮。

Unity 根据编辑器版本解析依赖。Unity 2022.3 / 2023.1 缺少独立 TMP 包时自动安装；较新版本使用 uGUI 集成的 TMP，不额外安装冲突的旧 TMP 包。

## 发射文字

将 `damageText` 关联到已配置的 `BrgDamageTextRenderer`：

```csharp
using BurstWord.BRG;
using UnityEngine;

public class DamageTextExample : MonoBehaviour
{
    [SerializeField] private BrgDamageTextRenderer damageText;

    public void ShowDamage(Vector3 hitPosition, int damage)
    {
        damageText.Emit(hitPosition, damage, Color.white, duration: 1.2f);
    }

    public void ShowCritical(Vector3 hitPosition, int damage)
    {
        damageText.EmitText(hitPosition, $"<b>Critical {damage}</b>", Color.yellow,
            duration: 2f);
    }
}
```

`Emit` 直接接收整数；`EmitText` 接收字符串。`duration` 是**本条存在的秒数**，省略时为 **1.5 秒**，必须是有限正数。管理器没有统一寿命字段；动画在本条寿命内播放完整曲线。

最大同时存在数量由 **Maximum Live Labels** 限制。持续发射时，可按“每秒发射量 × 平均寿命”估算基础容量，并为瞬发留出余量。容量不足会丢弃新的请求，可在 Runtime Status 查看计数。`Emit` 和位置版 `EmitText` 返回是否成功，Transform / TextPose 版本返回可查询存活状态的句柄。

## 每条选择字体、动画和 Shader

先在 Inspector 中配置资源列表，再在发射时传编号。同一管理器可以混用多种字体、动画和 Shader。

| 参数 | 编号 | 省略或空资源时 |
| --- | --- | --- |
| `fontIndex` | `0` 为 Default Text Font；`1..N` 为 Additional Text Fonts | 默认使用 `0`；越界或额外字体空槽报错 |
| `animationIndex` | 从 `0` 开始对应 Animations 列表 | 默认使用 `0`；空列表或空槽使用内置上浮、淡出 |
| `effectIndex` | 从 `0` 开始对应 Shader Effects 列表；`-1` 明确使用内置 Shader | 默认使用 `0`；空列表或空槽使用内置 Shader |

```csharp
// 先配置字体 1、动画 2、自定义 Shader 0。
damageText.EmitText(hitPosition, "Critical 1234", Color.yellow,
    duration: 1.2f, fontSize: 40,
    fontIndex: 1, animationIndex: 2, effectIndex: 0,
    effectParameters: new Vector4(1, 0.5f, 0, 3));

// 也可以直接传资源，无需登记；直接资源优先于对应编号。
damageText.Emit(hitPosition, 1234, Color.yellow,
    font: criticalFont, animation: criticalAnimation);

// 即使列表第 0 项有自定义 Shader，也明确使用内置绘制效果。
damageText.Emit(hitPosition, 200, Color.green, effectIndex: -1);
```

`fontSize > 0` 覆盖本条字号，`0` 使用管理器字号。直接 `font:` / `animation:` 优先于编号。列表顺序决定后续调用的编号，已发射的文字保留选定资源。数字、字符串、Transform、TextPose 和批量接口均支持这些选择。

### 字体、材质和 Emoji

普通字体必须包含目标字形。缺字沿用所选 TMP 字体的 **Fallback Font Assets** 和 TMP 全局 fallback，不需要在管理器额外字体列表重复登记。额外字体列表用于代码选择和 `<font="字体资源名称">` 标签查找。

**描边、阴影、发光使用所选 TMP Font Asset 自己的 Material。** 多种材质效果可以准备不同字体资源并共享同一图集，按 `fontIndex` 或直接资源选择；材质必须与图集匹配。管理器没有独立材质列表或 `material:` 参数。

BurstWord 的 Shader 实现相应 TMP 效果并读取其参数，**不会直接执行字体材质上的任意自定义 TMP Shader**。额外视觉效果走下面的 Shader Effects 接口。

在 **Fonts** 打开 **Use Sprite Fonts**，指定原生 `TMP_SpriteAsset` 到 **Default Sprite Font**；留空使用 TMP Settings 的默认 Sprite Asset。图片可以和普通文字一起发射：

```csharp
// Sprite Asset 中需要有对应 Unicode 条目。
damageText.EmitText(hitPosition, "😊 +100", Color.white);
damageText.EmitText(hitPosition, "<sprite=0> +100", Color.white);
damageText.EmitText(hitPosition, "<sprite name=\"coin\"> +100", Color.white);

// 另一套资源先登记到 Named Sprite Fonts。
damageText.EmitText(hitPosition,
    "<sprite=\"CombatIcons\" name=\"fire\"> 1234", Color.white);
```

Unicode、名称、图标序号及 Fallback Sprite Assets 均在 TMP Sprite Asset 内配置。`fontIndex` 选择普通文字字体；`<sprite>` 的数字选择图片资源中的图标，两者不是同一套编号。没有额外的文字到图片映射表。

## 排版、换行与 UI 缩放

**Text Layout** 提供 Horizontal：Left / Center / Right 和 Vertical：Top / Middle / Bottom，组合成九种对齐方式，默认居中。

- **Use Fixed Text Area 关闭**：以发射点为对齐锚点。
- **开启**：设置 **Text Area Size** 宽、高，在以发射点为中心的矩形区域中对齐。区域只保存数值，不创建 RectTransform，也不裁剪溢出的文字。
- **Automatic Wrapping 开启**：文字达到 **Wrap Width** 时按断行规则换行；固定区域更窄时使用区域宽度作为上限。关闭自动换行仍可用 `\n` 或 `<br>` 主动换行。

```csharp
damageText.EmitText(hitPosition, "Critical 1234", Color.yellow,
    alignment: TextAnchor.UpperLeft,
    textAreaSize: new Vector2(300, 100));
```

省略 `alignment` / `textAreaSize` 使用管理器设置；显式传 `Vector2.zero` 可让本条使用发射点锚定。对齐与区域在发射时记录，修改默认值影响后续发射。

支持的富文本标签为 `<b>`、`<i>`、`<u>`、`<s>`、`<color>`、`<alpha>`、`<size>`、`<voffset>`、`<cspace>`、`<sup>`、`<sub>`、`<nobr>`、`<font>`、`<sprite>` 和 `<br>`；它们是本插件实现的子集，不代表 TMP 所有标签均可用。可在 Text Layout 关闭 Rich Text Tags 或 Font Kerning。

### 与普通 UI 一致的大小

屏幕模式下使用 **UI Scaling**。可以把现有屏幕空间 Canvas 拖入 **Use Existing UI Canvas**，读取根 Canvas 实际的 `scaleFactor`；不创建新的 Canvas。World Space Canvas 不适用此选项。

没有 Canvas 时留空，手动设置与 Canvas Scaler 相同的参数：

| UI Scale Mode | 行为 |
| --- | --- |
| Constant Pixel Size | Scale Factor = 1 时按屏幕像素计算；改变分辨率不改变像素字号 |
| Scale With Screen Size | 按 Reference Resolution 缩放；Match = 0 匹配宽度、1 匹配高度、0.5 均衡；Expand 取较小缩放，Shrink 取较大缩放 |
| Constant Physical Size | 按设备 DPI 和所选单位缩放；设备未提供 DPI 时使用 Fallback Screen DPI |

默认为 **Scale With Screen Size / 1920×1080 / Match Width Or Height / Match 0.5**。Reference Resolution 是排版尺寸对应的设计分辨率，不是限制实际游戏分辨率。运行时分辨率变化会根据缩放设置改变屏幕字号和动画位移，行为与对应 Canvas Scaler 模式一致。

手动设置以相机完整显示输出或完整 Render Texture 尺寸为基准，分屏视口用于投影。缩放变化实时作用于存活的屏幕文字，无需重新排版；世界模式使用世界单位和透视，不受 UI Scaling 影响。

### 布局预览

点击 **Text Layout → Open Layout Preview**，在独立窗口输入内容，选择字体、Shader 和参数，并实时调整管理器排版设置。

| 标记 | 含义 |
| --- | --- |
| 青色框 | 固定文本区域及宽高；拖动右下角调整区域，支持 Undo |
| 黄色框 | 实际字形边界，包含材质效果所需 padding |
| 粉色虚线 | 有效换行宽度 |
| 十字 | 发射点 / 布局原点 |

支持 Auto Fit、滚轮缩放、中键或 Alt+左键平移及 Reset View。尺寸是文字排版单位，与字号一起接受 UI 缩放或世界单位转换；预览视图缩放不修改尺寸。

窗口使用独立隐藏预览场景和真实 Instancing 绘制。预览暂停动画，不包含游戏中的 UI 缩放与世界透视；边界线是自定义顶点偏移前的 CPU 布局范围。仅支持 BRG 的自定义 Shader 会明确提示无法在该预览后端运行。关闭窗口释放资源，不保存预览物体，也不向 Player 加入预览代码。

## 空间、跟随与遮挡

在 **Space & Occlusion** 选择默认空间和遮挡方式。

| Space Mode | 表现 |
| --- | --- |
| ScreenSnapshot，默认 | 发射时获取世界位置，之后不再跟随目标；始终朝向相机，保持 UI 缩放后的屏幕字号 |
| ScreenFollow | 持续跟随目标位置；始终朝向相机，目标旋转和缩放不改变字形朝向与大小 |
| WorldFollow | 跟随完整位置、旋转和缩放，受到镜头透视影响；不强制朝向相机 |

ScreenSnapshot 固定的是世界锚点，镜头移动仍会改变它在屏幕上的投影。世界模式通过 **World Units Per Layout Unit** 转换排版尺寸，默认 **0.01**。空间模式在发射时记录，修改影响后续发射。

| Occlusion | 表现 |
| --- | --- |
| AlwaysInFront，默认 | 场景透明物体画完后显示，不受场景深度遮挡 |
| OpaqueOcclusion | 在透明物体之后显示，但按不透明深度快照逐片元遮挡；透明物体不改变这份快照 |
| SceneTransparent | 参与普通 URP 透明队列排序与深度测试，可以被不透明物体挡住并与其他透明 Renderer 混色 |

三种模式都按**整条飘字**从远到近排序；同一条中的字形共用排序位置，不按字符单独排序。内部等距离时按发射顺序，后发射的在前，字体或 Shader 不改变这个次序。完整场景透明排序遵循普通透明 Renderer 的队列和排序规则，不提供逐像素透明交叉求解。

默认和不透明遮挡模式依赖 BurstWord Renderer Feature；完整场景模式走普通透明通道。完整场景排序需要按整条提交排序位置，通常显著增加绘制命令数。

### 跟随已有 Transform

```csharp
damageText.spaceMode = BrgDamageTextRenderer.SpaceMode.WorldFollow;
damageText.sortingMode = BrgDamageTextRenderer.SortingMode.OpaqueOcclusion;

var handle = damageText.EmitText(target, "Critical 1234", Color.yellow,
    offset: new Vector3(0, 1.8f, 0),
    rotation: Quaternion.identity, scale: Vector3.one,
    duration: 2f);
```

传入偏移、旋转和缩放是目标的局部值。世界模式使用 `target.localToWorldMatrix * TRS(offset, rotation, scale)`。目标销毁后文字冻结在最后有效姿态，直到到期；空目标不发射。

### 没有 Transform 时手动更新

```csharp
var pose = new BrgDamageTextRenderer.TextPose(
    hitPosition, Quaternion.identity, Vector3.one);
var handle = damageText.EmitText(pose, "1234", Color.white, duration: 2f);

pose.position = newPosition;
damageText.TryUpdatePose(handle, pose);

bool alive = handle.IsAlive;
damageText.TryRelease(handle); // 提前结束；自然到期无需手动释放。
```

`TryUpdatePose` 支持 ScreenFollow / WorldFollow，ScreenSnapshot 拒绝更新。更新原本跟随 Transform 的句柄会切换到手动姿态来源。到期、Clear、禁用重建或槽位重用后，旧句柄不会操作新文字。

一个管理器只为配置的 Camera 绘制。同一相机建议集中使用一个管理器，内部排序覆盖该管理器全部文字；没有多个管理器间的全局合并排序。

## GPU 动画

**Animations** 是从 `0` 开始的资源列表。每行 **Edit / Preview** 或 **Tools → BurstWord → Animation Editor** 打开独立编辑器，无需在 Scene 中编辑对象。

1. 点击 **新建**，创建动画资源和引用的 `.anim` Clip，或打开已有资源。
2. 选择时间，开启 **自动关键帧**。拖动预览区操作柄或修改位置、旋转、缩放、颜色、透明度和亮度，写入当前关键帧。
3. 移动或删除时间轴关键帧，使用曲线字段调整曲线和切线，支持 Undo / Redo。
4. 使用播放、暂停、重播、循环和速度查看效果，然后保存。

快捷键：**W** 移动、**E** 旋转、**R** 缩放、**空格** 播放/暂停、**Delete** 删除选中关键帧。输入字段获得焦点时不会触发相应快捷操作。

动画只允许固定的 11 条轨道：位置 X/Y、旋转 Z、缩放 X/Y、颜色 RGBA、透明度和亮度。窗口不提供任意层级、组件或属性入口，外部 Clip 会验证非法轨道并明确报错。位移单位与文字排版单位一致，颜色和透明度与发射及材质颜色相乘。

Clip 的完整时长映射到每条飘字的完整 `duration`；例如 1 秒 Clip 可以在 2 秒飘字中完整播放。编辑器将曲线烘焙为 **256 点采样数据**，Player 读取共享 GPU 表，不逐条、逐帧求值 AnimationCurve，也不使用 Animator。

列表或槽位为空时使用内置上浮和后半程淡出。**Animations → Built-in Motion → Rise Height** 调整上浮高度，默认 **90** 个排版单位，速度为高度 / 本条 `duration`；负值向下。代码可用 `useLegacyAnimation: true` 明确选择内置动画。

`animationAmplitude` 只改变曲线位移幅度，不改变播放时长、缩放、颜色或透明度。不同动画共用采样表，选择动画本身不拆绘制批次。

## 自定义 Shader 效果

在 **Shader Effects** 点击 **Create Custom Effect Shader**，保存到项目 Assets；生成的普通 Shader 自动加入列表。点击 **Edit**，修改两个函数：

```hlsl
#include "Packages/com.shanflyer.burstword/Runtime/BRG/BurstWordShaderEffects.hlsl"

void BurstWordModifyVertex(inout BurstWordEffectVertex vertex)
{
    // 在最终裁剪空间位置上增加水平像素偏移。
    vertex.positionCS.x += vertex.parameters.x * 2 / _BurstScreen.x
        * vertex.positionCS.w;
}

void BurstWordModifyFragment(inout half4 color, BurstWordEffectFragment fragment)
{
    // 修改完成 TMP / Sprite 绘制后的颜色，保留原 Alpha。
    color.rgb *= half3(1, 0.5, 0.2);
}
```

上述函数写入生成模板对应位置，保留模板的 Properties、Tags、Pass 和编译指令。共享 HLSL 负责数据解码、字形、TMP 材质效果、空间、动画和深度；用户文件不随包升级被覆盖。空模板效果与内置 Shader 一致，同时支持 BRG 和 Instancing。

每条通过 `effectParameters` 传四个有限数值，默认全零，含义由 Shader 定义。同一 Shader 的不同参数不需要分材质。持有句柄时可直接更新：

```csharp
damageText.TryUpdateEffectParameters(handle, new Vector4(1, 0, 0, 0));
```

参数更新不重新排版；句柄过期或该条使用内置 Shader 时返回 false。不同 Shader 会拆绘制段，但保留整条透明排序。

高级开发者可以完全自写 Shader，但必须符合标准的数据、Pass、BRG / Instancing 声明和绑定。Inspector 显示兼容性及错误原因，运行时拒绝不支持当前后端的效果。完整字段、实例数据和后端规范见 [自定义 Shader 开发规范](Documentation~/SHADER_EFFECTS.md)。

自定义顶点位移不改变 CPU 排版边界、换行宽度和整条排序中心。需要参与排序的运动使用姿态或动画系统；像素效果不能超出字形四边形范围，额外几何与 padding 属于高级扩展。

## 批量发射与性能

高频发射复用 `TextEmission[]`，通过 `EmitBatch` 一次提交，避免业务侧每次分配数组。同一批可以混用文字、字体、动画、Shader 和跟随目标：

```csharp
// 成员字段：初始化时分配一次，发射时复用。
private readonly BrgDamageTextRenderer.TextEmission[] requests =
    new BrgDamageTextRenderer.TextEmission[2];

// 以下代码放在业务发射方法中。
requests[0] = new BrgDamageTextRenderer.TextEmission(
    "Critical 1234", Color.yellow,
    new BrgDamageTextRenderer.TextPose(hitPosition, Quaternion.identity, Vector3.one),
    duration: 1.2f, fontIndex: 1, animationIndex: 2,
    effectIndex: 0, effectParameters: new Vector4(1, 0.5f, 0, 3));

requests[1] = new BrgDamageTextRenderer.TextEmission(
    "Healing +200", Color.green,
    new BrgDamageTextRenderer.TextPose(otherPosition, Quaternion.identity, Vector3.one),
    duration: 2f, wrapWidth: 240, effectIndex: -1);

damageText.EmitBatch(requests, 2);
```

`count` 指定使用的数组前缀，可选第三个参数 `TextHandle[]` 接收对应句柄，也应预先分配并复用。每条 `WrapWidth` 是独立值，`0` 关闭本条自动换行；批量不会自动继承管理器 Wrap Width。需要继承时显式传 `wrapWidth: damageText.wrapWidth`。

构造函数提供默认寿命；若用结构体对象初始化器，需显式填写正数 `Duration`，并按需要设置 `Pose` / `AnimationAmplitude` 等字段。批量先校验全部寿命、资源编号和效果参数，再发射；容量不足仍可能使部分有效请求无法显示。

CPU 负责解析、排版、缓存、跟随、排序和提交，GPU 负责动画及字形着色。缓存命中会复用布局；数字、换行等支持的批量路径通过 Burst/Jobs 准备，不支持的情况回到完整主线程排版。它不是完全异步的文字系统。

BRG 按 Shader 和图集资源组织页面，一页最多绑定 16 张图集。排序后同页的连续段可以合并；不同 Shader、跨页和完整场景透明排序可能增加绘制命令。BRG 命令数不等于整个场景的 Draw Call 数，也不能保证所有文字永远一条底层 Draw Call。

Instancing 复用相同排版和动画，每次最多提交 64 个字形实例以适应较低的 uniform 限制。它仍不创建文字对象，但通常比 BRG 消耗更多 CPU 提交时间。首次遇到新字体、图集、Shader 或布局需要建立缓存；不能将所有内容宣称为零分配或零 CPU 成本。

## 可选文字塑形

字形覆盖、双向排序与复杂文字塑形是不同的能力。不注册塑形委托时使用 TMP 字形数据、fallback、字偶调整和内置双向/断行规则；需要阿拉伯语上下文变形、印度文字重排等能力时，由使用者接入自己的实现或插件。

```csharp
using System.Collections.Generic;
using BurstWord.Typography;

// 初始化时注册已实现的方法；移除时传 null。
damageText.SetTextShaper(ShapeText);
// damageText.SetTextShaper(null);

void ShapeText(in TextShapingRequest input, List<TextShapingGlyph> output)
{
    // 将自己的实现或外部插件的结果追加到 output。
    // 必须返回真实的 GlyphId、UTF-32 Cluster、Advance 和 Offset。
}
```

示例空方法只是接口签名，未填写输出无法显示该段字形。委托使用解析后的 TMP 字体及 UTF-32 输入，在主线程执行；重复请求可复用缓存。更换委托或修改其内部规则后重新注册，会清空当前文字及旧缓存。

本仓库不提供 HarfBuzz 适配器、原生二进制或 C++ 塑形源码，也不要求塑形 Asset。完整输入输出约定与高级 Job-provider 接口见 [塑形接口开发规范](Documentation~/SHAPING.md)。所选外部插件的平台适配由使用者和插件自身负责。

## Inspector 分区

| 分区 | 用途 |
| --- | --- |
| Setup | 相机、容量、渲染管线检查及安装 |
| Fonts | 默认和额外 TMP 字体、字号、原生 Sprite 资源 |
| Text Layout | 对齐、固定区域、换行、富文本、字偶调整及布局预览 |
| Space & Occlusion | 空间模式、世界单位比例、遮挡和排序 |
| UI Scaling | 屏幕模式下的 Canvas 缩放或手动缩放设置 |
| Animations | 带编号动画列表、编辑预览及内置上浮高度 |
| Shader Effects | 带编号 Shader 列表、模板创建及兼容性反馈 |
| Runtime Status | Play 时的实际后端、活跃文字/字形、绘制及缺失资源计数 |

子选项仅在适用或开启时显示。折叠分组只隐藏界面，不停用功能。Play 中从 Inspector 修改字体、相机或排版资源可能重建管理器并清掉当前飘字；多种效果同时显示应使用逐条参数。Shader 列表在 Play 前配置。

## 版本与平台

最低版本为 **Unity 2022.3 + URP**，包含 Unity 6 的 Render Graph 路径；Unity 2022.3 使用经典 Render Pass 路径。已在 2022.3.62f3、6000.2.1f1、6000.4.7f1、6000.6.0f1 编译验证，最新自定义 Shader 功能在 2022.3.62f3 / 6000.6.0f1 的 Windows Player 验证。

| 图形后端 / 条件 | 绘制路径 |
| --- | --- |
| D3D11 / D3D12 / Vulkan / Metal 且满足设备、Shader 和 BRG 初始化要求 | 优先 BRG |
| BRG 不可用，或使用 OpenGL / OpenGL ES / WebGL 2 | 普通 GPU Instancing |
| 设备不支持 GPU Instancing | 明确报告无法初始化；不转成逐条对象绘制 |

代码字段 `renderBackend` 默认 Auto，可在管理器初始化前指定 Instancing。指定 BRG 但设备不可用时也会回退；自定义 Shader 必须支持实际选用的后端。

已实际运行的图形路径包括 Windows D3D11 的 BRG / Instancing 和 OpenGL Core 的 Instancing。其他 API 与 macOS、Linux、移动端、浏览器、主机平台需要在目标设备验证，不能把后端实现等同于全平台已实测认证。闭源主机平台仍需要 Unity 对应构建模块和 SDK；核心包没有额外的原生塑形库打包要求。

## 开发规范与许可

日常使用说明集中在本 README。扩展开发时查阅：

- [自定义 Shader 开发规范](Documentation~/SHADER_EFFECTS.md)：最终顶点/像素接口、逐条参数、完整 BRG / Instancing ABI。
- [塑形接口开发规范](Documentation~/SHAPING.md)：委托输入输出、缓存约定、原生 Job-provider 接入。
- [版本记录](CHANGELOG.md)。

BurstWord 代码采用 [MIT 许可证](LICENSE.md)。保留的第三方 Unicode 算法、数据和示例字体沿用各自许可证，见 [Third Party Notices](Third%20Party%20Notices.md)。
