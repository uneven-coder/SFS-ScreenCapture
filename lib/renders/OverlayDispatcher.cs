using UnityEngine;
using System;
using System.Reflection;

namespace FrameEmbededState.Lib.Renders
{
    // Minimal AccessTools for reflection (only what is needed)
    internal static class AccessTools
    {
        public static Type TypeByName(string name)
        {   // Find a type by its full name in loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }

        public static PropertyInfo Property(Type type, string name)
        {   // Get a property by name
            return type?.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }
    }

    public enum OverlayRenderMode
    {
        BehindUI,
        Inclusive,
        OnTop,
        Exclusive,
        CustomRender
    }

    public static class OverlayDispatcher
    {
        private static Camera _effectCamera;
        private static Shader _currentShader;
        private static Material _effectMaterial;
        private static OverlayImageEffect _attachedEffect;
        private static OverlayRenderMode _currentMode;

        // New: Static properties for selected module and current arguments
        public static IShaderModule SelectedModule { get; set; }
        public static object CurrentArgs { get; set; }
        public static Material CurrentMaterial => _effectMaterial;

        public static void Render(
            Camera cam,
            RenderTexture srcRT,
            RenderTexture dstRT,
            Shader selectedShader = null,
            System.Action<Unity.Collections.NativeArray<UnityEngine.Color32>, Unity.Collections.NativeArray<UnityEngine.Color32>, int, int> cpuEffect = null,
            OverlayRenderMode renderMode = OverlayRenderMode.BehindUI)
        {   // Dispatch rendering based on mode, ensuring Exclusive generates UI background RT
            
            if (selectedShader == null || cam == null)
            {
                RemoveCameraImageEffect();
                Inclusive_Render.Release();
                FrameEmbededState.CurrentUiShader.Value = null;
                return;
            }

            // Force reattachment if camera changed or effect was destroyed
            if (_effectCamera != cam || _attachedEffect == null || _currentMode != renderMode)
                RemoveCameraImageEffect();

            SetupCameraImageEffect(cam, selectedShader, renderMode);
        }

        private static void SetupCameraImageEffect(Camera cam, Shader shader, OverlayRenderMode mode)
        {   // Attach or update a MonoBehaviour to perform OnRenderImage blit

            if (_attachedEffect == null || _effectCamera != cam)
            {
                _attachedEffect = cam.gameObject.AddComponent<OverlayImageEffect>();
                _effectCamera = cam;
                _currentMode = mode;
            }

            bool needsSceneMat = mode == OverlayRenderMode.BehindUI || 
                                 mode == OverlayRenderMode.OnTop;

            if (needsSceneMat)
            {   // Create scene material for standard modes
                if (_effectMaterial == null || _currentShader != shader)
                {
                    if (_effectMaterial != null)
                        UnityEngine.Object.Destroy(_effectMaterial);

                    _effectMaterial = new Material(shader);
                    _currentShader = shader;

                    if (SelectedModule != null && CurrentArgs != null)
                        SelectedModule.ApplyArgs(_effectMaterial, CurrentArgs);
                }
            }
            else
            {   // CustomRender handles its own materials
                if (_effectMaterial != null)
                {
                    UnityEngine.Object.Destroy(_effectMaterial);
                    _effectMaterial = null;
                }
                _currentShader = shader;
            }

            _attachedEffect.Configure(mode, shader, _effectMaterial);
            _attachedEffect.enabled = true;

            Debug.Log($"[OverlayDispatcher] Shader '{shader?.name ?? "null"}' applied to camera '{cam.name}' with mode '{mode}'.");
        }

        private static void RemoveCameraImageEffect()
        {   // Remove the MonoBehaviour and material from the camera
            
            if (_attachedEffect != null)
            {
                if (_attachedEffect.gameObject != null)
                    UnityEngine.Object.Destroy(_attachedEffect);
                _attachedEffect = null;
            }

            if (_effectMaterial != null)
            {
                UnityEngine.Object.Destroy(_effectMaterial);
                _effectMaterial = null;
            }

            _effectCamera = null;
            _currentShader = null;
        }

        public static void ForceRefresh(Camera cam)
        {   // Force reattachment to current camera with current settings
            
            if (cam != null && _currentShader != null)
            {
                RemoveCameraImageEffect();
                SetupCameraImageEffect(cam, _currentShader, _currentMode);
            }
        }
    }

    // MonoBehaviour to perform OnRenderImage blit with the given material
    public sealed class OverlayImageEffect : MonoBehaviour
    {
        private OverlayRenderMode _mode;
        private Shader _shader;
        private Material _sceneMat;

        public void Configure(OverlayRenderMode mode, Shader shader, Material sceneMat)
        {   // Set the mode, shader, and scene material for rendering
            _mode = mode;
            _shader = shader;
            _sceneMat = sceneMat;
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dest)
        {   // Always write to dest, and update UI RT if needed
            if (_shader == null)
            {
                Graphics.Blit(src, dest);
                return;
            }

            switch (_mode)
            {
                case OverlayRenderMode.Exclusive:
                    // 1) build/update the RT for UI background using the active camera frame
                    Exclusive_Render.RenderUIBackground(src, _shader);
                    // 2) do NOT change the scene output
                    Graphics.Blit(src, dest);
                    return;

                case OverlayRenderMode.Inclusive:
                    // Always write to dest, even if shader is null
                    Inclusive_Render.Render(src, dest, _shader);
                    return;

                default:
                    // normal scene postprocess modes
                    if (_sceneMat != null) Graphics.Blit(src, dest, _sceneMat);
                    else Graphics.Blit(src, dest);
                    return;
            }
        }
    }
}
