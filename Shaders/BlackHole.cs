using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using FrameEmbededState.Lib;

namespace FrameEmbededState
{
    public static class BlackHoleShader
    {
        /* =========================
           COMPILE-TIME CONSTANTS
           ========================= */

        // Screen-space center (0..1), depth along +Z
        const float BH_CX = 0.5f;
        const float BH_CY = 0.5f;
        const float BH_CZ = 2.5f;

        const float SCHWARZSCHILD_RADIUS = 0.7f;

        const float DISK_HEIGHT       = 0.1f;
        const float DISK_OUTER_RADIUS = 5.0f;
        const float DISK_INNER_RADIUS = 1.75f;
        const float DISK_FADE         = 0.15f;

        const float NOISE_SEED = 13.37f;

        const int   MAX_STEPS = 200;

        static bool _registered;

        public static void EnsureRegistered()
        {
            if (_registered) return;

            MainUi.RegisterShader(
                "BlackHole",
                "Screen-space volumetric black hole (no init, constants only)",
                settings =>
                {
                    settings.Enable = true;
                    settings.RenderMode = OverlayRenderMode.Exclusive;
                    settings.Execute = Execute;
                }
            );

            _registered = true;
        }

        static void Execute(VisualOverlayManager.FrameData frame)
        {
            var dst = frame.Result;
            int w = frame.Width;
            int h = frame.Height;

            float t = Time.unscaledTime;

            float invW = (w > 1) ? 1f / (w - 1) : 0f;
            float invH = (h > 1) ? 1f / (h - 1) : 0f;

            Parallel.For(0, h, y =>
            {
                float v = y * invH;
                int row = y * w;

                for (int x = 0; x < w; x++)
                {
                    float u = x * invW;
                    Vector3 col = Raymarch(u, v, t);
                    dst[row + x] = ToColor32(col);
                }
            });
        }

        /* =========================
           RAYMARCH
           ========================= */

        static Vector3 Raymarch(float u, float v, float time)
        {
            // Ray origin & direction (screen-space)
            float px = u, py = v, pz = 0f;
            float dx = 0f, dy = 0f, dz = 1f;

            float tx = BH_CX, ty = BH_CY, tz = BH_CZ;

            float trans = 1f;
            Vector3 light = Vector3.zero;

            float step = 0.008f;
            float bound = DISK_OUTER_RADIUS;

            // Skip empty space
            float zStart = tz - bound;
            if (zStart > 0f) pz = zStart;

            for (int i = 0; i < MAX_STEPS; i++)
            {
                step *= 1.008f;

                px += dx * step;
                py += dy * step;
                pz += dz * step;

                float rx = px - tx;
                float ry = py - ty;
                float rz = pz - tz;

                float r2 = rx * rx + ry * ry + rz * rz;
                if (r2 < 1e-8f) break;

                float invR = 1f / Mathf.Sqrt(r2);
                float r = 1f / invR;

                float nx = rx * invR;
                float ny = ry * invR;
                float nz = rz * invR;

                float dot = nx * dx + ny * dy + nz * dz;
                float bend = 1f + dot;
                bend = bend * bend * bend;

                float c = Mathf.Clamp01(2f - 0.5f * r);
                c *= c;

                float r4 = r2 * r2;
                float la = -1.5f * SCHWARZSCHILD_RADIUS * bend * c / r4;

                dx += la * rx * step;
                dy += la * ry * step;
                dz += la * rz * step;

                float invLen = 1f / Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                dx *= invLen; dy *= invLen; dz *= invLen;

                float l = Mathf.Sqrt(rx * rx + rz * rz);
                float ang = Mathf.Atan2(rz, rx);

                float d = AccretionDensity(l, ang, ry, time);

                float bhAbsorb =
                    10f * Mathf.Clamp01((SCHWARZSCHILD_RADIUS - r) / 0.5f + 1f);

                float density = (d + bhAbsorb) * step;
                trans *= Mathf.Exp(-density);

                float rs = Mathf.Max(r, 0.1f);
                Vector3 diskCol = new Vector3(
                    3.5f,
                    0.85f / rs,
                    Mathf.Exp(r * 0.01f) - 1f
                );

                light += trans * d * diskCol * (2f * step);

                if (trans < 0.005f) break;
                if (pz > tz + bound) break;
            }

            Vector3 bg = Background(u, v, time);
            return MathUtil.ToneMapAndGamma(light + trans * bg);
        }

        /* =========================
           ACCRETION DISK
           ========================= */

        static float AccretionDensity(float l, float ang, float y, float time)
        {
            float n = MathUtil.Noise2D(
                new Vector2(
                    0.5f * ang / Mathf.PI + time * 0.2f,
                    Mathf.Log(l + 1e-4f) * 1.5f
                ),
                (int)NOISE_SEED
            );

            float outer = Mathf.Max(1f - l / DISK_OUTER_RADIUS, 0f);
            float inner = Mathf.Clamp((l - DISK_INNER_RADIUS) / DISK_FADE + 1f, 0f, 1f);

            float baseD = Mathf.Pow(outer * inner, 1.5f);

            return baseD
                 * Mathf.Exp(-y * y * 400f)
                 * 13f
                 * (n + Mathf.Max(0f, n - 0.65f) * 1.5f);
        }

        /* =========================
           BACKGROUND (SCREEN-SPACE)
           ========================= */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector3 Background(float u, float v, float t)
        {
            return new Vector3(
                Mathf.Lerp(0.05f, 0.12f, v),
                Mathf.Lerp(0.07f, 0.10f, u),
                0.13f + 0.07f * Mathf.Sin((u + v + t * 0.05f) * 6.2831853f)
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Color32 ToColor32(Vector3 c)
        {
            return new Color32(
                (byte)(Mathf.Clamp01(c.x) * 255f),
                (byte)(Mathf.Clamp01(c.y) * 255f),
                (byte)(Mathf.Clamp01(c.z) * 255f),
                255
            );
        }
    }
}
