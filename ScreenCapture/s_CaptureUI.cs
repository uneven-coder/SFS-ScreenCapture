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
            {   // Create preview area only; move hierarchy to its own Rockets window
                var toolsContainer = Builder.CreateContainer(owner.closableWindow, 0, 0);
                toolsContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, 12f, null, true);

                previewContainer = Builder.CreateContainer(toolsContainer, 0, 0);
                previewContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 4f, new RectOffset(6, 6, 6, 6), true);

                // Initialize preview immediately so it renders while the window is open
                owner.SetupPreview(previewContainer);
                MakePreviewNonBlocking();
                previewInitialized = true;

                // BuildRocketsWindow(owner);

                // make controlls pannel next to preview
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

            // Container CreateBottomContainer()
            // {   // Create bottom column with scene toggles
            //     var bottom = Builder.CreateContainer(owner.closableWindow, 0, 0);
            //     bottom.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, 10f, new RectOffset(12, 12, 2, 2), true);

            //     // var col1 = Builder.CreateContainer(bottom, 0, 0);
            //     // col1.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, null, true);



            //     // Builder.CreateSpace(bottom, 80, (37 * 2 + 20));

            //     var col1 = Builder.CreateContainer(bottom, 0, 0);
            //     col1.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 20f, null, true);





            //     return bottom;
            // }

            Container CreateControlsContainer()
            {   // Create capture controls for actions and resolution
                var controls = Builder.CreateContainer(owner.closableWindow, 0, 0);
                controls.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.LowerLeft, 10f, null, true);

                Builder.CreateButton(controls, 180, 58, 0, 0, owner.TakeScreenshot, "Capture");
                Builder.CreateTextInput(controls, 180, 58, 0, 0, owner.resolutionWidth.ToString(), owner.OnResolutionInputChange);

                Builder.CreateSpace(controls, 250, 58);

                var ZoomRow = Builder.CreateContainer(controls, 0, 0);
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

                    // Builder.CreateSpace(col2Row1, 15, 58);

                    InputWithLabel zoomInput = null;
                    zoomInput = Builder.CreateInputWithLabel(ZoomRow, (140 * 2 + 10), 52, 0, 0, "Zoom", $"{GetLevel():0.00}", val =>
                    {   // Parse unbounded zoom level from input
                        if (string.IsNullOrWhiteSpace(val))
                            return;

                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float lvl))
                        {
                            owner.SetPreviewZoomLevelUnclamped(lvl);
                            zoomInput.textInput.Text = $"{GetLevel():0.00}";
                        }
                    });


                    var bottomRow = Builder.CreateContainer(ZoomRow, 0, 0);
                    bottomRow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 10f, null, true);

                    Builder.CreateButton(bottomRow, 140, 58, 0, 0, () =>
                    {   // Decrease zoom using non-linear step (clamped factor)
                        float z = StepInLog(GetZoom(), -1);
                        SetZoom(z);
                        zoomInput.textInput.Text = $"{GetLevel():0.00}";
                    }, "Zoom -");

                    Builder.CreateButton(bottomRow, 140, 58, 0, 0, () =>
                    {   // Increase zoom using non-linear step (clamped factor)
                        float z = StepInLog(GetZoom(), +1);
                        SetZoom(z);
                        zoomInput.textInput.Text = $"{GetLevel():0.00}";
                    }, "Zoom +");

                    return ZoomRow;
                }

                CreateZoomControls();

                return controls;
            }

            CreateToolsContainer();

            var separator = Builder.CreateSeparator(owner.closableWindow, 930, 0, 0);
            // var scale = separator.rectTransform.transform.localScale;
            // scale.y = 1.2f;
            // separator.rectTransform.transform.localScale = scale;


            // CreateBottomContainer();
            CreateControlsContainer();

            // Ensure preview is created when the window opens; pause when collapsed via minimized flag
            var prevOpen = owner.windowOpenedAction;
            owner.windowOpenedAction = () =>
            {   // Initialize preview the first time the window opens
                prevOpen?.Invoke();
                EnsurePreviewSetup(owner);
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

            owner.previewImage = null;
            previewContainer = null;
            previewInitialized = false;

            if (currentOwner == owner)
                currentOwner = null;

            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null)
                UnityEngine.Object.Destroy(existing);
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

            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
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
