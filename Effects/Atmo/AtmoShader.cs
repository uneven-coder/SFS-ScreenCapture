using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using FrameEmbededState.Lib;
using FrameEmbededState.Lib.Renders;
using SFS.World;
using SFS.WorldBase;
using SFS.World.PlanetModules;

namespace FrameEmbededState.Effects.AtmoShader
{
    [ShaderModule("AtmoShader", ShaderType.Shader, "Hidden/FrameEmbededState/AtmoShader", OverlayRenderMode.CustomRender)]
    [ShaderDependency("CloudsShader")]
    [ShaderDependency("ScaledCloudsShader")]
    public class AtmoShaderModule : ObjectTargetShaderModule<AtmoShaderModule.Args, object>
    {
        public struct Args
        {
            [ShaderArg(group: "Atmosphere", property: "_PlanetRadius", defaultValue: 6371000f)] public float PlanetRadius;
            [ShaderArg(group: "Atmosphere", property: "_AtmosphereHeight", defaultValue: 60000f)] public float AtmosphereHeight;
            [ShaderArg(group: "Atmosphere", property: "_GradientMultiplier", defaultValue: 1f)] public float GradientMultiplier;
            [ShaderArg(group: "Atmosphere", property: "_GradientTex")] public Texture2D GradientTexture;
            [ShaderArg(group: "Atmosphere", property: "_AtmosphereScale", defaultValue: 1f)] public float AtmosphereScale;
            [ShaderArg(group: "Atmosphere", property: "_PlanetCenterWS")] public Vector3 PlanetCenterWS;
            [ShaderArg(group: "Lighting", property: "_SunDir")] public Vector3 SunDirection;
            [ShaderArg(group: "Lighting", property: "_SunColor")] public Color SunColor;
            [ShaderArg(group: "Physics", property: "_AtmosphereDensity", defaultValue: 3.4f)] public float AtmosphereDensity;
            [ShaderArg(group: "Physics", property: "_DensityCurve", defaultValue: 1.6f)] public float DensityCurve;
            [ShaderArg(group: "Physics", property: "_ScatterStrength", defaultValue: 0.9f)] public float ScatterStrength;
            [ShaderArg(group: "Physics", property: "_TerminatorWidth", defaultValue: 0.23f)] public float TerminatorWidth;
            [ShaderArg(group: "Physics", property: "_RefractiveIndex", defaultValue: 3f)] public float RefractiveIndex;
            [ShaderArg(group: "Clouds", autoApply: true)] public bool EnableClouds;
            [ShaderArg(group: "Clouds", autoApply: true)] public CloudsShader.CloudsShaderModule.Args CloudLayer;
            [ShaderArg(group: "Clouds", autoApply: true)] public ScaledCloudsShader.ScaledCloudsShaderModule.Args CloudLayerScaled;
        }

        private static AtmoLiveUpdater _updater;
        private string _activePlanetName;
        private Renderer[] _activeRenderers = Array.Empty<Renderer>();
        private CloudsShader.CloudsShaderModule _cloudsModule;
        private ScaledCloudsShader.ScaledCloudsShaderModule _scaledCloudsModule;
        private Camera _lastCamera;
        private float _lastCameraZoom;
        private Bounds _lastAtmoBounds;

        public AtmoShaderModule()
        {
            var cloudsModule = ShaderRegistry.Get("CloudsShader") as CloudsShader.CloudsShaderModule;
            if (cloudsModule != null) { _cloudsModule = cloudsModule; RegisterSubShader(_cloudsModule); }
            
            var scaledCloudsModule = ShaderRegistry.Get("ScaledCloudsShader") as ScaledCloudsShader.ScaledCloudsShaderModule;
            if (scaledCloudsModule != null) { _scaledCloudsModule = scaledCloudsModule; RegisterSubShader(_scaledCloudsModule); }
        }

        public override object Run(in Args args)
        {
            InitializeUpdater();
            _updater.SetModuleAndBaseArgs(this, args);
            return null;
        }

        private void InitializeUpdater()
        {
            if (_updater != null) return;

            var go = GameObject.Find("[FrameEmbededState] AtmoShaderLiveUpdater") ?? new GameObject("[FrameEmbededState] AtmoShaderLiveUpdater") { hideFlags = HideFlags.HideAndDontSave };
            _updater = go.GetComponent<AtmoLiveUpdater>() ?? go.AddComponent<AtmoLiveUpdater>();
        }

        internal void Tick(in Args baseArgs)
        {   // Update atmosphere shader every frame to handle camera changes and zoom
            
            if (Shader == null) { RemoveUpdater(); return; }

            var currentCamera = Camera.main;
            var currentZoom = currentCamera != null ? currentCamera.orthographicSize : 0f;
            var cameraChanged = currentCamera != _lastCamera || Mathf.Abs(currentZoom - _lastCameraZoom) > 0.01f;

            var dyn = BuildDynamicArgs(baseArgs, out var playerPlanetName, out var isScaledSpace);
            if (string.IsNullOrEmpty(playerPlanetName)) { RemoveUpdater(); return; }

            var needsReinit = _activePlanetName != playerPlanetName || 
                            _activeRenderers.Length == 0 || 
                            _activeRenderers.Any(r => r == null || r.sharedMaterials == null || r.sharedMaterials.Length == 0) ||
                            cameraChanged;

            if (needsReinit)
                ReinitializePlanetMaterials(playerPlanetName, dyn, isScaledSpace);
            else
                UpdateMaterialsWithArgs(dyn, isScaledSpace);

            _lastCamera = currentCamera;
            _lastCameraZoom = currentZoom;

            MainUi.UpdateShaderProvidedArgs("AtmoShader", dyn);
            MainUi.UpdateShaderProvidedArgs("CloudsShader", dyn.CloudLayer);
            MainUi.UpdateShaderProvidedArgs("ScaledCloudsShader", dyn.CloudLayerScaled);
        }

        private void ReinitializePlanetMaterials(string playerPlanetName, in Args args, bool isScaledSpace)
        {   // Reinitialize materials with atmosphere and cloud layer, calculate bounds-based scale
            _activePlanetName = playerPlanetName;
            RestoreMaterials();

            var atmospheres = UnityEngine.Object.FindObjectsOfType<Atmosphere>()
                .Where(a => a && a.planet && a.planet.name == playerPlanetName)
                .ToArray();

            _activeRenderers = atmospheres
                .Select(a => a.GetComponent<MeshRenderer>())
                .Where(mr => mr && mr.enabled && mr.gameObject.activeInHierarchy)
                .Cast<Renderer>()
                .ToArray();

            if (_activeRenderers.Length == 0) { RestoreMaterials(); return; }

            CalculateAtmosphereBounds(out var atmoBounds, out var planetCenter);
            _lastAtmoBounds = atmoBounds;

            var updatedArgs = args;
            updatedArgs.AtmosphereScale = atmoBounds.size.magnitude;
            updatedArgs.PlanetCenterWS = planetCenter;
            
            updatedArgs.CloudLayer.PlanetCenterWS = planetCenter;
            updatedArgs.CloudLayer.AtmosphereScale = atmoBounds.size.magnitude;
            updatedArgs.CloudLayerScaled.PlanetCenterWS = planetCenter;
            updatedArgs.CloudLayerScaled.AtmosphereScale = atmoBounds.size.magnitude;

            foreach (var r in _activeRenderers)
            {   // Create atmosphere and cloud materials based on scale mode
                var originalMats = r.sharedMaterials;
                var originalMat = originalMats.Length > 0 ? originalMats[0] : null;
                
                Material atmoMat = CreateAtmoMaterial(originalMat, updatedArgs);
                Material cloudMat = null;
                
                if (updatedArgs.EnableClouds)
                {
                    if (isScaledSpace && _scaledCloudsModule != null && _scaledCloudsModule.Shader != null)
                        cloudMat = _scaledCloudsModule.CreateMaterialWithArgs(originalMat, updatedArgs.CloudLayerScaled);
                    else if (!isScaledSpace && _cloudsModule != null && _cloudsModule.Shader != null)
                        cloudMat = _cloudsModule.CreateMaterialWithArgs(originalMat, updatedArgs.CloudLayer);
                }
                
                Material[] customMats = cloudMat != null ? new Material[] { atmoMat, cloudMat } : new Material[] { atmoMat };

                _originalMaterials[r] = originalMats;
                _customMaterials[r] = customMats;
                r.sharedMaterials = customMats;
            }
        }

        private void CalculateAtmosphereBounds(out Bounds combinedBounds, out Vector3 planetCenter)
        {   // Calculate combined bounds of all atmosphere renderers in world space
            
            combinedBounds = new Bounds();
            planetCenter = Vector3.zero;
            bool first = true;

            foreach (var r in _activeRenderers)
            {
                if (r == null) continue;

                var bounds = r.bounds;
                if (first) { combinedBounds = bounds; planetCenter = r.transform.position; first = false; }
                else combinedBounds.Encapsulate(bounds);
            }
        }

        private Material CreateAtmoMaterial(Material original, in Args args)
        {
            if (Shader == null) { Debug.LogWarning($"[AtmoShaderModule] Atmosphere shader not loaded. Cannot create material."); return original != null ? new Material(original) : null; }

            var mat = new Material(Shader) { name = $"CustomAtmo_{original?.name ?? "Material"}" };
            if (original?.HasProperty("_MainTex") == true && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", original.GetTexture("_MainTex"));

            ApplyArgsAutomatic(mat, args);
            return mat;
        }

        private void UpdateMaterialsWithArgs(in Args args, bool isScaledSpace)
        {
            if (_activeRenderers == null || _activeRenderers.Length == 0) return;

            CalculateAtmosphereBounds(out var atmoBounds, out var planetCenter);
            var updatedArgs = args;
            updatedArgs.AtmosphereScale = atmoBounds.size.magnitude;
            updatedArgs.PlanetCenterWS = planetCenter;
            
            updatedArgs.CloudLayer.PlanetCenterWS = planetCenter;
            updatedArgs.CloudLayer.AtmosphereScale = atmoBounds.size.magnitude;
            updatedArgs.CloudLayerScaled.PlanetCenterWS = planetCenter;
            updatedArgs.CloudLayerScaled.AtmosphereScale = atmoBounds.size.magnitude;

            foreach (var r in _activeRenderers)
            {
                if (r == null) continue;

                var currentMats = r.sharedMaterials;
                if (currentMats == null || currentMats.Length == 0)
                {   // Materials were cleared, trigger reinit
                    ReinitializePlanetMaterials(_activePlanetName, args, isScaledSpace);
                    return;
                }

                bool hasCloudMat = currentMats.Length > 1 && currentMats[1] != null;
                bool shouldHaveCloud = updatedArgs.EnableClouds;
                
                bool isCorrectScale = false;
                if (hasCloudMat)
                {
                    if (isScaledSpace) isCorrectScale = _scaledCloudsModule != null && currentMats[1].shader == _scaledCloudsModule.Shader;
                    else isCorrectScale = _cloudsModule != null && currentMats[1].shader == _cloudsModule.Shader;
                }

                if (shouldHaveCloud != hasCloudMat || (shouldHaveCloud && !isCorrectScale))
                {   // Recreate materials with correct scale module
                    var originalMat = _originalMaterials.ContainsKey(r) && _originalMaterials[r].Length > 0 ? _originalMaterials[r][0] : null;
                    
                    Material atmoMat = CreateAtmoMaterial(originalMat, updatedArgs);
                    Material cloudMat = null;
                    
                    if (shouldHaveCloud)
                    {
                        if (isScaledSpace && _scaledCloudsModule != null && _scaledCloudsModule.Shader != null)
                            cloudMat = _scaledCloudsModule.CreateMaterialWithArgs(originalMat, updatedArgs.CloudLayerScaled);
                        else if (!isScaledSpace && _cloudsModule != null && _cloudsModule.Shader != null)
                            cloudMat = _cloudsModule.CreateMaterialWithArgs(originalMat, updatedArgs.CloudLayer);
                    }
                    
                    Material[] newMats = cloudMat != null ? new Material[] { atmoMat, cloudMat } : new Material[] { atmoMat };
                    
                    _customMaterials[r] = newMats;
                    r.sharedMaterials = newMats;
                }
                else
                {   // Update existing materials
                    foreach (var m in currentMats)
                    {
                        if (m == null) continue;
                        
                        if (m.shader == Shader)
                            ApplyArgsAutomatic(m, updatedArgs);
                        else if (!isScaledSpace && _cloudsModule != null && m.shader == _cloudsModule.Shader)
                            _cloudsModule.UpdateMaterialArgs(m, updatedArgs.CloudLayer);
                        else if (isScaledSpace && _scaledCloudsModule != null && m.shader == _scaledCloudsModule.Shader)
                            _scaledCloudsModule.UpdateMaterialArgs(m, updatedArgs.CloudLayerScaled);
                    }
                }
            }
        }

        private Args BuildDynamicArgs(in Args baseArgs, out string playerPlanetName, out bool isScaledSpace)
        {   // Build dynamic arguments detecting scaled space from active camera type
            
            var a = baseArgs;
            playerPlanetName = null;
            isScaledSpace = false;

            var userOverrides = MainUi.GetUserArgs("AtmoShader");
            var mergedArgs = MainUi.GetCurrentArgs("AtmoShader") as Args?;
            if (mergedArgs.HasValue) a = mergedArgs.Value;

            Planet playerPlanet = null;
            try { playerPlanet = PlayerController.main.player.Value.location.planet.Value; playerPlanetName = playerPlanet?.name; }
            catch { }

            bool Edited(string key) => userOverrides?.ContainsKey(key) ?? false;

            // Detect scaled space from active camera
            var cameraManager = GameCamerasManager.main;
            if (cameraManager != null)
            {   // Check which camera is currently active
                var activeCamera = Camera.main;
                isScaledSpace = cameraManager.scaledWorld_Camera != null && 
                               cameraManager.scaledWorld_Camera.camera == activeCamera;
            }

            // Initialize cloud layer defaults from shader modules if not already initialized
            bool needsCloudDefaults = a.CloudLayer.CloudRaymarchSteps == 0;
            bool needsScaledCloudDefaults = a.CloudLayerScaled.CloudRaymarchSteps == 0;

            if (needsCloudDefaults && _cloudsModule != null)
            {   // Load defaults from CloudsShader module and register them
                var defaultCloudArgs = GetDefaultArgsFromModule(_cloudsModule);
                if (defaultCloudArgs != null)
                {
                    a.CloudLayer = (CloudsShader.CloudsShaderModule.Args)defaultCloudArgs;
                    MainUi.UpdateShaderProvidedArgs("CloudsShader", a.CloudLayer);
                }
            }

            if (needsScaledCloudDefaults && _scaledCloudsModule != null)
            {   // Load defaults from ScaledCloudsShader module and register them
                var defaultScaledArgs = GetDefaultArgsFromModule(_scaledCloudsModule);
                if (defaultScaledArgs != null)
                {
                    a.CloudLayerScaled = (ScaledCloudsShader.ScaledCloudsShaderModule.Args)defaultScaledArgs;
                    MainUi.UpdateShaderProvidedArgs("ScaledCloudsShader", a.CloudLayerScaled);
                }
            }

            // Apply merged cloud args from UI if they exist
            var cloudMergedArgs = MainUi.GetCurrentArgs("CloudsShader") as CloudsShader.CloudsShaderModule.Args?;
            var scaledCloudMergedArgs = MainUi.GetCurrentArgs("ScaledCloudsShader") as ScaledCloudsShader.ScaledCloudsShaderModule.Args?;

            if (cloudMergedArgs.HasValue) a.CloudLayer = cloudMergedArgs.Value;
            if (scaledCloudMergedArgs.HasValue) a.CloudLayerScaled = scaledCloudMergedArgs.Value;

            // Sync shared properties from atmosphere to cloud layers
            a.CloudLayer.PlanetRadius = a.PlanetRadius;
            a.CloudLayer.SunDirection = a.SunDirection;
            a.CloudLayer.SunColor = a.SunColor;
            
            a.CloudLayerScaled.PlanetRadius = a.PlanetRadius;
            a.CloudLayerScaled.SunDirection = a.SunDirection;
            a.CloudLayerScaled.SunColor = a.SunColor;

            if (playerPlanet != null && playerPlanet.HasAtmosphereVisuals)
            {   // Extract atmosphere data from planet
                try
                {
                    var atmoMat = playerPlanet.atmosphereMaterial;
                    if (atmoMat != null) { a.GradientTexture = atmoMat.GetTexture("_GradientTex") as Texture2D; a.GradientMultiplier = atmoMat.GetFloat("_GradientMultiplier"); }
                }
                catch { }

                try { if (playerPlanet.data?.atmosphereVisuals?.GRADIENT != null) { a.AtmosphereHeight = (float)playerPlanet.data.atmosphereVisuals.GRADIENT.height; a.PlanetRadius = (float)playerPlanet.Radius; a.CloudLayer.PlanetRadius = a.PlanetRadius; a.CloudLayerScaled.PlanetRadius = a.PlanetRadius; } }
                catch { }
            }

            Planet sunPlanet = null;
            try { sunPlanet = UnityEngine.Object.FindObjectsOfType<Planet>().FirstOrDefault(p => p && p.codeName == "Sun"); }
            catch { }

            if (sunPlanet != null)
            {   // Update sun direction and color dynamically
                
                if (!Edited("SunDirection"))
                {
                    try { var sunLoc = sunPlanet.GetLocation(WorldTime.main.worldTime); var planetLoc = playerPlanet.GetLocation(WorldTime.main.worldTime); a.SunDirection = (planetLoc.position - sunLoc.position).normalized; }
                    catch { }
                }

                if (!Edited("SunColor"))
                {
                    try
                    {
                        var sunFog = sunPlanet.data?.atmosphereVisuals?.FOG;
                        if (sunFog?.keys != null && sunFog.keys.Length > 0) a.SunColor = sunFog.Evaluate(sunFog.keys[0].distance);
                        else if (sunPlanet.data?.postProcessing?.keys != null && sunPlanet.data.postProcessing.keys.Length > 0) { var pp = sunPlanet.data.postProcessing.Evaluate(0f); a.SunColor = new Color(pp.red, pp.green, pp.blue, 1f); }
                    }
                    catch { }
                }

                a.CloudLayer.SunDirection = a.SunDirection;
                a.CloudLayer.SunColor = a.SunColor;
                a.CloudLayerScaled.SunDirection = a.SunDirection;
                a.CloudLayerScaled.SunColor = a.SunColor;
            }

            return a;
        }

        private object GetDefaultArgsFromModule(IShaderModule module)
        {   // Extract default arguments from shader module using attributes and material properties
            
            var argsType = module.GetType().BaseType?.GetGenericArguments()[0];
            if (argsType == null) return null;

            var defaultArgs = Activator.CreateInstance(argsType);
            var fields = argsType.GetFields();

            Material tempMat = null;
            try { if (module.Shader != null) tempMat = new Material(module.Shader); }
            catch { }

            foreach (var field in fields)
            {   // Apply defaults from attributes or material properties
                var attr = Attribute.GetCustomAttribute(field, typeof(ShaderArgAttribute)) as ShaderArgAttribute;
                
                if (attr?.DefaultValue != null)
                    field.SetValue(defaultArgs, attr.DefaultValue);
                else if (tempMat != null)
                {   // Try reading from material properties
                    var propName = !string.IsNullOrEmpty(attr?.Property) ? attr.Property : "_" + field.Name;
                    
                    if (field.FieldType == typeof(float) && tempMat.HasProperty(propName))
                        field.SetValue(defaultArgs, tempMat.GetFloat(propName));
                    else if (field.FieldType == typeof(int) && tempMat.HasProperty(propName))
                        field.SetValue(defaultArgs, (int)tempMat.GetFloat(propName));
                    else if (field.FieldType == typeof(Color) && tempMat.HasProperty(propName))
                        field.SetValue(defaultArgs, tempMat.GetColor(propName));
                    else if (field.FieldType == typeof(Vector3) && tempMat.HasProperty(propName))
                    {
                        var v4 = tempMat.GetVector(propName);
                        field.SetValue(defaultArgs, new Vector3(v4.x, v4.y, v4.z));
                    }
                    else if (field.FieldType == typeof(Vector4) && tempMat.HasProperty(propName))
                        field.SetValue(defaultArgs, tempMat.GetVector(propName));
                }
            }

            if (tempMat != null) UnityEngine.Object.Destroy(tempMat);

            return defaultArgs;
        }

        private void RemoveUpdater()
        {   // Only remove updater when no materials are using the shader
            
            if (_updater == null) return;

            bool anyUsing = false;
            try { anyUsing = UnityEngine.Object.FindObjectsOfType<MeshRenderer>().Any(r => r && r.sharedMaterial && r.sharedMaterial.shader == Shader); }
            catch { }

            if (!anyUsing) { UnityEngine.Object.Destroy(_updater.gameObject); _updater = null; }
        }

        protected override void ApplyArgsToMaterial(Material mat, Args args) { }

        protected override Material CreateCustomMaterial(Material original, Args args) => CreateAtmoMaterial(original, args);

        private sealed class AtmoLiveUpdater : MonoBehaviour
        {
            private AtmoShaderModule _module;
            private Args _baseArgs;

            public void SetModuleAndBaseArgs(AtmoShaderModule module, Args baseArgs)
            { _module = module; _baseArgs = baseArgs; }

            private void Update()
            { if (_module == null) { Destroy(gameObject); return; } _module.Tick(_baseArgs); }
        }
    }
}

namespace FrameEmbededState.Effects.CloudsShader
{
    [ShaderModule("CloudsShader", ShaderType.Shader, "Hidden/FrameEmbededState/CloudsShader", OverlayRenderMode.CustomRender)]
    public class CloudsShaderModule : ObjectTargetShaderModule<CloudsShaderModule.Args, object>
    {
        public struct Args
        {
            [ShaderArg(group: "n-General", property: "_PlanetRadius", defaultValue: 6371000f)] public float PlanetRadius;
            [ShaderArg(group: "n-General", property: "_CloudStartHeight", defaultValue: 3000f)] public float CloudStartHeight;
            [ShaderArg(group: "n-General", property: "_CloudMaxHeight", defaultValue: 22000f)] public float CloudMaxHeight;
            [ShaderArg(group: "n-General", property: "_PlanetCenterWS")] public Vector3 PlanetCenterWS;
            [ShaderArg(group: "n-General", property: "_AtmosphereScale", defaultValue: 1f)] public float AtmosphereScale;
            [ShaderArg(group: "n-Appearance", property: "_CloudScale", defaultValue: 0.00056f)] public float CloudScale;
            [ShaderArg(group: "n-Appearance", property: "_CloudThreshold", defaultValue: 0.6f)] public float CloudThreshold;
            [ShaderArg(group: "n-Appearance", property: "_CloudDensity", defaultValue: 0.6f)] public float CloudDensity;
            [ShaderArg(group: "n-Appearance", property: "_CloudAlpha", defaultValue: 1.0f)] public float CloudAlpha;
            [ShaderArg(group: "n-Appearance", property: "_CloudCoverage", defaultValue: 0.04f)] public float CloudCoverage;
            [ShaderArg(group: "n-Appearance", property: "_CloudType", defaultValue: 0.3f)] public float CloudType;
            [ShaderArg(group: "n-Appearance", property: "_CloudSoftness", defaultValue: 0.3f)] public float CloudSoftness;
            [ShaderArg(group: "n-Animation", property: "_CloudScrollSpeed", defaultValue: 100f)] public float CloudScrollSpeed;
            [ShaderArg(group: "n-Animation", property: "_CloudMovementDirection", defaultValue: "(1,0,0)")] public Vector3 CloudMovementDirection;
            [ShaderArg(group: "n-Animation", property: "_CloudRotationAxis", defaultValue: "(5,0,1)")] public Vector3 CloudRotationAxis;
            [ShaderArg(group: "n-Animation", property: "_CloudRotationSpeed", defaultValue: 0.009f)] public float CloudRotationSpeed;
            [ShaderArg(group: "n-Detail", property: "_CloudDetailIntensity", defaultValue: 0.0f)] public float CloudDetailIntensity;
            [ShaderArg(group: "n-Detail", property: "_CloudThresholdVariation", defaultValue: 0.8f)] public float CloudThresholdVariation;
            [ShaderArg(group: "n-Detail", property: "_CloudThresholdNoiseScale", defaultValue: 0.00007f)] public float CloudThresholdNoiseScale;
            [ShaderArg(group: "n-Performance", property: "_CloudRaymarchSteps", defaultValue: 22)] public int CloudRaymarchSteps;
            [ShaderArg(group: "n-Performance", property: "_CloudLightSteps", defaultValue: 2)] public int CloudLightSteps;
            [ShaderArg(group: "n-Performance", property: "_CloudDepthFade", defaultValue: 85000f)] public float CloudDepthFade;
            [ShaderArg(group: "n-Performance", property: "_CloudDepthFadeSoftness", defaultValue: 900f)] public float CloudDepthFadeSoftness;
            [ShaderArg(group: "n-Lighting", property: "_CloudLightAbsorption", defaultValue: 1f)] public float CloudLightAbsorption;
            [ShaderArg(group: "n-Lighting", property: "_CloudAmbient", defaultValue: 0.7f)] public float CloudAmbient;
            [ShaderArg(group: "n-Lighting", property: "_CloudMultiScatter", defaultValue: 3.0f)] public float CloudMultiScatter;
            [ShaderArg(group: "n-Lighting", property: "_CloudBloom", defaultValue: 0.3f)] public float CloudBloom;
            [ShaderArg(group: "n-Lighting", property: "_SunDir")] public Vector3 SunDirection;
            [ShaderArg(group: "n-Lighting", property: "_SunColor")] public Color SunColor;
        }

        public CloudsShaderModule() { }

        public override object Run(in Args args) => null;

        public Material CreateMaterialWithArgs(Material original, in Args args)
        {
            if (Shader == null) { Debug.LogWarning($"[CloudsShaderModule] Cloud shader not loaded. Cannot create material."); return original != null ? new Material(original) : null; }

            var mat = new Material(Shader) { name = $"CustomClouds_{original?.name ?? "Material"}" };
            if (original?.HasProperty("_MainTex") == true && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", original.GetTexture("_MainTex"));

            ApplyArgsAutomatic(mat, args);
            return mat;
        }

        public void UpdateMaterialArgs(Material mat, in Args args)
        { if (mat != null && mat.shader == Shader) ApplyArgsAutomatic(mat, args); }

        protected override void ApplyArgsToMaterial(Material mat, Args args) { }

        protected override Material CreateCustomMaterial(Material original, Args args) => CreateMaterialWithArgs(original, args);
    }
}

namespace FrameEmbededState.Effects.ScaledCloudsShader
{
    [ShaderModule("ScaledCloudsShader", ShaderType.Shader, "Hidden/FrameEmbededState/CloudsShader", OverlayRenderMode.CustomRender)]
    public class ScaledCloudsShaderModule : ObjectTargetShaderModule<ScaledCloudsShaderModule.Args, object>
    {
        public struct Args
        {
            [ShaderArg(group: "S-General", property: "_PlanetRadius", defaultValue: 6371000f)] public float PlanetRadius;
            [ShaderArg(group: "S-General", property: "_CloudStartHeight", defaultValue: 3000f)] public float CloudStartHeight;
            [ShaderArg(group: "S-General", property: "_CloudMaxHeight", defaultValue: 22000f)] public float CloudMaxHeight;
            [ShaderArg(group: "S-General", property: "_PlanetCenterWS")] public Vector3 PlanetCenterWS;
            [ShaderArg(group: "S-General", property: "_AtmosphereScale", defaultValue: 1f)] public float AtmosphereScale;
            [ShaderArg(group: "S-Appearance", property: "_CloudScale", defaultValue: 0.0001f)] public float CloudScale;
            [ShaderArg(group: "S-Appearance", property: "_CloudThreshold", defaultValue: 0.92f)] public float CloudThreshold;
            [ShaderArg(group: "S-Appearance", property: "_CloudDensity", defaultValue: 33f)] public float CloudDensity;
            [ShaderArg(group: "S-Appearance", property: "_CloudAlpha", defaultValue: 1f)] public float CloudAlpha;
            [ShaderArg(group: "S-Appearance", property: "_CloudCoverage", defaultValue: 0.45f)] public float CloudCoverage;
            [ShaderArg(group: "S-Appearance", property: "_CloudType", defaultValue: 0.0f)] public float CloudType;
            [ShaderArg(group: "S-Appearance", property: "_CloudSoftness", defaultValue: 0.5f)] public float CloudSoftness;
            [ShaderArg(group: "S-Animation", property: "_CloudScrollSpeed", defaultValue: 100f)] public float CloudScrollSpeed;
            [ShaderArg(group: "S-Animation", property: "_CloudMovementDirection", defaultValue: "(1,0,0)")] public Vector3 CloudMovementDirection;
            [ShaderArg(group: "S-Animation", property: "_CloudRotationAxis", defaultValue: "(5,0,1)")] public Vector3 CloudRotationAxis;
            [ShaderArg(group: "S-Animation", property: "_CloudRotationSpeed", defaultValue: 0.009f)] public float CloudRotationSpeed;
            [ShaderArg(group: "S-Detail", property: "_CloudDetailIntensity", defaultValue: 0.0f)] public float CloudDetailIntensity;
            [ShaderArg(group: "S-Detail", property: "_CloudThresholdVariation", defaultValue: 1f)] public float CloudThresholdVariation;
            [ShaderArg(group: "S-Detail", property: "_CloudThresholdNoiseScale", defaultValue: 0.00004f)] public float CloudThresholdNoiseScale;
            [ShaderArg(group: "S-Performance", property: "_CloudRaymarchSteps", defaultValue: 11)] public int CloudRaymarchSteps;
            [ShaderArg(group: "S-Performance", property: "_CloudLightSteps", defaultValue: 3)] public int CloudLightSteps;
            [ShaderArg(group: "S-Performance", property: "_CloudDepthFade", defaultValue: 10f)] public float CloudDepthFade;
            [ShaderArg(group: "S-Performance", property: "_CloudDepthFadeSoftness", defaultValue: 15f)] public float CloudDepthFadeSoftness;
            [ShaderArg(group: "S-Lighting", property: "_CloudLightAbsorption", defaultValue: 0.2f)] public float CloudLightAbsorption;
            [ShaderArg(group: "S-Lighting", property: "_CloudAmbient", defaultValue: 0.8f)] public float CloudAmbient;
            [ShaderArg(group: "S-Lighting", property: "_CloudMultiScatter", defaultValue: 23f)] public float CloudMultiScatter;
            [ShaderArg(group: "S-Lighting", property: "_CloudBloom", defaultValue: 8f)] public float CloudBloom;
            [ShaderArg(group: "S-Lighting", property: "_SunDir")] public Vector3 SunDirection;
            [ShaderArg(group: "S-Lighting", property: "_SunColor")] public Color SunColor;
        }

        public ScaledCloudsShaderModule() { }

        public override object Run(in Args args) => null;

        public Material CreateMaterialWithArgs(Material original, in Args args)
        {
            if (Shader == null) { Debug.LogWarning($"[ScaledCloudsShaderModule] Scaled cloud shader not loaded. Cannot create material."); return original != null ? new Material(original) : null; }

            var mat = new Material(Shader) { name = $"CustomScaledClouds_{original?.name ?? "Material"}" };
            if (original?.HasProperty("_MainTex") == true && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", original.GetTexture("_MainTex"));

            ApplyArgsAutomatic(mat, args);
            return mat;
        }

        public void UpdateMaterialArgs(Material mat, in Args args)
        { if (mat != null && mat.shader == Shader) ApplyArgsAutomatic(mat, args); }

        protected override void ApplyArgsToMaterial(Material mat, Args args) { }

        protected override Material CreateCustomMaterial(Material original, Args args) => CreateMaterialWithArgs(original, args);
    }
}
