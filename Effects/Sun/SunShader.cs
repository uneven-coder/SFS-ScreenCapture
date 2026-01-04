using System;
using System.Linq;
using UnityEngine;
using FrameEmbededState.Lib;
using FrameEmbededState.Lib.Renders;
using SFS.World;
using SFS.WorldBase;

namespace FrameEmbededState.Effects.SunShader
{
    [ShaderModule("SunShader", ShaderType.Shader, "Hidden/FrameEmbededState/SunShader", OverlayRenderMode.CustomRender)]
    public class SunShaderModule : ObjectTargetShaderModule<SunShaderModule.Args, object>
    {
        public struct Args
        {
            [ShaderArg(group: "Core", property: "_SunColor", defaultValue: "(1.0,0.95,0.85,1.0)")] public Color SunColor;
            [ShaderArg(group: "Core", property: "_SunIntensity", defaultValue: 2.5f)] public float SunIntensity;
            [ShaderArg(group: "Core", property: "_Brightness", defaultValue: 1.0f)] public float Brightness;
            [ShaderArg(group: "Core", property: "_MinBrightness", defaultValue: 0.3f)] public float MinBrightness;
            [ShaderArg(group: "Core", property: "_MaxBrightness", defaultValue: 1.5f)] public float MaxBrightness;
            [ShaderArg(group: "Core", property: "_BrightnessFadeStart", defaultValue: 500000f)] public float BrightnessFadeStart;
            [ShaderArg(group: "Core", property: "_BrightnessFadeEnd", defaultValue: 5000000f)] public float BrightnessFadeEnd;
            [ShaderArg(group: "Corona", property: "_CoronaColor", defaultValue: "(1.0,0.7,0.4,1.0)")] public Color CoronaColor;
            [ShaderArg(group: "Corona", property: "_CoronaIntensity", defaultValue: 1.14f)] public float CoronaIntensity;
            [ShaderArg(group: "Corona", property: "_CoronaSize", defaultValue: 2.24f)] public float CoronaSize;
            [ShaderArg(group: "Corona", property: "_AtmosphericGlow", defaultValue: 1.2f)] public float AtmosphericGlow;
            [ShaderArg(group: "Surface", property: "_LimbDarkeningStrength", defaultValue: 0.2f)] public float LimbDarkeningStrength;
            [ShaderArg(group: "Surface", property: "_GranulationScale", defaultValue: 22.0f)] public float GranulationScale;
            [ShaderArg(group: "Surface", property: "_GranulationIntensity", defaultValue: 1.59f)] public float GranulationIntensity;
            [ShaderArg(group: "Surface", property: "_TemperatureVariation", defaultValue: 12.93f)] public float TemperatureVariation;
            [ShaderArg(group: "Flares", property: "_FlareIntensity", defaultValue: 741.8f)] public float FlareIntensity;
            [ShaderArg(group: "Flares", property: "_FlareScale", defaultValue: 147.8f)] public float FlareScale;
            [ShaderArg(group: "Flares", property: "_FlareSpeed", defaultValue: 2.99f)] public float FlareSpeed;
            [ShaderArg(group: "Animation", property: "_RotationSpeed", defaultValue: 0.01f)] public float RotationSpeed;
            [ShaderArg(group: "Rendering", property: "_BloomThreshold", defaultValue: 1.43f)] public float BloomThreshold;
            [ShaderArg(group: "Mask", property: "_EdgeSoftness", defaultValue: 0.21f)] public float EdgeSoftness;
            [ShaderArg(group: "Positioning", property: "_SunCenterWS")] public Vector3 SunCenterWS;
            [ShaderArg(group: "Positioning", property: "_SunRadius", defaultValue: 696000000f)] public float SunRadius;
            [ShaderArg(group: "Positioning", property: "_SunScale", defaultValue: 1f)] public float SunScale;
            [ShaderArg(group: "Positioning", property: "_BoundsMin")] public Vector2 BoundsMin;
            [ShaderArg(group: "Positioning", property: "_BoundsMax")] public Vector2 BoundsMax;
            [ShaderArg(group: "Positioning", property: "_CameraDistanceWS")] public float CameraDistanceWS;
            
            [ShaderArg(group: "Corona Layers", property: "_CoronaLayerStart", defaultValue: 1.02f)] public float CoronaLayerStart;
            [ShaderArg(group: "Corona Layers", property: "_CoronaLayerEnd", defaultValue: 1.5f)] public float CoronaLayerEnd;
            [ShaderArg(group: "Corona Layers", property: "_CoronaLayerDensity", defaultValue: 0.3f)] public float CoronaLayerDensity;
            [ShaderArg(group: "Corona Layers", property: "_CoronaLayerSteps", defaultValue: 16)] public int CoronaLayerSteps;
            [ShaderArg(group: "Corona Layers", property: "_CoronaLayerScale", defaultValue: 4.0f)] public float CoronaLayerScale;
            [ShaderArg(group: "Corona Layers", property: "_CoronaLayerSpeed", defaultValue: 0.03f)] public float CoronaLayerSpeed;
            [ShaderArg(group: "Corona Layers", property: "_CoronaLayerColor", defaultValue: "(1.0,0.8,0.5,1.0)")] public Color CoronaLayerColor;
            
            [ShaderArg(group: "Horizon Effect", property: "_HorizonFadeStart", defaultValue: 1000000f)] public float HorizonFadeStart;
            [ShaderArg(group: "Horizon Effect", property: "_HorizonFadeEnd", defaultValue: 2000000f)] public float HorizonFadeEnd;
            [ShaderArg(group: "Horizon Effect", property: "_HorizonBrightness", defaultValue: 0.4f)] public float HorizonBrightness;
            [ShaderArg(group: "Horizon Effect", property: "_HorizonColorShift", defaultValue: "(1.0,0.7,0.4,1.0)")] public Color HorizonColorShift;
        }

        private static SunLiveUpdater _updater;
        private string _activeSunName;
        private Renderer[] _activeRenderers = Array.Empty<Renderer>();
        private Bounds _lastSunBounds;

        public SunShaderModule()
        {   // Initialize shader module for custom render replacing sun materials
        }

        public override object Run(in Args args)
        {   // Execute shader by initializing updater which will continuously apply materials
            InitializeUpdater();
            _updater.SetModuleAndBaseArgs(this, args);
            return null;
        }

        private void InitializeUpdater()
        {   // Create or reuse live updater component for continuous shader updates
            if (_updater != null) return;

            var go = GameObject.Find("[FrameEmbededState] SunShaderLiveUpdater") ?? new GameObject("[FrameEmbededState] SunShaderLiveUpdater") { hideFlags = HideFlags.HideAndDontSave };
            _updater = go.GetComponent<SunLiveUpdater>() ?? go.AddComponent<SunLiveUpdater>();
        }

        internal void Tick(in Args baseArgs)
        {   // Update sun shader every frame to find and replace sun materials
            
            if (Shader == null) { RemoveUpdater(); return; }

            var sunPlanet = FindSunPlanet();
            if (sunPlanet == null) { RemoveUpdater(); return; }

            string sunPlanetName = sunPlanet.name;
            bool needsReinit = string.IsNullOrEmpty(_activeSunName) || 
                             _activeSunName != sunPlanetName || 
                             _activeRenderers.Length == 0 ||
                             _activeRenderers.Any(r => r == null || r.sharedMaterials == null || r.sharedMaterials.Length == 0);

            if (needsReinit)
                ReinitializeSunMaterials(sunPlanet, baseArgs);
            else
                UpdateMaterialsWithArgs(baseArgs);

            MainUi.UpdateShaderProvidedArgs("SunShader", baseArgs);
        }

        private Planet FindSunPlanet()
        {   // Find sun by searching all Planet components for name matching "Sun"
            try
            {
                var allPlanets = UnityEngine.Object.FindObjectsOfType<Planet>();
                return allPlanets.FirstOrDefault(p => p && p.name != null && p.name.Equals("Sun", StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        private void ReinitializeSunMaterials(Planet sunPlanet, in Args args)
        {   // Reinitialize materials with object-space sun shader using bounds for scale
            
            _activeSunName = sunPlanet.name;
            RestoreMaterials();

            var sunRenderers = new System.Collections.Generic.List<Renderer>();

            var directRenderers = sunPlanet.GetComponentsInChildren<Renderer>(includeInactive: false)
                .Where(r => r && r.enabled && r.gameObject.activeInHierarchy && r.sharedMaterials != null && r.sharedMaterials.Length > 0)
                .ToArray();
            sunRenderers.AddRange(directRenderers);

            if (sunPlanet.HasAtmosphereVisuals)
            {   // Sun may have atmosphere component - use its renderers
                var atmospheres = UnityEngine.Object.FindObjectsOfType<Atmosphere>()
                    .Where(a => a && a.planet && a.planet.name == sunPlanet.name)
                    .ToArray();

                foreach (var atmo in atmospheres)
                {
                    var atmoRenderers = atmo.GetComponentsInChildren<Renderer>(includeInactive: false)
                        .Where(r => r && r.enabled && r.gameObject.activeInHierarchy && r.sharedMaterials != null && r.sharedMaterials.Length > 0)
                        .ToArray();
                    sunRenderers.AddRange(atmoRenderers);
                }
            }

            _activeRenderers = sunRenderers.Distinct().ToArray();

            if (_activeRenderers.Length == 0) return;

            CalculateSunBounds(out var sunBounds, out var sunCenter);
            _lastSunBounds = sunBounds;

            var cameraPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            
            var updatedArgs = args;
            updatedArgs.SunScale = sunBounds.size.magnitude;
            updatedArgs.SunCenterWS = sunCenter;
            updatedArgs.SunRadius = (float)sunPlanet.Radius;
            updatedArgs.CameraDistanceWS = Vector3.Distance(cameraPos, sunCenter);

            foreach (var r in _activeRenderers)
            {   // Replace all materials with single sun shader material using object-space bounds
                
                var originalMats = r.sharedMaterials;
                
                Texture mainTex = (originalMats.Length > 0 && originalMats[0] != null && originalMats[0].HasProperty("_MainTex")) 
                    ? originalMats[0].mainTexture 
                    : null;
                
                Material sunMat = CreateSunMaterial(mainTex, updatedArgs, r);
                if (sunMat == null) continue;

                Material[] customMats = new Material[] { sunMat };

                _originalMaterials[r] = originalMats;
                _customMaterials[r] = customMats;
                r.sharedMaterials = customMats;
            }
        }

        private void CalculateSunBounds(out Bounds combinedBounds, out Vector3 sunCenter)
        {   // Calculate combined bounds of all sun renderers in world space
            
            combinedBounds = new Bounds();
            sunCenter = Vector3.zero;
            bool first = true;

            foreach (var r in _activeRenderers)
            {
                if (r == null) continue;

                var bounds = r.bounds;
                if (first) { combinedBounds = bounds; sunCenter = r.transform.position; first = false; }
                else combinedBounds.Encapsulate(bounds);
            }
        }

        private Material CreateSunMaterial(Texture mainTex, in Args args, Renderer renderer)
        {   // Create new material using sun shader with object-space bounds
            if (Shader == null) return null;

            var mat = new Material(Shader) { name = "CustomSunMaterial" };
            if (mainTex != null && mat.HasProperty("_MainTex"))
                mat.mainTexture = mainTex;

            CalculateRendererBounds(renderer, out var boundsMin, out var boundsMax);
            var argsWithBounds = args;
            argsWithBounds.BoundsMin = boundsMin;
            argsWithBounds.BoundsMax = boundsMax;

            ApplyArgsAutomatic(mat, argsWithBounds);
            return mat;
        }

        private void CalculateRendererBounds(Renderer renderer, out Vector2 boundsMin, out Vector2 boundsMax)
        {   // Calculate object-space XY bounds for renderer mesh
            
            var meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var mesh = meshFilter.sharedMesh;
                var vertices = mesh.vertices;
                
                if (vertices.Length > 0)
                {
                    float minX = vertices.Min(v => v.x);
                    float maxX = vertices.Max(v => v.x);
                    float minY = vertices.Min(v => v.y);
                    float maxY = vertices.Max(v => v.y);
                    
                    boundsMin = new Vector2(minX, minY);
                    boundsMax = new Vector2(maxX, maxY);
                    return;
                }
            }

            boundsMin = new Vector2(-1, -1);
            boundsMax = new Vector2(1, 1);
        }

        private void UpdateMaterialsWithArgs(in Args args)
        {   // Update existing materials with new argument values including bounds
            
            if (_activeRenderers == null || _activeRenderers.Length == 0) return;

            CalculateSunBounds(out var sunBounds, out var sunCenter);
            
            var cameraPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            
            var updatedArgs = args;
            updatedArgs.SunScale = sunBounds.size.magnitude;
            updatedArgs.SunCenterWS = sunCenter;
            updatedArgs.CameraDistanceWS = Vector3.Distance(cameraPos, sunCenter);

            foreach (var r in _activeRenderers)
            {
                if (r == null) continue;

                var currentMats = r.sharedMaterials;
                if (currentMats == null || currentMats.Length == 0) continue;

                CalculateRendererBounds(r, out var boundsMin, out var boundsMax);
                updatedArgs.BoundsMin = boundsMin;
                updatedArgs.BoundsMax = boundsMax;

                foreach (var m in currentMats)
                {
                    if (m == null || m.shader != Shader) continue;

                    ApplyArgsAutomatic(m, updatedArgs);
                }
            }
        }

        private void RemoveUpdater()
        {   // Clean up updater when no longer needed
            
            if (_updater == null) return;

            RestoreMaterials();
            _activeSunName = null;
            _activeRenderers = Array.Empty<Renderer>();
        }

        protected override void ApplyArgsToMaterial(Material mat, Args args) { }

        protected override Material CreateCustomMaterial(Material original, Args args)
        {   // Create new material using sun shader
            if (Shader == null)
                return original != null ? new Material(original) : null;

            var mat = new Material(Shader);
            if (original?.HasProperty("_MainTex") == true && mat.HasProperty("_MainTex"))
                mat.mainTexture = original.mainTexture;

            ApplyArgsAutomatic(mat, args);
            return mat;
        }

        private sealed class SunLiveUpdater : MonoBehaviour
        {
            private SunShaderModule _module;
            private Args _baseArgs;

            internal void SetModuleAndBaseArgs(SunShaderModule module, Args args)
            {   // Assign module and arguments for continuous updates
                _module = module;
                _baseArgs = args;
            }

            private void LateUpdate()
            {   // Update shader each frame if module is active
                if (_module != null)
                    _module.Tick(_baseArgs);
            }
        }
    }
}
