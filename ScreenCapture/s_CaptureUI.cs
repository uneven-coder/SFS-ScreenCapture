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

namespace ScreenCapture
{
    public static class CaptureUI
    {   // Build and control the capture UI bound directly to a Captue instance
        public static Window bgWindow;
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
            owner.closableWindow = CreateClosableWindow(owner.uiHolder.transform, Builder.GetRandomID(), 950, 600, 300, 100, true, true, 1f, "ScreenShot", minimized: false);
            owner.wasMinimized = owner.closableWindow.Minimized;

            owner.closableWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, new RectOffset(6, 6, 10, 6), false);

            Container CreateToolsContainer()
            {   // Create preview area and hierarchy panel
                var toolsContainer = Builder.CreateContainer(owner.closableWindow, 0, 0);
                toolsContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 12f, null, true);

                previewContainer = Builder.CreateContainer(toolsContainer, 0, 0);
                previewContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 4f, new RectOffset(3, 3, 6, 4), true);

                // Initialize preview immediately so it renders while the window is open
                owner.SetupPreview(previewContainer);
                MakePreviewNonBlocking();
                previewInitialized = true;

                var hierarchy = Builder.CreateBox(toolsContainer, 310, 300, 0, 0, 0.5f);
                hierarchy.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 4f, new RectOffset(3, 3, 6, 4), true);
                Builder.CreateLabel(hierarchy, 200, 30, 0, 0, "Hierarchy");

                return toolsContainer;
            }

            Container CreateBottomContainer()
            {   // Create bottom column with scene toggles
                var bottom = Builder.CreateContainer(owner.closableWindow, 0, 0);
                bottom.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, 10f, null, true);

                var col1 = Builder.CreateContainer(bottom, 0, 0);
                col1.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, null, true);

                Builder.CreateToggleWithLabel(col1, 310, 37, () => owner.showBackground, () =>
                {   // Toggle background visibility and adjust preview
                    owner.showBackground = !owner.showBackground;
                    owner.UpdatePreviewCulling();
                    UpdateBackgroundWindowVisibility();
                }, 0, 0, "Show Background");

                Builder.CreateToggleWithLabel(col1, 310, 37, () => owner.showTerrain, () =>
                {   // Toggle terrain visibility and adjust preview
                    owner.showTerrain = !owner.showTerrain;
                    owner.UpdatePreviewCulling();
                }, 0, 0, "Show Terrain");

                                Builder.CreateSpace(bottom, 80, (37 * 2 + 20));

                var col2 = Builder.CreateContainer(bottom, 0, 0);
                col2.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, null, true);



                var col2Row1 = Builder.CreateContainer(col2, 0, 0);
                col2Row1.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 5f, null, true);

                Container CreateZoomControls()
                {
                    float GetZoom() => Mathf.Clamp(owner.previewZoom, 0.25f, 4f);  // Button/display factor in safe range
                    float GetLevel() => owner.previewZoomLevel;  // Unbounded level for input

                    void SetZoom(float z)
                    {   // Persist and apply zoom through the owner (clamped for buttons)
                        owner.SetPreviewZoom(z);
                    }

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
                    zoomInput = Builder.CreateInputWithLabel(col2Row1, (120 *2 +10), 42, 0, 0, "Zoom", $"{GetLevel():0.00}", val =>
                    {   // Parse unbounded zoom level from input
                        if (string.IsNullOrWhiteSpace(val))
                            return;

                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float lvl))
                        {
                            owner.SetPreviewZoomLevelUnclamped(lvl);
                            zoomInput.textInput.Text = $"{GetLevel():0.00}";
                        }
                    });


                    var bottomRow = Builder.CreateContainer(col2Row1, 0, 0);
                    bottomRow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 10f, null, true);

                    Builder.CreateButton(bottomRow, 120, 47, 0, 0, () =>
                    {   // Decrease zoom using non-linear step (clamped factor)
                        float z = StepInLog(GetZoom(), -1);
                        SetZoom(z);
                        zoomInput.textInput.Text = $"{GetLevel():0.00}";
                    }, "Zoom -");

                    Builder.CreateButton(bottomRow, 120, 47, 0, 0, () =>
                    {   // Increase zoom using non-linear step (clamped factor)
                        float z = StepInLog(GetZoom(), +1);
                        SetZoom(z);
                        zoomInput.textInput.Text = $"{GetLevel():0.00}";
                    }, "Zoom +");

                    return col2Row1;
                }

                CreateZoomControls();

                return bottom;
            }

            Container CreateControlsContainer()
            {   // Create capture controls for actions and resolution
                var controls = Builder.CreateContainer(owner.closableWindow, 0, 0);
                controls.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, 10f, null, true);

                Builder.CreateButton(controls, 180, 58, 0, 0, owner.TakeScreenshot, "Capture");
                Builder.CreateTextInput(controls, 180, 58, 0, 0, owner.resolutionWidth.ToString(), owner.OnResolutionInputChange);

                return controls;
            }

            CreateToolsContainer();

            var separator = Builder.CreateSeparator(owner.closableWindow, 80, 0, 0);
            var scale = separator.rectTransform.transform.localScale;
            scale.y = 2f;
            separator.rectTransform.transform.localScale = scale;
            

            CreateBottomContainer();
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
