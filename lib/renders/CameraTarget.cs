#nullable enable

namespace FrameEmbededState
{
    using UnityEngine;
    using FrameEmbededState.Lib.Renders;

    public static class Inclusive_RenderActive
    {
        public static bool Value;
    }
    public static class CurrentScreenShader
    {
        public static Shader? Value;
    }
    public static class CurrentUiShader
    {
        public static Shader? Value;
    }

    public static class Exclusive_Render
    {
        public static RenderTexture? UiBackgroundTexture;
        public static readonly int GlobalUiBackgroundTexId = Shader.PropertyToID("_FrameEmbededState_UIBackgroundTex");

        public static Texture? UiBlurOverrideTexture
        {
            get => _uiBlurOverrideTexture;
            set => _uiBlurOverrideTexture = value;
        }
        private static Texture? _uiBlurOverrideTexture;

        private static Material? _uiMat;
        private static Shader? _selectedShader;

        public static RenderTexture? RenderUIBackground(RenderTexture sceneTexture, Shader? selectedShader)
        {   // Render UI background using the selected shader, preserving alpha

            Debug.Log($"[Exclusive_Render] RenderUIBackground called. sceneTexture: {(sceneTexture != null ? sceneTexture.name : "null")}, selectedShader: {(selectedShader != null ? selectedShader.name : "null")}");

            if (sceneTexture == null)
                return null;

            EnsureUIRenderTexture(sceneTexture);

            Debug.Log($"[Exclusive_Render] Setting CurrentUiShader.Value = {selectedShader?.name ?? "null"}");

            CurrentUiShader.Value = selectedShader;
            Inclusive_RenderActive.Value = selectedShader != null;

            if (selectedShader != _selectedShader)
            {
                Debug.Log($"[Exclusive_Render] Shader changed. Old: {_selectedShader?.name ?? "null"}, New: {selectedShader?.name ?? "null"}");
                DestroyUiMaterial();
                _selectedShader = selectedShader;
                _uiMat = _selectedShader != null ? new(_selectedShader) : null;

                // Apply arguments to the new material if available
                if (_uiMat != null && OverlayDispatcher.SelectedModule != null && OverlayDispatcher.CurrentArgs != null)
                    OverlayDispatcher.SelectedModule.ApplyArgs(_uiMat, OverlayDispatcher.CurrentArgs);
            }

            // Clear the render texture to prevent old frames from persisting
            if (UiBackgroundTexture != null)
            {
                var prev = RenderTexture.active;
                RenderTexture.active = UiBackgroundTexture;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = prev;
            }

            if (_uiMat != null)
                Graphics.Blit(sceneTexture, UiBackgroundTexture, _uiMat);
            else
                Graphics.Blit(sceneTexture, UiBackgroundTexture);

            Shader.SetGlobalTexture(GlobalUiBackgroundTexId, UiBackgroundTexture);

            UiBlurOverrideTexture = UiBackgroundTexture;

            Debug.Log($"[Exclusive_Render] UI background rendered and global texture set.");

            return UiBackgroundTexture;
        }

        public static void Release()
        {   // Release all resources and reset state
            Debug.Log("[Exclusive_Render] Release called.");

            DestroyUiMaterial();

            CurrentUiShader.Value = null;
            Inclusive_RenderActive.Value = false;

            if (UiBackgroundTexture != null)
            {
                UiBackgroundTexture.Release();
                Object.Destroy(UiBackgroundTexture);
                UiBackgroundTexture = null;
            }

            Shader.SetGlobalTexture(GlobalUiBackgroundTexId, (Texture?)null);
            UiBlurOverrideTexture = null;
        }

        private static void EnsureUIRenderTexture(RenderTexture src)
        {   // Ensure the UI background RT matches the source
            var desc = src.descriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.colorFormat = RenderTextureFormat.ARGB32;

            bool needsRebuild =
                UiBackgroundTexture == null ||
                UiBackgroundTexture.width != desc.width ||
                UiBackgroundTexture.height != desc.height ||
                UiBackgroundTexture.format != desc.colorFormat;

            if (!needsRebuild)
                return;

            if (UiBackgroundTexture != null)
            {
                UiBackgroundTexture.Release();
                Object.Destroy(UiBackgroundTexture);
            }

            UiBackgroundTexture = new(desc)
            {
                name = "FrameEmbededState_UIBackground",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            UiBackgroundTexture.Create();

            Debug.Log($"[Exclusive_Render] Created new UiBackgroundTexture: {UiBackgroundTexture.width}x{UiBackgroundTexture.height}");
        }

        private static void DestroyUiMaterial()
        {   // Destroy the UI material if it exists
            if (_uiMat != null)
            {
                Object.Destroy(_uiMat);
                _uiMat = null;
                Debug.Log("[Exclusive_Render] Destroyed UI material.");
            }
        }
    }

    public static class BehindUI_Render
    {
        private static Material? _sceneMat;
        private static Shader? _selectedShader;

        public static void RenderScene(RenderTexture src, RenderTexture dest, Shader? selectedShader)
        {   // Render the scene behind UI, applying shader if provided

            Debug.Log($"[BehindUI_Render] RenderScene called. src: {(src != null ? src.name : "null")}, dest: {(dest != null ? dest.name : "null")}, selectedShader: {(selectedShader != null ? selectedShader.name : "null")}");

            if (src == null || dest == null)
            {   // Warn if destination is missing so rendering will not occur
                if (dest == null)
                    Debug.LogWarning("[BehindUI_Render] WARNING: Destination RenderTexture (dest) is null. Scene will not be rendered.");
                return;
            }

            CurrentScreenShader.Value = selectedShader;

            if (selectedShader != _selectedShader || _sceneMat == null)
            {   // Shader or material changed, rebuild
                Debug.Log($"[BehindUI_Render] Shader changed. Old: {_selectedShader?.name ?? "null"}, New: {selectedShader?.name ?? "null"}");
                DestroySceneMaterial();
                _selectedShader = selectedShader;
                _sceneMat = selectedShader != null ? new(selectedShader) : null;

                // Apply arguments to the new material if available
                if (_sceneMat != null && OverlayDispatcher.SelectedModule != null && OverlayDispatcher.CurrentArgs != null)
                    OverlayDispatcher.SelectedModule.ApplyArgs(_sceneMat, OverlayDispatcher.CurrentArgs);
            }

            if (_sceneMat != null)
                Graphics.Blit(src, dest, _sceneMat);
            else
                Graphics.Blit(src, dest);

            Debug.Log("[BehindUI_Render] Scene rendered.");
        }

        public static void Release()
            => DestroySceneMaterial();

        private static void DestroySceneMaterial()
        {   // Destroy the scene material if it exists
            if (_sceneMat != null)
            {
                Object.Destroy(_sceneMat);
                _sceneMat = null;
                Debug.Log("[BehindUI_Render] Destroyed scene material.");
            }
        }
    }


    public static class Inclusive_Render
    {
        public static void Render(RenderTexture src, RenderTexture dest, Shader? selectedShader)
        {   // Inclusive = apply shader to both scene output and UI background in parallel

            Debug.Log($"[Inclusive_Render] Render called. src: {(src != null ? src.name : "null")}, dest: {(dest != null ? dest.name : "null")}, selectedShader: {(selectedShader != null ? selectedShader.name : "null")}");

            // Always update UI background in parallel
            Exclusive_Render.RenderUIBackground(src, selectedShader);

            // Always write to dest (or screen if dest is null), using shader if provided
            if (selectedShader != null)
            {
                // Use a temporary material for the blit
                var mat = new UnityEngine.Material(selectedShader);

                // Apply arguments to the temporary material if available
                if (OverlayDispatcher.SelectedModule != null && OverlayDispatcher.CurrentArgs != null)
                    OverlayDispatcher.SelectedModule.ApplyArgs(mat, OverlayDispatcher.CurrentArgs);

                Graphics.Blit(src, dest, mat);
                UnityEngine.Object.Destroy(mat);
            }
            else
                Graphics.Blit(src, dest);

            Debug.Log("[Inclusive_Render] Both UI background and scene rendered.");
        }

        public static void Release()
        {   // Release all resources for inclusive render
            Debug.Log("[Inclusive_Render] Release called.");
            BehindUI_Render.Release();
            Exclusive_Render.Release();
        }
    }

    public static class CustomRender_Render
    {
        private static string _currentModuleKey;
        private static IShaderModule _currentModule;

        public static void Render(RenderTexture src, RenderTexture dest, string moduleKey, Shader? overrideShader = null)
        {
            Debug.Log($"[CustomRender_Render] Render called. Module: {moduleKey}, Shader: {overrideShader?.name ?? "null"}");

            if (string.IsNullOrEmpty(moduleKey))
            {
                Debug.LogError("[CustomRender_Render] Module key is null or empty");
                Graphics.Blit(src, dest);
                return;
            }

            var module = ShaderRegistry.Get(moduleKey);
            if (module == null)
            {
                Debug.LogError($"[CustomRender_Render] Module '{moduleKey}' not found in registry");
                Graphics.Blit(src, dest);
                return;
            }

            _currentModuleKey = moduleKey;
            _currentModule = module;

            if (overrideShader != null)
            {
                var shaderProp = module.GetType().GetProperty("_shader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (shaderProp != null && shaderProp.CanWrite)
                    shaderProp.SetValue(module, overrideShader);
            }

            var argsType = module.GetType().BaseType?.GetGenericArguments()[0];
            if (argsType != null)
            {
                var args = OverlayDispatcher.CurrentArgs ?? System.Activator.CreateInstance(argsType);
                
                OverlayDispatcher.SelectedModule = module;
                OverlayDispatcher.CurrentArgs = args;

                var runMethod = module.GetType().GetMethod("Run");
                if (runMethod != null)
                {
                    Debug.Log($"[CustomRender_Render] Invoking Run on module '{moduleKey}'");
                    runMethod.Invoke(module, new[] { args });
                }
                else
                    Debug.LogError($"[CustomRender_Render] Run method not found on module '{moduleKey}'");
            }

            Graphics.Blit(src, dest);
        }

        public static void Release()
        {
            Debug.Log("[CustomRender_Render] Release called");
            
            if (_currentModule != null && !string.IsNullOrEmpty(_currentModuleKey))
            {
                var restoreMethod = _currentModule.GetType().GetMethod("RestoreMaterials");
                restoreMethod?.Invoke(_currentModule, null);
            }

            _currentModule = null;
            _currentModuleKey = null;
        }
    }

    [UnityEngine.DisallowMultipleComponent]
    public sealed class FrameEmbededStateOverlayEffect : UnityEngine.MonoBehaviour
    {
        [UnityEngine.Header("UI Background (Exclusive)")]
        public Shader? selectedShader;

        public OverlayRenderMode renderMode = OverlayRenderMode.BehindUI;

        [UnityEngine.Header("Custom Render Settings")]
        public string customRenderKey = "AtmoShader";

        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {
            Debug.Log($"[FrameEmbededStateOverlayEffect] OnRenderImage called. Mode: {renderMode}, Key: {customRenderKey}");

            switch (renderMode)
            {
                case OverlayRenderMode.BehindUI:
                    FrameEmbededState.CurrentUiShader.Value = null;
                    FrameEmbededState.Exclusive_Render.UiBlurOverrideTexture = null;
                    FrameEmbededState.Inclusive_RenderActive.Value = false;
                    BehindUI_Render.RenderScene(src, dest, selectedShader);
                    break;

                case OverlayRenderMode.Exclusive:
                    Exclusive_Render.RenderUIBackground(src, selectedShader);
                    Exclusive_Render.UiBlurOverrideTexture = Exclusive_Render.UiBackgroundTexture;
                    if (dest != null) Graphics.Blit(src, dest);
                    break;

                case OverlayRenderMode.Inclusive:
                    FrameEmbededState.CurrentUiShader.Value = selectedShader;
                    FrameEmbededState.Exclusive_Render.UiBlurOverrideTexture = null;
                    FrameEmbededState.Inclusive_RenderActive.Value = selectedShader != null;
                    Inclusive_Render.Render(src, dest, selectedShader);
                    Exclusive_Render.UiBlurOverrideTexture = Exclusive_Render.UiBackgroundTexture;
                    break;

                case OverlayRenderMode.CustomRender:
                    CustomRender_Render.Render(src, dest, customRenderKey, selectedShader);
                    break;

                default:
                    FrameEmbededState.CurrentUiShader.Value = null;
                    FrameEmbededState.Exclusive_Render.UiBlurOverrideTexture = null;
                    FrameEmbededState.Inclusive_RenderActive.Value = false;
                    if (dest != null) Graphics.Blit(src, dest);
                    break;
            }
        }

        private void OnDisable()
        {
            Inclusive_Render.Release();
            if (renderMode == OverlayRenderMode.CustomRender)
                CustomRender_Render.Release();
        }
    }
}