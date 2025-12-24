using UnityEngine;

namespace FrameEmbededState.Lib
{
    public static class MathUtil
    {   // Shared math utilities used across shaders and effects

        public static uint Hash(uint x)
        {   // Wang hash function for uint
            x = (x ^ 61u) ^ (x >> 16);
            x *= 9u;
            x = x ^ (x >> 4);
            x *= 0x27d4eb2du;
            x = x ^ (x >> 15);
            return x;
        }

        public static float Hash01(int x, int y, int seed)
        {   // Hash to float in 0..1 range using 3D coords and seed
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(seed * 83492791);
            h = Hash(h);
            return (h & 0x00FFFFFFu) / 16777215f;
        }

        public static float Hash01(int seed)
        {   // Hash to float in 0..1 range using seed only
            uint h = Hash((uint)seed);
            return (h & 0x00FFFFFFu) / 16777215f;
        }

        public static int ClampInt(int v, int lo, int hi) =>
            v < lo ? lo : (v > hi ? hi : v);

        public static Color32 Lerp(Color32 a, Color32 b, float t)
        {   // Linear interpolate between two Color32 values (clamped)
            t = Mathf.Clamp01(t);
            return new Color32(
                (byte)(a.r + (b.r - a.r) * t),
                (byte)(a.g + (b.g - a.g) * t),
                (byte)(a.b + (b.b - a.b) * t),
                (byte)(a.a + (b.a - a.a) * t)
            );
        }

        public static byte Quantize(byte v, int keepBits)
        {   // Quantize a byte value to keep only the top bits
            int drop = 8 - Mathf.Clamp(keepBits, 1, 8);
            int mask = 0xFF << drop;
            return (byte)(v & mask);
        }
    }
}