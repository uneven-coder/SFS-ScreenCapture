using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using HarmonyLib;
using FrameEmbededState.Lib.Renders;
using SFS.World;
using SFS;

namespace FrameEmbededState
{
    public enum OverlayRenderMode
    {
        BehindUI,
        Inclusive,
        OnTop,
        Exclusive,
        ObjectLayer // Add new mode
    }

    public sealed class VisualOverlayManager
    {
        // Single mutable state (Rust-like: one mutable source of truth).
        public sealed class State
        {
            // Camera -> now supports multiple attached cameras
            public readonly HashSet<Camera> Cameras = new HashSet<Camera>();
            public VisualOverlaySettings Settings;

            // Processing buffers (CPU path)
            public Texture2D SrcTex;
            public Texture2D DstTex;
            public NativeArray<Color32> Src;
            public NativeArray<Color32> Dst;

            // GPU work surfaces
            public RenderTexture WorkRT;      // Safe-sized processing source
            public RenderTexture UiOutputRT;  // Full-res UI output (screen-sized)

            public int Width;
            public int Height;
            public Rect Rect;

            // Timing / safety
            public float LastProcessedTime;
            public float AdaptiveScale = 1f;
            public float NextScaleChangeTime;
            public int ConsecutiveFailures;

            // Async readback
            public bool AsyncSupported;
            public bool ReadbackInFlight;
            public bool HaveCpuFrame;
            public AsyncGPUReadbackRequest ReadbackReq;

            // GPU shader
            public Material GpuMaterial;
            public string LastShaderSource;
            public bool LastUseGpuShader;

            // UI target set (per-element)
            public readonly HashSet<RawImage> UiTargets = new HashSet<RawImage>();

            // Optional: a default fullscreen RawImage for OnTop (only) if no targets are registered
            public Canvas FullscreenCanvas;
            public RawImage FullscreenRawImage;
        }

        private readonly State _s = new State();

        // Used by TranslucentImage patch (blur source override)
        internal static Texture UiBlurOverrideTexture;

        // Optional global hook for custom UI shaders (transparent backgrounds, etc.)
        private static readonly int GlobalUiBackgroundTexId = Shader.PropertyToID("_FrameEmbededState_UIBackgroundTex");

        // --- Public API -------------------------------------------------------

        public void VisualManager(Action<VisualOverlaySettings> configure)
        {
            var settings = new VisualOverlaySettings();
            configure?.Invoke(settings);

            if (!settings.Enable || (settings.TargetCamera == null && (settings.TargetCameras == null || settings.TargetCameras.Length == 0)))
            {
                Disable();
                return;
            }

            _s.Settings = settings;
            _s.AsyncSupported = SystemInfo.supportsAsyncGPUReadback;

            EnsureGpuMaterial(settings);

            // Choose cameras: prefer explicit TargetCameras; fall back to single TargetCamera for compatibility.
            Camera[] targets = settings.TargetCameras != null && settings.TargetCameras.Length > 0
                ? settings.TargetCameras
                : (settings.TargetCamera != null ? new[] { settings.TargetCamera } : Array.Empty<Camera>());

            AttachToCameras(targets);

            // UI presentation same as before
            if (settings.RenderMode == OverlayRenderMode.OnTop)
                EnsureFullscreenOverlayCanvas();
            else
                DisableFullscreenOverlayCanvas();
        }

        // Register per-element UI targets (RawImage). Each will show the processed output cropped to its rect.
        public void RegisterUiTarget(RawImage image)
        {
            if (image == null) return;
            _s.UiTargets.Add(image);

            // If the user is doing per-element rendering, they usually don't want the fullscreen fallback.
            if (_s.FullscreenRawImage != null)
                _s.FullscreenRawImage.enabled = false;
        }

        public void UnregisterUiTarget(RawImage image)
        {
            if (image == null) return;
            _s.UiTargets.Remove(image);
        }

        // --- Attach / Detach --------------------------------------------------

        // Replace single-camera attach with multi-camera attach
        private void AttachToCameras(IEnumerable<Camera> cams)
        {   // Bind overlay to each provided camera
            // If same set already attached, do nothing.
            var newSet = new HashSet<Camera>(cams ?? Array.Empty<Camera>());

            if (_s.Cameras.SetEquals(newSet))
                return;

            Detach();

            foreach (var cam in newSet)
            {
                if (cam == null) continue;
                _s.Cameras.Add(cam);
                var hook = cam.GetComponent<OverlayHook>() ?? cam.gameObject.AddComponent<OverlayHook>();
                hook.hideFlags = HideFlags.HideAndDontSave;
                hook.Bind(this, cam);
            }

            // Invalidate CPU frame and async state so new camera gets a fresh frame
            _s.HaveCpuFrame = false;
            _s.ReadbackInFlight = false;
        }

        private void Detach()
        {   // Remove hooks from all attached cameras and release resources
            if (_s.Cameras.Count > 0)
            {
                foreach (var cam in _s.Cameras)
                {
                    if (cam == null) continue;
                    var hook = cam.GetComponent<OverlayHook>();
                    if (hook != null)
                        UnityEngine.Object.Destroy(hook);
                }
            }

            _s.Cameras.Clear();

            ReleaseBuffers();

            DisableFullscreenOverlayCanvas();

            if (_s.GpuMaterial != null)
            {
                UnityEngine.Object.Destroy(_s.GpuMaterial);
                _s.GpuMaterial = null;
            }

            _s.LastShaderSource = null;
            _s.LastUseGpuShader = false;

            UiBlurOverrideTexture = null;
            Shader.SetGlobalTexture(GlobalUiBackgroundTexId, null);

            // Invalidate CPU frame and async state on detach
            _s.HaveCpuFrame = false;
            _s.ReadbackInFlight = false;
        }

        private void Disable()
        {
            _s.Settings = null;
            Detach();
        }

        // --- Rendering entrypoint (called by OverlayHook) ----------------------

        internal void Render(Camera cam, RenderTexture srcRT, RenderTexture dstRT)
        {   // Main rendering entrypoint for overlays: only process when called from an attached camera
            var settings = _s.Settings;
            if (settings == null || !_s.Cameras.Contains(cam))
            {
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            if (settings.ShouldSkip?.Invoke() == true)
            {
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            var safe = settings.Safe ?? SafeDefaults.Instance;
            float now = Time.unscaledTime;

            bool cameraMustPassthrough =
                settings.RenderMode == OverlayRenderMode.Exclusive ||
                settings.RenderMode == OverlayRenderMode.OnTop;

            // Invalidate CPU frame at the start of every render to force update
            _s.HaveCpuFrame = false;

            // Invalidate CPU frame if resolution changes
            if (_s.Width != srcRT.width || _s.Height != srcRT.height)
            {
                _s.HaveCpuFrame = false;
                _s.ReadbackInFlight = false;
            }

            if (safe.MinIntervalSeconds > 0f && (now - _s.LastProcessedTime) < safe.MinIntervalSeconds)
            {
                if (cameraMustPassthrough)
                    Graphics.Blit(srcRT, dstRT);
                else
                    BlitLastOrPassthrough(srcRT, dstRT);

                return;
            }

            // Enable depth texture for occlusion checks in ObjectLayer
            cam.depthTextureMode |= DepthTextureMode.Depth;

            ComputeSafeSize(srcRT.width, srcRT.height, safe, out int safeW, out int safeH);
            EnsureBuffers(safeW, safeH);
            EnsureWorkRT(safeW, safeH);
            EnsureUiOutputRT(srcRT.width, srcRT.height);

            Graphics.Blit(srcRT, _s.WorkRT);

            bool wantAsync = safe.UseAsyncGpuReadback && _s.AsyncSupported;

            if (wantAsync)
            {
                if (!_s.ReadbackInFlight)
                {
                    _s.ReadbackReq = AsyncGPUReadback.Request(_s.WorkRT, 0, TextureFormat.RGBA32);
                    _s.ReadbackInFlight = true;
                }

                if (_s.ReadbackInFlight && _s.ReadbackReq.done)
                {
                    _s.ReadbackInFlight = false;

                    if (_s.ReadbackReq.hasError)
                    {
                        _s.ConsecutiveFailures++;
                        WriteCameraFallback(srcRT, dstRT, cameraMustPassthrough);
                        ApplyFailurePolicy(safe, now);
                        return;
                    }

                    try
                    {
                        var data = _s.ReadbackReq.GetData<Color32>();
                        CopyNative(data, _s.Src);
                        _s.HaveCpuFrame = true;
                    }
                    catch
                    {
                        _s.ConsecutiveFailures++;
                        WriteCameraFallback(srcRT, dstRT, cameraMustPassthrough);
                        ApplyFailurePolicy(safe, now);
                        return;
                    }
                }

                if (!_s.HaveCpuFrame)
                {
                    WriteCameraFallback(srcRT, dstRT, cameraMustPassthrough);
                    return;
                }
            }
            else
            {
                var prev = RenderTexture.active;
                RenderTexture.active = _s.WorkRT;
                try
                {
                    _s.SrcTex.ReadPixels(_s.Rect, 0, 0, false);
                }
                catch
                {
                    RenderTexture.active = prev;
                    _s.ConsecutiveFailures++;
                    WriteCameraFallback(srcRT, dstRT, cameraMustPassthrough);
                    ApplyFailurePolicy(safe, now);
                    return;
                }
                RenderTexture.active = prev;
                _s.HaveCpuFrame = true;
            }

            // --- All render logic is now in the renderer files ---
            switch (settings.RenderMode)
            {
                case OverlayRenderMode.BehindUI:
                    RenderBehindUIRenderer.Render(_s, srcRT, dstRT);
                    UiBlurOverrideTexture = null;
                    break;
                case OverlayRenderMode.Inclusive:
                    Inclusive.Render(_s, srcRT, dstRT);
                    UiBlurOverrideTexture = _s.UiOutputRT;
                    break;
                case OverlayRenderMode.OnTop:
                    Exclusive.Render(_s, srcRT);
                    Graphics.Blit(srcRT, dstRT);
                    UiBlurOverrideTexture = _s.UiOutputRT;
                    break;
                case OverlayRenderMode.Exclusive:
                    Exclusive.Render(_s, srcRT);
                    Graphics.Blit(srcRT, dstRT);
                    UiBlurOverrideTexture = _s.UiOutputRT;
                    break;
                case OverlayRenderMode.ObjectLayer:
                {   // Only render objects explicitly provided in Settings.ObjectRenderers
                    var objectRenderers = _s.Settings.ObjectRenderers;
                    if (objectRenderers == null || objectRenderers.Length == 0)
                    {   // If no objects specified, clear output and skip scene visuals
                        var prev = RenderTexture.active;
                        RenderTexture.active = _s.UiOutputRT;
                        GL.Clear(true, true, Color.clear);  // Clear color and depth
                        RenderTexture.active = prev;
                        Graphics.Blit(_s.UiOutputRT, dstRT);
                        UiBlurOverrideTexture = _s.UiOutputRT;
                        break;
                    }

                    Lib.Renders.ObjectTarget.Render(_s, srcRT, dstRT, objectRenderers);
                    UiBlurOverrideTexture = _s.UiOutputRT;
                    break;
                }
            }

            // Global texture for custom UI shaders (optional).
            Shader.SetGlobalTexture(GlobalUiBackgroundTexId, UiBlurOverrideTexture);

            // Update UI presentation:
            // - Always update explicitly registered targets (all modes).
            // - Only auto-show fullscreen overlay in OnTop.
            PresentToUi();

            _s.LastProcessedTime = now;
            _s.ConsecutiveFailures = 0;
        }

        // --- Mode renderers ---------------------------------------------------

        // --- UI presentation (per element) -----------------------------------

        private void PresentToUi()
        {   // Present overlay to UI targets or fullscreen fallback

            var settings = _s.Settings;

            if (_s.UiTargets.Count > 0)
            {
                foreach (var img in _s.UiTargets)
                {
                    if (img == null) continue;

                    img.texture = _s.UiOutputRT;
                    img.material = null;
                    img.color = Color.white;

                    // In Inclusive mode, always use full texture (no cropping)
                    if (settings != null && settings.RenderMode == OverlayRenderMode.Inclusive)
                        img.uvRect = new Rect(0, 0, 1, 1);
                    else if (TryComputeUvRect(img.rectTransform, out Rect uv))
                        img.uvRect = uv;
                    else
                        img.uvRect = new Rect(0, 0, 1, 1);
                }

                if (_s.FullscreenRawImage != null)
                    _s.FullscreenRawImage.enabled = false;

                return;
            }

            // Fullscreen overlay fallback is ONLY for OnTop.
            if (settings == null || settings.RenderMode != OverlayRenderMode.OnTop)
            {
                if (_s.FullscreenRawImage != null)
                    _s.FullscreenRawImage.enabled = false;
                return;
            }

            EnsureFullscreenOverlayCanvas();

            if (_s.FullscreenRawImage != null)
            {
                _s.FullscreenRawImage.enabled = true;
                _s.FullscreenRawImage.texture = _s.UiOutputRT;
                _s.FullscreenRawImage.uvRect = new Rect(0, 0, 1, 1);
                _s.FullscreenRawImage.material = null;
                _s.FullscreenRawImage.color = Color.white;
                _s.FullscreenRawImage.raycastTarget = false;
            }
        }

        private bool TryComputeUvRect(RectTransform rt, out Rect uvRect)
        {
            uvRect = new Rect(0, 0, 1, 1);
            if (rt == null) return false;

            // Screen-space rect from world corners.
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            // Use the canvas camera if available; for ScreenSpaceOverlay it's null.
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera uiCam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                uiCam = canvas.worldCamera;

            Vector2 p0 = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]);
            Vector2 p2 = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);

            float minX = Mathf.Min(p0.x, p2.x);
            float maxX = Mathf.Max(p0.x, p2.x);
            float minY = Mathf.Min(p0.y, p2.y);
            float maxY = Mathf.Max(p0.y, p2.y);

            if (Screen.width <= 0 || Screen.height <= 0) return false;

            float u0 = Mathf.Clamp01(minX / Screen.width);
            float v0 = Mathf.Clamp01(minY / Screen.height);
            float u1 = Mathf.Clamp01(maxX / Screen.width);
            float v1 = Mathf.Clamp01(maxY / Screen.height);

            // RawImage uvRect uses (x,y,width,height) in normalized texture coords.
            uvRect = new Rect(u0, v0, Mathf.Max(0, u1 - u0), Mathf.Max(0, v1 - v0));
            return true;
        }

        // --- Fullscreen UI overlay fallback (OnTop only) ----------------------

        private void EnsureFullscreenOverlayCanvas()
        {
            if (_s.FullscreenCanvas != null)
            {
                _s.FullscreenCanvas.enabled = true;
                _s.FullscreenCanvas.gameObject.SetActive(true);
                _s.FullscreenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _s.FullscreenCanvas.sortingOrder = short.MaxValue;
                _s.FullscreenCanvas.overrideSorting = true;

                // Prevent overlay from blocking input
                var raycaster = _s.FullscreenCanvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                    raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
                var cg = _s.FullscreenCanvas.GetComponent<CanvasGroup>();
                if (cg == null)
                    cg = _s.FullscreenCanvas.gameObject.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.interactable = false;

                return;
            }

            var root = new GameObject("VisualOverlay.FullscreenCanvas");
            root.hideFlags = HideFlags.HideAndDontSave;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            canvas.overrideSorting = true;

            var raycasterNew = root.AddComponent<GraphicRaycaster>();
            raycasterNew.blockingObjects = GraphicRaycaster.BlockingObjects.None;

            // Prevent overlay from blocking input
            var cgNew = root.AddComponent<CanvasGroup>();
            cgNew.blocksRaycasts = false;
            cgNew.interactable = false;

            var imgGO = new GameObject("VisualOverlay.FullscreenRawImage");
            imgGO.hideFlags = HideFlags.HideAndDontSave;
            imgGO.transform.SetParent(root.transform, false);

            var img = imgGO.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.color = Color.white;
            img.material = null;

            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _s.FullscreenCanvas = canvas;
            _s.FullscreenRawImage = img;
        }

        private void DisableFullscreenOverlayCanvas()
        {
            if (_s.FullscreenCanvas != null)
            {
                UnityEngine.Object.Destroy(_s.FullscreenCanvas.gameObject);
                _s.FullscreenCanvas = null;
                _s.FullscreenRawImage = null;
            }
        }

        // --- Buffer management ------------------------------------------------

        private void EnsureBuffers(int w, int h)
        {
            if (_s.SrcTex != null && _s.Width == w && _s.Height == h)
                return;

            ReleaseBuffers();

            _s.Width = w;
            _s.Height = h;
            _s.Rect = new Rect(0, 0, w, h);

            _s.SrcTex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            _s.DstTex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            _s.Src = _s.SrcTex.GetRawTextureData<Color32>();
            _s.Dst = _s.DstTex.GetRawTextureData<Color32>();

            _s.ReadbackInFlight = false;
            _s.HaveCpuFrame = false;
            _s.ConsecutiveFailures = 0;
        }

        private void EnsureWorkRT(int w, int h)
        {
            if (_s.WorkRT != null && _s.WorkRT.width == w && _s.WorkRT.height == h)
                return;

            if (_s.WorkRT != null)
            {
                _s.WorkRT.Release();
                UnityEngine.Object.Destroy(_s.WorkRT);
                _s.WorkRT = null;
            }

            _s.WorkRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "VisualOverlay.WorkRT"
            };
            _s.WorkRT.Create();
        }

        private void EnsureUiOutputRT(int fullW, int fullH)
        {
            if (_s.UiOutputRT != null && _s.UiOutputRT.width == fullW && _s.UiOutputRT.height == fullH)
                return;

            if (_s.UiOutputRT != null)
            {
                _s.UiOutputRT.Release();
                UnityEngine.Object.Destroy(_s.UiOutputRT);
                _s.UiOutputRT = null;
            }

            _s.UiOutputRT = new RenderTexture(fullW, fullH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "VisualOverlay.UiOutputRT"
            };
            _s.UiOutputRT.Create();
        }

        private void ReleaseBuffers()
        {
            if (_s.WorkRT != null)
            {
                _s.WorkRT.Release();
                UnityEngine.Object.Destroy(_s.WorkRT);
                _s.WorkRT = null;
            }

            if (_s.UiOutputRT != null)
            {
                _s.UiOutputRT.Release();
                UnityEngine.Object.Destroy(_s.UiOutputRT);
                _s.UiOutputRT = null;
            }

            if (_s.SrcTex != null) UnityEngine.Object.Destroy(_s.SrcTex);
            if (_s.DstTex != null) UnityEngine.Object.Destroy(_s.DstTex);

            _s.SrcTex = null;
            _s.DstTex = null;
            _s.Width = 0;
            _s.Height = 0;
            _s.ReadbackInFlight = false;
            _s.HaveCpuFrame = false;
        }

        // --- Safety / fallback ------------------------------------------------

        private void BlitLastOrPassthrough(RenderTexture srcRT, RenderTexture dstRT)
        {
            if (_s.DstTex != null)
                Graphics.Blit(_s.DstTex, dstRT);
            else
                Graphics.Blit(srcRT, dstRT);
        }

        private void WriteCameraFallback(RenderTexture srcRT, RenderTexture dstRT, bool cameraMustPassthrough)
        {
            if (cameraMustPassthrough)
            {
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            BlitLastOrPassthrough(srcRT, dstRT);
        }

        private void ComputeSafeSize(int srcW, int srcH, SafeOptions safe, out int w, out int h)
        {
            int cw = Mathf.Clamp(srcW, safe.MinWidth, safe.MaxWidth);
            int ch = Mathf.Clamp(srcH, safe.MinHeight, safe.MaxHeight);

            float s = Mathf.Clamp01(_s.AdaptiveScale);
            cw = Mathf.Max(safe.MinWidth, (int)(cw * s));
            ch = Mathf.Max(safe.MinHeight, (int)(ch * s));

            long pixels = (long)cw * (long)ch;
            if (safe.MaxPixels > 0 && pixels > safe.MaxPixels)
            {
                float r = Mathf.Sqrt((float)safe.MaxPixels / (float)pixels);
                cw = Mathf.Max(safe.MinWidth, (int)(cw * r));
                ch = Mathf.Max(safe.MinHeight, (int)(ch * r));
            }

            int m = Mathf.Max(1, safe.RequireMultipleOf);
            cw = Mathf.Max(m, (cw / m) * m);
            ch = Mathf.Max(m, (ch / m) * m);

            w = Mathf.Clamp(cw, safe.MinWidth, safe.MaxWidth);
            h = Mathf.Clamp(ch, safe.MinHeight, safe.MaxHeight);
        }

        private void ApplyFailurePolicy(SafeOptions safe, float now)
        {
            if (!safe.AdaptQuality)
                return;

            if (_s.ConsecutiveFailures >= safe.MaxConsecutiveFailures && now >= _s.NextScaleChangeTime)
            {
                _s.AdaptiveScale = Mathf.Clamp(_s.AdaptiveScale * safe.QualityDownStepOnFailure, safe.MinAdaptiveScale, 1f);
                _s.NextScaleChangeTime = now + safe.QualityCooldownSeconds;
            }
        }

        private static void CopyNative(NativeArray<Color32> from, NativeArray<Color32> to)
        {
            int n = Mathf.Min(from.Length, to.Length);
            for (int i = 0; i < n; i++)
                to[i] = from[i];
        }

        // --- Shader / material ------------------------------------------------

        private void EnsureGpuMaterial(VisualOverlaySettings settings)
        {
            if (settings.UseGpuShader && !string.IsNullOrEmpty(settings.ShaderSource))
            {
                bool needsRebuild = _s.GpuMaterial == null ||
                                    _s.LastShaderSource != settings.ShaderSource ||
                                    !_s.LastUseGpuShader;

                if (needsRebuild)
                {
                    if (_s.GpuMaterial != null)
                        UnityEngine.Object.Destroy(_s.GpuMaterial);

                    var shader = Shader.Find(settings.ShaderSource) ?? Shader.Find("Hidden/InternalErrorShader");
                    _s.GpuMaterial = new Material(shader);
                    _s.LastShaderSource = settings.ShaderSource;
                    _s.LastUseGpuShader = true;
                }
            }
            else
            {
                if (_s.GpuMaterial != null)
                {
                    UnityEngine.Object.Destroy(_s.GpuMaterial);
                    _s.GpuMaterial = null;
                }
                _s.LastShaderSource = null;
                _s.LastUseGpuShader = false;
            }
        }

        // Optional SFS queue access (no compile dependency).
        private static int TryGetRenderQueue(string layer, int fallback)
        {
            try
            {
                var t = AccessTools.TypeByName("SFS.RenderSortingManager");
                if (t == null) return fallback;

                var mainProp = AccessTools.Property(t, "main");
                var main = mainProp?.GetValue(null, null);
                if (main == null) return fallback;

                var m = AccessTools.Method(main.GetType(), "GetRenderQueue", new[] { typeof(string) });
                if (m == null) return fallback;

                var val = m.Invoke(main, new object[] { layer });
                if (val is int i) return i;
            }
            catch { /* ignore */ }

            return fallback;
        }

        // --- Settings / data --------------------------------------------------

        public sealed class VisualOverlaySettings
        {
            // Backwards-compatible single target camera...
            public Camera TargetCamera;

            // ...and optional multiple-target form (scaled + normal).
            public Camera[] TargetCameras;

            public bool Enable = true;
            public Func<bool> ShouldSkip;
            public Action<FrameData> Execute;

            public bool UseGpuShader = false;
            public string ShaderSource = null;

            public OverlayRenderMode RenderMode = OverlayRenderMode.BehindUI;
            public SafeOptions Safe = SafeDefaults.Instance;

            // Add: objects to render for ObjectLayer mode
            public Renderer[] ObjectRenderers = null;
        }

        public struct FrameData
        {
            public Unity.Collections.NativeArray<Color32> Source;
            public Unity.Collections.NativeArray<Color32> Result;
            public int Width;
            public int Height;

            // Add these fields for object-layer effects
            public Dictionary<int, List<int>> ObjectVisiblePixels; // objectId -> pixel indices
            public int[] MaskBuffer; // per-pixel objectId
            public Renderer[] MaskIdToRenderer; // objectId -> Renderer
        }

        public sealed class SafeOptions
        {
            public int MaxWidth = 2048;
            public int MaxHeight = 2048;
            public int MaxPixels = 1920 * 1080;

            public int MinWidth = 256;
            public int MinHeight = 256;
            public int RequireMultipleOf = 8;

            public float MinIntervalSeconds = 1f / 30f;

            public bool UseAsyncGpuReadback = true;
            public bool AdaptQuality = true;

            public float CpuBudgetMs = 8.0f;
            public float MinAdaptiveScale = 0.25f;

            public float QualityDownStep = 0.80f;
            public float QualityUpStep = 1.05f;

            public float QualityCooldownSeconds = 1.0f;
            public int MaxConsecutiveFailures = 3;
            public float QualityDownStepOnFailure = 0.70f;
        }

        private static class SafeDefaults
        {
            public static readonly SafeOptions Instance = new SafeOptions();
        }

        // --- Per-object mask utility for Execute --------------------------------

        public static void BuildObjectMask(
            Camera cam,
            int width,
            int height,
            int[] allowedLayers,
            int[] allowedRenderQueues,
            Func<Renderer, bool> extraFilter,
            ref int[] maskBuffer,
            int maskValue = 1)
        {   // Build a per-pixel mask for objects matching criteria (maskBuffer must be at least width*height)
            if (maskBuffer == null || maskBuffer.Length != width * height)
                maskBuffer = new int[width * height];

            Array.Clear(maskBuffer, 0, maskBuffer.Length);

            // Workaround: assign ref to local for lambda use
            var localMaskBuffer = maskBuffer;

            Lib.ObjectUtil.GetVisibleObjectsFiltered(
                cam,
                (renderer, bounds, rect) =>
                {
                    // Project bounds rect to screen and fill mask
                    int minX = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, width - 1);
                    int maxX = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, width - 1);
                    int minY = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, height - 1);
                    int maxY = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, height - 1);

                    for (int y = minY; y <= maxY; y++)
                    {
                        int row = y * width;
                        for (int x = minX; x <= maxX; x++)
                            localMaskBuffer[row + x] = maskValue;
                    }
                },
                allowedLayers,
                allowedRenderQueues,
                extraFilter
            );

            // Assign local back to ref in case it was reallocated
            maskBuffer = localMaskBuffer;
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
            if (_mgr == null || _cam == null || !_cam.isActiveAndEnabled)
            {
                Graphics.Blit(src, dst);
                return;
            }

            _mgr.Render(_cam, src, dst);
        }

        private void OnDisable()
        {
            _mgr = null;
            _cam = null;
        }
    }

    // Optional convenience component:
    // Add this to any UI RawImage that should display the overlay (per-element) in Exclusive/OnTop.
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
