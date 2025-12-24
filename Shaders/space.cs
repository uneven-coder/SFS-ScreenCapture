using System;
using UnityEngine;

namespace FrameEmbededState
{
    public static class spaceShader
    {
        static bool _registered = false;

        public static void EnsureRegistered()
        {
            if (_registered) return;

            MainUi.RegisterShader(
                "Swirlspace",
                "Swirling starfield that follows UI position (Exclusive mode)",
                settings =>
                {
                    settings.Enable = true;
                    settings.RenderMode = OverlayRenderMode.Exclusive;
                    settings.Execute = SwirlspaceExecute;
                }
            );

            _registered = true;
        }

        static void SwirlspaceExecute(VisualOverlayManager.FrameData frame)
        {   // Draw swirling starfield that follows UI position
            var dst = frame.Result;
            int w = frame.Width, h = frame.Height;
            float t = Time.unscaledTime;

            Vector2 uiPos = Input.mousePosition;
            uiPos.x /= Screen.width;
            uiPos.y /= Screen.height;

            int numStars = 250;
            float speed = 0.02f;
            float parallax = 0.09f;
            float starSize = 14.2f;

            for (int i = 0; i < dst.Length; i++) dst[i] = new Color32(0, 0, 0, 255);

            for (int s = 0; s < numStars; s++)
            {
                float seed = s * 37.77f;
                float baseX = Mathf.Repeat(Mathf.Sin(seed) * 0.5f + 0.5f, 1f);
                float baseY = Mathf.Repeat(Mathf.Cos(seed * 1.3f) * 0.5f + 0.5f, 1f);
                float depth = 0.3f + 0.7f * Mathf.Repeat(Mathf.Sin(seed * 2.1f), 1f);
                float px = baseX + (uiPos.x - 0.5f) * parallax * (1f - depth) + t * speed * (0.2f + 0.8f * depth);
                float py = baseY + (uiPos.y - 0.5f) * parallax * (1f - depth);
                px = Mathf.Repeat(px, 1f);
                py = Mathf.Repeat(py, 1f);
                int sx = Mathf.RoundToInt(px * (w - 1));
                int sy = Mathf.RoundToInt(py * (h - 1));
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = sx + dx, y = sy + dy;
                    if (x >= 0 && x < w && y >= 0 && y < h)
                    {
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float intensity = Mathf.Clamp01(1.0f - dist / starSize) * (0.7f + 0.3f * depth);
                        var c = dst[y * w + x];
                        byte val = (byte)Mathf.Clamp(c.r + intensity * 255f, 0, 255);
                        dst[y * w + x] = new Color32(val, val, val, 255);
                    }
                }
            }
        }
    }
}
