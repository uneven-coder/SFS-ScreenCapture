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
    {   // Primary static settings for capture with adaptive preview optimization
        public static int PreviewWidth { get; set; } = 256; // Start with low detail, adaptive system will adjust

        public static FolderPath ScreenCaptureFolder { get; private set; }
        private static Captue s_captureInstance;

        private static int s_resolutionWidth = 1980;
        public static int ResolutionWidth 
        { 
            get => s_resolutionWidth;
            set
            {
                if (s_resolutionWidth != value)
                {
                    s_resolutionWidth = value;
                    CacheManager.InvalidateMemoryCache();
                }
            }
        }

        public static float PreviewZoom { get; set; } = 1f;
        public static float PreviewZoomLevel { get; set; } = 0f;
        public static float PreviewBasePivotDistance { get; set; } = 100f;
        
        // Cropping values with change tracking for cache invalidation
        private static float s_cropLeft = 0f, s_cropTop = 0f, s_cropRight = 0f, s_cropBottom = 0f;
        
        public static float CropLeft 
        { 
            get => s_cropLeft; 
            set { if (s_cropLeft != value) { s_cropLeft = value; CacheManager.InvalidateCropCache(); } }
        }
        public static float CropTop 
        { 
            get => s_cropTop; 
            set { if (s_cropTop != value) { s_cropTop = value; CacheManager.InvalidateCropCache(); } }
        }
        public static float CropRight 
        { 
            get => s_cropRight; 
            set { if (s_cropRight != value) { s_cropRight = value; CacheManager.InvalidateCropCache(); } }
        }
        public static float CropBottom 
        { 
            get => s_cropBottom; 
            set { if (s_cropBottom != value) { s_cropBottom = value; CacheManager.InvalidateCropCache(); } }
        }

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

            SceneHelper.OnSceneLoaded += ManageUI;

            // Create persistent capture instance
            if (s_captureInstance == null)
            {   // Initialize persistent MonoBehaviour for capture functionality
                GameObject captureObject = new GameObject("ScreenCapture_Persistent");
                s_captureInstance = captureObject.AddComponent<Captue>();
                UnityEngine.Object.DontDestroyOnLoad(captureObject);
            }
        }

        private void ManageUI(UnityEngine.SceneManagement.Scene scene)
        {   // Handle UI creation and destruction based on scene
            Debug.Log($"ManageUI called for scene: {scene.name}");
            
            if (scene.name == "World_PC")
            {   // Create UI when entering world scene
                if (s_captureInstance != null && (World.UIHolder == null || s_captureInstance.mainWindow == null))
                {
                    Debug.Log("Creating UI for world scene");
                    s_captureInstance.OnSceneEntered();
                    s_captureInstance.ShowUI();
                }
                else if (s_captureInstance != null)
                    Debug.Log($"UI creation skipped - UIHolder null: {World.UIHolder == null}, mainWindow null: {s_captureInstance.mainWindow == null}");
            }
            else
            {   // Destroy UI when leaving world scene (single, safe cleanup)
                var inst = s_captureInstance;
                if (inst != null && (World.UIHolder != null || inst.mainWindow != null))
                {
                    Debug.Log("Destroying UI for non-world scene");
                    inst.OnSceneExited();
                }
            }
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

        // Screen size change detection and notification
        public static Action OnScreenSizeChanged;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private float lastPreviewUpdate;
        
        // Camera tracking for preview optimization
        private Vector3 lastCameraPosition;
        private Quaternion lastCameraRotation;
        
        // Adaptive preview rendering
        private float lastSignificantChange;
        private bool isHighDetailMode = false;
        private const float HIGH_DETAIL_TIMEOUT = 2f; // Switch to low detail after 2 seconds of minimal changes

        void Awake()
        {   // Initialize camera and actions when component awakens
            Main.World.Awake();

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            // Initialize adaptive preview in high detail mode
            lastSignificantChange = Time.unscaledTime;
            isHighDetailMode = true;

            windowOpenedAction = () =>
                    WorldTime.main?.SetState(0.0, true, false);

            windowCollapsedAction = () =>
                    WorldTime.main?.SetState(1.0, true, false);
        }

        public void OnWindowOpened(Action action) =>
            windowOpenedAction = action ?? (() => { });

        public void OnWindowCollapsed(Action action) =>
            windowCollapsedAction = action ?? (() => { });

        public void OnSceneEntered()
        {   // Reset state when entering world scene
            World.Awake();
        }

        public void OnSceneExited()
        {   // Clean up state when exiting world scene and destroy UI holder
            try
            {
                if (mainWindow != null)
                {
                    mainWindow.Hide();
                    mainWindow = null;
                }

                // Reset UI references and destroy holder
                closableWindow = null;
                PreviewImage = null;
                if (PreviewRT != null)
                {
                    PreviewRT.Release();
                    UnityEngine.Object.Destroy(PreviewRT);
                    PreviewRT = null;
                }

                // Destroy the UI holder to ensure fresh creation on next scene entry
                if (World.UIHolder != null)
                {
                    UnityEngine.Object.Destroy(World.UIHolder);
                    World.UIHolder = null;
                }

                Debug.Log("Scene exit cleanup completed");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Error during scene exit cleanup: {ex.Message}");
                mainWindow = null;
                closableWindow = null;
                PreviewImage = null;
                PreviewRT = null;
                World.UIHolder = null;
            }

            
        }

        public void ShowUI()
        {   // Open the UI and ensure the main camera is ready
            Debug.Log($"ShowUI called, mainWindow is null: {mainWindow == null}");
            if (mainWindow != null)
            {
                Debug.Log("UI already exists, returning");
                return;
            }

            Debug.Log("Creating new MainUI");
            mainWindow = new MainUI();
            mainWindow.Show();
            Debug.Log("MainUI created and shown");
            
            if (GameCamerasManager.main?.world_Camera?.rotation?.Value != null)
                GameCamerasManager.main.world_Camera.rotation.OnChange += updateCameraRotation;
        }

        public void HideUI()
        {   // Hide the UI instance
            try
            {
                if (mainWindow == null)
                    return;
                mainWindow.Hide();
                mainWindow = null;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Error hiding UI: {ex.Message}");
                mainWindow = null;
            }

            if (GameCamerasManager.main?.world_Camera?.rotation?.Value != null)
                GameCamerasManager.main.world_Camera.rotation.OnChange -= updateCameraRotation;
        }

        private void updateCameraRotation()
        {   // Update preview camera rotation to match main camera
            if (World.MainCamera != null && World.PreviewCamera != null && GameCamerasManager.main?.world_Camera?.camera != null)
            {
                var targetRotation = GameCamerasManager.main.world_Camera.camera.transform.rotation;
                World.MainCamera.transform.rotation = targetRotation;
                World.PreviewCamera.transform.rotation = targetRotation;
            }
        }

        // private void LateUpdate()
        // {
        //     if (World.MainCamera != null)
        //         World.MainCamera.transform.rotation = 
        // }

        private void Update()
        {   // Handle window minimization and adaptive preview updates
            if (World.MainCamera == null)
                World.MainCamera = GameCamerasManager.main?.world_Camera?.camera;

            CheckForScreenSizeChange();

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

            // Adaptive preview updates when window is open
            if (!minimized && PreviewImage != null && World.MainCamera != null && World.PreviewCamera != null)
            {
                bool needsUpdate = CheckForSignificantChanges();
                if (needsUpdate)
                    UpdatePreviewRendering();
            }
        }

        private bool CheckForSignificantChanges()
        {   // Adaptive change detection with different thresholds for high/low detail modes
            if (World.MainCamera == null || World.PreviewCamera == null)
                return false;

            var currentPos = World.MainCamera.transform.position;
            var currentRot = World.MainCamera.transform.rotation;
            
            float positionChange = (currentPos - lastCameraPosition).sqrMagnitude;
            float rotationChange = Quaternion.Angle(currentRot, lastCameraRotation);
            
            // Determine change level and appropriate update frequency
            bool isSignificantChange = positionChange > 0.1f || rotationChange > 2f;
            bool isMinorChange = positionChange > 0.001f || rotationChange > 0.1f;
            
            float currentTime = Time.unscaledTime;
            float timeSinceLastUpdate = currentTime - lastPreviewUpdate;
            
            // Adaptive update intervals based on change magnitude
            float updateInterval = isSignificantChange ? 0.02f :  // 50 FPS for significant changes
                                  isMinorChange ? 0.05f :         // 20 FPS for minor changes  
                                  0.2f;                           // 5 FPS for minimal changes

            bool timeForUpdate = timeSinceLastUpdate >= updateInterval;
            
            if (isSignificantChange || (timeForUpdate && isMinorChange))
            {   // Significant or scheduled minor change
                lastSignificantChange = currentTime;
                isHighDetailMode = true;
                lastCameraPosition = currentPos;
                lastCameraRotation = currentRot;
                return true;
            }
            
            if (timeForUpdate && currentTime - lastSignificantChange > HIGH_DETAIL_TIMEOUT)
            {   // Switch to low detail mode after timeout
                isHighDetailMode = false;
                lastCameraPosition = currentPos;
                lastCameraRotation = currentRot;
                return true;
            }
            
            return timeForUpdate && isMinorChange;
        }

        public void RequestPreviewUpdate()
        {   // Public method to request preview update from UI components with high detail mode
            if (closableWindow != null && !closableWindow.Minimized && PreviewImage != null && World.MainCamera != null && World.PreviewCamera != null)
            {
                // Force high detail mode for manual updates
                lastSignificantChange = Time.unscaledTime;
                isHighDetailMode = true;
                UpdatePreviewRendering();
            }
        }

        private void CheckForScreenSizeChange()
        {   // Monitor screen resolution and trigger action when it changes
            if (lastScreenWidth != Screen.width || lastScreenHeight != Screen.height)
            {
                lastScreenWidth = Screen.width;
                lastScreenHeight = Screen.height;
                
                try
                {
                    OnScreenSizeChanged?.Invoke();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Error in screen size change handler: {ex.Message}");
                }
            }
        }

        private void UpdatePreviewRendering()
        {   // Adaptive preview rendering with dynamic resolution based on change magnitude
            bool screenSizeChanged = CacheManager.IsScreenSizeChanged();
            
            // Determine appropriate preview resolution based on activity level
            int targetPreviewWidth = isHighDetailMode ? 1024 : 256;
            bool needsResolutionChange = Captue.PreviewRT == null || 
                                       !Captue.PreviewRT.IsCreated() || 
                                       Math.Abs(Captue.PreviewRT.width - CaptureUtilities.CalculateTargetRTWidth(targetPreviewWidth)) > 10;

            if (screenSizeChanged || needsResolutionChange)
            {   // Recreate render texture with appropriate resolution
                LastScreenWidth = Screen.width;
                LastScreenHeight = Screen.height;

                if (Captue.PreviewRT != null)
                {
                    Captue.PreviewRT.Release();
                    UnityEngine.Object.Destroy(Captue.PreviewRT);
                }

                PreviewWidth = targetPreviewWidth;
                Captue.PreviewRT = CaptureUtilities.CreatePreviewRenderTexture(targetPreviewWidth);
                
                if (Captue.PreviewImage != null)
                {
                    Captue.PreviewImage.texture = Captue.PreviewRT;
                    var rect = Captue.PreviewImage.GetComponent<RectTransform>();
                    var (finalWidth, finalHeight) = CaptureUtilities.CalculatePreviewDimensions();
                    rect.sizeDelta = new Vector2(finalWidth, finalHeight);
                    
                    if (CropLeft > 0 || CropTop > 0 || CropRight > 0 || CropBottom > 0)
                        CaptureUtilities.UpdatePreviewCropping();
                }

                if (World.PreviewCamera != null)
                    World.PreviewCamera.targetTexture = Captue.PreviewRT;
            }

            lastPreviewUpdate = Time.unscaledTime;

            if (World.PreviewCamera != null && Captue.PreviewRT != null)
            {   // Render preview with current visibility settings
                World.PreviewCamera.cullingMask = CaptureUtilities.ComputeCullingMask(showBackground);
                World.PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
                World.PreviewCamera.backgroundColor = CaptureUtilities.GetBackgroundColor();
                CaptureUtilities.ApplyPreviewZoom(World.MainCamera, World.PreviewCamera, PreviewZoomLevel);

                var modified = CaptureUtilities.ApplySceneVisibilityTemporary(showBackground, showTerrain, HiddenRockets);
                var prevTarget = World.PreviewCamera.targetTexture;

                if (CropLeft > 0 || CropTop > 0 || CropRight > 0 || CropBottom > 0)
                {
                    if (screenSizeChanged && mainWindow != null)
                        mainWindow.RefreshLayoutForCroppedPreview();
                }

                World.PreviewCamera.targetTexture = Captue.PreviewRT;
                World.PreviewCamera.Render();
                World.PreviewCamera.targetTexture = prevTarget;

                CaptureUtilities.RestoreSceneVisibility(modified);
            }
        }
    }
}