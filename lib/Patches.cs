using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using FrameEmbededState;
using System.Linq; // Fix: for ShaderAssetRegistry

public static class ShaderAssetRegistry
{   // Runtime registry for discovered shaders and compute shaders
    private static readonly Dictionary<string, Shader> _shaders = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ComputeShader> _computeShaders = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryGetShader(string key, out Shader shader)
    {
        shader = null;
        if (string.IsNullOrWhiteSpace(key)) return false;
        return _shaders.TryGetValue(key, out shader);
    }

    public static bool TryGetCompute(string key, out ComputeShader shader)
    {
        shader = null;
        if (string.IsNullOrWhiteSpace(key)) return false;
        return _computeShaders.TryGetValue(key, out shader);
    }

    public static void Register(Shader shader, params string[] extraKeys)
    {   // Register a Shader with multiple keys
        if (!shader) return;
        _shaders[shader.name] = shader;
        var last = LastSegment(shader.name);
        if (!string.IsNullOrEmpty(last)) _shaders[last] = shader;
        if (extraKeys != null)
            foreach (var k in extraKeys)
                if (!string.IsNullOrWhiteSpace(k)) _shaders[k] = shader;
    }

    public static void Register(ComputeShader shader, params string[] extraKeys)
    {   // Register a ComputeShader with multiple keys
        if (!shader) return;
        _computeShaders[shader.name] = shader;
        if (extraKeys != null)
            foreach (var k in extraKeys)
                if (!string.IsNullOrWhiteSpace(k)) _computeShaders[k] = shader;
    }

    private static string LastSegment(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int idx = s.LastIndexOf('/');
        return (idx >= 0 && idx + 1 < s.Length) ? s.Substring(idx + 1) : s;
    }
}

namespace FrameEmbededState.Lib
{
    // Back-compat: keep your existing static registries.
    public static class Patches
    {
        private static bool _applied;

        public static void ApplyAll()
        {   // Apply all Harmony patches for the library
            if (_applied) return;

            var harmony = new Harmony("FrameEmbededState.AllPatches");

            OverlaySortingPatches.Apply(harmony);
            SfsPartShaderPatches.Apply(harmony);

            AssetBundleLoadHooks.Apply(harmony);

            _applied = true;
        }
    }

    internal static class AssetBundleLoadHooks
    {
        private static readonly HashSet<MethodBase> _patched = new();
        private static bool _installed;

        private static FieldInfo[] _shaderFields;

        public static void Apply(Harmony harmony)
        {   // Patch all AssetBundle load entrypoints using reflection
            if (_installed) return;
            _installed = true;

            _shaderFields = typeof(ShaderRegistry).GetFields(BindingFlags.Public | BindingFlags.Static);

            var abType =
                Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule")
                ?? typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.AssetBundle");

            if (abType == null) return;

            var methods = abType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];
                if (m == null) continue;

                if (!m.Name.StartsWith("LoadFrom", StringComparison.Ordinal)) continue;

                var rt = m.ReturnType;
                if (rt.FullName != "UnityEngine.AssetBundle" && rt.FullName != "UnityEngine.AssetBundleCreateRequest") continue;

                if (_patched.Add(m))
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(AssetBundleLoadHooks), nameof(LoadFrom_Postfix)));
            }
        }

        public static void LoadFrom_Postfix(object __result)
        {   // Handle AssetBundle and AssetBundleCreateRequest via reflection
            if (__result == null) return;

            var abType = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule");
            var abReqType = Type.GetType("UnityEngine.AssetBundleCreateRequest, UnityEngine.AssetBundleModule");

            if (abType != null && abType.IsInstanceOfType(__result))
            {
                ScanRegisterAndBind_Reflection(__result, abType);
                return;
            }

            if (abReqType != null && abReqType.IsInstanceOfType(__result))
            {
                var req = __result;
                var isDoneProp = abReqType.GetProperty("isDone");
                var assetBundleProp = abReqType.GetProperty("assetBundle");
                var completedEvent = abReqType.GetEvent("completed");

                if (isDoneProp != null && (bool)isDoneProp.GetValue(req))
                {
                    if (assetBundleProp != null)
                        ScanRegisterAndBind_Reflection(assetBundleProp.GetValue(req), abType);
                }
                else if (completedEvent != null)
                {
                    Action<AsyncOperation> handler = op => OnBundleCreateCompleted(op);
                    var handlerDelegate = Delegate.CreateDelegate(completedEvent.EventHandlerType, handler.Target, handler.Method);
                    completedEvent.AddEventHandler(req, handlerDelegate);
                }
            }
        }

        private static void OnBundleCreateCompleted(AsyncOperation op)
        {   // Reflection: get assetBundle from AssetBundleCreateRequest
            var abReqType = Type.GetType("UnityEngine.AssetBundleCreateRequest, UnityEngine.AssetBundleModule");
            var abType = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule");
            if (abReqType == null || op == null || !abReqType.IsInstanceOfType(op)) return;

            var assetBundleProp = abReqType.GetProperty("assetBundle");
            if (assetBundleProp != null)
                ScanRegisterAndBind_Reflection(assetBundleProp.GetValue(op), abType);
        }

        private static void ScanRegisterAndBind_Reflection(object bundleObj, Type abType)
        {   // Use reflection to call LoadAllAssets<Shader>()
            if (bundleObj == null || abType == null || !abType.IsInstanceOfType(bundleObj)) return;

            var loadAllAssetsShader = abType.GetMethod("LoadAllAssets", new Type[] { typeof(Type) });
            if (loadAllAssetsShader == null) return;

            Shader[] shaders = Array.Empty<Shader>();

            try
            {
                var shaderObjs = loadAllAssetsShader.Invoke(bundleObj, new object[] { typeof(Shader) }) as UnityEngine.Object[];
                if (shaderObjs != null)
                    shaders = Array.ConvertAll(shaderObjs, o => o as Shader);
            }
            catch { }

            for (int i = 0; i < shaders.Length; i++)
            {
                var s = shaders[i];
                if (!s) continue;
                ShaderAssetRegistry.Register(s);
            }

            BindShaderFields(shaders);
        }

        private static void BindShaderFields(Shader[] shaders)
        {
            if (shaders == null || shaders.Length == 0 || _shaderFields == null) return;

            for (int f = 0; f < _shaderFields.Length; f++)
            {
                var field = _shaderFields[f];
                if (field == null || field.FieldType != typeof(Shader)) continue;

                var fieldName = field.Name;

                for (int i = 0; i < shaders.Length; i++)
                {
                    var sh = shaders[i];
                    if (!sh) continue;

                    // Shader.name is ShaderLab name (often "Category/Sub/Name").
                    // Match against full name, last segment, and "Shader" suffix variations.
                    if (ShaderFieldMatches(fieldName, sh.name))
                    {
                        field.SetValue(null, sh);
                        break;
                    }
                }
            }
        }

        private static bool NameMatches(string fieldName, string assetName)
        {
            return string.Equals(fieldName, assetName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShaderFieldMatches(string fieldName, string shaderLabName)
        {
            if (string.IsNullOrEmpty(fieldName) || string.IsNullOrEmpty(shaderLabName))
                return false;

            // 1) exact match vs full shader name
            if (string.Equals(fieldName, shaderLabName, StringComparison.OrdinalIgnoreCase))
                return true;

            // 2) match vs last segment after '/'
            var last = LastSegment(shaderLabName);
            if (string.Equals(fieldName, last, StringComparison.OrdinalIgnoreCase))
                return true;

            // 3) allow field name to have "Shader" suffix (ExampleShader -> Example)
            var trimmed = TrimSuffix(fieldName, "Shader");
            if (!string.Equals(trimmed, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(trimmed, shaderLabName, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(trimmed, last, StringComparison.OrdinalIgnoreCase)) return true;
            }

            // 4) allow shader name to end with field name (Hidden/ExampleShader)
            if (shaderLabName.EndsWith(fieldName, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static string LastSegment(string s)
        {
            int idx = s.LastIndexOf('/');
            return (idx >= 0 && idx + 1 < s.Length) ? s.Substring(idx + 1) : s;
        }

        private static string TrimSuffix(string s, string suffix)
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return s.Substring(0, s.Length - suffix.Length);
            return s;
        }
    }

    internal static class OverlaySortingPatches
    {
        private static PropertyInfo _tiMaterialProp;
        private static FieldInfo _tiMaterialField;
        private static PropertyInfo _tiMaterialForRenderingProp;
        private static Type _tiType;
        private static int _blurTexPropId = Shader.PropertyToID("_BlurTex");
        private static int _cropRegionPropId = Shader.PropertyToID("_CropRegion");

        public static void Apply(Harmony harmony)
        {   // Patch overlay sorting and TranslucentImage for UI shader override
            var rsmType = AccessTools.TypeByName("SFS.RenderSortingManager");
            var getQueue = AccessTools.Method(rsmType, "GetRenderQueue");
            if (getQueue != null)
                harmony.Patch(getQueue, postfix: new HarmonyMethod(typeof(OverlaySortingPatches), nameof(GetRenderQueue_Postfix)));

            var rsmModuleType = AccessTools.TypeByName("SFS.RenderSortingModule");
            var startMethod = AccessTools.Method(rsmModuleType, "Start");
            if (startMethod != null)
                harmony.Patch(startMethod, postfix: new HarmonyMethod(typeof(OverlaySortingPatches), nameof(RenderSortingModule_Start_Postfix)));

            _tiType = AccessTools.TypeByName("TranslucentImage.TranslucentImage");
            if (_tiType != null)
            {
                _tiMaterialForRenderingProp = _tiType.GetProperty("materialForRendering", BindingFlags.Public | BindingFlags.Instance);
                _tiMaterialProp = _tiType.GetProperty("material", BindingFlags.Public | BindingFlags.Instance);
                if (_tiMaterialProp == null)
                    _tiMaterialField = _tiType.GetField("material", BindingFlags.NonPublic | BindingFlags.Instance);

                var lateUpdate = AccessTools.Method(_tiType, "LateUpdate");
                if (lateUpdate != null)
                    harmony.Patch(lateUpdate, prefix: new HarmonyMethod(typeof(OverlaySortingPatches), nameof(TranslucentImage_LateUpdate_Prefix)));
            }
        }

        public static void GetRenderQueue_Postfix(ref int __result, string layer)
        {
            if (layer == "OverlayOnTop") __result = 32767;
            else if (layer == "UIOverlayInclusive") __result = 3100;
        }

        public static void RenderSortingModule_Start_Postfix(object __instance)
        {
            var selectedLayerField = AccessTools.Field(__instance.GetType(), "selectedLayer");
            var renderQueueField = AccessTools.Field(__instance.GetType(), "renderQueue");

            var selectedLayer = selectedLayerField?.GetValue(__instance) as string;
            if (renderQueueField == null) return;

            if (selectedLayer == "OverlayOnTop") renderQueueField.SetValue(__instance, 32767);
            else if (selectedLayer == "UIOverlayInclusive") renderQueueField.SetValue(__instance, 3100);
        }

        public static bool TranslucentImage_LateUpdate_Prefix(object __instance)
        {   // Patch TranslucentImage.LateUpdate for exclusive rendering by setting the processed render texture as _BlurTex and _CropRegion

            var matProp = _tiMaterialForRenderingProp ?? _tiMaterialProp;
            Material mat = matProp?.GetValue(__instance) as Material ?? _tiMaterialField?.GetValue(__instance) as Material;
            if (mat == null)
                return true;

            Shader shader = FrameEmbededState.CurrentUiShader.Value;
            Texture uiTex = FrameEmbededState.Exclusive_Render.UiBlurOverrideTexture;

            if (!shader || !uiTex)
                return true;

            // Set the processed render texture as the blur background for the UI shader
            mat.SetTexture(_blurTexPropId, uiTex);

            // Fullscreen processed texture => full crop region
            if (mat.HasProperty(_cropRegionPropId))
                mat.SetVector(_cropRegionPropId, new Vector4(0f, 0f, 1f, 1f));

            // Mark UI material dirty so CanvasRenderer updates
            var t = __instance.GetType();
            while (t != null)
            {
                var m = t.GetMethod("SetMaterialDirty", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (m != null) { m.Invoke(__instance, null); break; }
                t = t.BaseType;
            }

            return false;
        }
    }

    internal static class SfsPartShaderPatches
    {
        private const string SfsPartShaderName = "SFS/Part";

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
            var sh = __instance?.shader;
            if (sh == null || sh.name != SfsPartShaderName) return;

            if (nameID == ColorTexId || nameID == MainTexId)
            {
                if (__instance.HasProperty(ColorMultId))
                    __instance.SetVector(ColorMultId, Vector4.one);
            }
        }

        public static void Material_GetTexture_Postfix(Material __instance, int nameID, ref Texture __result)
        {
            var sh = __instance?.shader;
            if (sh == null || sh.name != SfsPartShaderName) return;

            if ((nameID == ColorTexId || nameID == MainTexId) && __result == null)
                __result = Texture2D.whiteTexture;
        }
    }
}
