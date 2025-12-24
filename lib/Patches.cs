using HarmonyLib;
using UnityEngine;

namespace FrameEmbededState.Lib
{
    public static class Patches
    {   // Entry point for all Harmony patches for this mod

        private static bool _applied;

        public static void ApplyAll()
        {   // Apply all Harmony patches for FrameEmbededState
            if (_applied)
                return;

            var harmony = new Harmony("FrameEmbededState.AllPatches");

            // Overlay sorting and UI blur patches
            OverlaySortingPatches.Apply(harmony);

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
}
