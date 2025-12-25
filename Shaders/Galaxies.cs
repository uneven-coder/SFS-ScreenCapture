using System;
using System.Threading.Tasks;
using UnityEngine;
using FrameEmbededState.Lib;

namespace FrameEmbededState
{
    public static class galaxyShader
    {
        static bool _registered = false;

        struct GalaxyInstance
        {   // Defines position, size, rotation and color for volumetric galaxy
            public Vector3 centerWorld;
            public float radius;
            public float height;
            public float rotation;
            public float rotationSpeed;
            public float seed;
            public Vector3 baseColor;
            public int armCount;
        }

        static GalaxyInstance[] _galaxies;
        static float _lastUpdateTime = -1f;

        public static void EnsureRegistered()
        {   // Register shader and initialise galaxies if not already done
            if (_registered)
                return;

            MainUi.RegisterShader(
                "Galaxies",
                "Volumetric raymarched galaxies with spiral arms, core, dust and gas particles",
                settings =>
                {   // Configure shader settings
                    settings.Enable = true;
                    settings.RenderMode = OverlayRenderMode.Exclusive;
                    settings.Execute = SwirlspaceExecute;
                }
            );

            InitializeGalaxies();
            _registered = true;
        }

        static void InitializeGalaxies()
        {   // Create sparse volumetric galaxies
            _galaxies = new GalaxyInstance[8];

            for (int i = 0; i < _galaxies.Length; i++)
            {
                float angle = (i / (float)_galaxies.Length) * 6.2831853f;
                float dist = 0.25f + 0.18f * Mathf.Sin(i * 1.3f);

                _galaxies[i] = new GalaxyInstance
                {
                    centerWorld = new Vector3(0.5f + Mathf.Cos(angle) * dist, 0.5f + Mathf.Sin(angle) * dist, 2f + i * 0.3f),
                    radius = 0.12f + 0.05f * MathUtil.Hash12(new Vector2(i * 7.3f, i * 13.7f)),
                    height = 0.04f + 0.02f * MathUtil.Hash12(new Vector2(i * 11.1f, i * 17.3f)),
                    rotation = i * 0.785f,
                    rotationSpeed = 0.05f + 0.03f * MathUtil.Hash12(new Vector2(i * 19.7f, i * 23.1f)),
                    seed = 11.7f + i * 31.4f,
                    baseColor = MathUtil.HsvToRgb((i / (float)_galaxies.Length) + 0.15f * MathUtil.Hash12(new Vector2(i * 5.1f, i * 9.3f)), 0.7f, 1f),
                    armCount = 2 + (i % 2)
                };
            }
        }

        static void SwirlspaceExecute(VisualOverlayManager.FrameData frame)
        {   // Main render function with parallel raymarching
            var dst = frame.Result;
            int w = frame.Width, h = frame.Height;
            float t = Time.unscaledTime;
            float aspect = (h > 0) ? (w / (float)h) : 1f;

            // Mouse position normalised (not used in core logic, can be removed if unused)
            // Vector2 mousePos = new Vector2(Input.mousePosition.x / Mathf.Max(1f, Screen.width),
            //                                Input.mousePosition.y / Mathf.Max(1f, Screen.height));

            // Update galaxy rotations and positions if enough time has passed
            float dt = t - _lastUpdateTime;
            if (dt > 0.033f)
            {   // Update rotation and position for each galaxy
                for (int i = 0; i < _galaxies.Length; i++)
                {
                    ref GalaxyInstance gal = ref _galaxies[i];
                    gal.rotation += gal.rotationSpeed * 0.033f;
                    gal.centerWorld.x += 0.0008f * Mathf.Sin(t * 0.2f + i * 0.9f);
                    gal.centerWorld.y += 0.0008f * Mathf.Cos(t * 0.15f + i * 1.2f);
                }
                _lastUpdateTime = t;
            }

            // Clear the NativeArray
            for (int i = 0; i < dst.Length; i++)
            {
                dst[i] = new Color32(0, 0, 0, 255);
            }

            Parallel.For(0, h, y =>
            {   // Process each row independently
                float v = (h <= 1) ? 0f : y / (float)(h - 1);
                int rowStart = y * w;

                for (int x = 0; x < w; x++)
                {
                    float u = (w <= 1) ? 0f : x / (float)(w - 1);

                    Vector3 rayOrigin = new Vector3(u, v, 0f);
                    Vector3 rayDir = Vector3.forward;

                    Vector3 col = Raymarch(rayOrigin, rayDir, t, aspect);

                    dst[rowStart + x] = new Color32(
                        (byte)(Mathf.Clamp01(col.x) * 255f),
                        (byte)(Mathf.Clamp01(col.y) * 255f),
                        (byte)(Mathf.Clamp01(col.z) * 255f),
                        255
                    );
                }
            });
        }

        static Vector3 Raymarch(Vector3 rayOrigin, Vector3 rayDir, float t, float aspect)
        {   // Raymarch through all galaxies accumulating color and density
            const int maxSteps = 120;
            float stepSize = 0.005f;

            float transmittance = 1f;
            Vector3 luminosity = Vector3.zero;
            Vector3 rayPos = rayOrigin;

            for (int step = 0; step < maxSteps; step++)
            {   // March ray through scene
                stepSize *= 1.008f;
                rayPos += rayDir * stepSize;

                if (transmittance < 0.01f)
                    break;

                for (int g = 0; g < _galaxies.Length; g++)
                {
                    ref GalaxyInstance gal = ref _galaxies[g];

                    Vector3 localPos = rayPos - gal.centerWorld;
                    localPos.x *= aspect;

                    float distSq = localPos.x * localPos.x + localPos.y * localPos.y;
                    if (distSq > gal.radius * gal.radius * 1.5f)
                        continue;

                    float cosR = Mathf.Cos(gal.rotation), sinR = Mathf.Sin(gal.rotation);
                    Vector3 rotatedPos = new Vector3(localPos.x * cosR - localPos.z * sinR,
                                                     localPos.y,
                                                     localPos.x * sinR + localPos.z * cosR);

                    float radialDist = Mathf.Sqrt(rotatedPos.x * rotatedPos.x + rotatedPos.z * rotatedPos.z);
                    float angle = Mathf.Atan2(rotatedPos.z, rotatedPos.x);

                    float noise2D = MathUtil.Noise2D(new Vector2(angle * 1.591549f + t * 0.025f, radialDist * 2.8f), gal.seed);
                    noise2D = Mathf.Clamp01(-Mathf.Max(0f, 0.8f - radialDist) + noise2D);

                    float noise3D = MathUtil.Noise3D(new Vector3(1.5f * angle * 0.3183f + t * 0.05f, rotatedPos.y * 8f, Mathf.Log(radialDist * 5f + 1f) * 0.5f), gal.seed * 2.1f);
                    float noise3DCart = MathUtil.Noise3D(rotatedPos * 2f + new Vector3(t * 0.03f, t * 0.02f, t * 0.025f), gal.seed * 3.7f);

                    float noiseFactor = 0.08f * (radialDist + 1f) * (noise2D - 0.5f);
                    Vector3 noisedPos = rotatedPos + new Vector3(noiseFactor, noiseFactor, noiseFactor);
                    float noisedRadial = Mathf.Sqrt(noisedPos.x * noisedPos.x + noisedPos.z * noisedPos.z);

                    float spiralR = noisedRadial * 1.85f - 1.2f;
                    float spiralT = angle % 3.14159f;
                    float spiralDiff = Mathf.Abs((spiralR - spiralT + 1.5708f) % 3.14159f - 1.5708f);

                    float spiralFactor = (1f + MathUtil.Tanh(3f * spiralR)) * 0.6f * Mathf.Max(0f, 0.42f - spiralDiff * spiralDiff);
                    float radialFalloff = Mathf.Max(0f, 1f - Mathf.Sqrt(0.045f * radialDist * radialDist + 24f * rotatedPos.y * rotatedPos.y));
                    float spiralDensity = (0.5f * Mathf.Max(0f, 1.15f - Mathf.Pow(spiralDiff, 0.15f)) + spiralFactor) * radialFalloff * (1.5f * noise2D + 0.55f) * 0.4f;

                    float coreDist1 = Mathf.Sqrt(0.3f * rotatedPos.x * rotatedPos.x + rotatedPos.z * rotatedPos.z + 3f * rotatedPos.y * rotatedPos.y);
                    float coreDist2 = Mathf.Sqrt(0.45f * rotatedPos.x * rotatedPos.x + rotatedPos.z * rotatedPos.z + 4f * rotatedPos.y * rotatedPos.y);
                    float coreDensity = 40f * Mathf.Pow(Mathf.Max(0f, 0.45f - coreDist1), 2f) +
                                       5f * Mathf.Pow(Mathf.Max(0f, 0.65f - coreDist2), 1.5f) * (noise2D + 0.5f);

                    float particleFalloff = Mathf.Max(0f, 1f - Mathf.Pow(0.045f * radialDist * radialDist + 16f * rotatedPos.y * rotatedPos.y, 4f));
                    float particleBase = (0.3f * Mathf.Max(0f, 1.25f - Mathf.Pow(spiralDiff, 0.15f)) + spiralFactor) * particleFalloff * (1f - Mathf.Abs(4f * rotatedPos.y)) * 400f;

                    float dustDensity = Mathf.Pow(Mathf.Max(0f, noise3D - 0.2f), 1.5f) * particleBase;
                    float gasBase = Mathf.Max(0f, 1f - 0.35f * spiralDiff - Mathf.Pow(0.2f * radialDist, 0.4f)) * (0.5f - Mathf.Abs(2.5f * rotatedPos.y));
                    float gasDensity = Mathf.Pow(Mathf.Max(0f, Mathf.Abs(noise3DCart - 0.55f) - 0.12f), 2f) * particleBase * 1.2f * gasBase;

                    Vector3 starCol = Vector3.Lerp(new Vector3(0.45f, 0.6f, 1f), new Vector3(1f, 0.5f, 0.2f),
                                                   Mathf.Pow(Mathf.Max(0f, 1f - 0.2f * radialDist), 1.8f));
                    starCol = new Vector3(starCol.x * gal.baseColor.x, starCol.y * gal.baseColor.y, starCol.z * gal.baseColor.z);

                    float camDist = Vector3.Distance(rayOrigin, rayPos);
                    float prox = MathUtil.Tanh(camDist * 2f);

                    float totalDensity = (spiralDensity + coreDensity + dustDensity + gasDensity) * prox * stepSize * 0.15f;
                    transmittance *= Mathf.Exp(-totalDensity);

                    Vector3 dustColor = new Vector3(0.7f, 0.4f, 0.3f);
                    Vector3 gasColor = new Vector3(1f, 0.3f, 0.3f);
                    Vector3 emissive = new Vector3(
                        starCol.x * (spiralDensity * 12f + coreDensity * 6f + dustColor.x * dustDensity * 0.02f) + gasColor.x * gasDensity * 8f,
                        starCol.y * (spiralDensity * 12f + coreDensity * 6f + dustColor.y * dustDensity * 0.02f) + gasColor.y * gasDensity * 8f,
                        starCol.z * (spiralDensity * 12f + coreDensity * 6f + dustColor.z * dustDensity * 0.02f) + gasColor.z * gasDensity * 8f
                    );

                    luminosity += new Vector3(
                        prox * transmittance * emissive.x * stepSize * 2f,
                        prox * transmittance * emissive.y * stepSize * 2f,
                        prox * transmittance * emissive.z * stepSize * 2f
                    );
                }
            }

            return MathUtil.ToneMapAndGamma(luminosity);
        }
    }
}
