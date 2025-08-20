using System;
using UnityEngine;
using UnityEngine.Events;
using UITools;
using SFS.UI.ModGUI;
using static UITools.UIToolsBuilder;

namespace ScreenCapture
{
    public class CaptureUIOptions
    {   // Encapsulates all UI callbacks and configuration for ShowUI
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
        // Show the UI and wire callbacks back to the capture instance
        public static void ShowUI(CaptureUIOptions options)
        {   // Build and attach UI, using owner to store references
            if (options == null || options.Owner == null)
                return;

            if (options.Owner.uiHolder != null)
                return; // already exists

            var owner = options.Owner;
            owner.uiHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "SFSRecorder");
            owner.closableWindow = CreateClosableWindow(owner.uiHolder.transform, Builder.GetRandomID(), 950, 600, 300, 100, true, true, 1f, "ScreenShot", minimized: false);
            owner.wasMinimized = owner.closableWindow.Minimized;

            owner.closableWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, new RectOffset(6, 6, 10, 6), false);

            var toolsContainer = Builder.CreateContainer(owner.closableWindow, 0, 0);
            toolsContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 12f, null, true);

            var imageContainer = Builder.CreateContainer(toolsContainer, 0, 0);
            imageContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 4f, new RectOffset(3, 3, 6, 4), true);

            // Let the capture instance setup the preview area (size, texture hookup)
            options.SetupPreview?.Invoke(imageContainer);

            var hierarchy = Builder.CreateBox(toolsContainer, 310, 300, 0, 0, 0.5f);
            hierarchy.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 4f, new RectOffset(3, 3, 6, 4), true);
            Builder.CreateLabel(hierarchy, 200, 30, 0, 0, "Hierarchy");

            Builder.CreateSeparator(owner.closableWindow, 80, 0, 0);

            var bottom = Builder.CreateContainer(owner.closableWindow, 0, 0);
            bottom.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, 10f, null, true);

            var col1 = Builder.CreateContainer(bottom, 0, 0);
            col1.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 20f, null, true);

            // Background toggle
            Builder.CreateToggleWithLabel(col1, 310, 37, options.GetShowBackground, () =>
            {
                options.ToggleBackground?.Invoke();
                options.UpdatePreviewCulling?.Invoke();
                options.UpdateBackgroundWindowVisibility?.Invoke();
            }, 0, 0, "Show Background");

            // Terrain toggle
            Builder.CreateToggleWithLabel(col1, 310, 37, options.GetShowTerrain, () =>
            {
                options.ToggleTerrain?.Invoke();
                options.UpdatePreviewCulling?.Invoke();
            }, 0, 0, "Show Terrain");

            var controls = Builder.CreateContainer(owner.closableWindow, 0, 0);
            controls.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, 10f, null, true);

            Builder.CreateButton(controls, 180, 58, 0, 0, options.CaptureAction, "Capture");
            Builder.CreateTextInput(controls, 180, 58, 0, 0, owner.resolutionWidth.ToString(), options.ResolutionInputAction);

            // Apply open behavior immediately if window starts expanded
            if (!owner.closableWindow.Minimized)
                options.OnWindowOpened?.Invoke();

            // Ensure BG window visibility matches initial state
            options.UpdateBackgroundWindowVisibility?.Invoke();
        }

        

        // Destroy the UI that was previously created and clear references on the owner
        public static void HideUI(Captue owner)
        {   // Remove UI and clear owner references
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
            {   // Destroy the preview camera to avoid lingering state
                UnityEngine.Object.Destroy(owner.previewCamera.gameObject);
                owner.previewCamera = null;
            }

            if (owner.bgWindow != null)
            {   // Destroy background settings window
                UnityEngine.Object.Destroy(owner.bgWindow.gameObject);
                owner.bgWindow = null;
            }

            owner.previewImage = null;

            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null)
                UnityEngine.Object.Destroy(existing);
        }
    }

    public static class CaptureUtilities
    {
        
        
    }
}
