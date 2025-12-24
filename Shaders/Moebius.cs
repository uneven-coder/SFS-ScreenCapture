using UnityEngine;
using FrameEmbededState.Lib;

namespace FrameEmbededState
{
    public class Moebius
    {
        // Tuning parameters for Moebius comic effect
        const float PosterizeLevels = 7f;      // Number of color bands
        const float EdgeStrength   = 0.38f;    // Edge enhancement
        const float SaturationBoost = 1.35f;   // Increase color intensity

        static bool _registered = false;

        public static void EnsureRegistered()
        {   // Register Moebius shader with UI if not already registered
            if (_registered)
                return;

            MainUi.RegisterShader(
                "Moebius",
                "Surreal comic effect with posterized colors and edge lines.",
                settings =>
                {
                    settings.Enable = true;
                    settings.Execute = MoebiusExecute;
                    settings.RenderMode = OverlayRenderMode.Inclusive;
                }
            );
            _registered = true;
        }

        static void MoebiusExecute(VisualOverlayManager.FrameData frame)
        {   // Moebius comic effect implementation for overlay

            var src = frame.Source;
            var dst = frame.Result;
            int w = frame.Width, h = frame.Height;

            System.Threading.Tasks.Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    Color32 c = src[i];

                    // Convert to float color
                    float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;

                    // Posterize and boost saturation (no hue cycling)
                    float hue, sat, val;
                    Color.RGBToHSV(new Color(r, g, b), out hue, out sat, out val);
                    sat = Mathf.Clamp01(sat * SaturationBoost);
                    val = Mathf.Floor(val * PosterizeLevels) / PosterizeLevels;
                    Color cycled = Color.HSVToRGB(hue, sat, val);

                    // Edge detection (simple Sobel-like)
                    float edge = 0f;
                    if (x > 0 && x < w - 1 && y > 0 && y < h - 1)
                    {
                        int idxL = y * w + (x - 1);
                        int idxR = y * w + (x + 1);
                        int idxU = (y - 1) * w + x;
                        int idxD = (y + 1) * w + x;

                        Color32 cL = src[idxL], cR = src[idxR], cU = src[idxU], cD = src[idxD];
                        float lumC = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                        float lumX = Mathf.Abs((0.299f * cR.r + 0.587f * cR.g + 0.114f * cR.b) / 255f -
                                              (0.299f * cL.r + 0.587f * cL.g + 0.114f * cL.b) / 255f);
                        float lumY = Mathf.Abs((0.299f * cD.r + 0.587f * cD.g + 0.114f * cD.b) / 255f -
                                              (0.299f * cU.r + 0.587f * cU.g + 0.114f * cU.b) / 255f);
                        edge = Mathf.Clamp01((lumX + lumY) * EdgeStrength);
                    }

                    // Blend edge as black lines
                    cycled = Color.Lerp(cycled, Color.black, edge);

                    dst[i] = new Color32(
                        (byte)(cycled.r * 255f),
                        (byte)(cycled.g * 255f),
                        (byte)(cycled.b * 255f),
                        c.a
                    );
                }
            });
        }
    }
}