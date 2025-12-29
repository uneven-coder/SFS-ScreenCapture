using System;
using UnityEngine;

namespace FrameEmbededState
{
    public class ChristmasCozyShader : BaseShaderEffect
    {
        static ChristmasCozyShader _instance = new ChristmasCozyShader(); // auto-register

        public ChristmasCozyShader() : base(
            "Christmas Cozy",
            "Night sky, snowfall, garland lights, and festive elements (Exclusive UI)"
        ) { }

        protected override void ApplyEffect(VisualOverlayManager.VisualOverlaySettings settings)
        {   // Register Christmas Cozy shader
            settings.Enable = true;
            settings.RenderMode = OverlayRenderMode.Exclusive;
            settings.Execute = ChristmasCozyExecute;
        }

        // --- Helpers ---------------------------------------------------------

        static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(a.r, b.r, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(a.g, b.g, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(a.b, b.b, t)),
                255
            );
        }

        static Color32 Additive(Color32 c, Color32 add, float a)
        {
            a = Mathf.Clamp01(a);
            return new Color32(
                (byte)Mathf.Clamp(c.r + add.r * a, 0, 255),
                (byte)Mathf.Clamp(c.g + add.g * a, 0, 255),
                (byte)Mathf.Clamp(c.b + add.b * a, 0, 255),
                255
            );
        }

        static Color32 AlphaBlend(Color32 dst, Color32 src, float a)
        {
            a = Mathf.Clamp01(a);
            float ia = 1f - a;
            return new Color32(
                (byte)Mathf.Clamp(dst.r * ia + src.r * a, 0, 255),
                (byte)Mathf.Clamp(dst.g * ia + src.g * a, 0, 255),
                (byte)Mathf.Clamp(dst.b * ia + src.b * a, 0, 255),
                255
            );
        }

        static void DrawCircleGlow(Color32[] dst, int w, int h, int cx, int cy, int radius, Color32 col, float intensity)
        {
            int r2 = radius * radius;
            int xmin = Mathf.Max(0, cx - radius);
            int xmax = Mathf.Min(w - 1, cx + radius);
            int ymin = Mathf.Max(0, cy - radius);
            int ymax = Mathf.Min(h - 1, cy + radius);

            for (int y = ymin; y <= ymax; y++)
            {
                int dy = y - cy;
                for (int x = xmin; x <= xmax; x++)
                {
                    int dx = x - cx;
                    int d2 = dx * dx + dy * dy;
                    if (d2 > r2) continue;

                    float d = Mathf.Sqrt(d2);
                    float a = Mathf.Clamp01(1f - (d / (radius + 0.0001f)));
                    a = a * a * intensity;
                    int idx = y * w + x;
                    dst[idx] = Additive(dst[idx], col, a);
                }
            }
        }

        static void DrawFilledTriangle(Color32[] dst, int w, int h, Vector2 a, Vector2 b, Vector2 c, Color32 col, float alpha)
        {
            // Bounding box
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), 0, w - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), 0, w - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), 0, h - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), 0, h - 1);

            float Area(Vector2 p1, Vector2 p2, Vector2 p3)
                => (p2.x - p1.x) * (p3.y - p1.y) - (p2.y - p1.y) * (p3.x - p1.x);

            float area = Area(a, b, c);
            if (Mathf.Abs(area) < 1e-5f) return;

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = Area(b, c, p);
                float w1 = Area(c, a, p);
                float w2 = Area(a, b, p);

                bool inside = (w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0);
                if (!inside) continue;

                int idx = y * w + x;
                dst[idx] = AlphaBlend(dst[idx], col, alpha);
            }
        }

        // --- Main ------------------------------------------------------------

        static Color32[] _staticBuffer;
        static int _staticW, _staticH;

        static void ChristmasCozyExecute(VisualOverlayManager.FrameData frame)
        {   // Main function to render the Christmas Cozy overlay

            var dstArr = frame.Result.ToArray();
            int w = frame.Width, h = frame.Height;
            float t = Time.unscaledTime;

            // --- Static layer caching ---
            bool staticChanged = _staticBuffer == null || _staticW != w || _staticH != h;
            if (staticChanged)
            {   // Redraw static background, ground, garland, tree

                _staticBuffer = new Color32[w * h];
                _staticW = w;
                _staticH = h;

                // NIGHT background gradient (no red)
                Color32 top = new Color32(6, 12, 30, 255);
                Color32 bottom = new Color32(14, 30, 70, 255);

                // Subtle horizon glow (cold)
                Color32 horizon = new Color32(30, 55, 110, 255);
                float horizonY = 0.72f;

                for (int y = 0; y < h; y++)
                {
                    float nyFlip = 1f - (float)y / (h - 1);
                    Color32 baseCol = Lerp(bottom, top, nyFlip);
                    float band = Mathf.Clamp01(1f - Mathf.Abs(nyFlip - horizonY) / 0.10f);
                    baseCol = Additive(baseCol, horizon, band * 0.18f);

                    for (int x = 0; x < w; x++)
                        _staticBuffer[y * w + x] = baseCol;
                }

                // Snowy ground with slight hills (bottom)
                int groundStart = (int)(h * 0.72f);
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / (w - 1);
                    float hill = Mathf.Sin(nx * 6.0f + 0.6f) * 10f + Mathf.Sin(nx * 14.0f + 1.3f) * 4f;
                    int yTop = Mathf.Clamp(groundStart + (int)hill, 0, h - 1);

                    for (int y = yTop; y < h; y++)
                    {
                        float ny = (float)(y - yTop) / Mathf.Max(1, (h - 1 - yTop));
                        Color32 snow = Lerp(new Color32(215, 230, 245, 255), new Color32(170, 195, 225, 255), ny);
                        int idx = y * w + x;
                        _staticBuffer[idx] = AlphaBlend(_staticBuffer[idx], snow, 0.92f);
                    }
                }

                // Garland across the top (only string, bulbs removed)
                int garlandYBase = (int)(h * 0.09f);
                float garlandAmp = Mathf.Max(6f, h * 0.015f);
                for (int x = 0; x < w; x++)
                {
                    float yCurve = garlandYBase + Mathf.Sin(x * 0.015f + 0.8f) * garlandAmp;
                    int y0 = Mathf.Clamp((int)yCurve, 0, h - 1);

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int y = Mathf.Clamp(y0 + dy, 0, h - 1);
                        int idx = y * w + x;
                        _staticBuffer[idx] = AlphaBlend(_staticBuffer[idx], new Color32(10, 40, 20, 255), 0.65f);
                    }
                }

                // Simple Christmas tree (bottom center) + star base (star itself is dynamic)
                int treeBaseY = (int)(h * 0.92f);
                int treeTopY = (int)(h * 0.60f);
                int treeX = (int)(w * 0.50f);
                int treeHalfBase = Mathf.Max(26, w / 12);

                DrawFilledTriangle(
                    _staticBuffer, w, h,
                    new Vector2(treeX, treeTopY),
                    new Vector2(treeX - treeHalfBase, treeBaseY),
                    new Vector2(treeX + treeHalfBase, treeBaseY),
                    new Color32(10, 55, 25, 255),
                    0.85f
                );

                DrawFilledTriangle(
                    _staticBuffer, w, h,
                    new Vector2(treeX, treeTopY + 10),
                    new Vector2(treeX, treeBaseY),
                    new Vector2(treeX + treeHalfBase, treeBaseY),
                    new Color32(5, 35, 15, 255),
                    0.35f
                );

                // Candy-cane side borders REMOVED
            }

            // --- Copy static layer ---
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int srcIdx = (h - 1 - y) * w + x;
                int dstIdx = y * w + x;
                dstArr[dstIdx] = _staticBuffer[srcIdx];
            }

            // --- Dynamic layer: stars, ornaments, snowfall, vignette ---

            // Stars (twinkle)
            int starCount = Mathf.Clamp(w * h / 12000, 80, 220);
            for (int i = 0; i < starCount; i++)
            {
                float seed = i * 19.37f;
                float px = Mathf.Repeat(Mathf.Sin(seed) * 999.1f, 1f);
                float py = Mathf.Repeat(Mathf.Sin(seed * 1.31f) * 555.7f, 1f);
                if (py > 0.75f) continue;

                int sx = Mathf.Clamp((int)(px * (w - 1)), 0, w - 1);
                int sy = Mathf.Clamp((int)(py * (h - 1)), 0, h - 1);

                float tw = 0.45f + 0.55f * Mathf.Sin(t * (0.9f + 0.2f * Mathf.Sin(seed)) + seed);
                float a = Mathf.Clamp01(0.15f + 0.35f * tw);
                int idx = sy * w + sx;
                dstArr[idx] = Additive(dstArr[idx], new Color32(220, 235, 255, 255), a);
            }



            int treeTopY_dyn = (int)(h * 0.60f);
            int treeX_center_dyn = (int)(w * 0.50f);
            DrawCircleGlow(dstArr, w, h, treeX_center_dyn, treeTopY_dyn - 8, 10, new Color32(255, 230, 120, 255), 0.65f);
            DrawCircleGlow(dstArr, w, h, treeX_center_dyn, treeTopY_dyn - 8, 5, new Color32(255, 250, 210, 255), 0.9f);



            // Snowfall (3 layers, looks more "night")
            int baseSnow = Mathf.Clamp(w * h / 2200, 60, 180);
            for (int layer = 0; layer < 3; layer++)
            {
                float spY = (layer == 0) ? 0.12f : (layer == 1 ? 0.20f : 0.30f);
                float spX = (layer == 0) ? 0.03f : (layer == 1 ? 0.05f : 0.07f);
                float layerAlpha = (layer == 0) ? 0.55f : (layer == 1 ? 0.70f : 0.85f);
                int count = baseSnow + layer * (baseSnow / 2);

                for (int i = 0; i < count; i++)
                {
                    float seed = i * 13.7f + layer * 101.3f;

                    float px = Mathf.Repeat(Mathf.Sin(seed) * 0.5f + 0.5f + (t * spX + seed * 0.01f), 1f);
                    float py = Mathf.Repeat(
                        Mathf.Cos(seed * 1.2f) * 0.5f + 0.5f
                        - (t * spY + seed * 0.02f), 1f
                    );

                    py += Mathf.Sin(t * (0.7f + 0.2f * layer) + seed) * 0.02f;
                    px += Mathf.Sin(t * (0.4f + 0.1f * layer) + seed * 0.5f) * 0.02f;

                    int sx = Mathf.RoundToInt(px * (w - 1));
                    int sy = Mathf.RoundToInt(py * (h - 1));

                    int flakeSize = (layer == 0) ? 1 : (layer == 1 ? 2 : 3);

                    for (int dy = -flakeSize; dy <= flakeSize; dy++)
                    for (int dx = -flakeSize; dx <= flakeSize; dx++)
                    {
                        int x = sx + dx, y = sy + dy;
                        if ((uint)x >= (uint)w || (uint)y >= (uint)h) continue;

                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - dist / (flakeSize + 0.35f)) * layerAlpha;

                        int idx = y * w + x;
                        dstArr[idx] = Additive(dstArr[idx], new Color32(235, 245, 255, 255), a);
                    }
                }
            }

            // Vignette (kept, tuned for night)
            float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
            float invMaxR = 1f / Mathf.Sqrt(cx * cx + cy * cy);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float rx = x - cx, ry = y - cy;
                float r = Mathf.Sqrt(rx * rx + ry * ry) * invMaxR;
                float vig = 1f - 0.28f * (r * r);
                int idx = y * w + x;
                var c = dstArr[idx];
                dstArr[idx] = new Color32(
                    (byte)(c.r * vig),
                    (byte)(c.g * vig),
                    (byte)(c.b * vig),
                    255
                );
            }

            // Copy back to NativeArray
            for (int i = 0; i < dstArr.Length; i++)
                frame.Result[i] = dstArr[i];
        }
    }
}
