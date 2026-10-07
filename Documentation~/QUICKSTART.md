# 直接接入 BurstWord

本教程不需要导入 Samples。Samples 仅用于压力对比和查看示例。

## 1. 安装和检查管线

使用 Unity 2022.3 或更新版本的 URP 项目。在 Package Manager 的 Add package from git URL 中输入 `https://github.com/shanflyer/BurstWord.git`。

安装后执行 **Tools → BurstWord → Install BRG Rendering**。打开游戏相机实际使用的 URP Renderer Data，确认 **Renderer Features → BurstWord ordered text** 存在且启用。仅安装依赖包不等于已经正确选择项目的管线。

多管线项目需检查 Project Settings → Graphics 和 Quality 的 URP 配置，以及相机的 Renderer 选择。Install 会配置 Assets 中的 URP 管线、添加 Renderer Feature 并保留绘制所需的 shader/instancing 变体。管理器 Inspector 的 Setup 区会检查当前质量级别及指定相机的 Renderer；缺少配置时，可点击 **Install BRG Rendering** 修复，或 **Select Renderer Data** 直接定位。

## 2. 准备字体

使用 **TMP Font Asset**，而非直接把 `.ttf` / `.otf` 填入管理器。已有 TMP 字体资源可直接使用。Unity 2022.3 中可选中字体文件执行 **Assets → Create → TextMeshPro → Font Asset**。若使用 TMP 自带字体且还没有基础资源，执行 **Window → TextMeshPro → Import TMP Essential Resources**。

字体必须包含要显示的字形。多个字体的字符覆盖与复杂语言塑形是不同的事情；普通数字、中英文先使用默认 TMP glyph data 即可。

## 3. 创建管理器

在场景创建空物体，添加 **Brg Damage Text Renderer**。指定 **Default Text Font [0]** 和 **Camera**，设置字号和 Maximum Live Labels。保持默认屏幕空间和最前显示即可开始。

默认不自动发射文字。角色受伤时由业务代码调用发射接口。一个管理器可处理多字体、多效果和多动画，无需每种效果创建一个管理器。

### 和普通 UI 使用相同的缩放

在 **UI Scaling** 中设置。只有 Screen Snapshot / Screen Follow 显示这些设置，World Follow 使用世界尺寸和镜头透视。

最方便的方式是将游戏已有的屏幕空间 Canvas 拖入 **Use Existing UI Canvas**。飘字直接使用其根 Canvas 的实际缩放值，跟随 Canvas Scaler 的设置与运行时变化；不创建新的 Canvas 或文字 UI 对象。World Space Canvas 不适用于这个选项。

没有现成 Canvas 时，留空并手动设置和 Canvas Scaler 相同的参数：

| UI Scale Mode | 配置与效果 |
| --- | --- |
| Constant Pixel Size | Scale Factor 为 1 时，字号按屏幕像素计算。改变分辨率不改变文字的像素尺寸。 |
| Scale With Screen Size | 设置 Reference Resolution、Screen Match Mode；Match Width Or Height 的 Match 为 0 时按宽度、1 时按高度，0.5 为宽高均衡匹配。Expand 取宽高缩放的较小值，Shrink 取较大值。 |
| Constant Physical Size | 设置 Physical Unit 和 Fallback Screen DPI。使用设备报告的 DPI；未报告时使用备用 DPI，行为与 Canvas Scaler 一致。 |

默认是 **Scale With Screen Size / 1920×1080 / Match Width Or Height / Match 0.5**。将参数设为和你的 UI 一样即可；设置实时影响已存在的屏幕飘字，无需重新发射。手动设置以相机的完整输出尺寸为缩放基准，分屏视口只影响投影；Render Texture 使用完整纹理尺寸。字号和动画的屏幕位移一起缩放，目标远近不会改变屏幕字号。

### 横向和纵向对齐

管理器的 **Text Layout** 提供 **Horizontal：Left / Center / Right** 和 **Vertical：Top / Middle / Bottom**，可组合成九种对齐方式。默认仍是横向、纵向居中。

- **Use Fixed Text Area 关闭**：以发射位置为锚点。Left 从该点向右排，Right 向左排；Top 向下排，Bottom 向上排，多行文字使用相同的对齐边。
- **Use Fixed Text Area 开启**：设置 **Text Area Size** 的宽、高，在以发射位置为中心的区域内对齐，类似 UI Text 在 RectTransform 内对齐。区域仅保存数值，不创建 UI 对象，也不裁剪超出的文字。
- **Automatic Wrapping** 开启后按 Wrap Width 换行；固定区域更窄时使用区域宽度作为上限。关闭时仍可通过 `\n` 主动换行。

尺寸使用和字号相同的排版单位：屏幕模式一起接受 UI scaling，世界模式一起接受世界单位比例和目标姿态。对齐和区域在发射时捕获，修改默认值只影响后续发射。

### 可视化布局预览

点击管理器 **Text Layout → Open Layout Preview**。左边输入任意文字、多行内容或富文本，选择管理器中的字体；左侧的字号、对齐、区域和换行参数直接编辑当前管理器，支持 Undo。Inspector 或预览窗口中修改设置，都会更新右侧预览。

- **青色框**：固定文本区域，标明宽 × 高。拖动右下角可调整区域大小；文字超出区域仍然显示，因为固定区域不裁剪。
- **黄色框**：实际字形绘制边界，包含描边、阴影等字形 padding。底部另显示排版尺寸、行数、字形数。
- **粉色虚线**：有效换行宽度，固定区域更窄时使用较小宽度。关闭换行时不显示。
- **十字**：发射点，也就是布局坐标的原点。关闭固定区域时，可看到不同对齐如何相对此点排布。

Auto Fit 自动展示完整区域和文字；滚轮缩放，中键或 Alt+左键拖动平移，Reset View 恢复。所有尺寸均为字体排版单位，预览缩放只改变视图。这里检查二维文字布局，游戏中的 UI 缩放和世界透视在绘制时应用；预览暂停 GPU 动画以便观察排版。

窗口使用独立、隐藏且不保存的预览场景，预览固定使用插件已有的 Instancing 绘制，与 BRG 共用排版和字形 shader 逻辑，不改变游戏管理器的绘制后端。沿用 TMP 字体、材质、fallback、Sprite 及已注册的塑形接口，不创建场景中的测试文字。Play 中可修改预览内容，但布局参数只读，不清空或重启游戏管理器。关闭窗口或重载脚本后释放预览资源；不向 Player 添加预览代码。

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
            damageText.Emit(target.position + Vector3.up * 1.5f, 1234, Color.white, duration: 1.2f);
    }
}
```

Play 后点击按钮。正式业务只需调用 `Emit(hitPosition, damage, color)`。字符串使用 `EmitText(hitPosition, "Miss", Color.gray)`。参数顺序始终是位置/目标、数字/文字、颜色。

每次发射通过 `duration:` 指定本条存在的秒数，例如 `Emit(hitPosition, damage, color, duration: 1.2f)` 或 `EmitText(hitPosition, "Miss", Color.gray, duration: 2f)`。省略时默认 1.5 秒；Setup 没有全局寿命设置。批量用 `new TextEmission(text, color, pose, duration: 2f)`，或设置请求的 `Duration` 字段。各条时长独立，GPU 动画会在各自的寿命内播放完整曲线。时长必须是有限的正数；对象初始化器方式也必须填写 `Duration`，非法批量时长会在发射任何文字前报错。

## 5. 每条选择字体、效果和动画

效果统一使用 **TMP Font Asset 自带的 Material**。想同时显示普通、描边、阴影或发光，准备各自拥有所需材质的字体资源，再选择对应的字体；同一图集可以共享，不需要为每种效果创建管理器。材质与字体图集需要匹配。管理器没有额外材质开关、材质列表，也没有 `material:` 发射参数。

可直接传入字体资源：

```csharp
damageText.Emit(hitPosition, 1234, Color.yellow,
    font: criticalFont, animation: criticalAnimation, fontSize: 40);
damageText.EmitText(hitPosition, "Healing +200", Color.green,
    font: healingFont, animation: healingAnimation);
```

也可以在 Inspector 的 **Fonts → Additional Text Fonts [1..N]** 列表登记字体，按显示的编号选择：

- `0`：Default Font。
- `1`：列表第一项；`2`：第二项，依次类推。
- 不传 `fontIndex` 时默认是 `0`。列表排序变化会改变编号。
- 如果同时传入 `font:`，直接字体资源优先。直接资源不需要登记到列表。
- 编号越界或列表槽位为空会抛出参数异常；批量请求会在发射前检查全部编号。
- 字体列表用于选择和 `<font="字体资源名称">` 标签查找。缺字使用所选字体资源自身的 **Fallback Font Assets** 和 TMP 全局 fallback，无需在管理器列表重复登记备用字体。

```csharp
damageText.Emit(hitPosition, 1234, Color.yellow, fontIndex: 1);
damageText.EmitText(hitPosition, "Healing +200", Color.green, fontIndex: 2);

// 跟随目标：Space Mode 选择 Screen Follow 或 World Follow。
damageText.EmitText(target, "1234", Color.white, offset: Vector3.up * 1.5f,
    fontIndex: 1, animation: criticalAnimation);

// 查询编号对应的资源。
TMP_FontAsset selectedFont = damageText.GetFont(1);
```

`fontSize` 大于 0 时覆盖本条字号，0 使用管理器字号。动画统一放在 Inspector 的 **Animations** 列表，按显示编号选择：`0` 是第一项，也是默认项；不传 `animationIndex` 时使用 `0`。列表为空或所选项为空时使用内置线性动画。每行的 **Edit / Preview** 打开独立动画编辑器，也可通过 **Tools → BurstWord → Animation Editor** 新建动画，无需示例资源。

```csharp
damageText.EmitText(hitPosition, "1234", Color.white, fontIndex: 1, animationIndex: 2);
damageText.Emit(hitPosition, 1234, Color.white, animationIndex: 1);
BrgTextAnimation selectedAnimation = damageText.GetAnimation(2);
```

也可直接传入 `animation:` 资源，优先于编号，无需登记到列表。`useLegacyAnimation: true` 保留为代码中明确选择线性动画的方式。改变列表顺序会改变后续发射的编号含义；已有飘字继续使用发射时选定的资源。逐条参数不修改管理器默认项。

## 6. 批量发射

高密度战斗应复用请求数组，用 `EmitBatch` 提交。同一批可以混用字体、效果和动画：

```csharp
requests[0] = new BrgDamageTextRenderer.TextEmission(
    "Critical 1234", Color.yellow,
    new BrgDamageTextRenderer.TextPose(hitPosition, Quaternion.identity, Vector3.one),
    fontIndex: 1, animationIndex: 2, fontSize: 40);

requests[1] = new BrgDamageTextRenderer.TextEmission(
    "Healing +200", Color.green,
    new BrgDamageTextRenderer.TextPose(otherPosition, Quaternion.identity, Vector3.one),
    font: healingFont, animation: healingAnimation);

damageText.EmitBatch(requests, 2);
```

`requests` 是预先分配的 `TextEmission[]`。也可直接设置每条的 `FontIndex`，或者 `Font`。每条可指定 `wrapWidth`、跟随目标、姿态、寿命倍率和漂移。新字体/效果首次使用建立资源缓存，之后复用；命令数量取决于资源页和排序模式。

## 7. Inspector 的可选设置

Inspector 按使用目的分区，默认展开，并在当前编辑器会话内记住折叠状态：

- **Setup**：相机、最大同时存在数量，以及管线状态。缺少配置时显示 Install BRG Rendering；已配置时显示 Rendering Ready。
- **Fonts**：普通文字字体和 Sprite 图片字体统一放在这组。Default Text Font [0]、字号和 Additional Text Fonts [1..N] 使用原生 `TMP_FontAsset`，列表供 `fontIndex` 和 `<font>` 标签选择。Use Sprite Fonts 开启后显示 Default Sprite Font 和 Named Sprite Fonts，直接使用原生 `TMP_SpriteAsset`；关闭时隐藏并停用这些 Sprite 设置。
- **Text Layout**：横竖对齐、固定文本区域、自动换行、富文本和字距。关闭固定区域或自动换行时，隐藏其尺寸或宽度参数。塑形通过代码注册委托，没有塑形 Asset 或启用开关。
- **Space & Occlusion**：空间模式和遮挡模式。World Follow 时显示世界单位比例。
- **UI Scaling**：只在屏幕模式显示。可以跟随已有屏幕 Canvas 的实际缩放，或手动配置 Canvas Scaler 对应模式；只显示当前模式需要的参数。
- **Animations**：一个带序号的动画列表，`0` 是默认项；每行可编辑、预览。代码通过 `animationIndex` 选择，没有单独的默认动画开关或预加载列表。 Built-in Motion → Rise Height 设置内置动画高度；速度为高度 / 本条 duration。
- **Runtime Status**：只在 Play 时显示当前后端、缩放、活跃文字/字形、绘制数量和缺失资源计数。

折叠分组只隐藏界面；分组内的功能开关才停用功能，并保留原先资源。Play 中修改字体、字号、相机或排版资源时会重建管理器，清掉当前飘字；需要多效果同时出现时使用逐条参数。修改动画列表影响后续发射，已有飘字继续使用原动画。

底层渲染无需在 Inspector 配置：默认自动选择 BRG / instancing、启用批量 Job 和紧凑字形边界，并选择对应 Shader。压力测试面板保留后端切换，方便做性能对比。

### Sprite 图片字体

将已有 TMP Sprite Asset 指定到 **Fonts → Default Sprite Font**。留空使用 TMP Settings 中的默认 Sprite Asset。直接输入资源中已配置 Unicode 的图片字符，或者使用 TMP 标签：

```csharp
damageText.EmitText(hitPosition, "😊 +100", Color.white);
damageText.EmitText(hitPosition, "<sprite=0> +100", Color.white);
damageText.EmitText(hitPosition, "<sprite name=\"coin\"> +100", Color.white);
// 登记到 Named Sprite Fonts 的另一套资源。
damageText.EmitText(hitPosition, "<sprite=\"CombatIcons\" name=\"fire\"> 1234", Color.white);
```

Unicode、名称、序号和 Fallback Sprite Assets 均在 TMP Sprite Asset 内设置，不再提供额外的 Text To Sprite Mappings 替换表。普通文字字体的 `fontIndex` 编号保持原有含义，Sprite 标签里的数字是对应图标的序号。

### 可选的文字塑形委托

不注册委托时，直接按 TMP 字形数据排版、绘制。需要额外塑形时，由你自己编写方法或调用所选插件：

```csharp
using System.Collections.Generic;
using BurstWord.Typography;

// 初始化时注册；无需制作 Asset。
damageText.SetTextShaper(ShapeText);
// 移除时：damageText.SetTextShaper(null);

void ShapeText(in TextShapingRequest input, List<TextShapingGlyph> output)
{
    // 在这里调用你自己的实现或外部插件，将结果追加到 output。
    // input.Font 是实际使用的 TMP_FontAsset；其余字段包含文字、范围、方向等。
}
```

上面的空方法只是接口位置，必须实际填写输出后才能显示字形。输出包含字体内的 GlyphId、原文 UTF-32 Cluster、Advance 和 OffsetX/OffsetY。完整约定见 [SHAPING.md](SHAPING.md)。委托在主线程处理需要塑形的文本段，重复请求可以使用缓存；更换委托或其内部设置后重新注册，会清除当前飘字和旧缓存。

## 自定义 Shader 效果

打开管理器的 **Shader Effects**，点击 **Create Custom Effect Shader**，将模板保存到项目的 Assets 中。它会自动加入列表。点击行内 **Edit**，在生成的 Shader 中修改 `BurstWordModifyVertex` 和 `BurstWordModifyFragment` 两个函数；底层的字形、空间、TMP 效果和动画由包内共享 HLSL 处理。

```csharp
renderer.EmitText(position, "暴击 1234", Color.white,
    effectIndex: 0,
    effectParameters: new Vector4(1, 0.5f, 0, 3));
```

`effectIndex: 0` 使用列表第一项，空列表或空项使用内置 Shader。`effectIndex: -1` 可以明确使用内置 Shader。`effectParameters` 是每条飘字独立的四个数值，含义由你的效果函数定义，默认全零。数字、Transform、TextPose 和批量发射接口均支持这两个参数。

模板同时支持 BRG 和 Instancing；高级开发者可以实现完整 Shader，但必须遵守数据、Pass 和后端声明规范。不兼容的 Shader 会在 Inspector 中显示原因，发射时也会明确拒绝。**Open Layout Preview** 可以选择 Shader 和输入参数查看静态效果。完整教程和规范见 [SHADER_EFFECTS.md](SHADER_EFFECTS.md)。
