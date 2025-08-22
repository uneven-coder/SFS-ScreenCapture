using System;
using ModLoader;
using ModLoader.Helpers;
using SFS.IO;
using SFS.World;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;
using UITools;

namespace ScreenCapture
{
    public class Main : Mod
    {
        public static FolderPath ScreenCaptureFolder { get; private set; }
        private static Captue s_captureInstance;

        public override string ModNameID => "ScreenCapture";
        public override string DisplayName => "ScreenCapture";
        public override string Author => "Cratior";
        public override string MinimumGameVersionNecessary => "1.5.10";
        public override string ModVersion => "2.2.3";
        public override string Description => "Adds a screenshot button to the world scene, allowing you to take screenshots at custom resolutions.";

        public override void Load()
        {   // Initialize mod components and register scene-specific event handlers
            base.Load();

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

        public override void Early_Load() => base.Early_Load();

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
    }
    
    public class Captue : MonoBehaviour
    {   // Main capture component that manages screenshot functionality
        internal Camera mainCamera;
        internal Camera previewCamera;
        
        // Resolution settings
        internal int resolutionWidth = 1980;
        internal int previewWidth = 384;
        
        // Camera zoom settings
        internal float previewZoom = 1f;
        internal float previewZoomLevel = 0f;
        internal float previewBasePivotDistance = 100f;
        
        // UI components
        internal ClosableWindow closableWindow;
        internal GameObject uiHolder;
        public RawImage previewImage;
        public RenderTexture previewRT;
        
        // Visibility flags
        internal bool showBackground = true;
        internal bool showTerrain = true;
        
        // Screen state tracking
        internal int lastScreenWidth;
        internal int lastScreenHeight;
        
        // Window state management
        internal Action windowOpenedAction;
        internal Action windowCollapsedAction;
        internal bool wasMinimized;
        
        // UI windows
        internal BackgroundUI backgroundWindow;
        internal RocketsUI rocketsWindow;
        internal MainUI mainWindow; // Use instance UI instead of static helpers
        
        internal System.Collections.Generic.HashSet<Rocket> hiddenRockets = new System.Collections.Generic.HashSet<Rocket>();
        internal bool onlyShowEnabledInHierarchy = false;

        void Awake()
        {   // Initialize camera and actions when component awakens
            mainCamera = GameCamerasManager.main?.world_Camera?.camera;

            windowOpenedAction = () =>
            {
                if (WorldTime.main != null)
                    WorldTime.main.SetState(0.0, true, false);
            };

            windowCollapsedAction = () =>
            {
                if (WorldTime.main != null)
                    WorldTime.main.SetState(1.0, true, false);
            };
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
            mainWindow.Show(this);
        }

        public void HideUI()
        {   // Hide the UI instance
            if (mainWindow == null)
                return;
            mainWindow.Hide();
            mainWindow = null;
        }

        private void Update()
        {   // Handle window minimization and update the preview
            if(mainCamera == null)
                mainCamera = GameCamerasManager.main?.world_Camera?.camera;
            
            if (closableWindow == null)
                return;
            
            bool minimized = closableWindow.Minimized;
            if (minimized != wasMinimized)
            {   // Execute appropriate actions based on window state
                if (minimized)
                    windowCollapsedAction?.Invoke();  // Pause time when minimized
                else
                    windowOpenedAction?.Invoke();  // Resume time when opened
                    
                wasMinimized = minimized;
                mainWindow?.UpdateBackgroundWindowVisibility();
            }
            
            if(!minimized && previewImage != null && mainCamera != null)
            {   // Update preview: recreate texture when screen changes or missing texture
                if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight || previewRT == null || !previewRT.IsCreated())
                {   // Update preview dimensions and render texture
                    lastScreenWidth = Screen.width; lastScreenHeight = Screen.height;
                    var rect = previewImage.GetComponent<RectTransform>();
                    var (finalWidth, finalHeight) = CaptureUtilities.CalculatePreviewDimensions(this);
                    rect.sizeDelta = new Vector2(finalWidth, finalHeight);
                    CaptureUtilities.CreatePreviewRenderTexture(this);
                    previewImage.texture = previewRT;
                }
                if (previewCamera != null)
                {   // Render preview image using updated camera settings
                    previewCamera.cullingMask = CaptureUtilities.ComputeCullingMask(this);
                    CaptureUtilities.ApplyBackgroundSettingsToCamera(this, previewCamera);
                    CaptureUtilities.ApplyPreviewZoom(this);
                    var modified = CaptureUtilities.ApplySceneVisibilityTemporary(this);
                    var prevTarget = previewCamera.targetTexture;
                    previewCamera.targetTexture = previewRT;
                    previewCamera.Render();
                    previewCamera.targetTexture = prevTarget;
                    CaptureUtilities.RestoreSceneVisibility(modified);
                }
            }
        }

        internal void OnResolutionInputChange(string newValue) => 
            CaptureUtilities.OnResolutionInputChange(this, newValue);

        internal void TakeScreenshot() => 
            CaptureUtilities.TakeScreenshot(this);
            
        internal bool IsRocketVisible(Rocket rocket) => 
            rocket != null && !hiddenRockets.Contains(rocket);

        internal void SetRocketVisible(Rocket rocket, bool visible)
        {   // Toggle a rocket's visibility in preview/screenshot
            if (rocket == null)
                return;
            if (visible) hiddenRockets.Remove(rocket); else hiddenRockets.Add(rocket);
            UpdatePreviewCulling();
        }

        internal void SetAllRocketsVisible(bool visible)
        {   // Set all rockets visible or hidden at once
            hiddenRockets.Clear();
            if (!visible)
            {   
                var rockets = UnityEngine.Object.FindObjectsOfType<Rocket>(includeInactive: true);
                foreach (var r in rockets) hiddenRockets.Add(r);
            }
            UpdatePreviewCulling();
        }
        
        internal void SetPreviewZoom(float factor) => 
            CaptureUtilities.SetPreviewZoom(this, factor);

        internal void SetPreviewZoomLevelUnclamped(float level) => 
            CaptureUtilities.SetPreviewZoomLevelUnclamped(this, level);
            
        public void UpdatePreviewCulling()
        {   // Update the preview camera's culling and background settings
            if (previewCamera == null)
                return;
            previewCamera.cullingMask = CaptureUtilities.ComputeCullingMask(this);
            CaptureUtilities.ApplyBackgroundSettingsToCamera(this, previewCamera);
        }

        internal void SetupPreview(Container imageContainer) 
        {   // Initialize preview rendering with the provided container
            CaptureUtilities.SetupPreview(this, imageContainer);
        }
    }

}
