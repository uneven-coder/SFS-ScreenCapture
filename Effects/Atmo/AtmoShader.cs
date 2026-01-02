using System;
using System.Linq;
using UnityEngine;
using FrameEmbededState.Lib;
using FrameEmbededState.Lib.Renders;
using SFS.World;
using SFS.WorldBase;
using SFS.World.PlanetModules;

namespace FrameEmbededState.Effects.AtmoShader
{
    [ShaderModule("AtmoShader", ShaderType.Shader, "Hidden/FrameEmbededState/AtmoShader", OverlayRenderMode.CustomRender)]
    public class AtmoShaderModule : ObjectTargetShaderModule<AtmoShaderModule.Args, object>
    {
        public struct Args
        {
            public float PlanetRadius;
            public float AtmosphereHeight;
            public float GradientMultiplier;
            public Texture2D GradientTexture;
            public Vector3 SunDirection;
            public Color SunColor;
            public Color AtmosphereColor;
            public float AtmosphereDensity;
            public float DensityCurve;
            public float ScatterStrength;
            public float TerminatorWidth;
            public float GodRayIntensity;
            
            public float CloudStartHeight;
            public float CloudMaxHeight;
            public float CloudScale;
            public float CloudThreshold;
            public float CloudDensity;
            public float CloudScrollSpeed;
            public float CloudDetailIntensity;
            public float CloudAlpha;
            public float CloudLightAbsorption;
            public float CloudAmbient;
            public float CloudCoverage;
            public float CloudType;
            public int CloudRaymarchSteps;
            public int CloudLightSteps;
            public float CloudSoftness;
            public Vector3 CloudMovementDirection;
            public float CloudMultiScatter;
            public float CloudBloom;
        }

        private static AtmoLiveUpdater _updater;
        private string _activePlanetName;
        private Renderer[] _activeRenderers = Array.Empty<Renderer>();

        public AtmoShaderModule() { }

        public override object Run(in Args args)
        {   // Initialize updater system for continuous atmosphere parameter updates
            EnsureUpdater();
            _updater.SetModuleAndBaseArgs(this, args);
            return null;
        }

        private void EnsureUpdater()
        {   // Create persistent GameObject to handle per-frame atmosphere updates
            if (_updater != null) return;

            var go = GameObject.Find("[FrameEmbededState] AtmoShaderLiveUpdater");
            if (go == null) go = new GameObject("[FrameEmbededState] AtmoShaderLiveUpdater") { hideFlags = HideFlags.HideAndDontSave };

            _updater = go.GetComponent<AtmoLiveUpdater>() ?? go.AddComponent<AtmoLiveUpdater>();
        }

        internal void Tick(in Args baseArgs)
        {   // Process frame-by-frame updates for sun position and dynamic atmosphere properties
            if (Shader == null) { TryKillUpdaterIfNoCustomShaders(); return; }

            var dyn = BuildDynamicArgs(baseArgs, out var playerPlanetName);
            if (string.IsNullOrEmpty(playerPlanetName)) { TryKillUpdaterIfNoCustomShaders(); return; }

            if (_activePlanetName != playerPlanetName || _activeRenderers.Length == 0 || _activeRenderers.Any(r => r == null))
                RebuildTargetsForPlanet(playerPlanetName, dyn);

            ApplyArgsToExistingMaterials(dyn);
            MainUi.UpdateShaderProvidedArgs("AtmoShader", dyn);
            TryKillUpdaterIfNoCustomShaders();
        }

        private void RebuildTargetsForPlanet(string playerPlanetName, in Args args)
        {   // Locate atmosphere renderers for current planet and apply materials
            _activePlanetName = playerPlanetName;
            RestoreMaterials();

            _activeRenderers = UnityEngine.Object.FindObjectsOfType<Atmosphere>()
                .Where(a => a && a.planet && a.planet.name == playerPlanetName)
                .Select(a => a.GetComponent<MeshRenderer>())
                .Where(mr => mr && mr.enabled && mr.gameObject.activeInHierarchy)
                .Cast<Renderer>()
                .ToArray();

            if (_activeRenderers.Length == 0) { RestoreMaterials(); return; }

            foreach (var r in _activeRenderers)
                StoreAndApplyMaterials(r, r.sharedMaterials, args);
        }

        private void ApplyArgsToExistingMaterials(in Args args)
        {   // Update all active materials with current atmospheric parameters
            if (_activeRenderers == null || _activeRenderers.Length == 0) return;

            foreach (var r in _activeRenderers)
            {   // Process each renderer's materials
                if (r == null) continue;

                var mats = r.sharedMaterials;
                if (mats == null) continue;

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader != Shader) continue;
                    ApplyArgsToMaterial(m, args);
                }
            }
        }

        private Args BuildDynamicArgs(in Args baseArgs, out string playerPlanetName)
        {   // Construct dynamic atmosphere parameters from planet data and user overrides
            var a = baseArgs;
            playerPlanetName = null;

            var userOverrides = MainUi.GetUserArgs("AtmoShader");
            var mergedArgs = MainUi.GetCurrentArgs("AtmoShader") as Args?;
            if (mergedArgs.HasValue) a = mergedArgs.Value;

            Planet playerPlanet = null;
            try { playerPlanet = PlayerController.main.player.Value.location.planet.Value; playerPlanetName = playerPlanet?.name; }
            catch { }

            bool Edited(string key) => userOverrides?.ContainsKey(key) ?? false;

            if (!Edited("SunDirection")) a.SunDirection = new Vector3(0, 1, 0);
            if (!Edited("SunColor")) a.SunColor = new Color(0.9058824f, 0.4901961f, 0.1568628f, 1f);
            if (!Edited("AtmosphereColor")) a.AtmosphereColor = new Color(0.5f, 0.7f, 1.0f, 1f);
            if (!Edited("AtmosphereDensity")) a.AtmosphereDensity = 4f;
            if (!Edited("DensityCurve")) a.DensityCurve = 2f;
            if (!Edited("ScatterStrength")) a.ScatterStrength = 0.5f;
            if (!Edited("TerminatorWidth")) a.TerminatorWidth = 0.2f;
            if (!Edited("GodRayIntensity")) a.GodRayIntensity = 1f;

            if (!Edited("CloudStartHeight")) a.CloudStartHeight = 3000f;
            if (!Edited("CloudMaxHeight")) a.CloudMaxHeight = 20000f;
            if (!Edited("CloudScale")) a.CloudScale = 0.000056f;
            if (!Edited("CloudThreshold")) a.CloudThreshold = 0.455f;
            if (!Edited("CloudDensity")) a.CloudDensity = 8f;
            if (!Edited("CloudScrollSpeed")) a.CloudScrollSpeed = 1500.0f;
            if (!Edited("CloudDetailIntensity")) a.CloudDetailIntensity = 0f;
            if (!Edited("CloudAlpha")) a.CloudAlpha = 0.06f;
            if (!Edited("CloudLightAbsorption")) a.CloudLightAbsorption = 0.5f;
            if (!Edited("CloudAmbient")) a.CloudAmbient = 1.5f;
            if (!Edited("CloudCoverage")) a.CloudCoverage = 0.221f;
            if (!Edited("CloudType")) a.CloudType = 1.28f;
            if (!Edited("CloudRaymarchSteps")) a.CloudRaymarchSteps = 12;
            if (!Edited("CloudLightSteps")) a.CloudLightSteps = 5;
            if (!Edited("CloudSoftness")) a.CloudSoftness = 0.3f;
            if (!Edited("CloudMovementDirection")) a.CloudMovementDirection = new Vector3(1, 0, 0);
            if (!Edited("CloudMultiScatter")) a.CloudMultiScatter = 2f;
            if (!Edited("CloudBloom")) a.CloudBloom = 1.2f;


            if (playerPlanet == null || !playerPlanet.HasAtmosphereVisuals) return a;

            try
            {   // Extract atmosphere gradient properties from planet material
                var atmoMat = playerPlanet.atmosphereMaterial;
                if (atmoMat != null)
                {
                    a.GradientTexture = atmoMat.GetTexture("_GradientTex") as Texture2D;
                    a.GradientMultiplier = atmoMat.GetFloat("_GradientMultiplier");
                    
                    if (!Edited("AtmosphereColor") && a.GradientTexture != null && a.GradientTexture.isReadable)
                    {
                        var pixels = a.GradientTexture.GetPixels(0, a.GradientTexture.height / 2, a.GradientTexture.width, 1);
                        a.AtmosphereColor = pixels.Aggregate(Color.clear, (acc, c) => new Color(acc.r + c.r, acc.g + c.g, acc.b + c.b, acc.a + c.a));
                        a.AtmosphereColor = new Color(a.AtmosphereColor.r / pixels.Length, a.AtmosphereColor.g / pixels.Length, a.AtmosphereColor.b / pixels.Length, 1f);
                    }
                }
            }
            catch { }

            try
            {   // Load atmosphere and cloud geometry from planet data
                if (playerPlanet.data?.atmosphereVisuals?.GRADIENT != null)
                {
                    a.AtmosphereHeight = (float)playerPlanet.data.atmosphereVisuals.GRADIENT.height;
                    a.PlanetRadius = (float)playerPlanet.Radius;
                }
                
                // if (playerPlanet.data?.atmosphereVisuals?.CLOUDS != null)
                // {   // Load absolute cloud heights from planet data
                //     var clouds = playerPlanet.data.atmosphereVisuals.CLOUDS;
                    
                //     if (!Edited("CloudStartHeight"))
                //         a.CloudStartHeight = clouds.startHeight;
                    
                //     if (!Edited("CloudMaxHeight"))
                //         a.CloudMaxHeight = clouds.startHeight + clouds.height;
                // }
            }
            catch { }

            // if (!Edited("AtmosphereDensity") || !Edited("DensityCurve"))
            // {
            //     try
            //     {
            //         if (playerPlanet.HasAtmospherePhysics && playerPlanet.data?.atmospherePhysics != null)
            //         {
            //             var physics = playerPlanet.data.atmospherePhysics;
            //             if (!Edited("AtmosphereDensity") && physics.density > 0) a.AtmosphereDensity = (float)physics.density;
            //             if (!Edited("DensityCurve") && physics.curve > 0) a.DensityCurve = (float)physics.curve;
            //         }
            //     }
            //     catch { }
            // }

            Planet sunPlanet = null;
            try { sunPlanet = UnityEngine.Object.FindObjectsOfType<Planet>().FirstOrDefault(p => p && p.codeName == "Sun"); }
            catch { }

            if (sunPlanet != null)
            {
                if (!Edited("SunDirection"))
                {
                    try
                    {
                        var sunLoc = sunPlanet.GetLocation(WorldTime.main.worldTime);
                        var planetLoc = playerPlanet.GetLocation(WorldTime.main.worldTime);
                        a.SunDirection = (planetLoc.position - sunLoc.position).normalized;
                    }
                    catch { }
                }

                if (!Edited("SunColor"))
                {
                    try
                    {
                        var sunFog = sunPlanet.data?.atmosphereVisuals?.FOG;
                        if (sunFog?.keys != null && sunFog.keys.Length > 0)
                            a.SunColor = sunFog.Evaluate(sunFog.keys[0].distance);
                        else if (sunPlanet.data?.postProcessing?.keys != null && sunPlanet.data.postProcessing.keys.Length > 0)
                        {
                            var pp = sunPlanet.data.postProcessing.Evaluate(0f);
                            a.SunColor = new Color(pp.red, pp.green, pp.blue, 1f);
                        }
                    }
                    catch { }
                }
            }

            return a;
        }

        private void TryKillUpdaterIfNoCustomShaders()
        {   // Cleanup updater when no materials use custom shader
            if (_updater == null) return;

            bool anyUsing = false;
            try { anyUsing = UnityEngine.Object.FindObjectsOfType<MeshRenderer>().Any(r => r && r.sharedMaterial && r.sharedMaterial.shader == Shader); }
            catch { }

            if (!anyUsing) { UnityEngine.Object.Destroy(_updater.gameObject); _updater = null; }
        }

        protected override void ApplyArgsToMaterial(Material mat, Args args)
        {   // Apply all atmosphere and cloud parameters to material
            if (mat == null) throw new ArgumentNullException(nameof(mat));

            if (mat.HasProperty("_PlanetRadius")) mat.SetFloat("_PlanetRadius", args.PlanetRadius);
            if (mat.HasProperty("_AtmosphereHeight")) mat.SetFloat("_AtmosphereHeight", args.AtmosphereHeight);
            if (mat.HasProperty("_GradientMultiplier")) mat.SetFloat("_GradientMultiplier", args.GradientMultiplier);
            if (mat.HasProperty("_GradientTex") && args.GradientTexture != null) mat.SetTexture("_GradientTex", args.GradientTexture);
            if (mat.HasProperty("_SunDir")) mat.SetVector("_SunDir", new Vector4(args.SunDirection.x, args.SunDirection.y, args.SunDirection.z, 0));
            if (mat.HasProperty("_SunColor")) mat.SetColor("_SunColor", args.SunColor);
            if (mat.HasProperty("_AtmosphereColor")) mat.SetColor("_AtmosphereColor", args.AtmosphereColor);
            if (mat.HasProperty("_AtmosphereDensity")) mat.SetFloat("_AtmosphereDensity", args.AtmosphereDensity);
            if (mat.HasProperty("_DensityCurve")) mat.SetFloat("_DensityCurve", args.DensityCurve);
            if (mat.HasProperty("_ScatterStrength")) mat.SetFloat("_ScatterStrength", args.ScatterStrength);
            if (mat.HasProperty("_TerminatorWidth")) mat.SetFloat("_TerminatorWidth", args.TerminatorWidth);
            if (mat.HasProperty("_GodRayIntensity")) mat.SetFloat("_GodRayIntensity", args.GodRayIntensity);
            if (mat.HasProperty("_CloudStartHeight")) mat.SetFloat("_CloudStartHeight", args.CloudStartHeight);
            if (mat.HasProperty("_CloudMaxHeight")) mat.SetFloat("_CloudMaxHeight", args.CloudMaxHeight);
            if (mat.HasProperty("_CloudScale")) mat.SetFloat("_CloudScale", args.CloudScale);
            if (mat.HasProperty("_CloudThreshold")) mat.SetFloat("_CloudThreshold", args.CloudThreshold);
            if (mat.HasProperty("_CloudDensity")) mat.SetFloat("_CloudDensity", args.CloudDensity);
            if (mat.HasProperty("_CloudScrollSpeed")) mat.SetFloat("_CloudScrollSpeed", args.CloudScrollSpeed);
            if (mat.HasProperty("_CloudDetailIntensity")) mat.SetFloat("_CloudDetailIntensity", args.CloudDetailIntensity);
            if (mat.HasProperty("_CloudAlpha")) mat.SetFloat("_CloudAlpha", args.CloudAlpha);
            if (mat.HasProperty("_CloudLightAbsorption")) mat.SetFloat("_CloudLightAbsorption", args.CloudLightAbsorption);
            if (mat.HasProperty("_CloudAmbient")) mat.SetFloat("_CloudAmbient", args.CloudAmbient);
            if (mat.HasProperty("_CloudCoverage")) mat.SetFloat("_CloudCoverage", args.CloudCoverage);
            if (mat.HasProperty("_CloudType")) mat.SetFloat("_CloudType", args.CloudType);
            if (mat.HasProperty("_CloudRaymarchSteps")) mat.SetInt("_CloudRaymarchSteps", args.CloudRaymarchSteps);
            if (mat.HasProperty("_CloudLightSteps")) mat.SetInt("_CloudLightSteps", args.CloudLightSteps);
            if (mat.HasProperty("_CloudSoftness")) mat.SetFloat("_CloudSoftness", args.CloudSoftness);
            if (mat.HasProperty("_CloudMovementDirection")) mat.SetVector("_CloudMovementDirection", new Vector4(args.CloudMovementDirection.x, args.CloudMovementDirection.y, args.CloudMovementDirection.z, 0));
            if (mat.HasProperty("_CloudMultiScatter")) mat.SetFloat("_CloudMultiScatter", args.CloudMultiScatter);
            if (mat.HasProperty("_CloudBloom")) mat.SetFloat("_CloudBloom", args.CloudBloom);
        }

        protected override Material CreateCustomMaterial(Material original, Args args)
        {   // Generate new material preserving original texture and applying atmosphere parameters
            if (Shader == null) return original != null ? new Material(original) : null;

            var mat = new Material(Shader) { name = $"CustomAtmo_{original?.name ?? "Material"}" };

            if (original?.HasProperty("_MainTex") == true && mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", original.GetTexture("_MainTex"));

            ApplyArgsToMaterial(mat, args);
            return mat;
        }

        private sealed class AtmoLiveUpdater : MonoBehaviour
        {
            private AtmoShaderModule _module;
            private Args _baseArgs;

            public void SetModuleAndBaseArgs(AtmoShaderModule module, Args baseArgs)
            { _module = module; _baseArgs = baseArgs; }

            private void Update()
            {   // Per-frame atmosphere parameter updates
                if (_module == null) { Destroy(gameObject); return; }
                _module.Tick(_baseArgs);
            }
        }
    }
}
