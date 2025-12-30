using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using FrameEmbededState.Lib;
using SFS.World;

namespace FrameEmbededState
{
    public class UVShader : BaseShaderEffect
    {
        static UVShader _instance = new UVShader(); // auto-register

        public UVShader() : base("Atmosphere UV Map", "Draws UV RGB + grid into the frame (UVs provided by ObjectTarget).") { }

        public static Atmosphere temp_obj;

        protected override void ApplyEffect(VisualOverlayManager.VisualOverlaySettings settings)
        {   // Register the brightness/UV map effect and handle all effect logic

            settings.RenderMode = OverlayRenderMode.ObjectLayer;
            settings.ObjectRenderers = FindCurrentPlanetAtmosphereRenderers();

            settings.Execute = frame =>
            {   // Edit the frame.Result pixel array only, do not touch textures

                Debug.Log($"[BrightnessEffect] Executing effect on frame size {frame.Width}x{frame.Height}");
                Debug.Log($"[BrightnessEffect] Executing effect on object {temp_obj?.planet?.name}");

                var dst = frame.Result;
                int width = frame.Width;
                int height = frame.Height;

                // Define corner colors for cartesian disc
                Color32 c00 = new Color32(255, 0, 0, 255);   // top-left: red
                Color32 c10 = new Color32(0, 0, 255, 255);   // top-right: blue
                Color32 c11 = new Color32(0, 255, 0, 255);   // bottom-right: green
                Color32 c01 = new Color32(255, 255, 255, 255); // bottom-left: white

                // For each output pixel, convert Cartesian UV to polar UV and blend colors to pre-warp for polar mesh sampling
                for (int y = 0; y < height; y++)
                {
                    float v = (float)y / (height - 1);
                    for (int x = 0; x < width; x++)
                    {
                        float u = (float)x / (width - 1);

                        // Convert Cartesian UV to polar UV for color mapping
                        FrameEmbededState.Lib.Renders.ObjectTarget.CartesianUvToPolarUv(u, v, out float polarU, out float polarV, out bool inside);

                        // Bilinear blend using polar UVs to pre-warp the texture
                        Color top = Color.Lerp(c00, c10, polarU);
                        Color bottom = Color.Lerp(c01, c11, polarU);
                        Color final = Color.Lerp(top, bottom, polarV);

                        dst[y * width + x] = (Color32)final;
                    }
                }
            };
        }

        static Renderer[] FindCurrentPlanetAtmosphereRenderers()
        {   // Find the MeshRenderer(s) for the current player's planet's atmosphere
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
                temp_obj = a;
                var mr = a.GetComponent<MeshRenderer>();
                if (mr != null && mr.enabled && mr.gameObject.activeInHierarchy)
                    list.Add(mr);
            }

            return list.Count == 0 ? Array.Empty<Renderer>() : list.ToArray();
        }

        static byte ToByte01(float v) =>
            (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        static float TriU(float u)
        {   // "red start and red end": triangle wave in U => 1 at 0, 0 at 0.5, 1 at 1
            u = Mathf.Repeat(u, 1f);
            return Mathf.Abs(2f * u - 1f);
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        static bool IsGridLine(float t, int divs, float thicknessUV)
        {   // Detects if a value t lies within a grid division in [0,1)
            float s = t * divs;
            float f = Frac(s);                 // [0,1)
            float dist = Mathf.Min(f, 1f - f); // distance to nearest grid line
            return dist <= thicknessUV * divs;
        }

        static Vector2 WorldToAtmosphere01(Vector3 worldPos, Vector3 center, float radius, Vector3 axisU, Vector3 axisV)
        {   // Normalize world position to [0,1]x[0,1] based on transform bounds/basis
            Vector3 d = worldPos - center;
            float u = Vector3.Dot(d, axisU) / (radius * 2f) + 0.5f;
            float v = Vector3.Dot(d, axisV) / (radius * 2f) + 0.5f;
            return new Vector2(u, v);
        }
    }
}
