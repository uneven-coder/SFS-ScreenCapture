using HarmonyLib;
using UnityEngine;

namespace FrameEmbededState.Lib
{
    public static class Patches
    {   // Entry point for all Harmony patches for this mod

        private static bool _applied;

        public static void ApplyAll()
        {
            if (_applied)
                return;

            var harmony = new Harmony("FrameEmbededState.AllPatches");

            OverlaySortingPatches.Apply(harmony);
            SfsPartShaderPatches.Apply(harmony);
            AtmosphereRadialShaderPatches.Apply(harmony);

            _applied = true;
        }
    }

    internal static class OverlaySortingPatches
    {
        public static void Apply(Harmony harmony)
        {   // Patch SFS and TranslucentImage for overlay sorting and blur
            var rsmType = AccessTools.TypeByName("SFS.RenderSortingManager");
            var getQueue = AccessTools.Method(rsmType, "GetRenderQueue");
            if (getQueue != null)
                harmony.Patch(getQueue, postfix: new HarmonyMethod(typeof(OverlaySortingPatches), nameof(GetRenderQueue_Postfix)));

            var rsmModuleType = AccessTools.TypeByName("SFS.RenderSortingModule");
            var startMethod = AccessTools.Method(rsmModuleType, "Start");
            if (startMethod != null)
                harmony.Patch(startMethod, postfix: new HarmonyMethod(typeof(OverlaySortingPatches), nameof(RenderSortingModule_Start_Postfix)));

            var tiType = AccessTools.TypeByName("TranslucentImage.TranslucentImage");
            var lateUpdate = AccessTools.Method(tiType, "LateUpdate");
            if (lateUpdate != null)
                harmony.Patch(lateUpdate, prefix: new HarmonyMethod(typeof(OverlaySortingPatches), nameof(TranslucentImage_LateUpdate_Prefix)));
        }

        public static void GetRenderQueue_Postfix(ref int __result, string layer)
        {   // Patch render queue for overlay layers
            if (layer == "OverlayOnTop")
                __result = 32767;
            else if (layer == "UIOverlayInclusive")
                __result = 3100;
        }

        public static void RenderSortingModule_Start_Postfix(object __instance)
        {   // Patch SFS.RenderSortingModule.Start to set correct queue for overlay layers
            var selectedLayerField = AccessTools.Field(__instance.GetType(), "selectedLayer");
            var renderQueueField = AccessTools.Field(__instance.GetType(), "renderQueue");

            string selectedLayer = selectedLayerField?.GetValue(__instance) as string;

            if (selectedLayer == "OverlayOnTop" && renderQueueField != null)
                renderQueueField.SetValue(__instance, 32767);
            else if (selectedLayer == "UIOverlayInclusive" && renderQueueField != null)
                renderQueueField.SetValue(__instance, 3100);
        }

        public static bool TranslucentImage_LateUpdate_Prefix(object __instance)
        {   // Patch TranslucentImage.LateUpdate to override blur source with overlay output
            var matForRenderingProp = AccessTools.Property(__instance.GetType(), "materialForRendering");
            var mat = matForRenderingProp?.GetValue(__instance, null) as Material;
            if (mat == null)
                return true;

            var overlayTex = FrameEmbededState.VisualOverlayManager.UiBlurOverrideTexture;
            if (overlayTex != null)
            {
                int blurTexId = Shader.PropertyToID("_BlurTex");
                mat.SetTexture(blurTexId, overlayTex);

                int cropRegionId = Shader.PropertyToID("_CropRegion");
                mat.SetVector(cropRegionId, new Vector4(0, 0, 1, 1));
                return false;
            }

            return true;
        }
    }

    internal static class SfsPartShaderPatches
    {
        private static readonly int ColorTexId = Shader.PropertyToID("_ColorTex");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorMultId = Shader.PropertyToID("_ColorMult");
        
        public static void Apply(Harmony harmony)
        {
            var materialType = typeof(Material);
            
            var setTexture = AccessTools.Method(materialType, nameof(Material.SetTexture), new[] { typeof(int), typeof(Texture) });
            if (setTexture != null)
                harmony.Patch(setTexture, postfix: new HarmonyMethod(typeof(SfsPartShaderPatches), nameof(Material_SetTexture_Postfix)));

            var getTexture = AccessTools.Method(materialType, nameof(Material.GetTexture), new[] { typeof(int) });
            if (getTexture != null)
                harmony.Patch(getTexture, postfix: new HarmonyMethod(typeof(SfsPartShaderPatches), nameof(Material_GetTexture_Postfix)));
        }

        public static void Material_SetTexture_Postfix(Material __instance, int nameID, Texture value)
        {
            if (__instance == null || __instance.shader == null || __instance.shader.name != "SFS/Part")
                return;

            if (nameID == ColorTexId || nameID == MainTexId)
            {
                if (__instance.HasProperty(ColorMultId))
                    __instance.SetVector(ColorMultId, Vector4.one);
            }
        }

        public static void Material_GetTexture_Postfix(Material __instance, int nameID, ref Texture __result)
        {
            if (__instance == null || __instance.shader == null || __instance.shader.name != "SFS/Part")
                return;

            if ((nameID == ColorTexId || nameID == MainTexId) && __result == null)
                __result = Texture2D.whiteTexture;
        }
    }

    internal static class AtmosphereRadialShaderPatches
    {   // Harmony patch to inject modular atmosphere effect properties into SFS/Atmosphere shader

        private static readonly int PlanetCenterId = Shader.PropertyToID("_PlanetCenter");
        private static readonly int InnerRadiusId = Shader.PropertyToID("_InnerRadius");
        private static readonly int OuterRadiusId = Shader.PropertyToID("_OuterRadius");
        private static readonly int AtmosphereColorId = Shader.PropertyToID("_AtmosphereColor");
        private static readonly int AtmosphereIntensityId = Shader.PropertyToID("_AtmosphereIntensity");
        private static readonly int AtmosphereParamsId = Shader.PropertyToID("_AtmosphereParams");

        public static void Apply(Harmony harmony)
        {   // Patch MeshRenderer.SetPropertyBlock to inject modular atmosphere properties
            var setPropBlock = AccessTools.Method(typeof(Renderer), nameof(Renderer.SetPropertyBlock), new[] { typeof(MaterialPropertyBlock), typeof(int) });
            if (setPropBlock != null)
                harmony.Patch(setPropBlock, postfix: new HarmonyMethod(typeof(AtmosphereRadialShaderPatches), nameof(Renderer_SetPropertyBlock_Postfix)));
        }

        public static void Renderer_SetPropertyBlock_Postfix(Renderer __instance, MaterialPropertyBlock properties, int materialIndex)
        {   // Inject modular atmosphere effect properties for SFS/Atmosphere
            if (__instance == null || properties == null)
                return;

            var mats = __instance.sharedMaterials;
            if (mats == null || materialIndex < 0 || materialIndex >= mats.Length)
                return;

            var mat = mats[materialIndex];
            if (mat == null || mat.shader == null || mat.shader.name != "SFS/Atmosphere")
                return;

            var atmos = __instance.GetComponent<SFS.World.Atmosphere>();
            if (atmos == null || atmos.planet == null)
                return;

            var mesh = __instance.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
                return;

            var bounds = mesh.bounds;
            float scale = __instance.transform.lossyScale.x;
            float outerRadius = bounds.extents.x * scale;
            float innerRadius = outerRadius * 0.7f;
            Vector3 planetCenter = atmos.planet.transform.position;

            if (mat.HasProperty(PlanetCenterId))
                properties.SetVector(PlanetCenterId, planetCenter);

            if (mat.HasProperty(InnerRadiusId))
                properties.SetFloat(InnerRadiusId, innerRadius);

            if (mat.HasProperty(OuterRadiusId))
                properties.SetFloat(OuterRadiusId, outerRadius);

            // // Modular atmosphere effect properties
            // // These can be configured or extended for different effects
            // Color effectColor = new Color(0.5f, 0.7f, 1.0f, 1.0f); // Example: soft blue
            // float effectIntensity = 0.8f; // Example: moderate intensity
            // Vector4 effectParams = new Vector4(outerRadius, innerRadius, scale, 1.0f); // Example: custom params

            // if (mat.HasProperty(AtmosphereColorId))
            //     properties.SetColor(AtmosphereColorId, effectColor);

            // if (mat.HasProperty(AtmosphereIntensityId))
            //     properties.SetFloat(AtmosphereIntensityId, effectIntensity);

            // if (mat.HasProperty(AtmosphereParamsId))
            //     properties.SetVector(AtmosphereParamsId, effectParams);
        }
    }
}
