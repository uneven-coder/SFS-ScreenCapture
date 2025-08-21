using System;
using System.IO;
using System.Linq;
using System.Globalization;
using SFS;
using SFS.IO;
using SFS.UI.ModGUI;
using SFS.World;
using UnityEngine;
using UnityEngine.UI;
using static ScreenCapture.FileHelper;
using static UITools.UIToolsBuilder;
using SFS.UI;
using SFS.Input;
using UI;
using static UITools.UIToolsBuilder;

namespace ScreenCapture
{
    public static class CaptureUI
    {   // Build and control the capture UI bound directly to a Captue instance
        public static Window bgWindow;
        public static Window rocketsWindow;
        public static float bgR = 0f, bgG = 0f, bgB = 0f;
        public static bool bgTransparent = false;

        private static Captue currentOwner;
        private static Container previewContainer;
        private static bool previewInitialized;

        // Stats UI elements
        private static Label resLabel;
        private static Label gpuLabel;
        private static Label cpuLabel;
        private static Label pngLabel;
        private static Label maxLabel;
        private static Label warnLabel;
        private static TextInput resInput;

        private static string FormatMB(long bytes) =>
            $"{bytes / (1024f * 1024f):0.#} MB";  // Present bytes in MB

        public static void ShowUI(Captue owner)
        {   // Build and display UI for the given owner
            if (owner == null)
                return;

            if (owner.uiHolder != null)
                return;

            currentOwner = owner;
            previewInitialized = false;
            previewContainer = null;

            owner.uiHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "SFSRecorder");
            owner.closableWindow = CreateClosableWindow(owner.uiHolder.transform, Builder.GetRandomID(), 950, 545, 300, 100, true, true, 1f, "ScreenShot", minimized: false);
            owner.wasMinimized = owner.closableWindow.Minimized;
            owner.closableWindow.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

            owner.closableWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, new RectOffset(6, 6, 10, 6), true);

            Container CreateToolsContainer()
            {   // Create preview and side toggles
                var toolsContainer = Builder.CreateContainer(owner.closableWindow, 0, 0);
                toolsContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, 12f, null, true);

                previewContainer = Builder.CreateContainer(toolsContainer, 0, 0);
                previewContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 4f, new RectOffset(6, 6, 6, 6), true);

                // Initialize preview immediately so it renders while the window is open
                owner.SetupPreview(previewContainer);
                MakePreviewNonBlocking();
                previewInitialized = true;

                // Controls panel next to preview
                var controlsContainer = Builder.CreateContainer(toolsContainer, 0, 0);
                controlsContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, null, true);

                Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => owner.showBackground, () =>
                {   // Toggle background visibility and adjust preview
                    owner.showBackground = !owner.showBackground;
                    owner.UpdatePreviewCulling();
                    UpdateBackgroundWindowVisibility();
                }, 0, 0, "Show Background");

                Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => owner.showTerrain, () =>
                {   // Toggle terrain visibility and adjust preview
                    owner.showTerrain = !owner.showTerrain;
                    owner.UpdatePreviewCulling();
                }, 0, 0, "Show Terrain");

                Builder.CreateToggleWithLabel(controlsContainer, 350, 37, () => rocketsWindow != null, () =>
                {
                    if (rocketsWindow == null)
                        BuildRocketsWindow(owner);
                    else
                    {
                        UnityEngine.Object.Destroy(rocketsWindow.gameObject);
                        rocketsWindow = null;
                    }
                }, 0, 0, "Show Rockets");

                return toolsContainer;
            }

            Container CreateControlsContainer()
            {   // Create capture controls and show estimates above them
                var controls = Builder.CreateContainer(owner.closableWindow, 0, 0);
                controls.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.LowerLeft, 10f, null, true);



                // Buttons + inputs row
                var row = Builder.CreateContainer(controls, 0, 0);
                row.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.LowerLeft, 10f, null, true);

                Builder.CreateButton(row, 180, 58, 0, 0, owner.TakeScreenshot, "Capture");
                resInput = Builder.CreateTextInput(row, 180, 58, 0, 0, owner.resolutionWidth.ToString(), val =>
                {
                    if (int.TryParse(val, out int w))
                    {
                        // clamp between 1 and the owner's max safe width
                        int maxW = owner.GetMaxSafeWidth();
                        w = Mathf.Clamp(w, 1, maxW);
                        // update the input display and owner state
                        resInput.Text = w.ToString();
                        owner.OnResolutionInputChange(w.ToString());
                        UpdateEstimatesUI(owner);
                    }
                });

                Builder.CreateSpace(row, 250, 58);

                var ZoomRow = Builder.CreateContainer(row, 0, 0);
                ZoomRow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 5f, null, true);

                Container CreateZoomControls()
                {
                    float GetZoom() => Mathf.Clamp(owner.previewZoom, 0.25f, 4f);  // Button/display factor in safe range
                    float GetLevel() => owner.previewZoomLevel;  // Unbounded level for input

                    void SetZoom(float z) =>
                        owner.SetPreviewZoom(z);

                    float StepInLog(float z, int dir)
                    {   // Compute next zoom via log-space lerp across [0.25, 4] using discrete steps
                        float min = 0.25f, max = 4f;
                        int steps = 20; // non-linear resolution
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
                            owner.SetPreviewZoomLevelUnclamped(lvl);
                            zoomInput.textInput.Text = $"{GetLevel():0.00}";
                            UpdateEstimatesUI(owner);
                        }
                    });

                    var bottomRow = Builder.CreateContainer(ZoomRow, 0, 0);
                    bottomRow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 10f, null, true);

                    Builder.CreateButton(bottomRow, 140, 58, 0, 0, () =>
                    {   // Decrease zoom using non-linear step (clamped factor)
                        float z = StepInLog(GetZoom(), -1);
                        SetZoom(z);
                        zoomInput.textInput.Text = $"{GetLevel():0.00}";
                        UpdateEstimatesUI(owner);
                    }, "Zoom -");

                    Builder.CreateButton(bottomRow, 140, 58, 0, 0, () =>
                    {   // Increase zoom using non-linear step (clamped factor)
                        float z = StepInLog(GetZoom(), +1);
                        SetZoom(z);
                        zoomInput.textInput.Text = $"{GetLevel():0.00}";
                        UpdateEstimatesUI(owner);
                    }, "Zoom +");


                                    // Stats row just above the capture button/input
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

                    return ZoomRow;
                }

                CreateZoomControls();

                // Initial stats update after building inputs
                UpdateEstimatesUI(owner);

                return controls;
            }

            CreateToolsContainer();
            var separator = Builder.CreateSeparator(owner.closableWindow, 930, 0, 0);
            CreateControlsContainer();

            // Ensure preview is created when the window opens; pause when collapsed via minimized flag
            var prevOpen = owner.windowOpenedAction;
            owner.windowOpenedAction = () =>
            {   // Initialize preview the first time the window opens
                prevOpen?.Invoke();
                EnsurePreviewSetup(owner);
                UpdateEstimatesUI(owner);
            };

            var prevCollapse = owner.windowCollapsedAction;
            owner.windowCollapsedAction = () =>
            {   // Pause preview rendering when collapsed (handled by minimized flag in Captue.Update)
                prevCollapse?.Invoke();
            };

            if (!owner.closableWindow.Minimized)
                owner.windowOpenedAction?.Invoke();

            UpdateBackgroundWindowVisibility();
        }

        public static void HideUI(Captue owner)
        {   // Tear down UI and related resources for the owner
            if (owner == null)
                return;

            if (owner.closableWindow != null && !owner.closableWindow.Minimized)
                owner.windowCollapsedAction?.Invoke();

            if (owner.uiHolder != null)
            {
                UnityEngine.Object.Destroy(owner.uiHolder);
                owner.uiHolder = null;
                owner.closableWindow = null;
            }

            if (owner.previewRT != null)
            {
                owner.previewRT.Release();
                UnityEngine.Object.Destroy(owner.previewRT);
                owner.previewRT = null;
            }

            if (owner.previewCamera != null)
            {
                UnityEngine.Object.Destroy(owner.previewCamera.gameObject);
                owner.previewCamera = null;
            }

            if (bgWindow != null)
            {
                UnityEngine.Object.Destroy(bgWindow.gameObject);
                bgWindow = null;
            }

            if (rocketsWindow != null)
            {
                UnityEngine.Object.Destroy(rocketsWindow.gameObject);
                rocketsWindow = null;
            }

            // Clear stats labels
            resLabel = null; gpuLabel = null; cpuLabel = null; pngLabel = null; maxLabel = null; warnLabel = null; resInput = null;

            owner.previewImage = null;
            previewContainer = null;
            previewInitialized = false;

            if (currentOwner == owner)
                currentOwner = null;

            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null)
                UnityEngine.Object.Destroy(existing);
        }

        public static void UpdateBackgroundWindowVisibility()
        {   // Show or hide the background settings window next to the main window
            var owner = currentOwner;
            if (owner == null)
                return;

            bool shouldShow = owner.closableWindow != null && !owner.closableWindow.Minimized && !owner.showBackground;

            if (shouldShow && bgWindow == null)
            {
                int id = Builder.GetRandomID();
                bgWindow = Builder.CreateWindow(owner.uiHolder.transform, id, 280, 320, (int)(owner.closableWindow.Position.x + 700), (int)owner.closableWindow.Position.y, draggable: true, savePosition: false, opacity: 1f, titleText: "Background");

                var content = Builder.CreateContainer(bgWindow, 0, 0);
                content.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 8f, new RectOffset(8, 8, 260, 8), true);

                Builder.CreateToggleWithLabel(content, 200, 46, () => bgTransparent, () =>
                {   // Toggle transparency and refresh preview bg
                    bgTransparent = !bgTransparent;
                    if (owner.previewCamera != null)
                        owner.ApplyBackgroundSettingsToCamera(owner.previewCamera);
                }, 0, 0, "Transparent BG");

                Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "R", ((int)bgR).ToString(), val =>
                {
                    if (int.TryParse(val, out int r))
                    {
                        bgR = Mathf.Clamp(r, 0, 255);
                        if (owner.previewCamera != null)
                            owner.ApplyBackgroundSettingsToCamera(owner.previewCamera);
                    }
                });

                Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "G", ((int)bgG).ToString(), val =>
                {
                    if (int.TryParse(val, out int g))
                    {
                        bgG = Mathf.Clamp(g, 0, 255);
                        if (owner.previewCamera != null)
                            owner.ApplyBackgroundSettingsToCamera(owner.previewCamera);
                    }
                });

                Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "B", ((int)bgB).ToString(), val =>
                {
                    if (int.TryParse(val, out int b))
                    {
                        bgB = Mathf.Clamp(b, 0, 255);
                        if (owner.previewCamera != null)
                            owner.ApplyBackgroundSettingsToCamera(owner.previewCamera);
                    }
                });

                Builder.CreateLabel(content, 200, 35, 0, 0, "RGB out of 255");
            }
            else if (!shouldShow && bgWindow != null)
            {
                UnityEngine.Object.Destroy(bgWindow.gameObject);
                bgWindow = null;
            }
        }

        private static void EnsurePreviewSetup(Captue owner)
        {   // Lazily initialize the preview when window is opened
            if (previewInitialized || previewContainer == null || owner == null)
                return;

            owner.SetupPreview(previewContainer);
            MakePreviewNonBlocking();
            previewInitialized = true;
        }

        private static void MakePreviewNonBlocking()
        {   // Prevent preview UI from capturing pointer events over other controls
            if (previewContainer == null)
                return;

            var go = previewContainer.gameObject;
            if (go == null)
                return;

            foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                g.raycastTarget = false;
        }

        private static void SetResolutionInputColor(Color color)
        {   // Color the resolution input background and text to reflect feasibility
            if (resInput == null)
                return;

            var img = resInput.gameObject.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
                img.color = color;

            var text = resInput.gameObject.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (text != null)
                text.color = color;
        }

        public static void UpdateEstimatesUI(Captue owner)
        {   // Refresh estimate labels, colors, and warnings based on current settings
            if (owner == null)
                return;

            if (resLabel == null || gpuLabel == null || cpuLabel == null || pngLabel == null || maxLabel == null || warnLabel == null)
                return;

            var (w, h) = owner.GetResolutionFromWidthPublic(owner.resolutionWidth);
            var (gpuNeed, cpuNeed, rawBytes) = owner.EstimateMemoryForWidthPublic(owner.resolutionWidth);
            var (gpuBudget, cpuBudget) = owner.GetMemoryBudgetsPublic();
            int maxSafe = owner.GetMaxSafeWidth();

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

            // Color the resolution input background/text
            SetResolutionInputColor((gpuUsage >= 1f || cpuUsage >= 1f) ? danger : ((gpuUsage >= 0.8f || cpuUsage >= 0.8f) ? warn : ok));

            if (gpuUsage >= 1f || cpuUsage >= 1f)
                warnLabel.Text = "Warning: resolution exceeds available memory. It will be clamped at capture.";
            else if (gpuUsage >= 0.8f || cpuUsage >= 0.8f)
                warnLabel.Text = "High memory usage: consider reducing resolution.";
            else
                warnLabel.Text = "";
        }

        private static void BuildRocketsWindow(Captue owner)
        {   // Create a separate window for rocket hierarchy and visibility toggles
            if (rocketsWindow != null)
                return;

            int id = Builder.GetRandomID();
            rocketsWindow = CreateClosableWindow(owner.uiHolder.transform, id, 480, 600, (int)(owner.closableWindow.Position.x + owner.closableWindow.Size.x + 10), (int)owner.closableWindow.Position.y, draggable: true, savePosition: false, opacity: 1f, titleText: "Rocket Hierarchy");
            rocketsWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 16f, new RectOffset(6, 6, 6, 6), true).childScaleHeight = true;
            rocketsWindow.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

            // Header row at the top: title + toggle all
            var header = Builder.CreateContainer(rocketsWindow, 0, 0);
            header.CreateLayoutGroup(SFS.UI.ModGUI.Type. Vertical, TextAnchor.UpperCenter, 8f, null, true);


            // Scrollable content container
            var listContent = Builder.CreateContainer(rocketsWindow, 0, 0);
            listContent.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 18f, null, true);

            var fitter = listContent.gameObject.GetComponent<ContentSizeFitter>() ?? listContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; 
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            void RefreshHierarchy()
            {   // Rebuild the rocket list and bind visibility toggles
                foreach (Transform child in listContent.gameObject.transform)
                    UnityEngine.Object.Destroy(child.gameObject);

                var rockets = UnityEngine.Object.FindObjectsOfType<SFS.World.Rocket>(includeInactive: true)
                               .OrderBy(r => r.rocketName ?? r.name)
                               .ToArray();

                foreach (var rocket in rockets)
                {
                    string label = string.IsNullOrWhiteSpace(rocket.rocketName) ? rocket.name : rocket.rocketName;

                    var toggle = Builder.CreateToggleWithLabel(listContent, 400, 34, () => owner.IsRocketVisible(rocket), () =>
                    {   // Toggle per-rocket visibility
                        bool cur = owner.IsRocketVisible(rocket); owner.SetRocketVisible(rocket, !cur);
                    }, 0, 0, label);

                    var toggleLE = toggle.gameObject.GetComponent<LayoutElement>() ?? toggle.gameObject.AddComponent<LayoutElement>();
                    toggleLE.minHeight = 30f; toggleLE.preferredHeight = 34f;
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(listContent.rectTransform);
            }

            Builder.CreateButton(header, 400, 46, 0, 0, () =>
            {   // Refresh the rocket hierarchy list
                RefreshHierarchy();
            }, "Refresh");
            Builder.CreateButton(header, 400, 46, 0, 0, () =>
            {   // Toggle all rockets on/off
                var rockets = UnityEngine.Object.FindObjectsOfType<SFS.World.Rocket>(includeInactive: true);
                bool anyVisible = rockets.Any(r => owner.IsRocketVisible(r)); owner.SetAllRocketsVisible(!anyVisible);
                RefreshHierarchy();
            }, "Toggle All");

            

            RefreshHierarchy();
        }

    }

    public static class CaptureUtilities
    {   // Stateless helpers for file-system and object checks
        public static bool IsAtmosphereObject(GameObject go)
        {   // Determine if a GameObject represents atmosphere
            if (go == null)
                return false;

            var name = go.name ?? string.Empty;
            if (name.IndexOf("atmosphere", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return go.GetComponent<Atmosphere>() != null;
        }

        public static FolderPath CreateWorldFolder(string worldName)
        {   // Ensure a per-world folder exists
            string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                                  new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

            return InsertIo(sanitizedName, Main.ScreenCaptureFolder);
        }

        public static string GetWorldName() =>
            (Base.worldBase?.paths?.worldName) ?? "Unknown";
    }
}
