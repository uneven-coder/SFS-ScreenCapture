using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using SFS.UI.ModGUI;
using SFS.World;
using SFS.UI;
using UnityEngine;
using System.Linq;
using System.IO;
using static UITools.UIToolsBuilder;
using SystemType = System.Type;
using static ScreenCapture.Main;
using System.Runtime.InteropServices;
using SFS.Builds;

namespace ScreenCapture
{
    // TimeStepHelper moved to Utilities.cs

    public class MainUI : UIBase
    {   // Manage the main capture UI bound directly to a Captue instance
        private Container previewContainer;
        private bool previewInitialized;

        // Stats UI elements
        private Label resLabel;
        private Label gpuLabel;
        private Label cpuLabel;
        private Label pngLabel;
        private Label maxLabel;
        private Label warnLabel;
        private TextInput resInput;


        public override void Show()
        {   // Build and display main window UI for the given owner
            if (IsOpen)
                return;

            previewInitialized = false;
            previewContainer = null;

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
                // Create preview container 
                previewContainer = CreateNestedContainer(toolsContainer, SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 4f, new RectOffset(6, 6, 6, 6), true);

                // Setup preview immediately
                try
                {
                    CaptureUtilities.SetupPreview(previewContainer);
                    DisableRaycastsInChildren(previewContainer.gameObject);
                    previewInitialized = true;
                    Debug.Log("Preview initialized during container creation");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to initialize preview: {ex.Message}");
                    previewInitialized = false;
                }

                CreateNestedVertical(toolsContainer, 20f, null, TextAnchor.UpperLeft, controlsContainer =>
                {
                    Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => World.OwnerInstance?.showBackground ?? true, () =>
                    {   // Toggle background visibility and adjust preview
                        ref Captue owner = ref World.OwnerInstance;
                        if (owner != null)
                        {
                            owner.showBackground = !owner.showBackground;
                            CaptureUtilities.UpdatePreviewCulling();
                            UpdateBackgroundWindowVisibility();
                        }
                    }, 0, 0, "Show Background");

                    Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => World.OwnerInstance?.showTerrain ?? true, () =>
                    {   // Toggle terrain visibility and adjust preview
                        ref Captue owner = ref World.OwnerInstance;
                        if (owner != null)
                        {
                            owner.showTerrain = !owner.showTerrain;
                            CaptureUtilities.UpdatePreviewCulling();
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
                });
            });
        }

        private Container CreateControlsContainer()
        {   // Create capture controls and show estimates above them
            var MainCol = CreateVerticalContainer(window, 4f, null, TextAnchor.UpperLeft);

            var controls = CreateNestedContainer(MainCol,
                SFS.UI.ModGUI.Type.Horizontal, TextAnchor.LowerLeft, 30f, null, true);

            CreateNestedVertical(controls, 5f, null, TextAnchor.LowerLeft, LeftRow =>
            {
                CreateNestedHorizontal(LeftRow, 5f, null, TextAnchor.MiddleCenter, timeControlRow =>
                {
                    // Add time control buttons
                    Builder.CreateButton(timeControlRow, 80, 58, 0, 0, () =>
                    {   // Toggle time pause/play state
                        try
                        {
                            bool isPaused = Time.timeScale == 0;
                            if (isPaused)
                                Time.timeScale = 1f;
                            else
                            {   // Pause and save the current frame via utilities
                                Time.timeScale = 0f;
                                CaptureTime.SaveCurrentFrame();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Play/Pause error: {ex.Message}");
                        }
                    }, "||");  // Simple ASCII-like pause representation

                    Builder.CreateButton(timeControlRow, 80, 58, 0, 0, () => CaptureTime.StepBackwardInTime(), "<<");  // ASCII skip back

                    Builder.CreateButton(timeControlRow, 80, 58, 0, 0, () => CaptureTime.StepForwardAndPause(), ">>");  // ASCII skip forward
                });

                CreateNestedHorizontal(LeftRow, 10f, null, TextAnchor.LowerLeft, CaptureRow =>
                {
                    Builder.CreateButton(CaptureRow, 180, 58, 0, 0, () => TakeScreenshot(), "Capture");
                    resInput = Builder.CreateTextInput(CaptureRow, 180, 58, 0, 0, ResolutionWidth.ToString(), val =>
                    {
                        if (int.TryParse(val, out int w))
                        {
                            int maxW = CaptureUtilities.ComputeMaxSafeWidth();
                            w = Mathf.Clamp(w, 1, maxW);
                            resInput.Text = w.ToString();
                            ResolutionWidth = w;
                            UpdateEstimatesUI();
                        }
                    });
                });
            });

            Builder.CreateSpace(controls, 210, 0);

            var ZoomRow = CreateNestedVertical(controls, 5f, null, TextAnchor.UpperLeft);
            CreateZoomControls(ZoomRow);

            CreateNestedHorizontal(MainCol, 10f, null, TextAnchor.UpperLeft, statsRow1 =>
            {
                resLabel = Builder.CreateLabel(statsRow1, 210, 34, 0, 0, "Res: -");
                gpuLabel = Builder.CreateLabel(statsRow1, 170, 34, 0, 0, "GPU: -");
                cpuLabel = Builder.CreateLabel(statsRow1, 170, 34, 0, 0, "RAM: -");
            });

            CreateNestedHorizontal(MainCol, 10f, null, TextAnchor.UpperLeft, statsRow2 =>
            {
                pngLabel = Builder.CreateLabel(statsRow2, 170, 34, 0, 0, "PNG: -");
                maxLabel = Builder.CreateLabel(statsRow2, 220, 34, 0, 0, "Max Width: -");
                warnLabel = Builder.CreateLabel(statsRow2, 400, 34, 0, 0, "");
                warnLabel.Color = new Color(1f, 0.35f, 0.2f);
            });

            return controls;
        }

        private void CreateZoomControls(Container ZoomRow)
        {
            float GetZoom() => Mathf.Clamp(PreviewZoom, 0.25f, 4f);  // Button/display factor in safe range
            float GetLevel() => PreviewZoomLevel;  // Unbounded level for input

            void SetZoom(float z)
            {   // Set zoom and update level based on factor
                PreviewZoom = z;
                PreviewZoomLevel = Mathf.Log(z);
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

            resLabel = null; gpuLabel = null; cpuLabel = null; pngLabel = null; maxLabel = null; warnLabel = null; resInput = null;

            Captue.PreviewImage = null;
            previewContainer = null;
            previewInitialized = false;

            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null)
                UnityEngine.Object.Destroy(existing);

            ownerRef = null;
        }

        public void UpdateBackgroundWindowVisibility()
        {   // Show background settings window when "Show Background" is off
            ref Captue ownerRef = ref World.OwnerInstance;
            if (ownerRef == null || window == null)
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
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to setup preview: {ex.Message}");
            }
        }

        public void UpdateEstimatesUI()
        {   // Refresh estimate labels, colors, and warnings based on current settings
            ref Captue ownerRef = ref World.OwnerInstance;
            if (ownerRef == null)
                return;

            if (resLabel == null || gpuLabel == null || cpuLabel == null || pngLabel == null || maxLabel == null || warnLabel == null)
                return;

            var (w, h) = CaptureUtilities.GetResolutionFromWidth(ResolutionWidth);
            var (gpuNeed, cpuNeed, rawBytes) = CaptureUtilities.EstimateMemoryForWidth(ResolutionWidth);
            var (gpuBudget, cpuBudget) = CaptureUtilities.GetMemoryBudgets();
            int maxSafe = CaptureUtilities.ComputeMaxSafeWidth();

            float gpuUsage = gpuBudget > 0 ? (float)gpuNeed / gpuBudget : 0f;
            float cpuUsage = cpuBudget > 0 ? (float)cpuNeed / cpuBudget : 0f;

            resLabel.Text = $"Res: {w}x{h}";
            gpuLabel.Text = $"GPU: {CaptureUtilities.FormatMB(gpuNeed)}";
            cpuLabel.Text = $"RAM: {CaptureUtilities.FormatMB(cpuNeed)}";
            pngLabel.Text = $"PNG: {CaptureUtilities.FormatMB((long)Math.Max(1024, rawBytes * 0.30))}";
            maxLabel.Text = $"Max Width: {maxSafe}";

            Color ok = Color.white;
            Color warn = new Color(1f, 0.8f, 0.25f);
            Color danger = new Color(1f, 0.35f, 0.2f);

            gpuLabel.Color = gpuUsage >= 1f ? danger : (gpuUsage >= 0.8f ? warn : ok);
            cpuLabel.Color = cpuUsage >= 1f ? danger : (cpuUsage >= 0.8f ? warn : ok);
            resLabel.Color = (gpuUsage >= 1f || cpuUsage >= 1f) ? danger : ((gpuUsage >= 0.8f || cpuUsage >= 0.8f) ? warn : ok);

            SetTextInputColor(resInput, (gpuUsage >= 1f || cpuUsage >= 1f) ? danger : ((gpuUsage >= 0.8f || cpuUsage >= 0.8f) ? warn : ok));

            if (gpuUsage >= 1f || cpuUsage >= 1f)
                warnLabel.Text = "Warning: resolution exceeds available memory. It will be clamped at capture.";
            else if (gpuUsage >= 0.8f || cpuUsage >= 0.8f)
                warnLabel.Text = "High memory usage: consider reducing resolution.";
            else
                warnLabel.Text = "";
        }

        public override void Refresh()
        {   // Refresh UI with current values
            if (window == null)
                return;

            Hide();
            Show();
        }

        public void TakeScreenshot()
        {   // Capture and save a screenshot at the specified resolution (moved from Utilities)
            ref Captue owner = ref World.OwnerInstance;
            if (owner == null) return;

            if (World.MainCamera == null)
            {
                if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
                    World.MainCamera = GameCamerasManager.main.world_Camera.camera;
                else
                {
                    UnityEngine.Debug.LogError("Cannot take screenshot: Camera not available");
                    return;
                }
            }

            int width = ResolutionWidth;
            int maxSafe = CaptureUtilities.ComputeMaxSafeWidth();
            if (width > maxSafe)
            {
                var (ow, oh) = CaptureUtilities.GetResolutionFromWidth(width);
                width = maxSafe;
                var (sw, sh) = CaptureUtilities.GetResolutionFromWidth(width);
                Debug.LogWarning($"Requested {ow}x{oh} exceeds safe memory budgets. Using {sw}x{sh} as the maximum available on this device.");
            }

            int height = Mathf.RoundToInt((float)width / (float)Screen.width * (float)Screen.height);

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
                renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                screenshotTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

                float z = Mathf.Max(Mathf.Exp(PreviewZoomLevel), 1e-6f);

                if (World.MainCamera.orthographic)
                {   // Orthographic: scale size inversely with zoom
                    World.MainCamera.orthographicSize = Mathf.Clamp(previousOrthographicSize / z, 1e-6f, 1_000_000f);
                }
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

                World.MainCamera.targetTexture = renderTexture;
                World.MainCamera.Render();

                CaptureUtilities.RestoreSceneVisibility(modified);
                World.MainCamera.cullingMask = prevMask;
                World.MainCamera.clearFlags = prevClearFlags;
                World.MainCamera.backgroundColor = prevBgColor;

                RenderTexture.active = renderTexture;

                screenshotTexture.ReadPixels(new Rect(0f, 0f, (float)width, (float)height), 0, 0);
                screenshotTexture.Apply();

                byte[] pngBytes = screenshotTexture.EncodeToPNG();

                string worldName = (SFS.Base.worldBase?.paths?.worldName) ?? "Unknown";
                string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                                      new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                var worldFolder = FileUtilities.InsertIo(sanitizedName, Main.ScreenCaptureFolder);

                string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";

                using (var ms = new MemoryStream(pngBytes))
                    FileUtilities.InsertIo(fileName, ms, worldFolder);

                var (gpuNeed, cpuNeed, rawBytes) = CaptureUtilities.EstimateMemoryForWidth(width);
                Debug.Log($"Saved {width}x{height}. Approx memory: GPU {CaptureUtilities.FormatMB(gpuNeed)} (incl. depth), CPU {CaptureUtilities.FormatMB(cpuNeed)}; file size {CaptureUtilities.FormatMB(pngBytes.LongLength)} (est PNG {CaptureUtilities.FormatMB(CaptureUtilities.EstimatePngSizeBytes(rawBytes))}).");
            }

            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Screenshot capture failed: {ex.Message}\n{ex.StackTrace}");
            }

            finally
            {
                World.MainCamera.targetTexture = null;
                RenderTexture.active = null;

                World.MainCamera.orthographic = previousOrthographic;
                World.MainCamera.orthographicSize = previousOrthographicSize;
                World.MainCamera.fieldOfView = previousFieldOfView;
                World.MainCamera.transform.position = previousPosition;
                QualitySettings.antiAliasing = previousAntiAliasing;

                if (renderTexture != null)
                    UnityEngine.Object.Destroy(renderTexture);

                if (screenshotTexture != null)
                    UnityEngine.Object.Destroy(screenshotTexture);
            }
        }
    }
}
