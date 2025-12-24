using UnityEngine;
using FrameEmbededState.Lib;  // <-- added to use MathUtil

namespace FrameEmbededState
{
    public class dataMosh
    {
        static Color32[] _prev; // previous "P-frame"
        static Color32[] _mosh; // intermediate
        static int _frameIndex;

        // Datamosh tuning
        const int   KeyframeInterval  = 90;   // reset every N frames (simulates I-frames)
        const int   BlockSize         = 16;   // macroblock size
        const int   MaxShift          = 14;   // px block displacement
        const float KeepPrevChance    = 0.72f;// higher = more smearing
        const float BlendToPrev       = 0.78f;// 0..1 strength when keeping prev
        const int   QuantBitsKeep     = 5;    // 5 => keep top 5 bits (32 levels)
        const int   ChromaShiftMax    = 3;    // px RGB misalignment
        const float OccasionalFreeze  = 0.10f;// chance to hold prev entirely for a frame

        // Explicit static initialization method to ensure registration
        public static void EnsureRegistered()
        {   // Register datamosh shader with UI if not already registered
            if (_registered)
                return;

            MainUi.RegisterShader(
                "Datamosh",
                "Simulates video compression artifacts and smearing.",
                settings =>
                {
                    settings.Enable = true;
                    settings.Execute = DatamoshExecute;
                }
            );
            _registered = true;
        }
        static bool _registered = false;

        public static void DatamoshExecute(VisualOverlayManager.FrameData frame)
        {   // Datamosh effect implementation for overlay

            var src = frame.Source;
            var dst = frame.Result;
            int w = frame.Width, h = frame.Height;
            int n = w * h;

            if (_prev == null || _prev.Length != n)
            {   // Allocate buffers if needed
                _prev = new Color32[n];
                _mosh = new Color32[n];
                System.Threading.Tasks.Parallel.For(0, n, i => _prev[i] = src[i]);
                _frameIndex = 0;
            }

            float t = Time.unscaledTime;
            int seed = _frameIndex + Mathf.FloorToInt(t * 30f);

            bool isKeyframe = (_frameIndex % KeyframeInterval) == 0;
            bool freeze = !isKeyframe && (MathUtil.Hash01(seed * 97 + 13) < OccasionalFreeze);

            if (isKeyframe)
            {   // Keyframe: clean copy, and refresh prev
                Unity.Collections.NativeArray<Color32>.Copy(src, dst);
                System.Threading.Tasks.Parallel.For(0, n, i => _prev[i] = src[i]);
                _frameIndex++;
                return;
            }

            int blocksX = (w + BlockSize - 1) / BlockSize;
            int blocksY = (h + BlockSize - 1) / BlockSize;

            System.Threading.Tasks.Parallel.For(0, blocksY, by =>
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    int x0 = bx * BlockSize;
                    int y0 = by * BlockSize;

                    float hx = MathUtil.Hash01(bx, by, seed);
                    float hy = MathUtil.Hash01(bx + 91, by - 37, seed);
                    int mvx = Mathf.RoundToInt((hx * 2f - 1f) * MaxShift);
                    int mvy = Mathf.RoundToInt((hy * 2f - 1f) * MaxShift);

                    bool keepPrev = freeze || (MathUtil.Hash01(bx + 17, by - 23, seed) < KeepPrevChance);

                    int x1 = Mathf.Min(x0 + BlockSize, w);
                    int y1 = Mathf.Min(y0 + BlockSize, h);

                    for (int y = y0; y < y1; y++)
                    {
                        int row = y * w;
                        for (int x = x0; x < x1; x++)
                        {
                            int i = row + x;

                            Color32 cur = src[i];

                            if (!keepPrev)
                            { _mosh[i] = cur; continue; }

                            int sx = MathUtil.ClampInt(x + mvx, 0, w - 1);
                            int sy = MathUtil.ClampInt(y + mvy, 0, h - 1);
                            Color32 prev = _prev[sy * w + sx];

                            _mosh[i] = MathUtil.Lerp(cur, prev, BlendToPrev);
                        }
                    }
                }
            });

            int cxShift = Mathf.RoundToInt((MathUtil.Hash01(seed * 31 + 5) * 2f - 1f) * ChromaShiftMax);
            int cyShift = Mathf.RoundToInt((MathUtil.Hash01(seed * 31 + 6) * 2f - 1f) * ChromaShiftMax);
            int bxShift = Mathf.RoundToInt((MathUtil.Hash01(seed * 31 + 7) * 2f - 1f) * ChromaShiftMax);
            int byShift = Mathf.RoundToInt((MathUtil.Hash01(seed * 31 + 8) * 2f - 1f) * ChromaShiftMax);

            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x;

                    Color32 baseC = _mosh[i];

                    int rx = MathUtil.ClampInt(x + cxShift, 0, w - 1);
                    int ry = MathUtil.ClampInt(y + cyShift, 0, h - 1);
                    int bx2 = MathUtil.ClampInt(x + bxShift, 0, w - 1);
                    int by2 = MathUtil.ClampInt(y + byShift, 0, h - 1);

                    Color32 rC = _mosh[ry * w + rx];
                    Color32 bC = _mosh[by2 * w + bx2];

                    byte r = rC.r;
                    byte g = baseC.g;
                    byte b = bC.b;

                    r = MathUtil.Quantize(r, QuantBitsKeep);
                    g = MathUtil.Quantize(g, QuantBitsKeep);
                    b = MathUtil.Quantize(b, QuantBitsKeep);

                    dst[i] = new Color32(r, g, b, baseC.a);
                }
            });

            System.Threading.Tasks.Parallel.For(0, n, i => _prev[i] = _mosh[i]);
            _frameIndex++;
        }
    }
}