using System;
using UnityEngine;

namespace FrameEmbededState
{
    public class WaterShader : BaseShaderEffect
    {
        static Vector2 _lastUiPos = Vector2.zero;
        static float _splashTime = 0f;
        static float _splashStrength = 0f;

        static WaterShader _instance = new WaterShader(); // auto-register

        public WaterShader() : base("WaterUI", "Water ripple effect that moves with UI (Exclusive mode)") { }

        protected override void ApplyEffect(VisualOverlayManager.VisualOverlaySettings settings)
        {   // Register water shader
            settings.Enable = true;
            settings.RenderMode = OverlayRenderMode.Exclusive;
            settings.Execute = WaterUIExecute;
        }

        static void WaterUIExecute(VisualOverlayManager.FrameData frame)
        {   // Draw water fill effect with ripples and splashes
            var dst = frame.Result;
            int w = frame.Width, h = frame.Height;
            float t = Time.unscaledTime;

            Vector2 uiPos = Input.mousePosition;
            uiPos.x /= Screen.width;
            uiPos.y /= Screen.height;

            float move = (uiPos - _lastUiPos).magnitude;
            if (move > 0.01f)
            {
                _splashTime = t;
                _splashStrength = Mathf.Clamp01(move * 12f);
            }
            _lastUiPos = uiPos;

            float waterline = 0.5f;
            float splashDuration = 0.7f;
            float splash = Mathf.Clamp01(1f - (t - _splashTime) / splashDuration) * _splashStrength;

            Color32 sky = new Color32(120, 180, 255, 255);
            Color32 water = new Color32(40, 90, 200, 255);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float ny = (float)y / (h - 1);
                dst[y * w + x] = ny < waterline ? sky : water;
            }

            for (int x = 0; x < w; x++)
            {
                float nx = (float)x / (w - 1);
                float wave = Mathf.Sin(nx * 6.0f + t * 2.0f + uiPos.x * 2.0f) * 0.012f;
                float splashWave = 0f;
                if (splash > 0.01f)
                {
                    float splashCenter = uiPos.x;
                    float dist = Mathf.Abs(nx - splashCenter);
                    float falloff = Mathf.Exp(-dist * 30f);
                    splashWave = Mathf.Sin(t * 8.0f - dist * 20f) * 0.04f * splash * falloff;
                }
                float surface = waterline + wave + splashWave;
                int sy = Mathf.Clamp(Mathf.RoundToInt(surface * (h - 1)), 0, h - 1);
                for (int dy = -1; dy <= 1; dy++)
                {
                    int y = sy + dy;
                    if (y >= 0 && y < h)
                        dst[y * w + x] = new Color32(180, 220, 255, 255);
                }
                if (splash > 0.01f && splashWave > 0.01f)
                {
                    int dropY = sy - Mathf.RoundToInt(Mathf.Abs(splashWave) * h * 1.5f);
                    if (dropY >= 0 && dropY < h)
                        dst[dropY * w + x] = new Color32(255, 255, 255, 255);
                }
            }
        }
    }
}
