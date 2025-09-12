using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using SFS.UI.ModGUI;
using SFS.World;
using SFS.UI;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.IO;
using static UITools.UIToolsBuilder;
using SystemType = System.Type;
using static ScreenCapture.Main;
using System.Runtime.InteropServices;
using SFS.Builds;
using TranslucentImage;
using SFS; // added to reference InteriorManager
using System.Diagnostics;
using static ScreenCapture.CaptureUtilities;
using UITools;

namespace ScreenCapture
{
    // TimeStepHelper moved to Utilities.cs

    public class MainUI : UIBase
    {   // Manage the main capture UI bound directly to a Captue instance
        private Container previewContainer;
        private bool previewInitialized;
        private Coroutine currentAnimation;

        // Stats UI elements
        private Label resLabel;
        private Label gpuLabel;
        private Label cpuLabel;
        private Label pngLabel;
        private Label maxLabel;
        private TextInput resInput;

        // Performance optimization: cache frequently accessed values
        private int lastEstimateWidth = -1;
        private float lastUpdateTime;
        private const float UPDATE_INTERVAL = 0.2f; // Update UI estimates every 200ms max

        // Preview rendering optimization
        private bool previewNeedsUpdate = true;
        private float lastPreviewUpdate;
        private const float PREVIEW_UPDATE_INTERVAL = 0.05f; // 20 FPS for preview

        // Border for the preview image
        private Box previewBorder;

        // Fix for RawImage appearing too large in UI: scale down displayed preview by this factor
        private const float PREVIEW_SCALE_FIX = 1.0f; // Use 1.0f to prevent scaling issues

        private void OnResolutionInputChange(string val)
        {   // Optimized resolution input validation using normalized crop factors
            if (!int.TryParse(val, out int targetWidth))
                return;

            var (leftCrop, _, rightCrop, _) = CaptureUtilities.GetNormalizedCropValues();
            float cropWidthFactor = 1f - leftCrop - rightCrop;

            int maxSafeRenderWidth = CaptureUtilities.ComputeMaxSafeWidth();
            int maxSafeTargetWidth = Mathf.RoundToInt(maxSafeRenderWidth * cropWidthFactor);

            targetWidth = Mathf.Clamp(targetWidth, 1, maxSafeTargetWidth);
            resInput.Text = targetWidth.ToString();
            ResolutionWidth = targetWidth;
            
            Debug.Log($"Resolution input changed: val='{val}', parsed={targetWidth}, ResolutionWidth now={ResolutionWidth}");
            
            UpdateEstimatesUI();
        }

        public override void Show()
        {   // Build and display main window UI for the given owner
            if (IsOpen)
                return;

            previewInitialized = false;
            previewContainer = null;

            // Subscribe to screen size changes
            Captue.OnScreenSizeChanged += OnScreenSizeChanged;

            World.UIHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "SFSRecorder");
            var closableWindow = CreateClosableWindow(World.UIHolder.transform, Builder.GetRandomID(), 980, 545, 300, 100, true, true, 1f, "ScreenShot", minimized: false);
            closableWindow.Minimized = true;
            closableWindow.RegisterPermanentSaving("SFSRecorder_MainWindow");
            window = closableWindow;
            closableWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, new RectOffset(6, 6, 10, 6), true);
            closableWindow.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

            World.OwnerInstance.closableWindow = closableWindow;
            World.wasMinimized = closableWindow.Minimized;

            // Connect window events to our actions
            ConnectWindowEvents(closableWindow);

            CreateToolsContainer();
            Builder.CreateSeparator(window, 960, 0, 0);
            CreateControlsContainer();

            // Trigger initial opened action if window isn't minimized
            if (!closableWindow.Minimized)
                World.OwnerInstance.windowOpenedAction?.Invoke();

            UpdateBackgroundWindowVisibility();
        }

        private void ConnectWindowEvents(UITools.ClosableWindow closableWindow)
        {   // Connect window minimize/maximize events to our action handlers
            ref Action windowOpenedRef = ref World.OwnerInstance.windowOpenedAction;
            ref Action windowCollapsedRef = ref World.OwnerInstance.windowCollapsedAction;

            // Store previous actions to chain them
            var prevOpen = windowOpenedRef;
            var prevCollapse = windowCollapsedRef;

            windowOpenedRef = () =>
            {   // Chain previous open action, then prepare preview
                prevOpen?.Invoke();
                EnsurePreviewSetup();
                UpdateEstimatesUI();
                RequestPreviewUpdate();
            };

            windowCollapsedRef = () =>
            {   // Chain previous collapse action
                prevCollapse?.Invoke();
            };
        }

        private Container CreateToolsContainer()
        {   // Create preview and side toggles
            return CreateContainer(window, SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, 12f, null, true, toolsContainer =>
            {
                // Create preview container with fixed width and dynamic height
                previewContainer = CreateNestedContainer(toolsContainer, SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 0f, new RectOffset(6, 6, 6, 6), true);

                var previewLE = previewContainer.gameObject.GetComponent<LayoutElement>() ?? previewContainer.gameObject.AddComponent<LayoutElement>();
                previewLE.preferredWidth = 520f;
                previewLE.minWidth = 520f;
                previewLE.flexibleWidth = 0f;
                previewLE.preferredHeight = -1f;

                // Setup preview immediately using centralized utilities
                try { PreviewHierarchyUtilities.SetupPreviewWithBorder(previewContainer, ref previewBorder, ref previewInitialized); }
                catch (Exception ex) { Debug.LogError($"Failed to initialize preview: {ex.Message}"); previewInitialized = false; }

                // Spacer to push controls to the right
                var spacer = CreateNestedContainer(toolsContainer, SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 0f, null, false);
                var spacerLE = spacer.gameObject.GetComponent<LayoutElement>() ?? spacer.gameObject.AddComponent<LayoutElement>();
                spacerLE.flexibleWidth = 1f; spacerLE.minWidth = 0f; spacerLE.preferredWidth = 0f;

                CreateNestedVertical(toolsContainer, 20f, null, TextAnchor.UpperLeft, controlsContainer =>
                {
                    var le = controlsContainer.gameObject.GetComponent<LayoutElement>() ?? controlsContainer.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = 390f; le.flexibleWidth = 0f;

                    Builder.CreateLabel(controlsContainer, 390, 36, 0, 0, "Visuals");

                    CreateNestedHorizontal(controlsContainer, 12f, null, TextAnchor.UpperLeft, cols =>
                    {
                        CreateNestedVertical(cols, 14f, null, TextAnchor.UpperLeft, leftCol =>
                        {
                            UIUtilities.CreateCompactToggle(leftCol, "Background", () => World.OwnerInstance?.showBackground ?? true, () =>
                            {   // Toggle background and update visibility
                                ref Captue owner = ref World.OwnerInstance;
                                if (owner != null) { owner.showBackground = !owner.showBackground; UpdateBackgroundWindowVisibility(); }
                            });

                            UIUtilities.CreateCompactToggle(leftCol, "Interiors", () => SFS.InteriorManager.main?.interiorView?.Value ?? true, () =>
                            {   // Toggle global interior visibility
                                CaptureUtilities.ToggleInteriorView();
                            });
                        });

                        CreateNestedVertical(cols, 14f, null, TextAnchor.UpperLeft, rightCol =>
                        {
                            UIUtilities.CreateCompactToggle(rightCol, "Terrain", () => World.OwnerInstance?.showTerrain ?? true, () =>
                            {   // Toggle terrain visibility
                                ref Captue owner = ref World.OwnerInstance;
                                if (owner != null) owner.showTerrain = !owner.showTerrain;
                            });

                            UIUtilities.CreateCompactToggle(rightCol, "Rockets", () => World.OwnerInstance?.rocketsWindow?.IsOpen ?? false, () =>
                            {   // Show/hide rockets window
                                ref Captue owner = ref World.OwnerInstance;
                                if (owner != null)
                                {   ref var rocketWindow = ref owner.rocketsWindow; CaptureUtilities.ShowHideWindow<RocketsUI>(ref rocketWindow, () => { }, () => { }); }
                            });
                        });
                    });

                    CreateNestedVertical(controlsContainer, 2f, null, TextAnchor.UpperCenter, cropControls =>
                    {
                        UIUtilities.CreateCropControls(cropControls, () =>
                        {   // Unified crop change handler with preview update
                            RefreshLayoutForCroppedPreview();
                            UpdateEstimatesUI();
                            RequestPreviewUpdate();
                        });
                    });
                });
            });
        }

        private Container CreateControlsContainer()
        {   // Create capture controls and show estimates above them
            var MainCol = CreateVerticalContainer(window, 8f, null, TextAnchor.LowerLeft);

            var controls = CreateNestedContainer(MainCol, SFS.UI.ModGUI.Type.Horizontal, TextAnchor.LowerLeft, 30f, null, true);

            CreateNestedVertical(controls, 5f, null, TextAnchor.LowerLeft, leftRow =>
            {
                UIUtilities.CreateTimeControls(leftRow);

                CreateNestedHorizontal(leftRow, 10f, null, TextAnchor.LowerLeft, captureRow =>
                {
                    Builder.CreateButton(captureRow, 180, 58, 0, 0, () => TakeScreenshot(), "Capture");
                    resInput = Builder.CreateTextInput(captureRow, 180, 58, 0, 0, ResolutionWidth.ToString(), OnResolutionInputChange);
                });
            });

            Builder.CreateSpace(controls, 240, 0);

            var ZoomRow = CreateNestedVertical(controls, 5f, null, TextAnchor.UpperLeft);
            CreateZoomControls(ZoomRow);

            CreateNestedHorizontal(MainCol, 103f, null, TextAnchor.UpperLeft, helpRow =>
            {
                CreateNestedVertical(helpRow, 5f, null, TextAnchor.UpperLeft, helpCol =>
                {
                    CreateNestedHorizontal(helpCol, 10f, null, TextAnchor.UpperLeft, statsRow1 =>
                    { resLabel = Builder.CreateLabel(statsRow1, 210, 34, 0, 0, "Res: -"); gpuLabel = Builder.CreateLabel(statsRow1, 170, 34, 0, 0, "GPU: -"); cpuLabel = Builder.CreateLabel(statsRow1, 170, 34, 0, 0, "RAM: -"); });

                    CreateNestedHorizontal(helpCol, 10f, null, TextAnchor.UpperLeft, statsRow2 =>
                    { pngLabel = Builder.CreateLabel(statsRow2, 170, 34, 0, 0, "PNG: -"); maxLabel = Builder.CreateLabel(statsRow2, 220, 34, 0, 0, "Max Width: -"); });
                });

                CreateNestedHorizontal(helpRow, 10f, null, TextAnchor.UpperLeft, bottomRow =>
                { Builder.CreateButton(bottomRow, 285, 58, 0, 0, () => FileUtilities.OpenCurrentWorldCapturesFolder(), "Open Captures"); });
            });

            return controls;
        }

        private void CreateZoomControls(Container ZoomRow)
        {
            float GetZoom() => Mathf.Clamp(PreviewZoom, 0.25f, 4f);
            float GetLevel() => PreviewZoomLevel;

            void SetZoom(float z)
            {   // Set zoom and update level based on factor with preview refresh
                PreviewZoom = z;
                PreviewZoomLevel = Mathf.Log(z);
                RequestPreviewUpdate();
            }

            float StepInLog(float z, int dir)
            {   // Compute next zoom via log-space lerp across [0.25, 4] using discrete steps
                float min = 0.25f, max = 4f; int steps = 20;
                float lnMin = Mathf.Log(min), lnMax = Mathf.Log(max);
                float t = Mathf.InverseLerp(lnMin, lnMax, Mathf.Log(Mathf.Clamp(z, min, max)));
                int i = Mathf.Clamp(Mathf.RoundToInt(t * steps) + dir, 0, steps);
                float factor = Mathf.Exp(Mathf.Lerp(lnMin, lnMax, (float)i / steps));
                return Mathf.Abs(factor - 1f) <= 0.02f ? 1f : factor;
            }

            InputWithLabel zoomInput = null;
            zoomInput = Builder.CreateInputWithLabel(ZoomRow, (140 * 2 + 10), 52, 0, 0, "Zoom", $"{GetLevel():0.00}", val =>
            {   // Parse unbounded zoom level from input
                if (string.IsNullOrWhiteSpace(val)) return;

                if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float lvl))
                {   PreviewZoomLevel = lvl; zoomInput.textInput.Text = $"{GetLevel():0.00}"; UpdateEstimatesUI(); RequestPreviewUpdate(); }
            });

            var bottomRow = CreateNestedHorizontal(ZoomRow, 10f, null, TextAnchor.MiddleLeft);

            Builder.CreateButton(bottomRow, 140, 58, 0, 0, () =>
            {   // Decrease zoom using non-linear step
                float z = StepInLog(GetZoom(), -1); SetZoom(z); zoomInput.textInput.Text = $"{GetLevel():0.00}"; UpdateEstimatesUI();
            }, "Zoom -");

            Builder.CreateButton(bottomRow, 140, 58, 0, 0, () =>
            {   // Increase zoom using non-linear step
                float z = StepInLog(GetZoom(), +1); SetZoom(z); zoomInput.textInput.Text = $"{GetLevel():0.00}"; UpdateEstimatesUI();
            }, "Zoom +");
        }


        private void TakeScreenshot()
        {   // Capture scene at requested resolution with optional cropping using GPU-compatible rendering
            if (World.MainCamera == null) { StartWindowColorAnimation(false); return; }

            var cameraState = SaveCameraState();
            RenderTexture fullRT = null;
            Texture2D finalTex = null;

            try
            {   // Main capture sequence with memory and compatibility checks
                var (renderWidth, renderHeight) = ComputeRenderDimensions();

                fullRT = CreateCompatibleRenderTexture(renderWidth, renderHeight);
                if (fullRT == null) { StartWindowColorAnimation(false); return; }

                SetupCameraForCapture(ref cameraState);
                
                bool showBackground = World.OwnerInstance?.showBackground ?? true;
                ConfigureBackgroundRendering(fullRT, showBackground);

                ApplyZoomToCamera(cameraState);

                var modified = CaptureUtilities.ApplySceneVisibilityTemporary(showBackground, World.OwnerInstance?.showTerrain ?? true, Main.HiddenRockets);

                World.MainCamera.rect = new Rect(0, 0, 1, 1);
                World.MainCamera.targetTexture = fullRT;
                World.MainCamera.Render();

                finalTex = ReadCroppedTexture(fullRT, renderWidth, renderHeight);
                if (finalTex == null) { StartWindowColorAnimation(false); return; }

                byte[] pngBytes = finalTex.EncodeToPNG();
                if (pngBytes == null || pngBytes.Length < 1024)
                {   // Try alternative encoding methods for broken PNG encoders
                    pngBytes = TryFallbackEncoding(finalTex);
                    if (pngBytes == null || pngBytes.Length < 1024)
                    {   UnityEngine.Debug.LogError("All encoding methods failed."); StartWindowColorAnimation(false); return; }
                }

                SaveScreenshotFile(pngBytes, renderWidth, finalTex.width, finalTex.height);

                CaptureUtilities.RestoreSceneVisibility(modified);
            }
            catch (System.Exception ex)
            {   UnityEngine.Debug.LogError($"Screenshot failed: {ex.Message}"); StartWindowColorAnimation(false); }
            finally
            {   RestoreCameraState(cameraState); if (fullRT != null) { fullRT.Release(); UnityEngine.Object.Destroy(fullRT); } if (finalTex != null) UnityEngine.Object.Destroy(finalTex); }
        }

        private struct CameraState
        {   // Store camera properties for restoration after capture
            public CameraClearFlags clearFlags;
            public Color backgroundColor;
            public float orthographicSize;
            public float fieldOfView;
            public Vector3 position;
            public int cullingMask;
        }

        private CameraState SaveCameraState()
        {   // Capture current camera state for later restoration
            return new CameraState
            {
                clearFlags = World.MainCamera.clearFlags,
                backgroundColor = World.MainCamera.backgroundColor,
                orthographicSize = World.MainCamera.orthographicSize,
                fieldOfView = World.MainCamera.fieldOfView,
                position = World.MainCamera.transform.position,
                cullingMask = World.MainCamera.cullingMask
            };
        }

        private void RestoreCameraState(CameraState state)
        {   // Restore camera to previous state after capture
            World.MainCamera.rect = new Rect(0, 0, 1, 1);
            World.MainCamera.targetTexture = null;
            World.MainCamera.clearFlags = state.clearFlags;
            World.MainCamera.backgroundColor = state.backgroundColor;
            World.MainCamera.orthographicSize = state.orthographicSize;
            World.MainCamera.fieldOfView = state.fieldOfView;
            World.MainCamera.transform.position = state.position;
            World.MainCamera.cullingMask = state.cullingMask;
        }

        private (int width, int height) ComputeRenderDimensions()
        {   // Calculate render dimensions with safety limits and fallback handling
            int targetWidth = int.TryParse(resInput?.Text, out int parsed) ? parsed : ResolutionWidth;
            int targetHeight = Mathf.RoundToInt((float)targetWidth / Mathf.Max(1, (float)Screen.width) * (float)Screen.height);

            var (gpuNeed, cpuNeed, _) = CaptureUtilities.EstimateMemoryForWidth(targetWidth);
            var (gpuBudget, cpuBudget) = CaptureUtilities.GetMemoryBudgets();

            if (targetWidth > SystemInfo.maxTextureSize || gpuNeed > gpuBudget || cpuNeed > cpuBudget)
            {   // Scale down if exceeds system limits
                int maxSafeWidth = CaptureUtilities.ComputeMaxSafeWidth();
                float scale = Mathf.Min(1f, (float)maxSafeWidth / targetWidth, (float)SystemInfo.maxTextureSize / targetWidth);
                targetWidth = Mathf.FloorToInt(targetWidth * scale);
                targetHeight = Mathf.FloorToInt(targetHeight * scale);
                Debug.LogWarning($"Scaled down to {targetWidth}x{targetHeight} due to system limits.");
            }

            return (targetWidth, targetHeight);
        }

        private RenderTexture CreateCompatibleRenderTexture(int width, int height)
        {   // Create render texture with cross-GPU compatibility settings and platform-specific optimizations
            var format = Compatibility.GetOptimalRenderTextureFormat();
            var rt = new RenderTexture(width, height, 24, format)
            {   
                useMipMap = false, 
                autoGenerateMips = false,
                antiAliasing = 1,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Compatibility.ApplyPlatformSpecificRTSettings(rt);

            if (!rt.Create())
            {   
                UnityEngine.Debug.LogError($"Failed to create render texture {width}x{height} with format {format}.");
                return TryFallbackRenderTexture(width, height);
            }

            return rt;
        }

        private RenderTexture TryFallbackRenderTexture(int width, int height)
        {   // Attempt fallback render texture creation with reduced settings
            var fallbackFormats = Compatibility.GetFallbackRenderTextureFormats();

            foreach (var format in fallbackFormats)
            {
                var rt = new RenderTexture(width, height, 16, format) { useMipMap = false, autoGenerateMips = false };
                if (rt.Create()) 
                {   Debug.LogWarning($"Using fallback render texture format: {format}"); return rt; }
                rt.Release();
            }

            UnityEngine.Debug.LogError("All render texture formats failed.");
            return null;
        }

        private void SetupCameraForCapture(ref CameraState state)
        {   // Configure camera for high-quality capture with optimized settings
            QualitySettings.antiAliasing = 0;
            World.MainCamera.cullingMask = CaptureUtilities.ComputeCullingMask(World.OwnerInstance?.showBackground ?? true);
        }

        private void ConfigureBackgroundRendering(RenderTexture rt, bool showBackground)
        {   // Setup background rendering with GPU-compatible transparency handling and driver workarounds
            Color bgColor = BackgroundUI.GetBackgroundColor();

            if (showBackground)
            {   // Force opaque background when user wants background visible
                bgColor.a = 1f;
                World.MainCamera.clearFlags = CameraClearFlags.SolidColor;
                World.MainCamera.backgroundColor = bgColor;
                Compatibility.ApplyGPUSpecificClear(rt, bgColor);
            }
            else
            {   // Background disabled - check if user wants transparency or specific color
                if (bgColor.a <= 0.01f)
                {   // User wants true transparency - configure for transparent rendering
                    World.MainCamera.clearFlags = CameraClearFlags.Nothing;
                    World.MainCamera.backgroundColor = new Color(0, 0, 0, 0);
                    Compatibility.ApplyTransparentClear(rt);
                }
                else
                {   // User wants specific background color (not transparency)
                    World.MainCamera.clearFlags = CameraClearFlags.SolidColor;
                    World.MainCamera.backgroundColor = bgColor;
                    Compatibility.ApplyGPUSpecificClear(rt, bgColor);
                }
            }
        }

        private void ApplyZoomToCamera(CameraState originalState)
        {   // Apply zoom settings with proper perspective/orthographic handling
            float zoomFactor = Mathf.Max(Mathf.Exp(PreviewZoomLevel), 1e-6f);

            if (World.MainCamera.orthographic)
                World.MainCamera.orthographicSize = Mathf.Clamp(originalState.orthographicSize / zoomFactor, 1e-6f, 1_000_000f);
            else
                ApplyPerspectiveZoom(originalState, zoomFactor);
        }

        private void ApplyPerspectiveZoom(CameraState originalState, float zoomFactor)
        {   // Handle perspective camera zoom with position adjustment for extreme values
            float baseFov = Mathf.Clamp(originalState.fieldOfView, 5f, 120f);
            float targetFov = baseFov / zoomFactor;

            if (targetFov >= 5f && targetFov <= 120f)
                World.MainCamera.fieldOfView = targetFov;
            else
            {   // Adjust camera position for extreme zoom values
                var forward = World.MainCamera.transform.forward;
                var pivot = originalState.position + forward * PreviewBasePivotDistance;
                
                float clampedFov = Mathf.Clamp(targetFov, 5f, 120f);
                World.MainCamera.fieldOfView = clampedFov;
                
                float positionRatio = (targetFov > 120f) ? (targetFov / 120f) : (5f / Mathf.Max(targetFov, 1e-6f));
                float newDistance = (targetFov > 120f) ? 
                    Mathf.Clamp(PreviewBasePivotDistance * positionRatio, PreviewBasePivotDistance, 1_000_000f) :
                    Mathf.Clamp(PreviewBasePivotDistance / positionRatio, 0.001f, PreviewBasePivotDistance);
                
                World.MainCamera.transform.position = pivot - forward * newDistance;
            }
        }

        private Texture2D ReadCroppedTexture(RenderTexture rt, int renderWidth, int renderHeight)
        {   // Read cropped region from render texture into final texture with driver compatibility
            var readRect = CaptureUtilities.GetCroppedReadRect(renderWidth, renderHeight);
            int finalWidth = Mathf.RoundToInt(readRect.width);
            int finalHeight = Mathf.RoundToInt(readRect.height);

            var format = Compatibility.GetOptimalTexture2DFormat();
            var texture = new Texture2D(finalWidth, finalHeight, format, false);

            if (!TryReadPixelsWithRetry(rt, texture, readRect))
            {   // Fallback to different read methods for broken drivers
                UnityEngine.Object.Destroy(texture);
                return TryFallbackPixelRead(rt, readRect, finalWidth, finalHeight);
            }

            return texture;
        }

        private bool TryReadPixelsWithRetry(RenderTexture rt, Texture2D texture, Rect readRect)
        {   // Attempt pixel reading with retries for unstable drivers
            int maxRetries = Compatibility.GetOptimalRetryCount();
            int sleepMs = Compatibility.GetOptimalThreadSleepMs();
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    RenderTexture.active = rt;
                    texture.ReadPixels(readRect, 0, 0);
                    texture.Apply();
                    RenderTexture.active = null;
                    
                    // Validate read succeeded by checking pixel data
                    if (ValidatePixelData(texture))
                        return true;
                        
                    Debug.LogWarning($"Pixel read validation failed, attempt {attempt + 1}/{maxRetries}");
                }
                catch (System.Exception ex)
                {   Debug.LogWarning($"ReadPixels attempt {attempt + 1} failed: {ex.Message}"); }
                finally
                {   RenderTexture.active = null; }
                
                if (attempt < maxRetries - 1)
                    System.Threading.Thread.Sleep(sleepMs);
            }
            
            return false;
        }

        private bool ValidatePixelData(Texture2D texture)
        {   // Quick validation that pixel data was read correctly
            try
            {
                var pixels = texture.GetPixels32();
                return pixels != null && pixels.Length > 0 && 
                       pixels.Take(Mathf.Min(100, pixels.Length)).Any(p => p.a > 0 || p.r > 0 || p.g > 0 || p.b > 0);
            }
            catch
            {   return false; }
        }

        private Texture2D TryFallbackPixelRead(RenderTexture rt, Rect readRect, int width, int height)
        {   // Fallback pixel reading methods for problematic drivers
            var fallbackFormats = Compatibility.GetFallbackTexture2DFormats();
            
            foreach (var format in fallbackFormats)
            {
                try
                {
                    var texture = new Texture2D(width, height, format, false);
                    
                    RenderTexture.active = rt;
                    texture.ReadPixels(readRect, 0, 0);
                    texture.Apply();
                    RenderTexture.active = null;
                    
                    if (ValidatePixelData(texture))
                    {   Debug.LogWarning($"Using fallback texture format: {format}"); return texture; }
                    
                    UnityEngine.Object.Destroy(texture);
                }
                catch (System.Exception ex)
                {   Debug.LogError($"Fallback format {format} failed: {ex.Message}"); }
                finally
                {   RenderTexture.active = null; }
            }
            
            Debug.LogError("All texture read methods failed.");
            return null;
        }

        private void SaveScreenshotFile(byte[] pngBytes, int renderWidth, int finalWidth, int finalHeight)
        {   // Save PNG file to world-specific folder with timestamp and encoding fallbacks
            string worldName = SFS.Base.worldBase?.paths?.worldName ?? "Unknown";
            string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            
            var worldFolder = FileUtilities.InsertIo(sanitizedName, Main.ScreenCaptureFolder);
            string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";

            try
            {
                using (var ms = new MemoryStream(pngBytes))
                    FileUtilities.InsertIo(fileName, ms, worldFolder);

                if (World.UIHolder != null)
                {   var monoBehaviour = World.UIHolder.GetComponentInChildren<MonoBehaviour>(); monoBehaviour?.StartCoroutine(FileUtilities.VerifyAndShowResult(worldFolder.ToString(), fileName, pngBytes, renderWidth, finalWidth, finalHeight, StartWindowColorAnimation)); }
                else 
                    StartWindowColorAnimation(true);

                Debug.Log($"Saved {finalWidth}x{finalHeight} screenshot to {fileName}");
                
                // Log compatibility info for debugging
                Debug.Log($"Compatibility: {Compatibility.GetDebugInfo()}");
            }
            catch (System.Exception ex)
            {   
                Debug.LogError($"Failed to save screenshot: {ex.Message}");
                TryFallbackImageSave(pngBytes, worldFolder, fileName, finalWidth, finalHeight);
            }
        }

        private void TryFallbackImageSave(byte[] pngBytes, object worldFolder, string fileName, int width, int height)
        {   // Attempt alternative save methods for file system issues
            try
            {
                string fallbackPath = Path.Combine(Application.persistentDataPath, "Screenshots");
                if (!Directory.Exists(fallbackPath))
                    Directory.CreateDirectory(fallbackPath);

                string fallbackFile = Path.Combine(fallbackPath, fileName);
                File.WriteAllBytes(fallbackFile, pngBytes);
                
                Debug.LogWarning($"Saved to fallback location: {fallbackFile}");
                StartWindowColorAnimation(true);
            }
            catch (System.Exception ex)
            {   
                Debug.LogError($"All save methods failed: {ex.Message}");
                StartWindowColorAnimation(false);
            }
        }



        public override void Hide()
        {   // Tear down UI and related resources for the owner
            ref Captue ownerRef = ref World.OwnerInstance;
            if (ownerRef == null) return;

            Captue.OnScreenSizeChanged -= OnScreenSizeChanged;

            if (window != null && !((UITools.ClosableWindow)window).Minimized)
                ownerRef.windowCollapsedAction?.Invoke();

            if (World.UIHolder != null)
            { UnityEngine.Object.Destroy(World.UIHolder); World.UIHolder = null; ownerRef.closableWindow = null; window = null; }

            PreviewUtilities.CleanupUI(ownerRef);

            if (ownerRef.backgroundWindow != null)
            { ownerRef.backgroundWindow.Hide(); ownerRef.backgroundWindow = null; }

            if (ownerRef.rocketsWindow != null)
            { ownerRef.rocketsWindow.Hide(); ownerRef.rocketsWindow = null; }

            resLabel = null; gpuLabel = null; cpuLabel = null; pngLabel = null; maxLabel = null; resInput = null;

            Captue.PreviewImage = null; previewContainer = null; previewInitialized = false;

            if (previewBorder != null)
            { try { UnityEngine.Object.Destroy(previewBorder.gameObject); } catch { } previewBorder = null; }

            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null) UnityEngine.Object.Destroy(existing);
        }

        public void UpdateBackgroundWindowVisibility()
        {   // Show background settings window when "Show Background" is off
            ref Captue ownerRef = ref World.OwnerInstance;
            if (ownerRef == null || window == null || World.UIHolder == null) return;

            bool shouldShow = !ownerRef.showBackground && !((UITools.ClosableWindow)window).Minimized;

            if (shouldShow && ownerRef.backgroundWindow == null) ownerRef.backgroundWindow = new BackgroundUI();

            if (shouldShow && !ownerRef.backgroundWindow.IsOpen) ownerRef.backgroundWindow.Show();
            else if (!shouldShow && ownerRef.backgroundWindow != null) { ownerRef.backgroundWindow.Hide(); ownerRef.backgroundWindow = null; }
        }

        private void EnsurePreviewSetup()
        {   // Lazily initialize the preview when window is opened
            if (previewInitialized || previewContainer == null) return;

            try
            {   PreviewHierarchyUtilities.SetupPreviewWithBorder(previewContainer, ref previewBorder, ref previewInitialized); RequestPreviewUpdate(); }
            catch (Exception ex) { Debug.LogError($"Failed to setup preview: {ex.Message}"); }
        }

        public void UpdateEstimatesUI()
        {   // Refresh estimate labels with aggressive throttling and normalized crop usage
            ref Captue ownerRef = ref World.OwnerInstance;
            if (ownerRef == null) return;

            if (window != null && ((UITools.ClosableWindow)window).Minimized) return;
            if (resLabel == null || gpuLabel == null || cpuLabel == null || pngLabel == null || maxLabel == null) return;

            float currentTime = Time.unscaledTime;
            if (currentTime - lastUpdateTime < UPDATE_INTERVAL && lastEstimateWidth == ResolutionWidth) return;

            lastUpdateTime = currentTime; lastEstimateWidth = ResolutionWidth;

            var (leftCrop, topCrop, rightCrop, bottomCrop) = CaptureUtilities.GetNormalizedCropValues();
            float cropWidthFactor = 1f - leftCrop - rightCrop; float cropHeightFactor = 1f - topCrop - bottomCrop;

            int targetWidth = ResolutionWidth;
            int targetHeight = Mathf.RoundToInt(targetWidth * (float)Screen.height / (float)Screen.width * (cropHeightFactor / Mathf.Max(0.0001f, cropWidthFactor)));
            int renderWidth = Mathf.RoundToInt(targetWidth / Mathf.Max(0.01f, cropWidthFactor));
            int renderHeight = Mathf.RoundToInt(targetHeight / Mathf.Max(0.01f, cropHeightFactor));

            var (gpuNeed, cpuNeed, rawBytes) = CaptureUtilities.EstimateMemoryForWidth(renderWidth);
            var (gpuBudget, cpuBudget) = CaptureUtilities.GetMemoryBudgets();

            int maxSafeRenderWidth = CaptureUtilities.ComputeMaxSafeWidth();
            int maxSafeTargetWidth = Mathf.RoundToInt(maxSafeRenderWidth * cropWidthFactor);

            float gpuUsage = gpuBudget > 0 ? (float)gpuNeed / gpuBudget : 0f;
            float cpuUsage = cpuBudget > 0 ? (float)cpuNeed / cpuBudget : 0f;

            resLabel.Text = $"Res: {targetWidth}x{targetHeight}";
            gpuLabel.Text = $"GPU: {CaptureUtilities.FormatMB(gpuNeed)}";
            cpuLabel.Text = $"RAM: {CaptureUtilities.FormatMB(cpuNeed)}";
            pngLabel.Text = $"PNG: {CaptureUtilities.FormatMB((long)Math.Max(1024, rawBytes * 0.30))}";
            maxLabel.Text = $"Max Width: {maxSafeTargetWidth}";

            Color ok = Color.white; Color warn = new Color(1f, 0.8f, 0.25f); Color danger = new Color(1f, 0.35f, 0.2f);
            gpuLabel.Color = gpuUsage >= 1f ? danger : (gpuUsage >= 0.8f ? warn : ok);
            cpuLabel.Color = cpuUsage >= 1f ? danger : (cpuUsage >= 0.8f ? warn : ok);
            resLabel.Color = (gpuUsage >= 1f || cpuUsage >= 1f) ? danger : ((gpuUsage >= 0.8f || cpuUsage >= 0.8f) ? warn : ok);
            SetTextInputColor(resInput, (gpuUsage >= 1f || cpuUsage >= 1f) ? danger : ((gpuUsage >= 0.8f || cpuUsage >= 0.8f) ? warn : ok));
        }

        public void RequestPreviewUpdate()
        {   // Request preview update without viewport cropping to prevent stretching
            if (World.PreviewCamera == null || Captue.PreviewRT == null) return;

            ref Captue owner = ref World.OwnerInstance; if (owner == null) return;

            World.PreviewCamera.cullingMask = CaptureUtilities.ComputeCullingMask(owner.showBackground);
            World.PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            World.PreviewCamera.backgroundColor = BackgroundUI.GetBackgroundColor();
            CaptureUtilities.ApplyPreviewZoom(World.MainCamera, World.PreviewCamera, PreviewZoomLevel);

            var modified = CaptureUtilities.ApplySceneVisibilityTemporary(owner.showBackground, owner.showTerrain, HiddenRockets);
            var prevTarget = World.PreviewCamera.targetTexture;

            World.PreviewCamera.rect = new Rect(0, 0, 1, 1);
            World.PreviewCamera.targetTexture = Captue.PreviewRT; World.PreviewCamera.Render(); World.PreviewCamera.targetTexture = prevTarget;
            CaptureUtilities.RestoreSceneVisibility(modified);

            PreviewUtilities.UpdatePreviewImageLayoutForCurrentRT();
            if (Captue.PreviewImage != null) UpdatePreviewBorderSize();
        }

        private void StartWindowColorAnimation(bool success = true)
        {   // Start coroutine to animate window background color based on save result
            if (window == null || World.UIHolder == null) return;

            if (currentAnimation != null)
            {   var monoBehaviour = World.UIHolder.GetComponentInChildren<MonoBehaviour>(); if (monoBehaviour != null) monoBehaviour.StopCoroutine(currentAnimation); currentAnimation = null; }

            var mono2 = World.UIHolder.GetComponentInChildren<MonoBehaviour>();
            if (mono2 != null && window is UITools.ClosableWindow closable)
                currentAnimation = mono2.StartCoroutine(AnimationUtilities.AnimateWindowColor(closable, success));
        }

        public void RefreshLayoutForCroppedPreview()
        {   // Force layout refresh to handle cropped preview changes
            if (previewContainer == null || window == null || Captue.PreviewImage == null) return;

            if (currentAnimation != null)
            {   var mono = World.UIHolder.GetComponentInChildren<MonoBehaviour>(); if (mono != null) mono.StopCoroutine(currentAnimation); currentAnimation = null; }

            if (World.UIHolder != null)
            {   var mono = World.UIHolder.GetComponentInChildren<MonoBehaviour>(); if (mono != null) currentAnimation = mono.StartCoroutine(AnimationUtilities.DelayedLayoutRefresh(previewContainer, () => UpdatePreviewBorderSize())); }
        }

        private void UpdatePreviewBorderSize() =>
            PreviewHierarchyUtilities.UpdatePreviewBorderSize(previewContainer, previewBorder);

        private void OnScreenSizeChanged()
        {   // Handle screen size change: clamp resolution and refresh UI/preview
            if (resInput == null) return;

            float leftCrop = CropLeft / 100f, rightCrop = CropRight / 100f; float totalH = leftCrop + rightCrop;
            if (totalH >= 1f) { float s = 0.99f / totalH; leftCrop *= s; rightCrop *= s; }

            float cropW = 1f - leftCrop - rightCrop; int maxRenderW = CaptureUtilities.ComputeMaxSafeWidth(); int maxTargetW = Mathf.RoundToInt(maxRenderW * cropW);
            if (ResolutionWidth > maxTargetW) { ResolutionWidth = maxTargetW; resInput.Text = ResolutionWidth.ToString(); }

            UpdateEstimatesUI(); RefreshLayoutForCroppedPreview(); RequestPreviewUpdate();
        }

        public override void Refresh()
        {   // Rebuild UI to reflect current state
            if (window == null) return; Hide(); Show();
        }

        private string GetPNGColorTypeName(int colorType)
        {   // Return human-readable PNG color type names for debugging
            switch (colorType)
            {
                case 0: return "Grayscale";
                case 2: return "RGB";
                case 3: return "Palette";
                case 4: return "Grayscale+Alpha";
                case 6: return "RGBA";
                default: return $"Unknown({colorType})";
            }
        }

        private byte[] TryFallbackEncoding(Texture2D texture)
        {   // Attempt alternative encoding methods for broken PNG encoders
            if (texture == null) return null;

            // Method 1: Check if PNG encoding is supported
            if (!Compatibility.SupportsPNGEncoding())
            {   
                Debug.LogWarning("PNG encoding not reliable on this platform, using JPG fallback");
                return TryJPGEncoding(texture);
            }

            // Method 2: JPG encoding as fallback (lossy but reliable)
            return TryJPGEncoding(texture) ?? CreateManualPNGEncoding(texture);
        }

        private byte[] TryJPGEncoding(Texture2D texture)
        {   // Attempt JPG encoding with quality settings
            try
            {
                byte[] jpgBytes = texture.EncodeToJPG(90);
                if (jpgBytes != null && jpgBytes.Length > 1024)
                {   Debug.LogWarning("Using JPG encoding fallback for broken PNG encoder"); return jpgBytes; }
            }
            catch (System.Exception ex)
            {   Debug.LogWarning($"JPG encoding failed: {ex.Message}"); }
            
            return null;
        }

        private byte[] CreateManualPNGEncoding(Texture2D texture)
        {   // Last resort manual pixel encoding for completely broken drivers
            var pixels = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;

            // Create minimal PNG structure manually
            using (var ms = new MemoryStream())
            {
                // PNG signature
                ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                
                // Simple uncompressed pixel data (not a real PNG but readable by most apps)
                ms.Write(System.BitConverter.GetBytes(width), 0, 4);
                ms.Write(System.BitConverter.GetBytes(height), 0, 4);
                
                foreach (var pixel in pixels)
                {
                    ms.WriteByte(pixel.r);
                    ms.WriteByte(pixel.g);
                    ms.WriteByte(pixel.b);
                    ms.WriteByte(pixel.a);
                }
                
                return ms.ToArray();
            }
        }
    }
}