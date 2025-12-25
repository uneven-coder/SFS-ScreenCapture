using UnityEngine;
using UnityEngine.UI;
using FrameEmbededState;

namespace FrameEmbededState.Lib.Renders
{
    public static class OverlayDispatcher
    {
        public static void Render(VisualOverlayManager mgr, Camera cam, RenderTexture srcRT, RenderTexture dstRT)
        {   // Coordinate rendering based on mode without forcing screen writes
            var settings = mgr.Settings;
            if (settings == null || cam == null)
            {
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            cam.depthTextureMode |= DepthTextureMode.Depth;

            RenderTexture uiOutput = null;

            switch (settings.RenderMode)
            {
                case OverlayRenderMode.BehindUI:
                    RenderBehindUIRenderer.Render(settings, mgr.GpuMaterial, srcRT, dstRT);
                    break;

                case OverlayRenderMode.Inclusive:
                    uiOutput = Inclusive.Render(settings, mgr.GpuMaterial, srcRT, dstRT, true);
                    break;

                case OverlayRenderMode.OnTop:
                    uiOutput = Exclusive.RenderUI(settings, mgr.GpuMaterial, srcRT);
                    Graphics.Blit(srcRT, dstRT);
                    break;

                case OverlayRenderMode.Exclusive:
                    uiOutput = Exclusive.RenderUI(settings, mgr.GpuMaterial, srcRT);
                    Graphics.Blit(srcRT, dstRT);
                    break;

                case OverlayRenderMode.ObjectLayer:
                    ObjectTarget.Render(settings, srcRT, settings.ObjectRenderers);
                    Graphics.Blit(srcRT, dstRT);
                    break;
            }

            VisualOverlayManager.UiBlurOverrideTexture = uiOutput;
            Shader.SetGlobalTexture(VisualOverlayManager.GlobalUiBackgroundTexId, uiOutput);

            PresentToUi(mgr, uiOutput, settings.RenderMode);
        }

        private static void PresentToUi(VisualOverlayManager mgr, Texture uiTex, OverlayRenderMode mode)
        {
            foreach (var img in mgr.UiTargets)
            {
                if (img == null) continue;

                img.texture = uiTex;
                img.material = null;
                img.color = Color.white;

                if (uiTex == null)
                {
                    img.uvRect = new Rect(0, 0, 1, 1);
                    continue;
                }

                if (mode == OverlayRenderMode.Inclusive)
                {
                    img.uvRect = new Rect(0, 0, 1, 1);
                    continue;
                }

                if (TryComputeUvRect(img.rectTransform, out Rect uv))
                    img.uvRect = uv;
                else
                    img.uvRect = new Rect(0, 0, 1, 1);
            }
        }

        private static bool TryComputeUvRect(RectTransform rt, out Rect uvRect)
        {
            uvRect = new Rect(0, 0, 1, 1);
            if (rt == null) return false;

            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            var canvas = rt.GetComponentInParent<Canvas>();
            Camera uiCam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                uiCam = canvas.worldCamera;

            Vector2 p0 = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]);
            Vector2 p2 = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);

            float minX = Mathf.Min(p0.x, p2.x);
            float maxX = Mathf.Max(p0.x, p2.x);
            float minY = Mathf.Min(p0.y, p2.y);
            float maxY = Mathf.Max(p0.y, p2.y);

            if (Screen.width <= 0 || Screen.height <= 0) return false;

            float u0 = Mathf.Clamp01(minX / Screen.width);
            float v0 = Mathf.Clamp01(minY / Screen.height);
            float u1 = Mathf.Clamp01(maxX / Screen.width);
            float v1 = Mathf.Clamp01(maxY / Screen.height);

            uvRect = new Rect(u0, v0, Mathf.Max(0, u1 - u0), Mathf.Max(0, v1 - v0));
            return true;
        }
    }
}
