using UnityEngine;
using UnityEngine.UI;

namespace BurstWord.BRG
{
    public sealed partial class BrgDamageTextRenderer
    {
        [Tooltip("Optional existing screen-space Canvas. Uses its root Canvas scale factor instead of the manual UI settings. No Canvas is created by BurstWord.")]
        public Canvas scalingCanvas;
        public CanvasScaler.ScaleMode uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        [Min(0.01f)] public float scaleFactor = 1;
        public CanvasScaler.ScreenMatchMode screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        [Range(0, 1)] public float matchWidthOrHeight = 0.5f;
        public CanvasScaler.Unit physicalUnit = CanvasScaler.Unit.Points;
        [Min(1)] public float fallbackScreenDPI = 96;
        public float CurrentScreenScale { get; private set; } = 1;

        /// <summary>Same text-scale calculations as Canvas Scaler. Does not allocate or modify UI.</summary>
        public float CalculateScreenScale(Vector2 renderingSize, float screenDPI = 0)
        {
            switch (uiScaleMode)
            {
                case CanvasScaler.ScaleMode.ConstantPixelSize:
                    return Mathf.Max(0.01f, scaleFactor);
                case CanvasScaler.ScaleMode.ConstantPhysicalSize:
                    float dpi = screenDPI > 0 ? screenDPI : Mathf.Max(1, fallbackScreenDPI);
                    float unitsPerInch;
                    switch (physicalUnit)
                    {
                        case CanvasScaler.Unit.Centimeters: unitsPerInch = 2.54f; break;
                        case CanvasScaler.Unit.Millimeters: unitsPerInch = 25.4f; break;
                        case CanvasScaler.Unit.Points: unitsPerInch = 72; break;
                        case CanvasScaler.Unit.Picas: unitsPerInch = 6; break;
                        default: unitsPerInch = 1; break;
                    }
                    return dpi / unitsPerInch;
                default:
                    float width = Mathf.Max(1, renderingSize.x) / Mathf.Max(1, referenceResolution.x);
                    float height = Mathf.Max(1, renderingSize.y) / Mathf.Max(1, referenceResolution.y);
                    switch (screenMatchMode)
                    {
                        case CanvasScaler.ScreenMatchMode.Expand: return Mathf.Min(width, height);
                        case CanvasScaler.ScreenMatchMode.Shrink: return Mathf.Max(width, height);
                        default:
                            return Mathf.Pow(2, Mathf.Lerp(Mathf.Log(width, 2), Mathf.Log(height, 2), matchWidthOrHeight));
                    }
            }
        }

        private Vector2 UIRenderingSize(float viewportWidth, float viewportHeight)
        {
            if (worldCamera == null) return new Vector2(viewportWidth, viewportHeight);
            if (worldCamera.targetTexture != null)
                return new Vector2(worldCamera.targetTexture.width, worldCamera.targetTexture.height);
            int display = worldCamera.targetDisplay;
            if (display > 0 && display < Display.displays.Length)
                return new Vector2(Display.displays[display].renderingWidth, Display.displays[display].renderingHeight);
            // The viewport is only used to project pixel offsets. Scaling is based on
            // the full output, like an Overlay Canvas, including split-screen cameras.
            if (Screen.width > 0 && Screen.height > 0) return new Vector2(Screen.width, Screen.height);
            // Editor render requests may not have an active Game view yet.
            var viewport = worldCamera.rect;
            return new Vector2(viewportWidth / Mathf.Max(0.00001f, viewport.width),
                viewportHeight / Mathf.Max(0.00001f, viewport.height));
        }

        private Vector4 ScreenParameters()
        {
            float width = worldCamera == null ? Screen.width : worldCamera.pixelWidth;
            float height = worldCamera == null ? Screen.height : worldCamera.pixelHeight;
            if (width <= 0) width = Mathf.Max(1, referenceResolution.x);
            if (height <= 0) height = Mathf.Max(1, referenceResolution.y);
            var root = scalingCanvas != null ? scalingCanvas.rootCanvas : null;
            CurrentScreenScale = root != null && root.renderMode != RenderMode.WorldSpace ? root.scaleFactor :
                CalculateScreenScale(UIRenderingSize(width, height), Screen.dpi);
            return new Vector4(width, height, CurrentScreenScale, 0);
        }

        private void UpdateScreenParameters()
        {
            var screen = ScreenParameters();
            float now = Now;
            foreach (var page in glyphPages)
            {
                page.Material.SetFloat("_BurstTime", now);
                page.Material.SetVector("_BurstScreen", screen);
            }
        }
    }
}
