using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using FrameEmbededState.Lib;
using SFS.World;

namespace FrameEmbededState
{
    public class BrightnessEffect : BaseShaderEffect
    {
        static BrightnessEffect _instance = new BrightnessEffect(); // auto-register

        public BrightnessEffect() : base("Atmosphere UV Map", "Draws UV RGB + grid into the frame (UVs provided by ObjectTarget).") { }

        protected override void ApplyEffect(VisualOverlayManager.VisualOverlaySettings settings)
        {   // Register the brightness/UV map effect
            settings.RenderMode = OverlayRenderMode.ObjectLayer;
            settings.ObjectRenderers = FindCurrentPlanetAtmosphereRenderers();


            settings.Execute = frame =>
            {
                // Keep this simple: ensure we have atmosphere renderers.
                // (No timers; just re-acquire if empty/null.)
                // if (settings.ObjectRenderers == null ||
                //     settings.ObjectRenderers.Length == 0 ||
                //     settings.ObjectRenderers[0] == null)
                // {
                //     settings.ObjectRenderers = FindCurrentPlanetAtmosphereRenderers();
                // }

                var uvs = frame.CorrectedUvBuffer;
                if (uvs == null) return;

                NativeArray<Color32> dst = frame.Result;
                int n = Mathf.Min(dst.Length, uvs.Length);

                // Grid parameters (in UV space)
                int majorDivs = 12;
                int minorDivs = majorDivs * 4;

                // thickness in UV based on pixel size
                float thickMajor = 2.0f / Mathf.Max(1, frame.Width);
                float thickMinor = 1.0f / Mathf.Max(1, frame.Width);

                for (int i = 0; i < n; i++)
                {
                    var uv = uvs[i];

                    // invalid/outside ring
                    if (uv.x < 0f || uv.y < 0f) continue;

                    float u = Mathf.Repeat(uv.x, 1f);
                    float v = Mathf.Clamp01(uv.y);

                    // Base: R=Tri(U), G=V, B=0
                    byte r = ToByte01(TriU(u));
                    byte g = ToByte01(v);
                    byte b = 0;

                    // Grid overlay
                    bool major = IsGridLine(u, majorDivs, thickMajor) || IsGridLine(v, majorDivs, thickMajor);
                    bool minor = !major && (IsGridLine(u, minorDivs, thickMinor) || IsGridLine(v, minorDivs, thickMinor));

                    if (major)
                    {
                        // major lines white
                        r = 255; g = 255; b = 255;
                    }
                    else if (minor)
                    {
                        // minor lines blue overlay (keep base RG visible a bit)
                        b = 255;
                    }

                    dst[i] = new Color32(r, g, b, 255);
                }
            };
        }

        static Renderer[] FindCurrentPlanetAtmosphereRenderers()
        {
            string playerPlanetName = null;
            try { playerPlanetName = PlayerController.main.player.Value.location.planet.Value.name; }
            catch { }

            var atmos = GameObject.FindObjectsOfType<Atmosphere>();
            if (atmos == null || atmos.Length == 0) return Array.Empty<Renderer>();

            var list = new List<Renderer>(4);
            for (int i = 0; i < atmos.Length; i++)
            {
                var a = atmos[i];
                if (!a) continue;

                if (!string.IsNullOrEmpty(playerPlanetName))
                {
                    try { if (a.planet == null || a.planet.name != playerPlanetName) continue; }
                    catch { }
                }

                var mr = a.GetComponent<MeshRenderer>();
                if (mr != null && mr.enabled && mr.gameObject.activeInHierarchy)
                    list.Add(mr);
            }

            return list.Count == 0 ? Array.Empty<Renderer>() : list.ToArray();
        }

        static byte ToByte01(float v)
        {
            v = Mathf.Clamp01(v);
            return (byte)Mathf.RoundToInt(v * 255f);
        }

        // "red start and red end": triangle wave in U => 1 at 0, 0 at 0.5, 1 at 1
        static float TriU(float u)
        {
            u = Mathf.Repeat(u, 1f);
            return Mathf.Abs(2f * u - 1f);
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        static bool IsGridLine(float t, int divs, float thicknessUV)
        {
            // t in [0,1)
            float s = t * divs;
            float f = Frac(s);                 // [0,1)
            float dist = Mathf.Min(f, 1f - f); // distance to nearest grid line
            return dist <= thicknessUV * divs;
        }
    }
}
