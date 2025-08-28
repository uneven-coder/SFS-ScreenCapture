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
            // Note: This might need adjustment based on the actual ClosableWindow event structure
            ref Action windowOpenedRef = ref World.OwnerInstance.windowOpenedAction;
            ref Action windowCollapsedRef = ref World.OwnerInstance.windowCollapsedAction;

            // Store previous actions to chain them
            var prevOpen = windowOpenedRef;
            var prevCollapse = windowCollapsedRef;

            windowOpenedRef = () =>
            {
                prevOpen?.Invoke();
                EnsurePreviewSetup();
                UpdateEstimatesUI();
                RequestPreviewUpdate();  // Use local method instead of owner method
                Debug.Log("Window opened action triggered");
            };

            windowCollapsedRef = () =>
            {
                prevCollapse?.Invoke();
                Debug.Log("Window collapsed action triggered");
            };
        }

        private Container CreateToolsContainer()
        {   // Create preview and side toggles
            return CreateContainer(window, SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, 12f, null, true, toolsContainer =>
            {
                // Create preview container with fixed width and dynamic height
                previewContainer = CreateNestedContainer(toolsContainer, SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 0f, new RectOffset(6, 6, 6, 6), true);

                // Reserve fixed width for preview container so layout doesn't shift when preview resizes
                var previewLE = previewContainer.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ?? previewContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                previewLE.preferredWidth = 520f;
                previewLE.minWidth = 520f;
                previewLE.flexibleWidth = 0f;
                previewLE.preferredHeight = -1f; // Let height adjust to content

                // Setup preview immediately
                try
                {
                    // Simple single-pass preview setup
                    SetupPreview(previewContainer);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to initialize preview: {ex.Message}");
                    previewInitialized = false;
                }

                // Flexible spacer to push the controls panel to the right edge regardless of preview size
                var spacer = CreateNestedContainer(toolsContainer, SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 0f, null, false);
                var spacerLE = spacer.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ?? spacer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                spacerLE.flexibleWidth = 1f;
                spacerLE.minWidth = 0f;
                spacerLE.preferredWidth = 0f;

                CreateNestedVertical(toolsContainer, 20f, null, TextAnchor.UpperLeft, controlsContainer =>
                {
                    // Fix controls panel width so it doesn't stretch and remains aligned right
                    var le = controlsContainer.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ?? controlsContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    le.preferredWidth = 390f;
                    le.flexibleWidth = 0f;

                    Builder.CreateLabel(controlsContainer, 390, 36, 0, 0, "Visuals");

                    // Compact toggles arranged in two columns (2x2 grid)
                    CreateNestedHorizontal(controlsContainer, 12f, null, TextAnchor.UpperLeft, cols =>
                    {
                        // Left column
                        CreateNestedVertical(cols, 14f, null, TextAnchor.UpperLeft, leftCol =>
                        {
                            CreateCompactToggle(leftCol, "Background", () => World.OwnerInstance?.showBackground ?? true, () =>
                            {   // Toggle background and update visibility
                                ref Captue owner = ref World.OwnerInstance;
                                if (owner != null)
                                {
                                    owner.showBackground = !owner.showBackground;
                                    UpdateBackgroundWindowVisibility();
                                }
                            });

                            CreateCompactToggle(leftCol, "Interiors", () => SFS.InteriorManager.main?.interiorView?.Value ?? true, () =>
                            {   // Toggle global interior visibility using the game's InteriorManager
                                CaptureUtilities.ToggleInteriorView();
                            });
                        });

                        // Right column
                        CreateNestedVertical(cols, 14f, null, TextAnchor.UpperLeft, rightCol =>
                        {
                            CreateCompactToggle(rightCol, "Terrain", () => World.OwnerInstance?.showTerrain ?? true, () =>
                            {   // Toggle terrain visibility
                                ref Captue owner = ref World.OwnerInstance;
                                if (owner != null) owner.showTerrain = !owner.showTerrain;
                            });

                            CreateCompactToggle(rightCol, "Rockets", () => World.OwnerInstance?.rocketsWindow?.IsOpen ?? false, () =>
                            {   // Show/hide rockets window
                                ref Captue owner = ref World.OwnerInstance;
                                if (owner != null)
                                {
                                    ref var rocketWindow = ref owner.rocketsWindow;
                                    CaptureUtilities.ShowHideWindow<RocketsUI>(ref rocketWindow,
                                        () => { },
                                        () => { });
                                }
                            });
                        });
                    });

                    CreateNestedVertical(controlsContainer, 2f, null, TextAnchor.UpperCenter, cropControls =>
                    {
                        CaptureUtilities.CreateCropControls(cropControls, () =>
                        {   // Unified crop change handler with preview update
                            CaptureUtilities.UpdatePreviewCropping();
                            RefreshLayoutForCroppedPreview();
                            UpdateEstimatesUI();
                            RequestPreviewUpdate();  // Use local method instead of owner method
                        });
                    });
                });
            });
        }

        private Container CreateControlsContainer()
        {   // Create capture controls and show estimates above them
            var MainCol = CreateVerticalContainer(window, 8f, null, TextAnchor.LowerLeft);

            var controls = CreateNestedContainer(MainCol,
                SFS.UI.ModGUI.Type.Horizontal, TextAnchor.LowerLeft, 30f, null, true);

            CreateNestedVertical(controls, 5f, null, TextAnchor.LowerLeft, leftRow =>
            {
                CaptureUtilities.CreateTimeControls(leftRow);

                CreateNestedHorizontal(leftRow, 10f, null, TextAnchor.LowerLeft, captureRow =>
                {
                    Builder.CreateButton(captureRow, 180, 58, 0, 0, () => TakeScreenshot(), "Capture");
                    resInput = Builder.CreateTextInput(captureRow, 180, 58, 0, 0, ResolutionWidth.ToString(), OnResolutionInputChange);
                });
            });

            Builder.CreateSpace(controls, 240, 0);

            var ZoomRow = CreateNestedVertical(controls, 5f, null, TextAnchor.UpperLeft);
            CreateZoomControls(ZoomRow);

            CreateNestedHorizontal(MainCol,     103f, null, TextAnchor.UpperLeft, helpRow =>
            {
                CreateNestedVertical(helpRow, 5f, null, TextAnchor.UpperLeft, helpCol =>
                {
                    CreateNestedHorizontal(helpCol, 10f, null, TextAnchor.UpperLeft, statsRow1 =>
                    {
                        resLabel = Builder.CreateLabel(statsRow1, 210, 34, 0, 0, "Res: -");
                        gpuLabel = Builder.CreateLabel(statsRow1, 170, 34, 0, 0, "GPU: -");
                        cpuLabel = Builder.CreateLabel(statsRow1, 170, 34, 0, 0, "RAM: -");
                    });

                    CreateNestedHorizontal(helpCol, 10f, null, TextAnchor.UpperLeft, statsRow2 =>
                    {
                        pngLabel = Builder.CreateLabel(statsRow2, 170, 34, 0, 0, "PNG: -");
                        maxLabel = Builder.CreateLabel(statsRow2, 220, 34, 0, 0, "Max Width: -");
                        // warnLabel = Builder.CreateLabel(statsRow2, 400, 34, 0, 0, "");
                        // warnLabel.Color = new Color(1f, 0.35f, 0.2f);
                    });

                });

                CreateNestedHorizontal(helpRow, 10f, null, TextAnchor.UpperLeft, bottomRow =>
                {
                    Builder.CreateButton(bottomRow, 285, 58, 0, 0, () => FileUtilities.OpenCurrentWorldCapturesFolder(), "Open Captures");
                });
            });



            return controls;
        }

        private void CreateZoomControls(Container ZoomRow)
        {
            float GetZoom() => Mathf.Clamp(PreviewZoom, 0.25f, 4f);  // Button/display factor in safe range
            float GetLevel() => PreviewZoomLevel;  // Unbounded level for input

            void SetZoom(float z)
            {   // Set zoom and update level based on factor with preview refresh
                PreviewZoom = z;
                PreviewZoomLevel = Mathf.Log(z);
                RequestPreviewUpdate();  // Use local method instead of owner method
            }

            float StepInLog(float z, int dir)
            {   // Compute next zoom via log-space lerp across [0.25, 4] using discrete steps
                float min = 0.25f, max = 4f;
                int steps = 20;
                float lnMin = Mathf.Log(min), lnMax = Mathf.Log(max);
                float t = Mathf.InverseLerp(lnMin, lnMax, Mathf.Log(Mathf.Clamp(z, min, max)));
                int i = Mathf.Clamp(Mathf.RoundToInt(t * steps) + dir, 0, steps);
                float factor = Mathf.Exp(Mathf.Lerp(lnMin, lnMax, (float)i / steps));
                return Mathf.Abs(factor - 1f) <= 0.02f ? 1f : factor;
            }

            InputWithLabel zoomInput = null;
            zoomInput = Builder.CreateInputWithLabel(ZoomRow, (140 * 2 + 10), 52, 0, 0, "Zoom", $"{GetLevel():0.00}", val =>
            {   // Parse unbounded zoom level from input
                if (string.IsNullOrWhiteSpace(val))
                    return;

                if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float lvl))
                {
                    PreviewZoomLevel = lvl;
                    zoomInput.textInput.Text = $"{GetLevel():0.00}";
                    UpdateEstimatesUI();
                    RequestPreviewUpdate();  // Use local method instead of owner method
                }
            });

            var bottomRow = CreateNestedHorizontal(ZoomRow, 10f, null, TextAnchor.MiddleLeft);

            Builder.CreateButton(bottomRow, 140, 58, 0, 0, () =>
            {   // Decrease zoom using non-linear step
                float z = StepInLog(GetZoom(), -1);
                SetZoom(z);
                zoomInput.textInput.Text = $"{GetLevel():0.00}";
                UpdateEstimatesUI();
            }, "Zoom -");

            Builder.CreateButton(bottomRow, 140, 58, 0, 0, () =>
            {   // Increase zoom using non-linear step
                float z = StepInLog(GetZoom(), +1);
                SetZoom(z);
                zoomInput.textInput.Text = $"{GetLevel():0.00}";
                UpdateEstimatesUI();
            }, "Zoom +");
        }

        public override void Hide()
        {   // Tear down UI and related resources for the owner
            ref Captue ownerRef = ref World.OwnerInstance;

            if (ownerRef == null)
                return;

            // Unsubscribe from screen size changes
            Captue.OnScreenSizeChanged -= OnScreenSizeChanged;

            if (window != null && !((UITools.ClosableWindow)window).Minimized)
                ownerRef.windowCollapsedAction?.Invoke();

            if (World.UIHolder != null)
            {
                UnityEngine.Object.Destroy(World.UIHolder);
                World.UIHolder = null;
                ownerRef.closableWindow = null;
                window = null;
            }

            // Centralized cleanup of UI resources
            CaptureUtilities.CleanupUI(ownerRef);

            // Hide child windows
            if (ownerRef.backgroundWindow != null)
            {
                ownerRef.backgroundWindow.Hide();
                ownerRef.backgroundWindow = null;
            }

            if (ownerRef.rocketsWindow != null)
            {
                ownerRef.rocketsWindow.Hide();
                ownerRef.rocketsWindow = null;
            }

            resLabel = null; gpuLabel = null; cpuLabel = null; pngLabel = null; maxLabel = null; resInput = null; 

            Captue.PreviewImage = null;
            previewContainer = null;
            previewInitialized = false;

            // Destroy preview border if present
            if (previewBorder != null)
            {
                try { UnityEngine.Object.Destroy(previewBorder.gameObject); } catch { }
                previewBorder = null;
            }

            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null)
                UnityEngine.Object.Destroy(existing);

            // Do not null the global OwnerInstance here; it is a persistent component
            // ownerRef = null;
        }

        public void UpdateBackgroundWindowVisibility()
        {   // Show background settings window when "Show Background" is off
            ref Captue ownerRef = ref World.OwnerInstance;
            
            // Prevent showing background window if main UI is hidden or being destroyed
            if (ownerRef == null || window == null || World.UIHolder == null)
                return;

            // Only show background window when background is disabled and main window isn't minimized
            bool shouldShow = !ownerRef.showBackground && !((UITools.ClosableWindow)window).Minimized;

            if (shouldShow && ownerRef.backgroundWindow == null)
                ownerRef.backgroundWindow = new BackgroundUI();

            if (shouldShow && !ownerRef.backgroundWindow.IsOpen)
                ownerRef.backgroundWindow.Show();
            else if (!shouldShow && ownerRef.backgroundWindow != null)
            {   // Hide background window when no longer needed
                ownerRef.backgroundWindow.Hide();
                ownerRef.backgroundWindow = null;
            }
        }

        private void EnsurePreviewSetup()
        {   // Lazily initialize the preview when window is opened
            if (previewInitialized || previewContainer == null)
                return;

            try
            {
                // Simple single-pass preview setup
                SetupPreview(previewContainer);
                
                // Immediately apply proper scaling and update after setup
                RequestPreviewUpdate();
                
                Debug.Log("Preview setup completed successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to setup preview: {ex.Message}");
            }
        }

        public void UpdateEstimatesUI()
        {   // Refresh estimate labels with aggressive throttling and normalized crop usage
            ref Captue ownerRef = ref World.OwnerInstance;
            if (ownerRef == null)
                return;

            // Skip updates when window is minimized to save performance
            if (window != null && ((UITools.ClosableWindow)window).Minimized)
                return;

            if (resLabel == null || gpuLabel == null || cpuLabel == null || pngLabel == null || maxLabel == null)
                return;

            // More aggressive throttling to reduce CPU usage
            float currentTime = Time.unscaledTime;
            if (currentTime - lastUpdateTime < UPDATE_INTERVAL && lastEstimateWidth == ResolutionWidth)
                return;

            lastUpdateTime = currentTime;
            lastEstimateWidth = ResolutionWidth;

            var (leftCrop, topCrop, rightCrop, bottomCrop) = CaptureUtilities.GetNormalizedCropValues();

            float cropWidthFactor = 1f - leftCrop - rightCrop;
            float cropHeightFactor = 1f - topCrop - bottomCrop;

            int targetWidth = ResolutionWidth;
            int targetHeight = Mathf.RoundToInt((float)targetWidth / (float)Screen.width * (float)Screen.height);
            int renderWidth = Mathf.RoundToInt(targetWidth / Mathf.Max(0.01f, cropWidthFactor));
            int renderHeight = Mathf.RoundToInt(targetHeight / Mathf.Max(0.01f, cropHeightFactor));

            var (gpuNeed, cpuNeed, rawBytes) = CaptureUtilities.EstimateMemoryForWidth(renderWidth);
            var (gpuBudget, cpuBudget) = CaptureUtilities.GetMemoryBudgets();

            // Calculate max safe width accounting for crop
            int maxSafeRenderWidth = CaptureUtilities.ComputeMaxSafeWidth();
            int maxSafeTargetWidth = Mathf.RoundToInt(maxSafeRenderWidth * cropWidthFactor);

            float gpuUsage = gpuBudget > 0 ? (float)gpuNeed / gpuBudget : 0f;
            float cpuUsage = cpuBudget > 0 ? (float)cpuNeed / cpuBudget : 0f;

            resLabel.Text = $"Res: {targetWidth}x{targetHeight}";
            gpuLabel.Text = $"GPU: {CaptureUtilities.FormatMB(gpuNeed)}";
            cpuLabel.Text = $"RAM: {CaptureUtilities.FormatMB(cpuNeed)}";
            pngLabel.Text = $"PNG: {CaptureUtilities.FormatMB((long)Math.Max(1024, rawBytes * 0.30))}";
            maxLabel.Text = $"Max Width: {maxSafeTargetWidth}";

            Color ok = Color.white;
            Color warn = new Color(1f, 0.8f, 0.25f);
            Color danger = new Color(1f, 0.35f, 0.2f);

            gpuLabel.Color = gpuUsage >= 1f ? danger : (gpuUsage >= 0.8f ? warn : ok);
            cpuLabel.Color = cpuUsage >= 1f ? danger : (cpuUsage >= 0.8f ? warn : ok);
            resLabel.Color = (gpuUsage >= 1f || cpuUsage >= 1f) ? danger : ((gpuUsage >= 0.8f || cpuUsage >= 0.8f) ? warn : ok);

            SetTextInputColor(resInput, (gpuUsage >= 1f || cpuUsage >= 1f) ? danger : ((gpuUsage >= 0.8f || cpuUsage >= 0.8f) ? warn : ok));
        }

        public void RequestPreviewUpdate()
        {   // Request preview update without viewport cropping to prevent stretching
            if (World.PreviewCamera == null || Captue.PreviewRT == null)
                return;

            ref Captue owner = ref World.OwnerInstance;
            if (owner == null)
                return;

            World.PreviewCamera.cullingMask = CaptureUtilities.ComputeCullingMask(owner.showBackground);
            World.PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            World.PreviewCamera.backgroundColor = CaptureUtilities.GetBackgroundColor();
            CaptureUtilities.ApplyPreviewZoom(World.MainCamera, World.PreviewCamera, PreviewZoomLevel);

            var modified = CaptureUtilities.ApplySceneVisibilityTemporary(owner.showBackground, owner.showTerrain, HiddenRockets);
            var prevTarget = World.PreviewCamera.targetTexture;

            // Render full scene without viewport manipulation to prevent stretching
            World.PreviewCamera.rect = new Rect(0, 0, 1, 1);
            World.PreviewCamera.targetTexture = Captue.PreviewRT;
            World.PreviewCamera.Render();
            World.PreviewCamera.targetTexture = prevTarget;

            CaptureUtilities.RestoreSceneVisibility(modified);

            // Update UI layout to reflect current crop settings
            if (Captue.PreviewImage != null)
                UpdatePreviewBorderSize();
        }

        private void OnScreenSizeChanged()
        {   // Handle screen size changes by updating UI elements and clamping resolution if needed
            if (resInput == null)
                return;

            // Calculate crop factors for new screen size
            float leftCrop = CropLeft / 100f;
            float rightCrop = CropRight / 100f;
            float totalHorizontal = leftCrop + rightCrop;

            if (totalHorizontal >= 1f)
            {
                float scale = 0.99f / totalHorizontal;
                leftCrop *= scale;
                rightCrop *= scale;
            }

            float cropWidthFactor = 1f - leftCrop - rightCrop;
            int maxSafeRenderWidth = CaptureUtilities.ComputeMaxSafeWidth();
            int maxSafeTargetWidth = Mathf.RoundToInt(maxSafeRenderWidth * cropWidthFactor);

            // Clamp current resolution if it exceeds new max
            if (ResolutionWidth > maxSafeTargetWidth)
            {
                ResolutionWidth = maxSafeTargetWidth;
                resInput.Text = ResolutionWidth.ToString();
            }

            // Update estimates to reflect new screen dimensions
            UpdateEstimatesUI();

            // Refresh preview layout for new screen size and trigger update
            RefreshLayoutForCroppedPreview();
            RequestPreviewUpdate();
        }

        public override void Refresh()
        {   // Refresh UI with current values
            if (window == null)
                return;

            Hide();
            Show();
        }

        private void StartWindowColorAnimation(bool success = true)
        {   // Start coroutine to animate window background color based on save result
            if (window != null && World.UIHolder != null)
            {
                // Stop any current animation before starting new one
                if (currentAnimation != null)
                {
                    var monoBehaviour = World.UIHolder.GetComponentInChildren<MonoBehaviour>();
                    if (monoBehaviour != null)
                        monoBehaviour.StopCoroutine(currentAnimation);
                    currentAnimation = null;
                }

                var monoBehaviour2 = World.UIHolder.GetComponentInChildren<MonoBehaviour>();
                if (monoBehaviour2 != null)
                    currentAnimation = monoBehaviour2.StartCoroutine(AnimateWindowColor(success));
            }
        }

        private IEnumerator AnimateWindowColor(bool success = true)
        {   // Animate window background using sine wave over 0.8 seconds with result-based color
            var closableWindow = window as UITools.ClosableWindow;
            if (closableWindow == null)
                yield break;

            // Find any graphic component to animate
            var graphics = closableWindow.gameObject.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            var targetGraphic = graphics.FirstOrDefault(g =>
                g.name.ToLower().Contains("back") ||
                g.name.ToLower().Contains("background") ||
                g.name.ToLower().Contains("game"));

            if (targetGraphic == null && graphics.Length > 0)
                targetGraphic = graphics[0];

            if (targetGraphic == null)
            {
                Debug.LogWarning("Could not find background component to animate");
                yield break;
            }

            Color originalColor = targetGraphic.color;
            Color effectColor = success ?
                new Color(0.0018f, 0.6902f, 0.0804f, 1f) :  // Green for success
                new Color(0.8f, 0.1f, 0.1f, 1f);           // Red for failure

            float duration = 0.8f;
            float elapsed = 0f;

            while (elapsed < duration)
            {   // Use sine wave pattern for smooth color transition
                float t = elapsed / duration;
                float sineWave = Mathf.Sin(t * Mathf.PI);

                targetGraphic.color = Color.Lerp(originalColor, effectColor, sineWave);

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            targetGraphic.color = originalColor;
        }

        private IEnumerator DelayedAnimation(bool success = true)
        {   // Wait for game to settle then show save result animation
            yield return new WaitForSecondsRealtime(0.15f);
            StartWindowColorAnimation(success);
        }

        private IEnumerator VerifyAndShowResult(string worldFolderPath, string fileName, byte[] pngBytes, int renderWidth, int outWidth, int outHeight)
        {   // Wait for file system to flush, verify save, then show result with delay
            yield return new WaitForSecondsRealtime(0.1f);

            string fullPath = Path.Combine(worldFolderPath, fileName);
            bool saveSuccess = false;

            try
            {
                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Length > 0 && fileInfo.Length == pngBytes.Length)
                    {
                        saveSuccess = true;
                        var (gpuNeed, cpuNeed, rawBytes) = CaptureUtilities.EstimateMemoryForWidth(renderWidth);
                        Debug.Log($"Verified save {outWidth}x{outHeight}. Memory (render): GPU {CaptureUtilities.FormatMB(gpuNeed)}, CPU {CaptureUtilities.FormatMB(cpuNeed)}; file {CaptureUtilities.FormatMB(pngBytes.LongLength)}");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"File size mismatch. Expected: {pngBytes.Length}, Actual: {fileInfo.Length}");
                        saveSuccess = false;
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError("Screenshot file does not exist after save");
                    saveSuccess = false;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"File verification failed: {ex.Message}");
                saveSuccess = false;
            }

            yield return new WaitForSecondsRealtime(0.1f);
            StartWindowColorAnimation(saveSuccess);
        }

        public void TakeScreenshot()
        {   // Capture and save a screenshot at the specified resolution and show result animation using normalized crop values
            ref Captue owner = ref World.OwnerInstance;
            if (owner == null) return;

            if (World.MainCamera == null)
            {
                if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
                    World.MainCamera = GameCamerasManager.main.world_Camera.camera;
                else
                {   // Camera unavailable, abort and animate failure
                    UnityEngine.Debug.LogError("Cannot take screenshot: Camera not available");
                    StartWindowColorAnimation(false);
                    return;
                }
            }

            var (leftCrop, topCrop, rightCrop, bottomCrop) = CaptureUtilities.GetNormalizedCropValues();

            float cropWidthFactor = 1f - leftCrop - rightCrop;
            float cropHeightFactor = 1f - topCrop - bottomCrop;

            int targetWidth = ResolutionWidth;
            int targetHeight = Mathf.RoundToInt((float)targetWidth / (float)Screen.width * (float)Screen.height);

            // Render at full scene resolution needed to support cropped target at requested size
            int renderWidth = Mathf.RoundToInt(targetWidth / Mathf.Max(0.01f, cropWidthFactor));
            int renderHeight = Mathf.RoundToInt(targetHeight / Mathf.Max(0.01f, cropHeightFactor));

            int maxTextureSize = SystemInfo.maxTextureSize;
            var (gpuNeed, cpuNeed, rawBytes) = CaptureUtilities.EstimateMemoryForWidth(renderWidth);
            var (gpuBudget, cpuBudget) = CaptureUtilities.GetMemoryBudgets();

            if (renderWidth > maxTextureSize || renderHeight > maxTextureSize || gpuNeed > gpuBudget || cpuNeed > cpuBudget)
            {   // Clamp to safe render dimensions accounting for crop
                int maxSafeRenderWidth = CaptureUtilities.ComputeMaxSafeWidth();
                int maxSafeTargetWidth = Mathf.RoundToInt(maxSafeRenderWidth * cropWidthFactor);

                var (ow, oh) = (targetWidth, targetHeight);
                targetWidth = maxSafeTargetWidth;
                targetHeight = Mathf.RoundToInt((float)targetWidth / (float)Screen.width * (float)Screen.height);
                renderWidth = Mathf.RoundToInt(targetWidth / Mathf.Max(0.01f, cropWidthFactor));
                renderHeight = Mathf.RoundToInt(targetHeight / Mathf.Max(0.01f, cropHeightFactor));

                Debug.LogWarning($"Requested {ow}x{oh} exceeds safe limits with current crop settings. Using {targetWidth}x{targetHeight} (render {renderWidth}x{renderHeight}).");
            }

            RenderTexture fullRT = null;
            Texture2D finalTex = null;
            int previousAntiAliasing = QualitySettings.antiAliasing;
            bool previousOrthographic = World.MainCamera.orthographic;
            float previousOrthographicSize = World.MainCamera.orthographicSize;
            float previousFieldOfView = World.MainCamera.fieldOfView;
            var prevClearFlags = World.MainCamera.clearFlags;
            var prevBgColor = World.MainCamera.backgroundColor;
            Vector3 previousPosition = World.MainCamera.transform.position;

            try
            {   // Render the full scene to RT, then read the cropped rectangle into final texture
                fullRT = new RenderTexture(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32);
                finalTex = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);

                float z = Mathf.Max(Mathf.Exp(PreviewZoomLevel), 1e-6f);

                if (World.MainCamera.orthographic)
                    World.MainCamera.orthographicSize = Mathf.Clamp(previousOrthographicSize / z, 1e-6f, 1_000_000f);
                else
                {   // Perspective: use FOV or dolly when out of bounds
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
                World.MainCamera.cullingMask = CaptureUtilities.ComputeCullingMask(owner.showBackground);
                World.MainCamera.clearFlags = CameraClearFlags.SolidColor;
                World.MainCamera.backgroundColor = CaptureUtilities.GetBackgroundColor();

                var modified = CaptureUtilities.ApplySceneVisibilityTemporary(owner.showBackground, owner.showTerrain, HiddenRockets);

                // Render full scene (no viewport cropping) then crop via ReadPixels
                World.MainCamera.rect = new Rect(0, 0, 1, 1);
                World.MainCamera.targetTexture = fullRT;
                World.MainCamera.Render();

                CaptureUtilities.RestoreSceneVisibility(modified);
                World.MainCamera.cullingMask = prevMask;
                World.MainCamera.clearFlags = prevClearFlags;
                World.MainCamera.backgroundColor = prevBgColor;

                // Compute cropped read rect in fullRT pixel space
                var readRect = CaptureUtilities.GetCroppedReadRect(renderWidth, renderHeight);

                // Read the cropped area into a temporary texture, then scale into finalTex if needed
                var cropTex = new Texture2D((int)readRect.width, (int)readRect.height, TextureFormat.RGBA32, false);
                RenderTexture.active = fullRT;
                cropTex.ReadPixels(readRect, 0, 0);
                cropTex.Apply();

                // If cropTex size differs from target, resample; else assign directly
                if (cropTex.width != targetWidth || cropTex.height != targetHeight)
                {   // Scale cropped texture to target dimensions
                    var rtScale = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(cropTex, rtScale);
                    RenderTexture.active = rtScale;
                    finalTex.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                    finalTex.Apply();
                    RenderTexture.ReleaseTemporary(rtScale);
                }
                else
                {
                    finalTex = cropTex; // Same size, reuse
                }

                byte[] pngBytes = finalTex.EncodeToPNG();

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
                        monoBehaviour.StartCoroutine(VerifyAndShowResult(worldFolderPath, fileName, pngBytes, renderWidth, targetWidth, targetHeight));
                }

                var (finalGpuNeed, finalCpuNeed, finalRawBytes) = CaptureUtilities.EstimateMemoryForWidth(renderWidth);
                Debug.Log($"Saved {targetWidth}x{targetHeight} (render {renderWidth}x{renderHeight}). Approx memory: GPU {CaptureUtilities.FormatMB(finalGpuNeed)} (incl. depth), CPU {CaptureUtilities.FormatMB(finalCpuNeed)}; file size {CaptureUtilities.FormatMB(pngBytes.LongLength)} (est PNG {CaptureUtilities.FormatMB(CaptureUtilities.EstimatePngSizeBytes(finalRawBytes))}).");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Screenshot capture failed: {ex.Message}\n{ex.StackTrace}");
                StartWindowColorAnimation(false);
            }
            finally
            {
                World.MainCamera.targetTexture = null;
                RenderTexture.active = null;

                World.MainCamera.orthographic = previousOrthographic;
                World.MainCamera.orthographicSize = previousOrthographicSize;
                World.MainCamera.fieldOfView = previousFieldOfView;
                World.MainCamera.transform.position = previousPosition;
                World.MainCamera.rect = new Rect(0, 0, 1, 1);
                QualitySettings.antiAliasing = previousAntiAliasing;

                if (fullRT != null)
                    UnityEngine.Object.Destroy(fullRT);

                if (finalTex != null)
                    UnityEngine.Object.Destroy(finalTex);
            }
        }

        public void RefreshLayoutForCroppedPreview()
        {   // Force layout refresh to handle cropped preview changes
            if (previewContainer == null || window == null || Captue.PreviewImage == null)
                return;

            // Give the layout system time to update
            if (currentAnimation != null)
            {
                var monoBehaviour = World.UIHolder.GetComponentInChildren<MonoBehaviour>();
                if (monoBehaviour != null)
                    monoBehaviour.StopCoroutine(currentAnimation);
                currentAnimation = null;
            }

            // Start a coroutine to update layouts after a short delay
            if (World.UIHolder != null)
            {
                var monoBehaviour = World.UIHolder.GetComponentInChildren<MonoBehaviour>();
                if (monoBehaviour != null)
                    currentAnimation = monoBehaviour.StartCoroutine(DelayedLayoutRefresh());
            }
        }

        private IEnumerator DelayedLayoutRefresh()
        {   // Wait a frame then refresh layouts to ensure proper rendering
            yield return null; // Wait a frame

            if (previewContainer != null)
            {
                // Force immediate layout recalculation
                LayoutRebuilder.ForceRebuildLayoutImmediate(previewContainer.rectTransform);

                // Refresh parent layouts too
                if (previewContainer.rectTransform.parent != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(previewContainer.rectTransform.parent as RectTransform);

                // Ensure image fits properly in container
                var (origW, origH) = CaptureUtilities.CalculatePreviewDimensions();
                var (cropW, cropH) = CaptureUtilities.GetCroppedResolution(origW, origH);

                // Calculate proper aspect ratio of the cropped content
                float croppedAspect = (float)cropW / Mathf.Max(1, cropH);

                // Maintain container width constraint and adjust height
                float containerMaxWidth = 520f;
                float finalWidth, finalHeight;

                if (cropW > containerMaxWidth)
                {   // Scale down to fit width
                    finalWidth = containerMaxWidth;
                    finalHeight = finalWidth / croppedAspect;
                }
                else
                {   // Use original cropped dimensions
                    finalWidth = cropW;
                    finalHeight = cropH;
                }

                // Apply updated dimensions
                if (Captue.PreviewImage.rectTransform != null)
                {
                    Captue.PreviewImage.rectTransform.sizeDelta = new Vector2(finalWidth, finalHeight);
                    Captue.PreviewImage.rectTransform.localScale = Vector3.one * PREVIEW_SCALE_FIX;
                }

                // Update border to match new image size
                UpdatePreviewBorderSize();

                // Update parent layout
                var parentLayout = previewContainer.gameObject.GetComponent<LayoutElement>();
                if (parentLayout != null)
                {
                    parentLayout.preferredHeight = finalHeight;
                    parentLayout.minHeight = finalHeight;
                }
            }

            // Re-render the preview with cropping applied
            if (World.PreviewCamera != null && Captue.PreviewRT != null)
            {
                World.PreviewCamera.targetTexture = Captue.PreviewRT;
                World.PreviewCamera.Render();
            }
        }

        private void CreatePreviewBorder()
        {   // Create a border box and make the preview image a child element
            if (previewContainer == null || Captue.PreviewImage == null)
                return;

            try
            {
                var imageRect = Captue.PreviewImage.rectTransform;
                if (imageRect == null)
                    return;

                // Store current image dimensions and settings
                var currentSize = imageRect.sizeDelta;

                // Create border box with same dimensions as image
                previewBorder = Builder.CreateBox(previewContainer, Mathf.RoundToInt(currentSize.x), Mathf.RoundToInt(currentSize.y), 0, 0, 0.2f);
                previewBorder.Color = new Color(0.4f, 0.48f, 0.6f, 0.6f);

                // Move the preview image to be a child of the border box
                Captue.PreviewImage.transform.SetParent(previewBorder.gameObject.transform, false);
                Captue.PreviewImage.gameObject.layer = previewContainer.gameObject.layer;

                // Keep image at its original size and center it in the border
                // Ensure the RawImage always fills the border exactly (no 1.5x scaling)
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.sizeDelta = Vector2.zero;
                imageRect.anchoredPosition = Vector2.zero;
                imageRect.localScale = Vector3.one * PREVIEW_SCALE_FIX;

                // Reset UV and texture to avoid unexpected scaling
                try { Captue.PreviewImage.uvRect = new Rect(0f, 0f, 1f, 1f); } catch { }
                if (Captue.PreviewRT != null && Captue.PreviewImage.texture != Captue.PreviewRT)
                    Captue.PreviewImage.texture = Captue.PreviewRT;

                // Disable layout on image since it's now positioned by its parent box
                var imageLayout = Captue.PreviewImage.gameObject.GetComponent<LayoutElement>();
                if (imageLayout != null)
                    imageLayout.ignoreLayout = true;

                // Ensure mask exists to contain the image inside border
                var borderRect = previewBorder.gameObject.GetComponent<RectTransform>();
                var mask = previewBorder.gameObject.GetComponent<RectMask2D>() ?? previewBorder.gameObject.AddComponent<RectMask2D>();

                // Disable raycasts on the image so it doesn't block UI interaction and won't float above
                Captue.PreviewImage.raycastTarget = false;

                Debug.Log($"Created preview border with image as child element");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not create preview border: {ex.Message}");
            }
        }

        private void UpdatePreviewBorderSize()
        {   // Calculate border size and UV mapping with normalized side cropping and no position offset
            if (previewContainer == null || Captue.PreviewImage == null || previewBorder == null)
                return;

            try
            {
                var imageRect = Captue.PreviewImage.rectTransform;
                var borderRect = previewBorder.gameObject.GetComponent<RectTransform>();
                if (imageRect == null || borderRect == null)
                    return;

                // Get base preview dimensions then apply normalized crop factors
                var (origW, origH) = CaptureUtilities.CalculatePreviewDimensions();
                var (leftCrop, topCrop, rightCrop, bottomCrop) = CaptureUtilities.GetNormalizedCropValues();

                float remainingWidthFactor = 1f - leftCrop - rightCrop;
                float remainingHeightFactor = 1f - topCrop - bottomCrop;

                int croppedWidth = Mathf.RoundToInt(origW * remainingWidthFactor);
                int croppedHeight = Mathf.RoundToInt(origH * remainingHeightFactor);

                float containerMaxWidth = 520f;
                float aspectRatio = croppedHeight > 0 ? (float)croppedWidth / (float)croppedHeight : 1f;

                float finalWidth, finalHeight;
                if (croppedWidth > containerMaxWidth)
                {   // Scale down proportionally to fit container
                    finalWidth = containerMaxWidth;
                    finalHeight = finalWidth / Mathf.Max(1e-6f, aspectRatio);
                }
                else
                {   // Use actual cropped dimensions with no minimums to avoid pixel stretching
                    finalWidth = Mathf.Max(1f, croppedWidth);
                    finalHeight = Mathf.Max(1f, croppedHeight);
                }

                borderRect.sizeDelta = new Vector2(finalWidth, finalHeight);

                imageRect.transform.SetParent(borderRect.gameObject.transform, false);
                imageRect.localScale = Vector3.one;
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.anchoredPosition = Vector2.zero;
                imageRect.sizeDelta = Vector2.zero;

                // UV cropping directly from normalized values
                float uvLeft = leftCrop;
                float uvBottom = bottomCrop;
                float uvWidth = remainingWidthFactor;
                float uvHeight = remainingHeightFactor;

                // Safety clamp to texture bounds
                if (uvLeft + uvWidth > 1f) uvWidth = 1f - uvLeft;
                if (uvBottom + uvHeight > 1f) uvHeight = 1f - uvBottom;

                try { Captue.PreviewImage.uvRect = new Rect(uvLeft, uvBottom, uvWidth, uvHeight); } catch { }

                if (Captue.PreviewRT != null && Captue.PreviewImage.texture != Captue.PreviewRT)
                    Captue.PreviewImage.texture = Captue.PreviewRT;

                var mask = borderRect.GetComponent<RectMask2D>() ?? borderRect.gameObject.AddComponent<RectMask2D>();

                var parentLayout = previewContainer.gameObject.GetComponent<LayoutElement>() ?? previewContainer.gameObject.AddComponent<LayoutElement>();
                parentLayout.preferredHeight = finalHeight + 12f;
                parentLayout.minHeight = finalHeight + 12f;

                Debug.Log($"Updated preview: Border={finalWidth}x{finalHeight}, UV=({uvLeft:F3},{uvBottom:F3},{uvWidth:F3},{uvHeight:F3}), Crops=L{CropLeft}%T{CropTop}%R{CropRight}%B{CropBottom}%");
            }
            catch (Exception ex)
            {   // Log but prevent UI errors from bubbling up
                Debug.LogWarning($"Could not update preview border size: {ex.Message}");
            }
        }

        private void SetupPreview(Container parent)
        {   // Create preview image first with border sized exactly to cropped dimensions
            if (parent == null)
                return;

            DisableRaycastsInChildren(parent.gameObject);

            // Create preview image first to get actual dimensions
            if (Captue.PreviewImage == null)
            {
                var tempContainer = CreateNestedContainer(parent, SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 0f, null, false);
                CaptureUtilities.SetupPreview(tempContainer);
                UnityEngine.Object.Destroy(tempContainer.gameObject);
            }
            
            // Calculate dimensions based on crop settings
            var (origW, origH) = CaptureUtilities.CalculatePreviewDimensions();
            var (cropW, cropH) = CaptureUtilities.GetCroppedResolution(origW, origH);
            
            // Use exact cropped dimensions without artificial scaling or minimum constraints
            int borderWidth = cropW;
            int borderHeight = cropH;

            // Create properly sized border box
            previewBorder = Builder.CreateBox(parent, borderWidth, borderHeight, 0, 0, 0.2f);
            previewBorder.Color = new Color(0.4f, 0.48f, 0.6f, 0.6f);

            var borderGO = previewBorder.gameObject;
            borderGO.layer = parent.gameObject.layer;

            // Add RectMask2D component to clip overflow
            var mask = borderGO.GetComponent<RectMask2D>();
            if (mask == null)
                mask = borderGO.AddComponent<RectMask2D>();

            // Configure image to fit border proportionally
            if (Captue.PreviewImage != null)
            {
                // Parent image to border
                Captue.PreviewImage.transform.SetParent(borderGO.transform, false);
                Captue.PreviewImage.gameObject.layer = borderGO.layer;

                // Configure proper RawImage settings
                Captue.PreviewImage.enabled = true;
                Captue.PreviewImage.gameObject.SetActive(true);
                
                // Enforce fill mode for proper scaling
                Captue.PreviewImage.uvRect = new Rect(0, 0, 1, 1);
                if (Captue.PreviewRT != null)
                    Captue.PreviewImage.texture = Captue.PreviewRT;

                // Set image to completely fill border with proper anchoring
                var imageRect = Captue.PreviewImage.rectTransform;
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.sizeDelta = Vector2.zero;  // Fill border exactly
                imageRect.anchoredPosition = Vector2.zero;
                imageRect.localScale = Vector3.one * PREVIEW_SCALE_FIX;
                
                // Ignore layout system
                var imgLE = Captue.PreviewImage.gameObject.GetComponent<LayoutElement>() ?? 
                    Captue.PreviewImage.gameObject.AddComponent<LayoutElement>();
                imgLE.ignoreLayout = true;

                // Disable raycast so preview doesn't intercept input and so it won't float above
                Captue.PreviewImage.raycastTarget = false;

                // Ensure no independent Canvas exists on image or children
                var imgCanvases = Captue.PreviewImage.gameObject.GetComponentsInChildren<UnityEngine.Component>(true)
                    .Where(c => c != null && c.GetType().Name == "Canvas").ToArray();
                foreach (var c in imgCanvases) try { UnityEngine.Object.Destroy(c); } catch { }
            }

            // Ensure proper window hierarchy and visibility control
            ForceWindowHierarchyCompliance(borderGO);

            // Start visibility sync coroutine to follow window state
            if (World.UIHolder != null)
            {
                var monoBehaviour = World.UIHolder.GetComponentInChildren<MonoBehaviour>();
                if (monoBehaviour != null)
                    monoBehaviour.StartCoroutine(SyncPreviewVisibility(borderGO));
            }

            // Set container layout to match border size
            var le = parent.gameObject.GetComponent<LayoutElement>() ?? parent.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 520f;
            le.minWidth = 520f;
            le.flexibleWidth = 0f;
            le.preferredHeight = borderHeight + 12f;
            le.minHeight = borderHeight + 12f;

            previewInitialized = true;
            Debug.Log($"Preview setup: Border {borderWidth}x{borderHeight}, Original {origW}x{origH}, Cropped {cropW}x{cropH}");
        }

        private void ForceWindowHierarchyCompliance(GameObject borderGO)
        {   // Ensure preview follows window visibility and is properly constrained within border
            if (borderGO == null || window == null)
                return;

            // Remove all Canvas components in preview hierarchy to prevent independent rendering
            var allComponents = borderGO.GetComponentsInChildren<UnityEngine.Component>(true)
                .Where(c => c != null && c.GetType().Name == "Canvas")
                .ToArray();
            foreach (var canvas in allComponents)
            {
                try { UnityEngine.Object.Destroy(canvas); } catch { }
            }

            // Disable all UI raycasting so preview follows window interaction
            var allGraphics = borderGO.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var graphic in allGraphics)
                graphic.raycastTarget = false;

            // Match layers exactly to ensure same rendering order as window
            borderGO.layer = window.gameObject.layer;
            var allChildren = borderGO.GetComponentsInChildren<Transform>(true);
            foreach (var child in allChildren)
                child.gameObject.layer = window.gameObject.layer;

            // Ensure image is properly constrained within border
            if (Captue.PreviewImage != null)
            {
                var imageRect = Captue.PreviewImage.rectTransform;
                if (imageRect != null)
                {
                    // Force image to fill border exactly without overflow
                    imageRect.anchorMin = Vector2.zero;
                    imageRect.anchorMax = Vector2.one;
                    imageRect.pivot = new Vector2(0.5f, 0.5f);
                    imageRect.sizeDelta = Vector2.zero;
                    imageRect.anchoredPosition = Vector2.zero;
                    imageRect.localScale = Vector3.one * PREVIEW_SCALE_FIX;
                    
                    // Ensure layout doesn't interfere
                    var imgLE = imageRect.GetComponent<LayoutElement>() ?? imageRect.gameObject.AddComponent<LayoutElement>();
                    imgLE.ignoreLayout = true;

                    // Reset UVs and ensure texture is preview RT
                    try { Captue.PreviewImage.uvRect = new Rect(0f, 0f, 1f, 1f); } catch { }
                    if (Captue.PreviewRT != null && Captue.PreviewImage.texture != Captue.PreviewRT)
                        Captue.PreviewImage.texture = Captue.PreviewRT;

                    // Disable raycast so image does not block window interactions
                    Captue.PreviewImage.raycastTarget = false;
                }
            }

            // Ensure border has proper clipping mask
            var mask = borderGO.GetComponent<RectMask2D>() ?? borderGO.AddComponent<RectMask2D>();

            // Ensure preview follows window active state
            borderGO.gameObject.SetActive(window.gameObject.activeInHierarchy);
        }

        private IEnumerator SyncPreviewVisibility(GameObject previewBorder)
        {   // Continuously sync preview visibility with window state to prevent showing when hidden
            while (previewBorder != null && window != null)
            {
                var windowActive = window.gameObject.activeInHierarchy;
                var windowMinimized = window is UITools.ClosableWindow closable && closable.Minimized;
                bool shouldShow = windowActive && !windowMinimized;

                if (previewBorder.activeSelf != shouldShow)
                    previewBorder.SetActive(shouldShow);

                yield return new WaitForSecondsRealtime(0.1f);  // Check 10 times per second
            }
        }

        private void DestroyIndependentCanvasComponents(GameObject rootGO)
        {   // Remove all Canvas components in hierarchy to prevent independent rendering above menu
            if (rootGO == null)
                return;

            var allComponents = rootGO.GetComponentsInChildren<UnityEngine.Component>(true)
                .Where(c => c != null && c.GetType().Name == "Canvas")
                .ToArray();

            foreach (var canvas in allComponents)
            {
                try { UnityEngine.Object.Destroy(canvas); } catch { }
            }

            // Disable all raycasting to ensure clicks go through to window
            var allGraphics = rootGO.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var graphic in allGraphics)
                graphic.raycastTarget = false;

            // Match window layer exactly
            if (window != null)
            {
                rootGO.layer = window.gameObject.layer;
                var allChildren = rootGO.GetComponentsInChildren<Transform>(true);
                foreach (var child in allChildren)
                    child.gameObject.layer = window.gameObject.layer;
            }
        }

        private void UpdatePreviewLayoutImmediate()
        {   // Recompute sizes and apply to border and image instantly
            if (previewContainer == null || Captue.PreviewImage == null)
                return;

            SyncPreviewLayerAndMask();
            UpdatePreviewBorderSize();

            // Force layout so neighbors update immediately
            LayoutRebuilder.ForceRebuildLayoutImmediate(previewContainer.rectTransform);
            if (previewContainer.rectTransform.parent != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(previewContainer.rectTransform.parent as RectTransform);
        }

        private void SyncPreviewLayerAndMask()
        {   // Enforce strict window hierarchy so preview follows menu visibility and draw order
            if (previewContainer == null)
                return;

            int parentLayer = previewContainer.gameObject.layer;

            if (previewBorder != null)
            {
                var borderGO = previewBorder.gameObject;
                borderGO.layer = parentLayer;

                // Ensure RectMask2D clipping
                var mask = borderGO.GetComponent<RectMask2D>() ?? borderGO.AddComponent<RectMask2D>();

                // Destroy any Canvas components to prevent override
                var canvasComponents = borderGO.GetComponents<UnityEngine.Component>()
                    .Where(c => c.GetType().Name == "Canvas")
                    .ToArray();
                foreach (var canvas in canvasComponents)
                    UnityEngine.Object.Destroy(canvas);

                // Ensure strict parent hierarchy: border -> container -> window
                if (borderGO.transform.parent != previewContainer.gameObject.transform)
                    borderGO.transform.SetParent(previewContainer.gameObject.transform, false);

                // Disable independent raycasting and interaction to ensure it follows window behavior
                var graphics = borderGO.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                foreach (var graphic in graphics)
                    graphic.raycastTarget = false;
            }

            if (Captue.PreviewImage != null)
            {
                var imgGO = Captue.PreviewImage.gameObject;
                imgGO.layer = parentLayer;

                // Remove any Canvas to prevent override
                var imgCanvasComponents = imgGO.GetComponents<UnityEngine.Component>()
                    .Where(c => c.GetType().Name == "Canvas")
                    .ToArray();
                foreach (var canvas in imgCanvasComponents)
                    UnityEngine.Object.Destroy(canvas);

                // Enforce image under border hierarchy: image -> border -> container -> window
                if (previewBorder != null && imgGO.transform.parent != previewBorder.gameObject.transform)
                    imgGO.transform.SetParent(previewBorder.gameObject.transform, false);

                // Disable raycasting on preview image so it follows window behavior
                var imgGraphics = imgGO.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                foreach (var graphic in imgGraphics)
                    graphic.raycastTarget = false;

                // Ensure transform settings are normalized to avoid scaling issues
                var imageRect = Captue.PreviewImage.rectTransform;
                if (imageRect != null)
                {
                    imageRect.localScale = Vector3.one * PREVIEW_SCALE_FIX;
                    imageRect.anchorMin = Vector2.zero;
                    imageRect.anchorMax = Vector2.one;
                    imageRect.sizeDelta = Vector2.zero;
                    imageRect.anchoredPosition = Vector2.zero;
                    try { Captue.PreviewImage.uvRect = new Rect(0f, 0f, 1f, 1f); } catch { }
                }
            }
        }


    }
}
