using UnityEngine;
using SFS.World;
using SFS.Parts;
using System.Linq;
using SFS;

namespace FrameEmbededState
{
    public static class ObjectRgbCycle
    {
        static bool _registered;

        static readonly int ColorId     = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmisId      = Shader.PropertyToID("_EmissionColor");

        static readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        public static void EnsureRegistered()
        {   // Register the shader effect for the Rocket only

            if (_registered) return;

            MainUi.RegisterShader(
                "Rocket RGB Cycle",
                "RGB cycle effect applied to visible rocket parts only.",
                settings =>
                {
                    settings.Enable = true;
                    settings.RenderMode = OverlayRenderMode.ObjectLayer;
                    settings.UseGpuShader = false;

                    var rocket = GameObject.FindObjectsOfType<Rocket>()
                        .FirstOrDefault(r => r != null && r.isPlayer);

                    if (rocket == null)
                    {
                        settings.ObjectRenderers = new Renderer[0];
                        settings.Execute = _ => { };
                        return;
                    }

                    settings.ObjectRenderers = rocket.gameObject.GetComponentsInChildren<Renderer>(true)
                        .Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy)
                        .ToArray();

                    settings.Execute = frame =>
                    {
                        float t = Time.unscaledTime * 1.2f;

                        float r = 0.5f + 0.5f * Mathf.Sin(t);
                        float g = 0.5f + 0.5f * Mathf.Sin(t + 2.094f);
                        float b = 0.5f + 0.5f * Mathf.Sin(t + 4.188f);

                        var c = new Color(r, g, b, 1f);

                        // Prefer per-renderer MPB so you don’t mutate shared materials globally
                        var renderers = frame.TargetRenderers ?? settings.ObjectRenderers;
                        for (int i = 0; i < renderers.Length; i++)
                        {
                            var rend = renderers[i];
                            if (!rend) continue;

                            rend.GetPropertyBlock(_mpb);

                            // Set common color property names (different pipelines/shaders use different ones)
                            _mpb.SetColor(ColorId, c);
                            _mpb.SetColor(BaseColorId, c);

                            // Optional: emission (only works if shader uses it)
                            _mpb.SetColor(EmisId, c);

                            rend.SetPropertyBlock(_mpb);
                        }
                    };
                }
            );

            _registered = true;
        }
    }
}
