using System;
using ModLoader;
using ModLoader.Helpers;
using SFS.IO;
using SFS.World;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;
using UITools;
using static ScreenCapture.Main;

namespace ScreenCapture
{
    public class Main : Mod
    {   // Primary static settings for capture
        public static int PreviewWidth { get; set; } = 384;

        public static FolderPath ScreenCaptureFolder { get; private set; }
        private static Captue s_captureInstance;

        public static int ResolutionWidth { get; set; } = 1980;
        public static float PreviewZoom { get; set; } = 1f;
        public static float PreviewZoomLevel { get; set; } = 0f;
        public static float PreviewBasePivotDistance { get; set; } = 100f;


        public static int LastScreenWidth { get; set; }
        public static int LastScreenHeight { get; set; }
        public static System.Collections.Generic.HashSet<Rocket> HiddenRockets { get; } = new System.Collections.Generic.HashSet<Rocket>();

        public override string ModNameID => "ScreenCapture";
        public override string DisplayName => "ScreenCapture";
        public override string Author => "Cratior";
        public override string MinimumGameVersionNecessary => "1.5.10";
        public override string ModVersion => "2.2.3";
        public override string Description => "Adds a screenshot button to the world scene, allowing you to take screenshots at custom resolutions.";

        public override void Load()
        {   // Initialize mod components and register scene-specific event handlers

            ScreenCaptureFolder = FileUtilities.InsertIo("ScreenCaptures", FileUtilities.savingFolder);

            SceneHelper.OnWorldSceneLoaded += CreateScreenCaptureUI;
            SceneHelper.OnWorldSceneUnloaded += DestroyScreenCaptureUI;

            // Create persistent capture instance
            if (s_captureInstance == null)
            {
                GameObject captureObject = new GameObject("ScreenCapture_Persistent");
                s_captureInstance = captureObject.AddComponent<Captue>();
                UnityEngine.Object.DontDestroyOnLoad(captureObject);
            }
        }

        private void CreateScreenCaptureUI()
        {   // Display the capture UI when entering the world scene
            if (s_captureInstance != null)
                s_captureInstance.ShowUI();
        }

        private void DestroyScreenCaptureUI()
        {   // Hide the capture UI when leaving the world scene
            if (s_captureInstance != null)
                s_captureInstance.HideUI();
        }

        // Static version of Captue for world context
        public static class World
        {   // Static version of Captue for world capture context

            public static GameObject UIHolder;
            public static Camera MainCamera;
            public static Camera PreviewCamera;
            public static bool wasMinimized;
            public static ref Captue OwnerInstance => ref s_captureInstance;

            static World()
            {   // Initialize static world capture settings
                MainCamera = GameCamerasManager.main?.world_Camera?.camera;
                // UIHolder = Main.uiHolder;
                wasMinimized = false;
            }

            public static void Awake()
            {   // Reset static state on awakening
                MainCamera = GameCamerasManager.main?.world_Camera?.camera;
                // UIHolder = Main.uiHolder;
                wasMinimized = false;
            }
        }
    }

    public class Captue : MonoBehaviour
    {
        // UI components
        internal ClosableWindow closableWindow;

        public static RawImage PreviewImage { get; set; }
        public static RenderTexture PreviewRT { get; set; }

        // Visibility flags
        public bool showBackground = true;
        public bool showTerrain = true;

        // Window state management
        internal Action windowOpenedAction;
        internal Action windowCollapsedAction;

        // UI windows
        internal BackgroundUI backgroundWindow;
        internal RocketsUI rocketsWindow;
        internal MainUI mainWindow; // Use instance UI instead of static helpers

        void Awake()
        {   // Initialize camera and actions when component awakens
            Main.World.Awake();

            windowOpenedAction = () =>
                    WorldTime.main?.SetState(0.0, true, false);

            windowCollapsedAction = () =>
                    WorldTime.main?.SetState(1.0, true, false);
        }

        public void OnWindowOpened(Action action) =>
            windowOpenedAction = action ?? (() => { });

        public void OnWindowCollapsed(Action action) =>
            windowCollapsedAction = action ?? (() => { });

        public void ShowUI()
        {   // Open the UI and ensure the main camera is ready
            if (mainWindow != null)
                return;
            mainWindow = new MainUI();
            mainWindow.Show();
            // World.UIHolder = closableWindow?.gameObject;
        }

        public void HideUI()
        {   // Hide the UI instance
            if (mainWindow == null)
                return;
            mainWindow.Hide();
            mainWindow = null;
        }

        private void Update()
        {   // Handle window minimization and update the preview from MonoBehaviour
            if (World.MainCamera == null)
                World.MainCamera = GameCamerasManager.main?.world_Camera?.camera;

            if (closableWindow == null)
                return;

            bool minimized = closableWindow.Minimized;
            ref bool wasMinimized = ref Main.World.wasMinimized;
            if (minimized != wasMinimized)
            {   // Handle window state change
                (minimized ? windowCollapsedAction : windowOpenedAction)?.Invoke();
                wasMinimized = minimized;
                Main.World.wasMinimized = wasMinimized;
                mainWindow?.UpdateBackgroundWindowVisibility();
            }

            // Only update preview when window is open and components are available
            if (minimized || PreviewImage == null || World.MainCamera == null || World.PreviewCamera == null)
                return;

            UpdatePreviewRendering();
        }

        private void UpdatePreviewRendering()
        {   // Handle preview rendering and screen size changes
            bool screenSizeChanged = Screen.width != LastScreenWidth || Screen.height != LastScreenHeight;
            bool rtNeedsRecreation = PreviewRT == null || !PreviewRT.IsCreated();

            if (screenSizeChanged || rtNeedsRecreation)
            {   // Recreate render texture when screen size changes
                LastScreenWidth = Screen.width;
                LastScreenHeight = Screen.height;

                if (PreviewRT != null)
                {
                    PreviewRT.Release();
                    UnityEngine.Object.Destroy(PreviewRT);
                }

                PreviewRT = CaptureUtilities.CreatePreviewRenderTexture(PreviewWidth);
                if (PreviewImage != null)
                {
                    PreviewImage.texture = PreviewRT;
                    var rect = PreviewImage.GetComponent<RectTransform>();
                    var (finalWidth, finalHeight) = CaptureUtilities.CalculatePreviewDimensions();
                    rect.sizeDelta = new Vector2(finalWidth, finalHeight);
                }

                // Update preview camera target
                if (World.PreviewCamera != null)
                    World.PreviewCamera.targetTexture = PreviewRT;
            }

            // Render preview frame
            if (World.PreviewCamera != null && PreviewRT != null)
            {   // Configure and render preview
                World.PreviewCamera.cullingMask = CaptureUtilities.ComputeCullingMask(showBackground);
                World.PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
                World.PreviewCamera.backgroundColor = CaptureUtilities.GetBackgroundColor();
                CaptureUtilities.ApplyPreviewZoom(World.MainCamera, World.PreviewCamera, PreviewZoomLevel);

                var modified = CaptureUtilities.ApplySceneVisibilityTemporary(showBackground, showTerrain, HiddenRockets);
                var prevTarget = World.PreviewCamera.targetTexture;

                World.PreviewCamera.targetTexture = PreviewRT;
                World.PreviewCamera.Render();
                World.PreviewCamera.targetTexture = prevTarget;

                CaptureUtilities.RestoreSceneVisibility(modified);
            }
        }
    }
}