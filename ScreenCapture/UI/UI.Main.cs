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
        {   // Capture scene at requested resolution with optional cropping
            if (World.MainCamera == null) { StartWindowColorAnimation(false); return; }

            RenderTexture fullRT = null;
            Texture2D finalTex = null;

            var prevClearFlags = World.MainCamera.clearFlags;
            var prevBgColor = World.MainCamera.backgroundColor;
            var previousOrthographicSize = World.MainCamera.orthographicSize;
            var previousFieldOfView = World.MainCamera.fieldOfView;
            var previousPosition = World.MainCamera.transform.position;

            // Get target dimensions from current width setting
            int targetWidth = PreviewWidth;  // Use current preview width setting
            int targetHeight = Mathf.RoundToInt((float)targetWidth / Mathf.Max(1, (float)Screen.width) * (float)Screen.height);

            try
            {   // Render scene at requested resolution, then crop via ReadPixels
                // Use requested target dimensions as render dimensions
                int renderWidth = targetWidth;
                int renderHeight = targetHeight;

                // Safety limits check
                int maxTextureSize = SystemInfo.maxTextureSize;
                var (gpuNeed, cpuNeed, rawBytes) = CaptureUtilities.EstimateMemoryForWidth(renderWidth);
                var (gpuBudget, cpuBudget) = CaptureUtilities.GetMemoryBudgets();

                if (renderWidth > maxTextureSize || renderHeight > maxTextureSize || gpuNeed > gpuBudget || cpuNeed > cpuBudget)
                {   // Scale down if exceeds limits
                    int maxSafeWidth = CaptureUtilities.ComputeMaxSafeWidth();
                    float scale = Mathf.Min(1f, (float)maxSafeWidth / renderWidth, (float)maxTextureSize / renderWidth);
                    
                    renderWidth = Mathf.FloorToInt(renderWidth * scale);
                    renderHeight = Mathf.FloorToInt(renderHeight * scale);
                    
                    Debug.LogWarning($"Requested {targetWidth}x{targetHeight} exceeds limits. Rendering at {renderWidth}x{renderHeight}.");
                }

                fullRT = new RenderTexture(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32);
                
                if (!fullRT.Create())
                {
                    UnityEngine.Debug.LogError($"Failed to create render texture {renderWidth}x{renderHeight}. Aborting capture.");
                    StartWindowColorAnimation(false);
                    return;
                }

                // Apply zoom and camera settings
                float z = Mathf.Max(Mathf.Exp(PreviewZoomLevel), 1e-6f);

                if (World.MainCamera.orthographic)
                    World.MainCamera.orthographicSize = Mathf.Clamp(previousOrthographicSize / z, 1e-6f, 1_000_000f);
                else
                {   // Perspective zoom handling
                    float baseFov = Mathf.Clamp(previousFieldOfView, 5f, 120f);
                    float rawFov = baseFov / z;

                    if (rawFov >= 5f && rawFov <= 120f)
                        World.MainCamera.fieldOfView = rawFov;
                    else if (rawFov > 120f)
                    {
                        World.MainCamera.fieldOfView = 120f;
                        var fwd = World.MainCamera.transform.forward;
                        var pivot = previousPosition + fwd * PreviewBasePivotDistance;
                        float ratio = rawFov / 120f;
                        float newDist = Mathf.Clamp(PreviewBasePivotDistance * ratio, PreviewBasePivotDistance, 1_000_000f);
                        World.MainCamera.transform.position = pivot - fwd * newDist;
                    }
                    else
                    {
                        World.MainCamera.fieldOfView = 5f;
                        var fwd = World.MainCamera.transform.forward;
                        var pivot = previousPosition + fwd * PreviewBasePivotDistance;
                        float ratio = 5f / Mathf.Max(rawFov, 1e-6f);
                        float newDist = Mathf.Clamp(PreviewBasePivotDistance / ratio, 0.001f, PreviewBasePivotDistance);
                        World.MainCamera.transform.position = pivot - fwd * newDist;
                    }
                }

                QualitySettings.antiAliasing = 0;

                var prevMask = World.MainCamera.cullingMask;
                World.MainCamera.cullingMask = CaptureUtilities.ComputeCullingMask(World.OwnerInstance?.showBackground ?? true);
                World.MainCamera.clearFlags = CameraClearFlags.SolidColor;
                World.MainCamera.backgroundColor = BackgroundUI.GetBackgroundColor();

                var modified = CaptureUtilities.ApplySceneVisibilityTemporary(World.OwnerInstance?.showBackground ?? true, World.OwnerInstance?.showTerrain ?? true, Main.HiddenRockets);

                // Render full scene without viewport cropping
                World.MainCamera.rect = new Rect(0, 0, 1, 1);
                World.MainCamera.targetTexture = fullRT;
                World.MainCamera.Render();

                CaptureUtilities.RestoreSceneVisibility(modified);
                World.MainCamera.cullingMask = prevMask;
                World.MainCamera.clearFlags = prevClearFlags;
                World.MainCamera.backgroundColor = prevBgColor;

                if (fullRT == null || !fullRT.IsCreated())
                {
                    UnityEngine.Debug.LogError("Render texture is invalid. Cannot read pixels.");
                    StartWindowColorAnimation(false);
                    return;
                }

                // Read cropped area from rendered texture
                var readRect = CaptureUtilities.GetCroppedReadRect(renderWidth, renderHeight);
                
                int finalWidth = Mathf.RoundToInt(readRect.width);
                int finalHeight = Mathf.RoundToInt(readRect.height);
                finalTex = new Texture2D(finalWidth, finalHeight, TextureFormat.RGBA32, false);

                RenderTexture.active = fullRT;
                finalTex.ReadPixels(readRect, 0, 0);
                finalTex.Apply();
                RenderTexture.active = null;

                byte[] pngBytes = finalTex.EncodeToPNG();
                
                if (pngBytes == null || pngBytes.Length < 1024)
                {
                    UnityEngine.Debug.LogError("PNG encoding failed or resulted in suspiciously small file.");
                    StartWindowColorAnimation(false);
                    return;
                }

                // Save file
                string worldName = (SFS.Base.worldBase?.paths?.worldName) ?? "Unknown";
                string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                                      new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                var worldFolder = FileUtilities.InsertIo(sanitizedName, Main.ScreenCaptureFolder);
                string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";

                using (var ms = new MemoryStream(pngBytes))
                    FileUtilities.InsertIo(fileName, ms, worldFolder);

                string worldFolderPath = worldFolder.ToString();

                if (World.UIHolder != null)
                {
                    var monoBehaviour = World.UIHolder.GetComponentInChildren<MonoBehaviour>();
                    if (monoBehaviour != null)
                        monoBehaviour.StartCoroutine(FileUtilities.VerifyAndShowResult(worldFolderPath, fileName, pngBytes, renderWidth, finalWidth, finalHeight, new System.Action<bool>(StartWindowColorAnimation)));
                }
                else StartWindowColorAnimation(true);

                var (finalGpuNeed, finalCpuNeed, finalRawBytes) = CaptureUtilities.EstimateMemoryForWidth(renderWidth);
                Debug.Log($"Saved {finalWidth}x{finalHeight} (render {renderWidth}x{renderHeight}). Memory: GPU {CaptureUtilities.FormatMB(finalGpuNeed)}, CPU {CaptureUtilities.FormatMB(finalCpuNeed)}; file {CaptureUtilities.FormatMB(pngBytes.LongLength)}.");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Screenshot failed: {ex.Message}");
                StartWindowColorAnimation(false);
            }
            finally
            {   // Restore camera state
                World.MainCamera.rect = new Rect(0, 0, 1, 1);
                World.MainCamera.targetTexture = null;
                World.MainCamera.orthographicSize = previousOrthographicSize;
                World.MainCamera.fieldOfView = previousFieldOfView;
                World.MainCamera.transform.position = previousPosition;

                if (fullRT != null) { fullRT.Release(); UnityEngine.Object.Destroy(fullRT); }
                if (finalTex != null) UnityEngine.Object.Destroy(finalTex);
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
    }
}