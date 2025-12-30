using System.Linq;
using Unity.Collections;
using UnityEngine;
using FrameEmbededState.Lib;
using SFS.World;

namespace FrameEmbededState
{
    public class RgbCycleEffect : BaseShaderEffect
    {
        static Color32[] _hueLut; // 256 colors
        static readonly int ColorTextureId = Shader.PropertyToID("_ColorTexture");

        static RgbCycleEffect _instance = new RgbCycleEffect(); // auto-register

        public RgbCycleEffect() : base("RGB Cycle", "RGB cycle as a pixel effect and SFS/Part color cycler.") { }

        protected override void ApplyEffect(VisualOverlayManager.VisualOverlaySettings settings)
        {   // Register the RGB cycle effect, supporting both pixel and SFS/Part color cycling
            BuildHueLut();

            settings.RenderMode = OverlayRenderMode.ObjectLayer;

            var playerRocket = GameObject.FindObjectsOfType<Rocket>()
                .FirstOrDefault(r => r != null && r.isPlayer);

            settings.ObjectRenderers = playerRocket
                ? playerRocket.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy)
                    .ToArray()
                : null;

            settings.Execute = frame =>
            {   // Apply pixel effect and SFS/Part color cycling

                int w = frame.Width;
                int h = frame.Height;
                var src = frame.Source;
                var dst = frame.Result;

                int baseOffset = (int)(Time.time * 64f) & 255;
                int xStep = 1;
                int yStep = 3;

                // Pixel effect for all
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    int rowHue = (baseOffset + y * yStep) & 255;

                    for (int x = 0; x < w; x++)
                    {
                        int i = row + x;
                        Color32 c = _hueLut[(rowHue + x * xStep) & 255];
                        c.a = src[i].a;
                        dst[i] = c;
                    }
                }

                // SFS/Part color cycling (material property block)
                var groups = frame.RendererMaterials;
                if (groups != null && groups.Length > 0)
                {
                    for (int i = 0; i < groups.Length; i++)
                    {
                        var group = groups[i];
                        if (group.Renderer == null || group.Materials == null || group.Materials.Length == 0)
                            continue;

                        var mat = group.Materials[0];
                        if (mat != null && mat.shader != null && mat.shader.name == "SFS/Part")
                        {
                            float hue = Mathf.Repeat((baseOffset / 256f) + i * 0.1f, 1f);
                            Color cycled = Color.HSVToRGB(hue, 1f, 1f);
                            var color32 = new Color32(
                                (byte)(cycled.r * 255),
                                (byte)(cycled.g * 255),
                                (byte)(cycled.b * 255),
                                255);

                            var tintTex = GetOrCreateColorTexture(color32);

                            var mpb = new MaterialPropertyBlock();
                            int submeshCount = group.Materials.Length;
                            for (int submeshIndex = 0; submeshIndex < submeshCount; submeshIndex++)
                            {
                                group.Renderer.GetPropertyBlock(mpb, submeshIndex);
                                mpb.SetTexture(ColorTextureId, tintTex);
                                group.Renderer.SetPropertyBlock(mpb, submeshIndex);
                            }
                        }
                    }
                }
            };
        }

        static void BuildHueLut()
        {   // Build a 256-color hue lookup table for fast pixel cycling
            if (_hueLut != null && _hueLut.Length == 256) return;

            _hueLut = new Color32[256];
            for (int i = 0; i < 256; i++)
            {
                float hue = i / 256f;
                Color rgb = Color.HSVToRGB(hue, 1f, 1f);
                _hueLut[i] = new Color32(
                    (byte)(rgb.r * 255f),
                    (byte)(rgb.g * 255f),
                    (byte)(rgb.b * 255f),
                    255);
            }
        }

        static readonly System.Collections.Generic.Dictionary<Color32, Texture2D> _colorTextureCache = new System.Collections.Generic.Dictionary<Color32, Texture2D>();

        static Texture2D GetOrCreateColorTexture(Color32 color)
        {   // Cache single-pixel color textures for SFS/Part cycling
            if (_colorTextureCache.TryGetValue(color, out var cached)) return cached;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            _colorTextureCache[color] = tex;
            return tex;
        }
    }
}
