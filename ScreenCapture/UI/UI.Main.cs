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
using System.Diagnostics;

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

        private void OnResolutionInputChange(string val)
        {   // Optimized resolution input validation with crop factor consideration
            if (!int.TryParse(val, out int targetWidth))
                return;

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
            var closableWindow = CreateClosableWindow(World.UIHolder.transform, Builder.GetRandomID(), 950, 545, 300, 100, true, true, 1f, "ScreenShot", minimized: false);
            window = closableWindow;
            closableWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, new RectOffset(6, 6, 10, 6), true);
            closableWindow.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

            World.OwnerInstance.closableWindow = closableWindow;
            World.wasMinimized = closableWindow.Minimized;

            // Connect window events to our actions
            ConnectWindowEvents(closableWindow);

            CreateToolsContainer();
            Builder.CreateSeparator(window, 930, 0, 0);
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
                World.OwnerInstance?.RequestPreviewUpdate();
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
                    CaptureUtilities.SetupPreview(previewContainer);
                    DisableRaycastsInChildren(previewContainer.gameObject);
                    previewInitialized = true;
                    Debug.Log("Preview initialized during container creation");

                    // Create border after preview setup to match image dimensions
                    CreatePreviewBorder();
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
                    le.preferredWidth = 360f;
                    le.flexibleWidth = 0f;

                    Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => World.OwnerInstance?.showBackground ?? true, () =>
                    {   // Toggle background visibility and refresh preview
                        ref Captue owner = ref World.OwnerInstance;
                        if (owner != null)
                        {
                            owner.showBackground = !owner.showBackground;
                            UpdateBackgroundWindowVisibility();
                            owner.RequestPreviewUpdate();
                        }
                    }, 0, 0, "Show Background");

                    Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => World.OwnerInstance?.showTerrain ?? true, () =>
                    {   // Toggle terrain visibility and refresh preview
                        ref Captue owner = ref World.OwnerInstance;
                        if (owner != null)
                        {
                            owner.showTerrain = !owner.showTerrain;
                            owner.RequestPreviewUpdate();
                        }
                    }, 0, 0, "Show Terrain");

                    Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => World.OwnerInstance?.rocketsWindow?.IsOpen ?? false, () =>
                    {   // Show or hide the rockets window
                        ref Captue owner = ref World.OwnerInstance;
                        if (owner != null)
                        {
                            ref var rocketWindow = ref owner.rocketsWindow;
                            CaptureUtilities.ShowHideWindow<RocketsUI>(ref rocketWindow,
                                () => { }, // Show action
                                () => { }); // Hide action
                        }
                    }, 0, 0, "Show Rockets");

                    CreateNestedVertical(controlsContainer, 2f, null, TextAnchor.UpperCenter, cropControls =>
                    {
                        CaptureUtilities.CreateCropControls(cropControls, () =>
                        {   // Unified crop change handler with preview update
                            CaptureUtilities.UpdatePreviewCropping();
                            RefreshLayoutForCroppedPreview();
                            UpdateEstimatesUI();
                            World.OwnerInstance?.RequestPreviewUpdate();
                        });
                    });
                });
            });
        }

        private Container CreateControlsContainer()
        {   // Create capture controls and show estimates above them
            var MainCol = CreateVerticalContainer(window, 8f, null, TextAnchor.UpperLeft);

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

            Builder.CreateSpace(controls, 210, 0);

            var ZoomRow = CreateNestedVertical(controls, 5f, null, TextAnchor.UpperLeft);
            CreateZoomControls(ZoomRow);

            CreateNestedHorizontal(MainCol, 73f, null, TextAnchor.UpperLeft, helpRow =>
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
                World.OwnerInstance?.RequestPreviewUpdate();
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
                    World.OwnerInstance?.RequestPreviewUpdate();
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
                CaptureUtilities.SetupPreview(previewContainer);
                DisableRaycastsInChildren(previewContainer.gameObject);
                previewInitialized = true;
                Debug.Log("Preview setup completed successfully");
                
                // Create border if it doesn't exist
                if (previewBorder == null)
                    CreatePreviewBorder();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to setup preview: {ex.Message}");
            }
        }

        public void UpdateEstimatesUI()
        {   // Refresh estimate labels with aggressive throttling and caching optimization
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

            // Calculate crop factors to determine render requirements
            float leftCrop = CropLeft / 100f;
            float topCrop = CropTop / 100f;
            float rightCrop = CropRight / 100f;
            float bottomCrop = CropBottom / 100f;

            float totalHorizontal = leftCrop + rightCrop;
            float totalVertical = topCrop + bottomCrop;

            if (totalHorizontal >= 1f)
            {
                float scale = 0.99f / totalHorizontal;
                leftCrop *= scale;
                rightCrop *= scale;
            }

            if (totalVertical >= 1f)
            {
                float scale = 0.99f / totalVertical;
                topCrop *= scale;
                bottomCrop *= scale;
            }

            float cropWidthFactor = 1f - leftCrop - rightCrop;
            float cropHeightFactor = 1f - topCrop - bottomCrop;

            // Calculate render dimensions needed for the target resolution
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

            // Refresh preview layout for new screen size
            RefreshLayoutForCroppedPreview();
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
        {   // Capture and save a screenshot at the specified resolution and show result animation
            ref Captue owner = ref World.OwnerInstance;
            if (owner == null) return;

            if (World.MainCamera == null)
            {
                if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
                    World.MainCamera = GameCamerasManager.main.world_Camera.camera;
                else
                {
                    UnityEngine.Debug.LogError("Cannot take screenshot: Camera not available");
                    StartWindowColorAnimation(false);
                    return;
                }
            }

            // Calculate crop factors
            float leftCrop = CropLeft / 100f;
            float topCrop = CropTop / 100f;
            float rightCrop = CropRight / 100f;
            float bottomCrop = CropBottom / 100f;

            // Ensure total crop doesn't exceed 100%
            float totalHorizontal = leftCrop + rightCrop;
            float totalVertical = topCrop + bottomCrop;

            if (totalHorizontal >= 1f)
            {
                float scale = 0.99f / totalHorizontal;
                leftCrop *= scale;
                rightCrop *= scale;
            }

            if (totalVertical >= 1f)
            {
                float scale = 0.99f / totalVertical;
                topCrop *= scale;
                bottomCrop *= scale;
            }

            // Calculate the render dimensions to achieve target resolution for cropped area
            float cropWidthFactor = 1f - leftCrop - rightCrop;
            float cropHeightFactor = 1f - topCrop - bottomCrop;

            int targetWidth = ResolutionWidth;
            int targetHeight = Mathf.RoundToInt((float)targetWidth / (float)Screen.width * (float)Screen.height);

            int renderWidth = Mathf.RoundToInt(targetWidth / Mathf.Max(0.01f, cropWidthFactor));
            int renderHeight = Mathf.RoundToInt(targetHeight / Mathf.Max(0.01f, cropHeightFactor));

            // Check against texture limits and memory budgets using render dimensions
            int maxTextureSize = SystemInfo.maxTextureSize;
            var (gpuNeed, cpuNeed, rawBytes) = CaptureUtilities.EstimateMemoryForWidth(renderWidth);
            var (gpuBudget, cpuBudget) = CaptureUtilities.GetMemoryBudgets();

            if (renderWidth > maxTextureSize || renderHeight > maxTextureSize || gpuNeed > gpuBudget || cpuNeed > cpuBudget)
            {
                // Calculate maximum safe render dimensions accounting for crop
                int maxSafeRenderWidth = CaptureUtilities.ComputeMaxSafeWidth();
                int maxSafeTargetWidth = Mathf.RoundToInt(maxSafeRenderWidth * cropWidthFactor);

                var (ow, oh) = (targetWidth, targetHeight);
                targetWidth = maxSafeTargetWidth;
                targetHeight = Mathf.RoundToInt((float)targetWidth / (float)Screen.width * (float)Screen.height);
                renderWidth = Mathf.RoundToInt(targetWidth / cropWidthFactor);
                renderHeight = Mathf.RoundToInt(targetHeight / cropHeightFactor);

                Debug.LogWarning($"Requested {ow}x{oh} exceeds safe limits with current crop settings. Using {targetWidth}x{targetHeight} (render {renderWidth}x{renderHeight}).");
            }

            RenderTexture renderTexture = null;
            Texture2D screenshotTexture = null;
            int previousAntiAliasing = QualitySettings.antiAliasing;
            bool previousOrthographic = World.MainCamera.orthographic;
            float previousOrthographicSize = World.MainCamera.orthographicSize;
            float previousFieldOfView = World.MainCamera.fieldOfView;
            var prevClearFlags = World.MainCamera.clearFlags;
            var prevBgColor = World.MainCamera.backgroundColor;
            Vector3 previousPosition = World.MainCamera.transform.position;

            try
            {
                renderTexture = new RenderTexture(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32);
                screenshotTexture = new Texture2D(renderWidth, renderHeight, TextureFormat.RGBA32, false);

                float z = Mathf.Max(Mathf.Exp(PreviewZoomLevel), 1e-6f);

                if (World.MainCamera.orthographic)
                    World.MainCamera.orthographicSize = Mathf.Clamp(previousOrthographicSize / z, 1e-6f, 1_000_000f);
                else
                {   // Perspective: use FOV when in range, otherwise dolly relative to a forward pivot
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

                // Set camera viewport to capture only the cropped area
                World.MainCamera.rect = new Rect(leftCrop, bottomCrop, 1f - leftCrop - rightCrop, 1f - topCrop - bottomCrop);
                World.MainCamera.targetTexture = renderTexture;
                World.MainCamera.Render();

                CaptureUtilities.RestoreSceneVisibility(modified);
                World.MainCamera.cullingMask = prevMask;
                World.MainCamera.clearFlags = prevClearFlags;
                World.MainCamera.backgroundColor = prevBgColor;

                RenderTexture.active = renderTexture;

                // Read the full render texture since we've already cropped at the viewport level
                screenshotTexture.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
                screenshotTexture.Apply();

                byte[] pngBytes = screenshotTexture.EncodeToPNG();

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

                if (renderTexture != null)
                    UnityEngine.Object.Destroy(renderTexture);

                if (screenshotTexture != null)
                    UnityEngine.Object.Destroy(screenshotTexture);
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
                    Captue.PreviewImage.rectTransform.sizeDelta = new Vector2(finalWidth, finalHeight);

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
                var currentAnchorMin = imageRect.anchorMin;
                var currentAnchorMax = imageRect.anchorMax;
                var currentPivot = imageRect.pivot;
                var currentPos = imageRect.anchoredPosition;

                // Create border box with same dimensions as image
                previewBorder = Builder.CreateBox(previewContainer, Mathf.RoundToInt(currentSize.x), Mathf.RoundToInt(currentSize.y), 0, 0, 0.2f);
                previewBorder.Color = new Color(0.4f, 0.48f, 0.6f, 0.6f);

                // Move the preview image to be a child of the border box
                Captue.PreviewImage.transform.SetParent(previewBorder.gameObject.transform, false);

                // Keep image at its original size and center it in the border
                imageRect.anchorMin = new Vector2(0.5f, 0.5f);
                imageRect.anchorMax = new Vector2(0.5f, 0.5f);
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.sizeDelta = currentSize;
                imageRect.anchoredPosition = Vector2.zero;

                // Disable layout on image since it's now positioned by its parent box
                var imageLayout = Captue.PreviewImage.gameObject.GetComponent<LayoutElement>();
                if (imageLayout != null)
                    imageLayout.ignoreLayout = true;

                Debug.Log($"Created preview border with image as child element");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not create preview border: {ex.Message}");
            }
        }

        private void UpdatePreviewBorderSize()
        {   // Update border size when image dimensions change while preserving image size
            if (previewBorder == null || Captue.PreviewImage == null)
                return;

            try
            {
                var imageRect = Captue.PreviewImage.rectTransform;
                var borderRect = previewBorder.gameObject.GetComponent<RectTransform>();
                
                if (imageRect != null && borderRect != null)
                {
                    var (origW, origH) = CaptureUtilities.CalculatePreviewDimensions();
                    var (cropW, cropH) = CaptureUtilities.GetCroppedResolution(origW, origH);

                    float croppedAspect = (float)cropW / Mathf.Max(1, cropH);
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

                    // Update border size to match new dimensions
                    borderRect.sizeDelta = new Vector2(finalWidth, finalHeight);
                    
                    // Update image size to match but keep it centered
                    imageRect.sizeDelta = new Vector2(finalWidth, finalHeight);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not update preview border size: {ex.Message}");
            }
        }
    }
}
