using System;
using UnityEngine;
using FrameEmbededState.Lib;
using FrameEmbededState; // Needed for OverlayRenderMode

namespace FrameEmbededState
{
    public class oldFilter : BaseShaderEffect
    {
        // Film look tuning
        const float Contrast     = 1.25f;   // >1 = punchier
        const float Exposure     = 1.05f;   // overall brightness
        const float Gamma        = 0.95f;   // <1 lifts mids a bit
        const float GrainAmount  = 0.18f;   // 0..0.4 typical
        const float GrainSpeed   = 24f;     // noise changes per second
        const float FlickerAmt   = 0.06f;   // 0..0.15
        const float VignetteAmt  = 0.35f;   // 0..0.6
        const float ScanlineAmt  = 0.06f;   // subtle
        const float DustChance   = 0.0022f; // per-pixel probability
        const float ScratchAmt   = 0.22f;   // 0..0.5
        const int   ScratchWidth = 1;       // px

        static oldFilter _instance = new oldFilter(); // auto-register

        public oldFilter() : base("FilmLook", "Cinematic film look with grain, vignette and scratches.") { }

        protected override void ApplyEffect(VisualOverlayManager.VisualOverlaySettings settings)
        {   // Register the film look effect
            settings.Enable = true;
            settings.Execute = FilmExecute;
            settings.RenderMode = OverlayRenderMode.Inclusive;
        }

        static void FilmExecute(VisualOverlayManager.FrameData frame)
        {   // Film look effect implementation for overlay
            var src = frame.Source;
            var dst = frame.Result;
            int w = frame.Width, h = frame.Height;

            // Time-driven noise (use Unity time)
            float t = Time.unscaledTime;

            // Discrete frame seed to make grain feel like film frames
            int filmFrame = Mathf.FloorToInt(t * GrainSpeed);

            // Subtle projector flicker (per-frame + slow sine)
            float flickerRand = (MathUtil.Hash01(filmFrame) * 2f - 1f) * FlickerAmt;
            float flickerSine = Mathf.Sin(t * 6.2f) * (FlickerAmt * 0.35f);
            float flicker = 1f + flickerRand + flickerSine;

            // Gate weave (tiny camera jitter)
            int dx = Mathf.RoundToInt(Mathf.Sin(t * 1.30f) * 1.3f + Mathf.Sin(t * 0.73f) * 0.7f);
            int dy = Mathf.RoundToInt(Mathf.Sin(t * 1.11f) * 1.1f + Mathf.Sin(t * 0.58f) * 0.6f);

            // Scratches: pick 1–2 vertical lines per ~2 seconds
            int scratchSeg = Mathf.FloorToInt(t * 0.5f);
            int scratchX1 = (int)(MathUtil.Hash01(scratchSeg * 17 + 1) * (w - 1));
            int scratchX2 = (int)(MathUtil.Hash01(scratchSeg * 17 + 2) * (w - 1));
            bool twoScratches = MathUtil.Hash01(scratchSeg * 17 + 3) > 0.55f;

            float cx = (w - 1) * 0.5f;
            float cy = (h - 1) * 0.5f;
            float invMaxR = 1f / Mathf.Sqrt(cx * cx + cy * cy);

            System.Threading.Tasks.Parallel.For(0, h, y =>
            {   // Process each row with subtle scanline and per-pixel film effects
                float scan = 1f - ScanlineAmt * (0.5f + 0.5f * Mathf.Sin((y + t * 18f) * 3.14159f));

                for (int x = 0; x < w; x++)
                {
                    // Gate weave sampling
                    int sx = MathUtil.ClampInt(x + dx, 0, w - 1);
                    int sy = MathUtil.ClampInt(y + dy, 0, h - 1);
                    int si = sy * w + sx;

                    Color32 c = src[si];

                    // Grayscale luma
                    float l = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

                    // Exposure/contrast/gamma
                    l *= Exposure;
                    l = (l - 0.5f) * Contrast + 0.5f;
                    l = Mathf.Pow(Mathf.Clamp01(l), Gamma);

                    // Film grain (time-varying)
                    float n = MathUtil.Hash01(x, y, filmFrame) - 0.5f;
                    float n2 = MathUtil.Hash01(x + 131, y - 71, filmFrame) - 0.5f;
                    l += (n * 0.75f + n2 * 0.25f) * GrainAmount;

                    // Dust specks (white/black)
                    float d = MathUtil.Hash01(x - 19, y + 23, filmFrame * 3 + 7);
                    if (d > 1f - DustChance)
                        l = (MathUtil.Hash01(x + 5, y + 9, filmFrame * 11) > 0.5f) ? 1f : 0f;

                    // Vertical scratches (bright streaks)
                    int dist1 = Mathf.Abs(x - scratchX1);
                    int dist2 = Mathf.Abs(x - scratchX2);
                    bool hitScratch = dist1 <= ScratchWidth || (twoScratches && dist2 <= ScratchWidth);
                    if (hitScratch)
                    {
                        float s = 1f - (Mathf.Min(dist1, dist2) / (float)(ScratchWidth + 1));
                        l = Mathf.Lerp(l, 1f, ScratchAmt * s);
                    }

                    // Vignette
                    float rx = x - cx, ry = y - cy;
                    float r = Mathf.Sqrt(rx * rx + ry * ry) * invMaxR; // 0..1
                    float vig = 1f - VignetteAmt * (r * r);
                    l *= vig;

                    // Flicker + scanlines
                    l *= flicker * scan;

                    // Clamp and write (grayscale output)
                    byte q = (byte)(Mathf.Clamp01(l) * 255f);
                    dst[y * w + x] = new Color32(q, q, q, c.a);
                }
            });
        }
    }
}