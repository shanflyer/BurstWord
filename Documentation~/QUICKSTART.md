# 直接接入 BurstWord

本教程不需要导入 Samples。Samples 仅用于压力对比和查看示例。

## 1. 安装和检查管线

使用 Unity 2022.3 或更新版本的 URP 项目。在 Package Manager 的 Add package from git URL 中输入 `https://github.com/shanflyer/BurstWord.git`。

安装后执行 **Tools → BurstWord → Install BRG Rendering**。打开游戏相机实际使用的 URP Renderer Data，确认 **Renderer Features → BurstWord ordered text** 存在且启用。仅安装依赖包不等于已经正确选择项目的管线。

多管线项目需检查 Project Settings → Graphics 和 Quality 的 URP 配置，以及相机的 Renderer 选择。Install 会配置 Assets 中的 URP 管线、添加 Renderer Feature 并保留绘制所需的 shader/instancing 变体。管理器 Inspector 会检查当前质量级别及指定相机的 Renderer，可点击 **Install BRG Rendering** 修复，或 **Select Renderer Data** 直接定位。

## 2. 准备字体

使用 **TMP Font Asset**，而非直接把 `.ttf` / `.otf` 填入管理器。已有 TMP 字体资源可直接使用。Unity 2022.3 中可选中字体文件执行 **Assets → Create → TextMeshPro → Font Asset**。若使用 TMP 自带字体且还没有基础资源，执行 **Window → TextMeshPro → Import TMP Essential Resources**。

字体必须包含要显示的字形。多个字体的字符覆盖与复杂语言塑形是不同的事情；普通数字、中英文先使用默认 TMP glyph data 即可。

## 3. 创建管理器

在场景创建空物体，添加 **Brg Damage Text Renderer**。指定 **Font** 和 **Camera**，设置字号、Lifetime 和 Maximum Live Labels。保持默认屏幕空间和最前显示即可开始。

默认不自动发射文字。角色受伤时由业务代码调用发射接口。一个管理器可处理多字体、多材质和多动画，无需每种效果创建一个管理器。

### 和普通 UI 使用相同的缩放

在 **Space and ordering → UI scaling** 中设置。只有 Screen Snapshot / Screen Follow 显示这些设置，World Follow 使用世界尺寸和镜头透视。

最方便的方式是将游戏已有的屏幕空间 Canvas 拖入 **Use Existing UI Canvas**。飘字直接使用其根 Canvas 的实际缩放值，跟随 Canvas Scaler 的设置与运行时变化；不创建新的 Canvas 或文字 UI 对象。World Space Canvas 不适用于这个选项。

没有现成 Canvas 时，留空并手动设置和 Canvas Scaler 相同的参数：

| UI Scale Mode | 配置与效果 |
| --- | --- |
| Constant Pixel Size | Scale Factor 为 1 时，字号按屏幕像素计算。改变分辨率不改变文字的像素尺寸。 |
| Scale With Screen Size | 设置 Reference Resolution、Screen Match Mode；Match Width Or Height 的 Match 为 0 时按宽度、1 时按高度，0.5 为宽高均衡匹配。Expand 取宽高缩放的较小值，Shrink 取较大值。 |
| Constant Physical Size | 设置 Physical Unit 和 Fallback Screen DPI。使用设备报告的 DPI；未报告时使用备用 DPI，行为与 Canvas Scaler 一致。 |

默认是 **Scale With Screen Size / 1920×1080 / Match Width Or Height / Match 0.5**。将参数设为和你的 UI 一样即可；设置实时影响已存在的屏幕飘字，无需重新发射。手动设置以相机的完整输出尺寸为缩放基准，分屏视口只影响投影；Render Texture 使用完整纹理尺寸。字号和动画的屏幕位移一起缩放，目标远近不会改变屏幕字号。

### 横向和纵向对齐

管理器的 **Text alignment** 提供 **Horizontal：Left / Center / Right** 和 **Vertical：Top / Middle / Bottom**，可组合成九种对齐方式。默认仍是横向、纵向居中。

- **Use Fixed Text Area 关闭**：以发射位置为锚点。Left 从该点向右排，Right 向左排；Top 向下排，Bottom 向上排，多行文字使用相同的对齐边。
- **Use Fixed Text Area 开启**：设置 **Text Area Size** 的宽、高，在以发射位置为中心的区域内对齐，类似 UI Text 在 RectTransform 内对齐。区域仅保存数值，不创建 UI 对象，也不裁剪超出的文字。
- **Automatic Wrapping** 开启后按 Wrap Width 换行；固定区域更窄时使用区域宽度作为上限。关闭时仍可通过 `\n` 主动换行。

尺寸使用和字号相同的排版单位：屏幕模式一起接受 UI scaling，世界模式一起接受世界单位比例和目标姿态。对齐和区域在发射时捕获，修改默认值只影响后续发射。

单条可以覆盖设置：

```csharp
damageText.EmitText(hitPosition, "Critical 1234", Color.yellow,
    alignment: TextAnchor.UpperLeft,
    textAreaSize: new Vector2(300, 100));
```

`alignment` 留空使用管理器对齐，`textAreaSize` 留空使用管理器区域；明确传入 `Vector2.zero` 可让本条使用发射点锚定。这些参数也适用于数字、Transform / TextPose 和批量 `TextEmission` 接口。

## 4. 第一条飘字

创建 `DamageTextExample.cs`，挂到任意场景物体，在 Inspector 中关联管理器和目标：

```csharp
using BurstWord.BRG;
using UnityEngine;

public class DamageTextExample : MonoBehaviour
{
    public BrgDamageTextRenderer damageText;
    public Transform target;

    private void OnGUI()
    {
        if (damageText == null || target == null) return;
        if (GUI.Button(new Rect(20, 20, 160, 40), "Show Damage"))
            damageText.Emit(target.position + Vector3.up * 1.5f, 1234, Color.white);
    }
}
```

Play 后点击按钮。正式业务只需调用 `Emit(hitPosition, damage, color)`。字符串使用 `EmitText(hitPosition, "Miss", Color.gray)`。参数顺序始终是位置/目标、数字/文字、颜色。

## 5. 每条选择字体、材质效果和动画

把字体、TMP 材质预设和 `BrgTextAnimation` 预设保存在自己的业务脚本字段中，在 Inspector 关联资源：

```csharp
damageText.Emit(hitPosition, 1234, Color.yellow,
    font: criticalFont,
    material: outlineMaterial,
    animation: criticalAnimation,
    fontSize: 40);

damageText.EmitText(hitPosition, "Healing +200", Color.green,
    font: healingFont,
    material: glowMaterial,
    animation: healingAnimation);

// 跟随目标：管理器的 Space Mode 需选 Screen Follow 或 World Follow。
damageText.EmitText(target, "1234", Color.white, offset: Vector3.up * 1.5f,
    font: criticalFont, material: outlineMaterial, animation: criticalAnimation);
```

- `font` 留空使用管理器的默认字体。直接传入不要求先登记到 Additional Fonts。
- `material` 是 TMP 字体材质预设，可配置描边、阴影（Underlay）、发光（Glow）；应与使用的字体图集匹配。直接传入不要求登记到标签材质列表。
- `material` 留空且不换字体时，使用启用的 Default Font Material；换成其他字体时，使用该字体自身的材质，避免继承错误图集的预设。
- `fontSize` 大于 0 时覆盖本条字号；0 使用管理器字号。
- `animation` 是 GPU 动画预设；留空使用启用的 Default Animation。`useLegacyAnimation: true` 可明确使用内置线性动画。
- 默认动画可以在 **Tools → BurstWord → Animation Editor** 中点击“新建”，编辑并保存，再指定到管理器或业务字段。无需示例资源。
- 参数只作用于本次发射，不修改管理器配置，也不替换其他已存在的飘字。

## 6. 批量发射

高密度战斗应复用请求数组，用 `EmitBatch` 提交。同一批可以混用字体、材质和动画：

```csharp
requests[0] = new BrgDamageTextRenderer.TextEmission(
    "Critical 1234", Color.yellow,
    new BrgDamageTextRenderer.TextPose(hitPosition, Quaternion.identity, Vector3.one),
    font: criticalFont, material: outlineMaterial, animation: criticalAnimation, fontSize: 40);

requests[1] = new BrgDamageTextRenderer.TextEmission(
    "Healing +200", Color.green,
    new BrgDamageTextRenderer.TextPose(otherPosition, Quaternion.identity, Vector3.one),
    font: healingFont, material: glowMaterial, animation: healingAnimation);

damageText.EmitBatch(requests, 2);
```

`requests` 是预先分配的 `TextEmission[]`。每条也可指定 `wrapWidth`、跟随目标、姿态、寿命倍率和漂移。新字体/效果首次使用需要建立资源缓存；之后复用。多字体和材质仍使用同一绘制体系，具体命令数量取决于资源页与排序模式。

## 7. Inspector 的可选设置

基础字体、相机、字号、寿命、容量和管线状态直接显示。其余按功能分组：

- **Automatic Wrapping**：关闭时隐藏 Wrap Width；关闭实际设置为不自动换行。
- **Additional fonts**：开启 Use Additional Fonts 后才显示列表，用于自动 fallback 和 `<font>` 标签。
- **Font material effects**：开启默认/标签材质覆盖后显示预设。关闭后保留配置；直接 `material:` 参数仍可使用。
- **Sprites and emoji**：关闭时隐藏图集及文本到 Sprite 的映射，并停止这些 Sprite 替换。
- **Text layout and optional shaping**：关闭塑形时隐藏适配器设置；没有选择适配器时隐藏连字和字体源覆盖。
- **GPU animation**：关闭默认预设时隐藏预设字段，使用线性动画；直接 `animation:` 参数仍可使用。
- **Advanced rendering**：通常保持默认，Backend 为 Auto，批量 Job 和紧凑字形边界开启，shader 自动选择。

折叠分组只隐藏界面；分组内的功能开关才停用功能，并保留原先资源。Play 中修改字体、材质列表或排版资源时会重建管理器，清掉当前飘字；需要多效果同时出现时使用逐条参数。
