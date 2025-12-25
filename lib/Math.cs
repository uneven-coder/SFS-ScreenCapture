using System.Runtime.CompilerServices;
using UnityEngine;

namespace FrameEmbededState.Lib
{
    public static class MathUtil
    {
        // 24-bit to 0..1 scale (matches the original / 16777215f behavior)
        private const float INV_24BIT = 1f / 16777215f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Hash(uint x)
        {
            // Wang hash (uint)
            x = (x ^ 61u) ^ (x >> 16);
            x *= 9u;
            x ^= (x >> 4);
            x *= 0x27d4eb2du;
            x ^= (x >> 15);
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h =
                    (uint)(x * 73856093) ^
                    (uint)(y * 19349663) ^
                    (uint)(seed * 83492791);

                h = Hash(h);
                return (h & 0x00FFFFFFu) * INV_24BIT; // multiply is cheaper than divide
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Hash01(int seed)
        {
            unchecked
            {
                uint h = Hash((uint)seed);
                return (h & 0x00FFFFFFu) * INV_24BIT;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ClampInt(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            // Manual clamp avoids Mathf.Clamp01 call overhead in hot paths
            if (t <= 0f) return a;
            if (t >= 1f) return b;

            // Integer lerp with 8-bit fractional weight (fast, stable, no per-channel floats)
            int ti = (int)(t * 256f); // 0..256 (t in (0,1))
            if (ti > 256) ti = 256;
            int inv = 256 - ti;

            // +128 for rounding before >> 8
            return new Color32(
                (byte)((a.r * inv + b.r * ti + 128) >> 8),
                (byte)((a.g * inv + b.g * ti + 128) >> 8),
                (byte)((a.b * inv + b.b * ti + 128) >> 8),
                (byte)((a.a * inv + b.a * ti + 128) >> 8)
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Quantize(byte v, int keepBits)
        {
            // Clamp keepBits to 1..8 without Mathf
            if (keepBits <= 1) keepBits = 1;
            else if (keepBits >= 8) return v;

            int drop = 8 - keepBits;                 // 0..7
            int mask = 0xFF ^ ((1 << drop) - 1);     // keep top bits (correct for all drop values)
            return (byte)(v & mask);
        }

        // -----------------------------
        // Noise helpers (value noise + fbm)
        // -----------------------------
        public static float Fbm(Vector2 p)
        {   // Fractal Brownian Motion, 4 octaves for 2D noise
            float v = 0f;
            float a = 0.5f;
            float f = 1.0f;

            for (int i = 0; i < 4; i++)
            {
                v += a * Noise(p * f);
                f *= 2.0f;
                a *= 0.5f;
            }
            return v;
        }

        public static float Noise(Vector2 p)
        {   // 2D value noise
            Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            Vector2 f = new Vector2(p.x - i.x, p.y - i.y);

            float a = Hash12(i);
            float b = Hash12(i + new Vector2(1f, 0f));
            float c = Hash12(i + new Vector2(0f, 1f));
            float d = Hash12(i + new Vector2(1f, 1f));

            Vector2 u = f * f * (new Vector2(3f, 3f) - 2f * f);

            float x1 = Mathf.Lerp(a, b, u.x);
            float x2 = Mathf.Lerp(c, d, u.x);
            return Mathf.Lerp(x1, x2, u.y);
        }

        public static float Hash12(Vector2 p)
        {   // Deterministic hash to 0..1
            float h = Mathf.Sin(Vector2.Dot(p, new Vector2(127.1f, 311.7f))) * 43758.5453123f;
            return h - Mathf.Floor(h);
        }

        public static Vector2 Hash22(Vector2 p)
        {   // 2D hash to 2D vector
            float x = Hash12(p);
            float y = Hash12(p + new Vector2(269.5f, 183.3f));
            return new Vector2(x, y);
        }

        public static float Frac(float x) =>
            x - Mathf.Floor(x);  // Fractional part

        public static float NoiseSimple(Vector2 p)
        {   // Simplified 2-octave noise
            Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            Vector2 f = p - i;
            Vector2 u = f * f * (new Vector2(3f, 3f) - 2f * f);

            float a = Hash12(i);
            float b = Hash12(i + new Vector2(1f, 0f));
            float c = Hash12(i + new Vector2(0f, 1f));
            float d = Hash12(i + new Vector2(1f, 1f));

            float v1 = Mathf.Lerp(a, b, u.x);
            float v2 = Mathf.Lerp(c, d, u.x);
            float oct1 = Mathf.Lerp(v1, v2, u.y);

            Vector2 p2 = p * 2f;
            i = new Vector2(Mathf.Floor(p2.x), Mathf.Floor(p2.y));
            f = p2 - i;
            u = f * f * (new Vector2(3f, 3f) - 2f * f);

            a = Hash12(i); b = Hash12(i + new Vector2(1f, 0f));
            c = Hash12(i + new Vector2(0f, 1f)); d = Hash12(i + new Vector2(1f, 1f));

            v1 = Mathf.Lerp(a, b, u.x); v2 = Mathf.Lerp(c, d, u.x);

            return oct1 * 0.6f + Mathf.Lerp(v1, v2, u.y) * 0.4f;
        }

        public static float Hash13(Vector3 p)
        {   // Hash 3D vector to float
            float h = Mathf.Sin(Vector3.Dot(p, new Vector3(127.1f, 311.7f, 74.3f))) * 43758.5453123f;
            return h - Mathf.Floor(h);
        }

        public static float Noise2D(Vector2 p, float seed)
        {   // 2D value noise with seed
            Vector2 i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            Vector2 f = p - i;
            Vector2 u = f * f * (new Vector2(3f, 3f) - 2f * f);

            float a = Hash12(i + new Vector2(seed, seed * 1.1f));
            float b = Hash12(i + new Vector2(1f, 0f) + new Vector2(seed, seed * 1.1f));
            float c = Hash12(i + new Vector2(0f, 1f) + new Vector2(seed, seed * 1.1f));
            float d = Hash12(i + new Vector2(1f, 1f) + new Vector2(seed, seed * 1.1f));

            return Mathf.Lerp(Mathf.Lerp(a, b, u.x), Mathf.Lerp(c, d, u.x), u.y);
        }

        public static float Noise3D(Vector3 p, float seed)
        {   // 3D value noise with seed
            Vector3 i = new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
            Vector3 f = p - i;
            Vector3 u = Vector3.Scale(Vector3.Scale(f, f), (new Vector3(3f, 3f, 3f) - 2f * f));

            float h000 = Hash13(i + new Vector3(0f, 0f, 0f) + new Vector3(seed, seed * 1.3f, seed * 1.7f));
            float h100 = Hash13(i + new Vector3(1f, 0f, 0f) + new Vector3(seed, seed * 1.3f, seed * 1.7f));
            float h010 = Hash13(i + new Vector3(0f, 1f, 0f) + new Vector3(seed, seed * 1.3f, seed * 1.7f));
            float h110 = Hash13(i + new Vector3(1f, 1f, 0f) + new Vector3(seed, seed * 1.3f, seed * 1.7f));
            float h001 = Hash13(i + new Vector3(0f, 0f, 1f) + new Vector3(seed, seed * 1.3f, seed * 1.7f));
            float h101 = Hash13(i + new Vector3(1f, 0f, 1f) + new Vector3(seed, seed * 1.3f, seed * 1.7f));
            float h011 = Hash13(i + new Vector3(0f, 1f, 1f) + new Vector3(seed, seed * 1.3f, seed * 1.7f));
            float h111 = Hash13(i + new Vector3(1f, 1f, 1f) + new Vector3(seed, seed * 1.3f, seed * 1.7f));

            float x00 = Mathf.Lerp(h000, h100, u.x);
            float x10 = Mathf.Lerp(h010, h110, u.x);
            float x01 = Mathf.Lerp(h001, h101, u.x);
            float x11 = Mathf.Lerp(h011, h111, u.x);

            float y0 = Mathf.Lerp(x00, x10, u.y);
            float y1 = Mathf.Lerp(x01, x11, u.y);

            return Mathf.Lerp(y0, y1, u.z);
        }

        public static float Tanh(float x)
        {   // Hyperbolic tangent approximation
            if (x > 3f) return 1f;
            if (x < -3f) return -1f;

            float x2 = x * x;
            return x * (27f + x2) / (27f + 9f * x2);
        }

        // -----------------------------
        // Color helpers
        // -----------------------------
        public static Vector3 HsvToRgb(float h, float s, float v)
        {   // Convert HSV to RGB
            h = Frac(h);
            s = Mathf.Clamp01(s);
            v = Mathf.Clamp01(v);

            float c = v * s;
            float hp = h * 6f;
            float x = c * (1f - Mathf.Abs((hp % 2f) - 1f));

            float r1, g1, b1;
            if (hp < 1f)      { r1 = c; g1 = x; b1 = 0f; }
            else if (hp < 2f) { r1 = x; g1 = c; b1 = 0f; }
            else if (hp < 3f) { r1 = 0f; g1 = c; b1 = x; }
            else if (hp < 4f) { r1 = 0f; g1 = x; b1 = c; }
            else if (hp < 5f) { r1 = x; g1 = 0f; b1 = c; }
            else              { r1 = c; g1 = 0f; b1 = x; }

            float m = v - c;
            return new Vector3(r1 + m, g1 + m, b1 + m);
        }

        public static Vector3 ToneMapAndGamma(Vector3 col)
        {   // Combined tone mapping and gamma correction
            col = new Vector3(col.x / (1f + col.x * 0.5f),
                             col.y / (1f + col.y * 0.5f),
                             col.z / (1f + col.z * 0.5f));
            return new Vector3(Mathf.Pow(Mathf.Clamp01(col.x), 0.4545f),
                              Mathf.Pow(Mathf.Clamp01(col.y), 0.4545f),
                              Mathf.Pow(Mathf.Clamp01(col.z), 0.4545f));
        }
    }
}
