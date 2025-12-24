using UnityEngine;
using FrameEmbededState.Lib;  // <-- if callers here use MathUtil

namespace FrameEmbededState.Lib
{
    public static class FillTriangleUtil
    {
        public static void FillTriangle(Color32[] mask, int w, int h, Vector3 v0, Vector3 v1, Vector3 v2, Color32 color)
        {   // Fill a triangle in a mask array
            int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x))), 0, w - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x))), 0, w - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y))), 0, h - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y))), 0, h - 1);

            float Area(Vector3 a, Vector3 b, Vector3 c) => (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);

            float area = Area(v0, v1, v2);
            if (Mathf.Abs(area) < 1e-2f) return;

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    Vector3 p = new Vector3(x + 0.5f, y + 0.5f, 0);
                    float w0 = Area(v1, v2, p);
                    float w1 = Area(v2, v0, p);
                    float w2 = Area(v0, v1, p);
                    if ((w0 >= 0 && w1 >= 0 && w2 >= 0) || (w0 <= 0 && w1 <= 0 && w2 <= 0))
                        mask[y * w + x] = color;
                }
        }
    }
}
