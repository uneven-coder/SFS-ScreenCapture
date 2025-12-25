using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FrameEmbededState
{
    public enum OverlayRenderMode
    {
        BehindUI,
        Inclusive,
        OnTop,
        Exclusive,
        ObjectLayer
    }

    public sealed class VisualOverlayManager
    {
        private readonly HashSet<Camera> _cameras = new HashSet<Camera>();
        private readonly HashSet<RawImage> _uiTargets = new HashSet<RawImage>();

        public VisualOverlaySettings Settings { get; private set; }

        internal Material GpuMaterial => _gpuMaterial;
        internal IEnumerable<RawImage> UiTargets => _uiTargets;

        internal static Texture UiBlurOverrideTexture;
        internal static readonly int GlobalUiBackgroundTexId =
            Shader.PropertyToID("_FrameEmbededState_UIBackgroundTex");

        private Material _gpuMaterial;
        private string _lastShaderSource;
        private bool _lastUseGpuShader;

        // --- Public API -------------------------------------------------------

        public void ConfigureOverlay(Action<VisualOverlaySettings> configure)
        {   // Configure and enable the overlay system
            var settings = new VisualOverlaySettings();
            configure?.Invoke(settings);

            if (!settings.Enable ||
                (settings.TargetCamera == null &&
                 (settings.TargetCameras == null || settings.TargetCameras.Length == 0)))
            {
                Disable();
                return;
            }

            Settings = settings;
            EnsureGpuMaterial(Settings);

            Camera[] targets =
                (Settings.TargetCameras != null && Settings.TargetCameras.Length > 0)
                    ? Settings.TargetCameras
                    : (Settings.TargetCamera != null ? new[] { Settings.TargetCamera } : Array.Empty<Camera>());

            AttachToCameras(targets);
        }

        public void Disable()
        {
            Settings = null;
            Detach();
        }

        public void RegisterUiTarget(RawImage image)
        {
            if (image == null) return;
            _uiTargets.Add(image);
            image.raycastTarget = false;
        }

        public void UnregisterUiTarget(RawImage image)
        {
            if (image == null) return;
            _uiTargets.Remove(image);
        }

        // --- Attach / Detach --------------------------------------------------

        private void AttachToCameras(IEnumerable<Camera> cams)
        {
            var newSet = new HashSet<Camera>(cams ?? Array.Empty<Camera>());

            if (_cameras.SetEquals(newSet))
                return;

            Detach();

            foreach (var cam in newSet)
            {
                if (cam == null) continue;
                _cameras.Add(cam);

                var hook = cam.GetComponent<OverlayHook>() ?? cam.gameObject.AddComponent<OverlayHook>();
                hook.hideFlags = HideFlags.HideAndDontSave;
                hook.Bind(this, cam);
            }
        }

        private void Detach()
        {
            foreach (var cam in _cameras)
            {
                if (cam == null) continue;
                var hook = cam.GetComponent<OverlayHook>();
                if (hook != null)
                    UnityEngine.Object.Destroy(hook);
            }
            _cameras.Clear();

            // renderer-owned resources
            Lib.Renders.RenderBehindUIRenderer.Release();
            Lib.Renders.Exclusive.Release();
            Lib.Renders.ObjectTarget.Release();

            UiBlurOverrideTexture = null;
            Shader.SetGlobalTexture(GlobalUiBackgroundTexId, null);

            if (_gpuMaterial != null)
            {
                UnityEngine.Object.Destroy(_gpuMaterial);
                _gpuMaterial = null;
            }

            _lastShaderSource = null;
            _lastUseGpuShader = false;
        }

        // --- Shader / material ------------------------------------------------

        private void EnsureGpuMaterial(VisualOverlaySettings settings)
        {
            if (settings.UseGpuShader && !string.IsNullOrEmpty(settings.ShaderSource))
            {
                bool needsRebuild =
                    _gpuMaterial == null ||
                    _lastShaderSource != settings.ShaderSource ||
                    !_lastUseGpuShader;

                if (!needsRebuild)
                    return;

                if (_gpuMaterial != null)
                    UnityEngine.Object.Destroy(_gpuMaterial);

                var shader = Shader.Find(settings.ShaderSource);
                if (shader == null)
                {
                    _gpuMaterial = null;
                    _lastShaderSource = null;
                    _lastUseGpuShader = false;
                    return;
                }

                _gpuMaterial = new Material(shader);
                _lastShaderSource = settings.ShaderSource;
                _lastUseGpuShader = true;
            }
            else
            {
                if (_gpuMaterial != null)
                {
                    UnityEngine.Object.Destroy(_gpuMaterial);
                    _gpuMaterial = null;
                }

                _lastShaderSource = null;
                _lastUseGpuShader = false;
            }
        }

        // --- Settings / data --------------------------------------------------

        public sealed class VisualOverlaySettings
        {
            public Camera TargetCamera;
            public Camera[] TargetCameras;

            public bool Enable = true;

            public Action<FrameData> Execute;

            public bool UseGpuShader = false;
            public string ShaderSource = null;

            public OverlayRenderMode RenderMode = OverlayRenderMode.BehindUI;

            public Renderer[] ObjectRenderers = null;
        }

        public struct FrameData
        {
            public Unity.Collections.NativeArray<Color32> Source;
            public Unity.Collections.NativeArray<Color32> Result;
            public int Width;
            public int Height;

            public Dictionary<int, System.Collections.Generic.List<int>> ObjectVisiblePixels;
            public int[] MaskBuffer;
            public Renderer[] MaskIdToRenderer;
            public RendererMaterialGroup[] RendererMaterials;
            public ModelTextureData[] ModelTextures;
        }

        public struct RendererMaterialGroup
        {
            public Renderer Renderer;
            public Material[] Materials;
        }

        public struct ModelTextureData
        {
            public MeshRenderer Renderer;
            public Texture2D ColorTexture;
            public Texture2D NormalTexture;
            public bool UseNormals;
            public float Smoothness;
        }
    }

    [DisallowMultipleComponent]
    internal sealed class OverlayHook : MonoBehaviour
    {
        private VisualOverlayManager _mgr;
        private Camera _cam;

        public void Bind(VisualOverlayManager m, Camera c)
        {
            _mgr = m;
            _cam = c;
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (_mgr == null || _cam == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            FrameEmbededState.Lib.Renders.OverlayDispatcher.Render(_mgr, _cam, src, dst);
        }

        private void OnDisable()
        {
            _mgr = null;
            _cam = null;
        }
    }

    [RequireComponent(typeof(RawImage))]
    public sealed class VisualOverlayUiTarget : MonoBehaviour
    {
        public VisualOverlayManager Manager;
        private RawImage _img;

        private void Awake()
        {
            _img = GetComponent<RawImage>();
            if (_img != null)
                _img.raycastTarget = false;
        }

        private void OnEnable()
        {
            if (Manager != null)
                Manager.RegisterUiTarget(_img);
        }

        private void OnDisable()
        {
            if (Manager != null)
                Manager.UnregisterUiTarget(_img);
        }
    }
}
