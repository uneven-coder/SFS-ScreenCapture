using System;
using System.Globalization;
using SFS.UI.ModGUI;
using UnityEngine;
using static UITools.UIToolsBuilder;

namespace ScreenCapture
{
    public class MainUI : UIBase
    {   // Manage the main capture UI bound directly to a Captue instance
        private Captue ownerRef;
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

        public override void Show(Captue owner)
        {   // Build and display main window UI for the given owner
            if (owner == null || IsOpen)
                return;

            ownerRef = owner;
            previewInitialized = false;
            previewContainer = null;

            owner.uiHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "SFSRecorder");
            var closableWindow = CreateClosableWindow(owner.uiHolder.transform, Builder.GetRandomID(), 950, 545, 300, 100, true, true, 1f, "ScreenShot", minimized: false);
            window = closableWindow;
            owner.closableWindow = closableWindow;
            owner.wasMinimized = closableWindow.Minimized;
            window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

            window.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, new RectOffset(6, 6, 10, 6), true);

            CreateToolsContainer();
            Builder.CreateSeparator(window, 930, 0, 0);
            CreateControlsContainer();

            var prevOpen = owner.windowOpenedAction;
            owner.windowOpenedAction = () =>
            {   // Initialize preview the first time the window opens
                prevOpen?.Invoke();
                EnsurePreviewSetup();
                UpdateEstimatesUI();
            };

            var prevCollapse = owner.windowCollapsedAction;
            owner.windowCollapsedAction = () =>
            {   // Pause preview rendering when collapsed (handled in Captue.Update)
                prevCollapse?.Invoke();
            };
            
            if (!((UITools.ClosableWindow)window).Minimized)
                owner.windowOpenedAction?.Invoke();

            UpdateBackgroundWindowVisibility();
        }

        private Container CreateToolsContainer()
        {   // Create preview and side toggles
            var toolsContainer = Builder.CreateContainer(window, 0, 0);
            toolsContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, 12f, null, true);

            previewContainer = Builder.CreateContainer(toolsContainer, 0, 0);
            previewContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 4f, new RectOffset(6, 6, 6, 6), true);

            CaptureUtilities.SetupPreview(ownerRef, previewContainer);
            DisableRaycastsInChildren(previewContainer.gameObject);
            previewInitialized = true;

            var controlsContainer = Builder.CreateContainer(toolsContainer, 0, 0);
            controlsContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, null, true);

            Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => ownerRef.showBackground, () =>
            {   // Toggle background visibility and adjust preview
                ownerRef.showBackground = !ownerRef.showBackground;
                ownerRef.UpdatePreviewCulling();
                UpdateBackgroundWindowVisibility();
            }, 0, 0, "Show Background");

            Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => ownerRef.showTerrain, () =>
            {   // Toggle terrain visibility and adjust preview
                ownerRef.showTerrain = !ownerRef.showTerrain;
                ownerRef.UpdatePreviewCulling();
            }, 0, 0, "Show Terrain");

            Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => ownerRef.rocketsWindow?.IsOpen ?? false, () =>
            {   // Show or hide the rockets window
                CaptureUtilities.ShowHideWindow<RocketsUI>(ownerRef, ref ownerRef.rocketsWindow, 
                    (o) => {}, // Show action
                    () => {}); // Hide action
            }, 0, 0, "Show Rockets");

            return toolsContainer;
        }

        private Container CreateControlsContainer()
        {   // Create capture controls and show estimates above them
            var controls = Builder.CreateContainer(window, 0, 0);
            controls.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.LowerLeft, 10f, null, true);

            var row = Builder.CreateContainer(controls, 0, 0);
            row.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.LowerLeft, 10f, null, true);

            Builder.CreateButton(row, 180, 58, 0, 0, ownerRef.TakeScreenshot, "Capture");
            resInput = Builder.CreateTextInput(row, 180, 58, 0, 0, ownerRef.resolutionWidth.ToString(), val =>
            {
                if (int.TryParse(val, out int w))
                {
                    int maxW = ownerRef.GetMaxSafeWidth();
                    w = Mathf.Clamp(w, 1, maxW);
                    resInput.Text = w.ToString();
                    ownerRef.OnResolutionInputChange(w.ToString());
                    UpdateEstimatesUI();
                }
            });

            Builder.CreateSpace(row, 250, 58);

            var ZoomRow = Builder.CreateContainer(row, 0, 0);
            ZoomRow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 5f, null, true);

            CreateZoomControls(ZoomRow);

            var statsRow1 = Builder.CreateContainer(controls, 0, 0);
            statsRow1.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, 10f, null, true);

            resLabel = Builder.CreateLabel(statsRow1, 210, 34, 0, 0, "Res: -");
            gpuLabel = Builder.CreateLabel(statsRow1, 170, 34, 0, 0, "GPU: -");
            cpuLabel = Builder.CreateLabel(statsRow1, 170, 34, 0, 0, "RAM: -");

            var statsRow2 = Builder.CreateContainer(controls, 0, 0);
            statsRow2.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, 10f, null, true);

            pngLabel = Builder.CreateLabel(statsRow2, 170, 34, 0, 0, "PNG: -");
            maxLabel = Builder.CreateLabel(statsRow2, 220, 34, 0, 0, "Max Width: -");
            warnLabel = Builder.CreateLabel(statsRow2, 400, 34, 0, 0, "");
            warnLabel.Color = new Color(1f, 0.35f, 0.2f);

            return controls;
        }

        private void CreateZoomControls(Container ZoomRow)
        {
            float GetZoom() => Mathf.Clamp(ownerRef.previewZoom, 0.25f, 4f);  // Button/display factor in safe range
            float GetLevel() => ownerRef.previewZoomLevel;  // Unbounded level for input

            void SetZoom(float z) =>
                ownerRef.SetPreviewZoom(z);

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
                    ownerRef.SetPreviewZoomLevelUnclamped(lvl);
                    zoomInput.textInput.Text = $"{GetLevel():0.00}";
                    UpdateEstimatesUI();
                }
            });

            var bottomRow = Builder.CreateContainer(ZoomRow, 0, 0);
            bottomRow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 10f, null, true);

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
            if (ownerRef == null)
                return;

            if (window != null && !((UITools.ClosableWindow)window).Minimized)
                ownerRef.windowCollapsedAction?.Invoke();

            if (ownerRef.uiHolder != null)
            {
                UnityEngine.Object.Destroy(ownerRef.uiHolder);
                ownerRef.uiHolder = null;
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

            ownerRef.previewImage = null;
            previewContainer = null;
            previewInitialized = false;

            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null)
                UnityEngine.Object.Destroy(existing);

            ownerRef = null;
        }

        public void UpdateBackgroundWindowVisibility()
        {   // Show background settings window when "Show Background" is off
            if (ownerRef == null || window == null)
                return;

            // Only show background window when background is disabled and main window isn't minimized
            bool shouldShow = !ownerRef.showBackground && !((UITools.ClosableWindow)window).Minimized;
            
            if (shouldShow && ownerRef.backgroundWindow == null)
                ownerRef.backgroundWindow = new BackgroundUI();
                
            if (shouldShow && !ownerRef.backgroundWindow.IsOpen)
                ownerRef.backgroundWindow.Show(ownerRef);
            else if (!shouldShow && ownerRef.backgroundWindow != null)
            {   // Hide background window when no longer needed
                ownerRef.backgroundWindow.Hide();
                ownerRef.backgroundWindow = null;
            }
        }

        private void EnsurePreviewSetup()
        {   // Lazily initialize the preview when window is opened
            if (previewInitialized || previewContainer == null || ownerRef == null)
                return;

            CaptureUtilities.SetupPreview(ownerRef, previewContainer);
            DisableRaycastsInChildren(previewContainer.gameObject);
            previewInitialized = true;
        }

        public void UpdateEstimatesUI()
        {   // Refresh estimate labels, colors, and warnings based on current settings
            if (ownerRef == null)
                return;

            if (resLabel == null || gpuLabel == null || cpuLabel == null || pngLabel == null || maxLabel == null || warnLabel == null)
                return;

            var (w, h) = ownerRef.GetResolutionFromWidthPublic(ownerRef.resolutionWidth);
            var (gpuNeed, cpuNeed, rawBytes) = ownerRef.EstimateMemoryForWidthPublic(ownerRef.resolutionWidth);
            var (gpuBudget, cpuBudget) = ownerRef.GetMemoryBudgetsPublic();
            int maxSafe = ownerRef.GetMaxSafeWidth();

            float gpuUsage = gpuBudget > 0 ? (float)gpuNeed / gpuBudget : 0f;
            float cpuUsage = cpuBudget > 0 ? (float)cpuNeed / cpuBudget : 0f;

            resLabel.Text = $"Res: {w}x{h}";
            gpuLabel.Text = $"GPU: {FormatMB(gpuNeed)}";
            cpuLabel.Text = $"RAM: {FormatMB(cpuNeed)}";
            pngLabel.Text = $"PNG: {FormatMB((long)Math.Max(1024, rawBytes * 0.30))}";
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
            if (window == null || ownerRef == null)
                return;
                
            Hide();
            Show(ownerRef);
        }
    }
}
