using System;
using UnityEngine;
using UnityEngine.Events;
using SFS.UI.ModGUI;
using static UITools.UIToolsBuilder;
using SFS.World;
using System.IO;
using System.Linq;
using SFS;
using SFS.IO;
using static ScreenCapture.FileHelper;
using static ScreenCapture.CaptureUtilities;

namespace ScreenCapture
{
    public class CaptureUIOptions
    {
        public Captue Owner;
        public Func<bool> GetShowBackground;
        public Action ToggleBackground;
        public Func<bool> GetShowTerrain;
        public Action ToggleTerrain;
        public Action CaptureAction;
        public UnityAction<string> ResolutionInputAction;
        public Action OnWindowOpened;
        public Action OnWindowCollapsed;
        public Action UpdatePreviewCulling;
        public Action UpdateBackgroundWindowVisibility;
        public Action<Container> SetupPreview;
    }

    public static class CaptureUI
    {
        public static Window bgWindow;
        public static float bgR = 0f, bgG = 0f, bgB = 0f;
        public static bool bgTransparent = true;

        public static void ShowUI(CaptureUIOptions options)
        {
            if (options == null || options.Owner == null)
                return;

            if (options.Owner.uiHolder != null)
                return;

            var owner = options.Owner;
            owner.uiHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "SFSRecorder");
            owner.closableWindow = CreateClosableWindow(owner.uiHolder.transform, Builder.GetRandomID(), 950, 600, 300, 100, true, true, 1f, "ScreenShot", minimized: false);
            owner.wasMinimized = owner.closableWindow.Minimized;

            owner.closableWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, new RectOffset(6, 6, 10, 6), false);

            var toolsContainer = Builder.CreateContainer(owner.closableWindow, 0, 0);
            toolsContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 12f, null, true);

            var imageContainer = Builder.CreateContainer(toolsContainer, 0, 0);
            imageContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 4f, new RectOffset(3, 3, 6, 4), true);

            options.SetupPreview?.Invoke(imageContainer);

            var hierarchy = Builder.CreateBox(toolsContainer, 310, 300, 0, 0, 0.5f);
            hierarchy.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 4f, new RectOffset(3, 3, 6, 4), true);
            Builder.CreateLabel(hierarchy, 200, 30, 0, 0, "Hierarchy");

            Builder.CreateSeparator(owner.closableWindow, 80, 0, 0);

            var bottom = Builder.CreateContainer(owner.closableWindow, 0, 0);
            bottom.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, 10f, null, true);

            var col1 = Builder.CreateContainer(bottom, 0, 0);
            col1.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, null, true);

            Builder.CreateToggleWithLabel(col1, 310, 37, options.GetShowBackground, () =>
            {
                options.ToggleBackground?.Invoke();
                options.UpdatePreviewCulling?.Invoke();
                options.UpdateBackgroundWindowVisibility?.Invoke();
            }, 0, 0, "Show Background");

            Builder.CreateToggleWithLabel(col1, 310, 37, options.GetShowTerrain, () =>
            {
                options.ToggleTerrain?.Invoke();
                options.UpdatePreviewCulling?.Invoke();
            }, 0, 0, "Show Terrain");

            var controls = Builder.CreateContainer(owner.closableWindow, 0, 0);
            controls.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, 10f, null, true);

            Builder.CreateButton(controls, 180, 58, 0, 0, options.CaptureAction, "Capture");
            Builder.CreateTextInput(controls, 180, 58, 0, 0, owner.resolutionWidth.ToString(), options.ResolutionInputAction);

            if (!owner.closableWindow.Minimized)
                options.OnWindowOpened?.Invoke();

            options.UpdateBackgroundWindowVisibility?.Invoke();
        }

        public static void HideUI(Captue owner)
        {
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

            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null)
                UnityEngine.Object.Destroy(existing);
        }

        public static void UpdateBackgroundWindowVisibility()
        {
            var owner = CaptureUtilities.CaptueInstance;
            bool shouldShow = owner != null && owner.closableWindow != null && !owner.closableWindow.Minimized && !owner.showBackground;

            if (shouldShow && bgWindow == null)
            {
                int id = Builder.GetRandomID();
                bgWindow = Builder.CreateWindow(owner.uiHolder.transform, id, 280, 320, (int)(owner.closableWindow.Position.x + 700), (int)owner.closableWindow.Position.y, draggable: true, savePosition: false, opacity: 1f, titleText: "Background");

                var content = Builder.CreateContainer(bgWindow, 0, 0);
                content.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 8f, new RectOffset(8, 8, 260, 8), true);

                Builder.CreateToggleWithLabel(content, 200, 46, () => bgTransparent, () =>
                {
                    bgTransparent = !bgTransparent;
                    UpdatePreviewCulling();
                }, 0, 0, "Transparent BG");

                Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "R", ((int)bgR).ToString(), val =>
                {
                    if (int.TryParse(val, out int r))
                    {
                        bgR = Mathf.Clamp(r, 0, 255);
                        UpdatePreviewCulling();
                    }
                });

                Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "G", ((int)bgG).ToString(), val =>
                {
                    if (int.TryParse(val, out int g))
                    {
                        bgG = Mathf.Clamp(g, 0, 255);
                        UpdatePreviewCulling();
                    }
                });

                Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "B", ((int)bgB).ToString(), val =>
                {
                    if (int.TryParse(val, out int b))
                    {
                        bgB = Mathf.Clamp(b, 0, 255);
                        UpdatePreviewCulling();
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
    {
        public static Captue CaptueInstance { get; set; }
        private static int previewWidth = 384;

        public static void UpdatePreviewCulling()
        {   // Update the preview camera's culling and background settings
            var instance = CaptueInstance;
            if (instance == null || instance.previewCamera == null)
                return;

            instance.previewCamera.cullingMask = instance.ComputeCullingMask();
            instance.ApplyBackgroundSettingsToCamera(instance.previewCamera);
        }

        public static bool IsAtmosphereObject(GameObject go)
        {   // Determine if the GameObject is an atmosphere object
            if (go == null)
                return false;
            var name = go.name ?? string.Empty;
            if (name.IndexOf("atmosphere", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return go.GetComponent<Atmosphere>() != null;
        }

        public static void CreatePreviewRenderTexture()
        {   // Create or recreate the preview RenderTexture
            if (CaptueInstance.previewRT != null)
            {
                CaptueInstance.previewRT.Release();
                UnityEngine.Object.Destroy(CaptueInstance.previewRT);
            }

            float screenAspect = (float)Screen.width / Screen.height;
            int rtHeight = Mathf.RoundToInt(previewWidth / screenAspect);

            CaptueInstance.previewRT = new RenderTexture(previewWidth, rtHeight, 0, RenderTextureFormat.ARGB32) { antiAliasing = 1, filterMode = FilterMode.Bilinear };
        }

        public static FolderPath CreateWorldFolder(string worldName)
        {   // Create a folder for the current world
            string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                                  new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

            return InsertIo(sanitizedName, Main.ScreenCaptureFolder);
        }

        public static string GetWorldName() =>
            (Base.worldBase?.paths?.worldName) ?? "Unknown";
    }
}
