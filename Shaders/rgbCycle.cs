using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FrameEmbededState.Lib;
using SFS.World;

namespace FrameEmbededState
{
    public class RgbCycleEffect
    {
        static bool _registered = false;
        static int _executeCount = 0;
        static readonly Dictionary<Color32, Texture2D> _colorTextureCache = new Dictionary<Color32, Texture2D>();
        static readonly int ColorTextureId = Shader.PropertyToID("_ColorTexture");
        static readonly int ShapeTextureId = Shader.PropertyToID("_ShapeTexture");
        static readonly int ShadowTextureId = Shader.PropertyToID("_ShadowTexture");

        static readonly int[] ColorPropIds =
        {
            Shader.PropertyToID("_Color"),
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_TintColor"),
            Shader.PropertyToID("_EmissionColor"),
            Shader.PropertyToID("_MainColor")
        };

        public static void EnsureRegistered()
        {
            if (_registered) return;

            MainUi.RegisterShader("RGB Cycle", "Applies an RGB color cycle to the targeted object.", settings =>
            {
                settings.RenderMode = OverlayRenderMode.ObjectLayer;

                var playerRocket = GameObject.FindObjectsOfType<Rocket>().FirstOrDefault(r => r != null && r.isPlayer);
                settings.ObjectRenderers = playerRocket?.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy).ToArray();

                var mpb = new MaterialPropertyBlock();

                settings.Execute = frame =>
                {   // Execute RGB cycle effect each frame with MaterialPropertyBlock for SFS/Part shader compatibility
                    float baseHue = Time.time * 0.25f;
                    bool shouldLog = _executeCount % 60 == 0;

                    var groups = frame.RendererMaterials;
                    if (groups != null && groups.Length > 0)
                    {
                        if (shouldLog) Debug.Log($"[RgbCycle] Execute {_executeCount}: {groups.Length} groups, hue {baseHue:F2}");

                        for (int i = 0; i < groups.Length; i++)
                        {   // Apply color cycling per renderer group
                            var group = groups[i];
                            if (group.Renderer == null || group.Materials == null) continue;

                            float hue = Mathf.Repeat(baseHue + i * 0.1f, 1f);
                            Color cycled = Color.HSVToRGB(hue, 1f, 1f);

                            bool isSfsPart = group.Materials.Length > 0 && group.Materials[0]?.shader?.name == "SFS/Part";

                            if (isSfsPart) ApplySfsPartColor(group, cycled, shouldLog && i < 3, mpb);
                            else ApplyStandardColor(group, cycled, mpb);

                            if (shouldLog && i < 3)
                                Debug.Log($"[RgbCycle] {group.Renderer.gameObject.name}: {cycled}, shader={group.Materials[0]?.shader?.name}, type={( isSfsPart ? "SFS/Part" : "Standard")}");
                        }
                    }

                    var modelTextures = frame.ModelTextures;
                    if (modelTextures != null && modelTextures.Length > 0)
                    {   // Process model textures with property blocks
                        if (shouldLog) Debug.Log($"[RgbCycle] Processing {modelTextures.Length} model textures");

                        for (int i = 0; i < modelTextures.Length; i++)
                        {
                            var modelData = modelTextures[i];
                            if (modelData.Renderer == null) continue;

                            float hue = Mathf.Repeat(baseHue + i * 0.15f, 1f);
                            Color cycled = Color.HSVToRGB(hue, 1f, 1f);

                            mpb.Clear();
                            modelData.Renderer.GetPropertyBlock(mpb);
                            foreach (int propId in ColorPropIds) mpb.SetColor(propId, cycled);
                            if (modelData.ColorTexture != null) mpb.SetTexture("_ColorTex", modelData.ColorTexture);
                            modelData.Renderer.SetPropertyBlock(mpb);

                            if (shouldLog && i < 3) Debug.Log($"[RgbCycle] Model {modelData.Renderer.gameObject.name}: {cycled}");
                        }
                    }

                    _executeCount++;
                };
            });

            _registered = true;
        }

        static Texture2D GetOrCreateColorTexture(Color32 color)
        {   // Cache single-pixel color textures to avoid creating duplicates
            if (_colorTextureCache.TryGetValue(color, out var cached)) return cached;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            _colorTextureCache[color] = tex;
            return tex;
        }

        static void ApplySfsPartColor(VisualOverlayManager.RendererMaterialGroup group, Color cycled, bool log, MaterialPropertyBlock mpb)
        {   // Apply color to SFS/Part shader using MaterialPropertyBlock per submesh
            var color32 = new Color32((byte)(cycled.r * 255), (byte)(cycled.g * 255), (byte)(cycled.b * 255), 255);
            var tintTex = GetOrCreateColorTexture(color32);

            int submeshCount = group.Materials.Length;
            for (int submeshIndex = 0; submeshIndex < submeshCount; submeshIndex++)
            {   // Apply property block per submesh to override SFS/Part textures
                var mat = group.Materials[submeshIndex];
                if (mat == null) continue;

                mpb.Clear();
                group.Renderer.GetPropertyBlock(mpb, submeshIndex);

                mpb.SetTexture(ColorTextureId, tintTex);

                if (mat.HasProperty(ShapeTextureId))
                {
                    var existingShape = mpb.GetTexture(ShapeTextureId);
                    if (existingShape == null) existingShape = mat.GetTexture(ShapeTextureId);
                    if (existingShape != null) mpb.SetTexture(ShapeTextureId, existingShape);
                    else mpb.SetTexture(ShapeTextureId, Texture2D.whiteTexture);
                }

                if (mat.HasProperty(ShadowTextureId))
                {
                    var existingShadow = mpb.GetTexture(ShadowTextureId);
                    if (existingShadow == null) existingShadow = mat.GetTexture(ShadowTextureId);
                    if (existingShadow != null) mpb.SetTexture(ShadowTextureId, existingShadow);
                    else mpb.SetTexture(ShadowTextureId, Texture2D.whiteTexture);
                }

                group.Renderer.SetPropertyBlock(mpb, submeshIndex);
            }

            if (log) Debug.Log($"[RgbCycle] Applied SFS/Part: {submeshCount} submeshes, color={cycled}");
        }

        static void ApplyStandardColor(VisualOverlayManager.RendererMaterialGroup group, Color cycled, MaterialPropertyBlock mpb)
        {   // Apply standard color properties via MaterialPropertyBlock and material
            mpb.Clear();
            group.Renderer.GetPropertyBlock(mpb);
            foreach (int propId in ColorPropIds) mpb.SetColor(propId, cycled);
            group.Renderer.SetPropertyBlock(mpb);

            foreach (var mat in group.Materials)
            {
                if (mat == null) continue;

                foreach (int colorPropId in ColorPropIds)
                    if (mat.HasProperty(colorPropId)) mat.SetColor(colorPropId, cycled);

                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", cycled * 0.5f);
                }
            }
        }
    }
}
