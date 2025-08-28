using System;
using System.Collections.Generic;
using ModLoader;
using ModLoader.Helpers;
using SFS.IO;
using SFS.World;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;
using UITools;
using static ScreenCapture.Main;
using static ScreenCapture.CaptureUtilities;

namespace ScreenCapture
{
    public class Main : Mod
    {
        public static int PreviewWidth { get; set; } = 256;
        public static FolderPath ScreenCaptureFolder { get; private set; }

        private static Captue s_captureInstance;
        private static int s_resolutionWidth = 1980;
        private static float s_cropLeft, s_cropTop, s_cropRight, s_cropBottom;

        public static int ResolutionWidth
        {
            get => s_resolutionWidth;
            set
            {   // Update resolution width and invalidate cache if changed
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

        public static float CropLeft
        {
            get => s_cropLeft;
            set
            {   // Update crop left and invalidate cache if changed
                if (s_cropLeft != value)
                {
                    s_cropLeft = value;
                    CacheManager.InvalidateCropCache();
                }
            }
        }

        public static float CropTop
        {
            get => s_cropTop;
            set
            {   // Update crop top and invalidate cache if changed
                if (s_cropTop != value)
                {
                    s_cropTop = value;
                    CacheManager.InvalidateCropCache();
                }
            }
        }

        public static float CropRight
        {
            get => s_cropRight;
            set
            {   // Update crop right and invalidate cache if changed
                if (s_cropRight != value)
                {
                    s_cropRight = value;
                    CacheManager.InvalidateCropCache();
                }
            }
        }

        public static float CropBottom
        {
            get => s_cropBottom;
            set
            {   // Update crop bottom and invalidate cache if changed
                if (s_cropBottom != value)
                {
                    s_cropBottom = value;
                    CacheManager.InvalidateCropCache();
                }
            }
        }

        public static int LastScreenWidth { get; set; }
        public static int LastScreenHeight { get; set; }
        public static HashSet<Rocket> HiddenRockets { get; } = new HashSet<Rocket>();

        public override string ModNameID => "ScreenCapture";
        public override string DisplayName => "ScreenCapture";
        public override string Author => "Cratior";
        public override string MinimumGameVersionNecessary => "1.5.10";
        public override string ModVersion => "1.4.3"; // release, updates, fixes/changes
        public override string Description => "Adds a screenshot button to the world scene, allowing you to take screenshots at custom resolutions.";

        public override void Load()
        {   // Initialize mod components and register scene-specific event handlers
            ScreenCaptureFolder = FileUtilities.InsertIo("ScreenCaptures", FileUtilities.savingFolder);
            SceneHelper.OnSceneLoaded += ManageUI;

            if (s_captureInstance == null)
                CreateCaptureInstance();
        }

        private void CreateCaptureInstance()
        {   // Create persistent capture instance that survives scene changes
            var captureObject = new GameObject("ScreenCapture_Persistent");
            s_captureInstance = captureObject.AddComponent<Captue>();
            UnityEngine.Object.DontDestroyOnLoad(captureObject);
        }

        private void ManageUI(UnityEngine.SceneManagement.Scene scene)
        {   // Handle UI creation and destruction based on scene
            bool isWorldScene = scene.name == "World_PC";

            if (isWorldScene)
                HandleWorldSceneEntry();
            else
                HandleSceneExit();
        }

        private void HandleWorldSceneEntry()
        {   // Create UI when entering world scene
            if (s_captureInstance != null)
            {
                s_captureInstance.OnSceneEntered();
                s_captureInstance.ShowUI();
            }
        }

        private void HandleSceneExit()
        {   // Destroy UI when leaving world scene
            if (s_captureInstance != null)
                s_captureInstance.OnSceneExited();
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
                wasMinimized = false;
            }

            public static void Awake()
            {   // Reset static state on awakening
                MainCamera = GameCamerasManager.main?.world_Camera?.camera;
                wasMinimized = false;
            }
        }
    }

    public class Captue : MonoBehaviour
    {
        // UI components
        internal ClosableWindow closableWindow;
        internal BackgroundUI backgroundWindow;
        internal RocketsUI rocketsWindow;
        internal MainUI mainWindow;

        public static RawImage PreviewImage { get; set; }
        public static RenderTexture PreviewRT { get; set; }

        // Visibility flags
        public bool showBackground = true;
        public bool showTerrain = true;

        // Window state management
        internal Action windowOpenedAction;
        internal Action windowCollapsedAction;

        // Screen size change detection and notification
        public static Action OnScreenSizeChanged;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private float lastPreviewUpdate;

        // Camera tracking for preview optimization
        private Vector3 lastCameraPosition;
        private Quaternion lastCameraRotation;

        // Adaptive preview rendering with simplified quality system
        private CameraActivity currentActivity = CameraActivity.Static;

        // Debounce fields for camera quality updates
        private CameraActivity lastAppliedActivity = CameraActivity.Static;
        private CameraActivity lastRequestedActivity = CameraActivity.Static;
        private float qualityDebounceDelay = 0.05f; // Reduced delay for faster quality switching
        private float lastQualityRequestTime;
        private bool qualityApplyScheduled = false;

        // Optimized movement detection thresholds
        private static readonly float VelocityThresholdSq = 0.01f;       // Squared velocity threshold for faster comparison
        private static readonly float RotationVelocityThreshold = 2f;    // Increased for less sensitivity
        private static readonly float PositionDeltaThresholdSq = 0.0001f; // Squared position change threshold
        private static readonly float RotationDeltaThreshold = 1f;       // Increased threshold
        private static readonly float[] UpdateIntervals = { 0.05f, 0.12f }; // Moving: 20fps, Static: 8.3fps

        void Awake()
        {   // Initialize camera and actions when component awakens
            Main.World.Awake();

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            windowOpenedAction = () => WorldTime.main?.SetState(0.0, true, false);
            windowCollapsedAction = () => WorldTime.main?.SetState(1.0, true, false);
        }

        public void OnWindowOpened(Action action) =>
            windowOpenedAction = action ?? (() => { });

        public void OnWindowCollapsed(Action action) =>
            windowCollapsedAction = action ?? (() => { });

        public void OnSceneEntered() =>
            World.Awake();


        public void OnSceneExited()
        {   // Clean up state when exiting world scene and destroy UI holder
            try
            {
                CleanupMainWindow();
                CleanupPreviewResources();
                CleanupUIHolder();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Error during scene exit cleanup: {ex.Message}");
                ResetToSafeState();
            }
        }

        private void CleanupMainWindow()
        {   // Safely cleanup main window instance
            if (mainWindow != null)
            {
                mainWindow.Hide();
                mainWindow = null;
            }
            closableWindow = null;
        }

        private void CleanupPreviewResources()
        {   // Release and destroy preview rendering resources
            PreviewImage = null;
            if (PreviewRT != null)
            {
                PreviewRT.Release();
                UnityEngine.Object.Destroy(PreviewRT);
                PreviewRT = null;
            }
        }

        private void CleanupUIHolder()
        {   // Destroy the UI holder to ensure fresh creation on next scene entry
            if (World.UIHolder != null)
            {
                UnityEngine.Object.Destroy(World.UIHolder);
                World.UIHolder = null;
            }
        }

        private void ResetToSafeState()
        {   // Reset all references to safe state in case of cleanup errors
            mainWindow = null;
            closableWindow = null;
            PreviewImage = null;
            PreviewRT = null;
            World.UIHolder = null;
        }

        public void ShowUI()
        {   // helper function from main
            if (mainWindow != null)
                return;

            mainWindow = new MainUI();
            mainWindow.Show();

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

            if (!minimized && PreviewImage != null && World.MainCamera != null && World.PreviewCamera != null)
            {
                if (CheckForSignificantChanges(ref lastCameraPosition, ref lastCameraRotation, lastPreviewUpdate,
                                               VelocityThresholdSq, RotationVelocityThreshold, PositionDeltaThresholdSq, RotationDeltaThreshold,
                                               UpdateIntervals[0], UpdateIntervals[1], out var activity))
                {
                    currentActivity = activity;
                    UpdatePreviewRendering();
                }
            }

            // Apply any pending quality change if debounce time passed
            // Debounced application of camera quality settings
            if (qualityApplyScheduled && Time.unscaledTime - lastQualityRequestTime >= qualityDebounceDelay)
                ApplyCameraQualitySettingsImmediate();
        }





        public void RequestPreviewUpdate()
        {   // Public method to request preview update from UI components
            if (closableWindow != null && !closableWindow.Minimized &&
                PreviewImage != null && World.MainCamera != null && World.PreviewCamera != null)
            { /* Intentionally minimal: next Update will handle refresh */ }
        }

        private void CheckForScreenSizeChange()
        {   // Monitor screen resolution and trigger action when it changes
            if (lastScreenWidth != Screen.width || lastScreenHeight != Screen.height)
            {
                lastScreenWidth = Screen.width;
                lastScreenHeight = Screen.height;

                try
                { OnScreenSizeChanged?.Invoke(); }
                catch (System.Exception ex)
                { Debug.LogWarning($"Error in screen size change handler: {ex.Message}"); }
            }
        }

        private void UpdatePreviewRendering()
        {   // Delegate to UI system which handles proper PREVIEW_SCALE_FIX scaling and cropping
            EnsureAdaptivePreviewRT();
            ApplyCameraQualitySettings(); // Schedule camera quality update (debounced)
            mainWindow?.RequestPreviewUpdate();
            lastPreviewUpdate = Time.unscaledTime;
        }

        private void EnsureAdaptivePreviewRT()
        {   // Adjust preview RenderTexture size and filtering based on activity
            try
            {
                if (PreviewImage == null)
                    return;

                int targetResolution;
                FilterMode filter;
                int antiAliasing;

                if (currentActivity == CameraActivity.Moving)
                {   // Low quality for moving
                    targetResolution = 128;
                    filter = FilterMode.Point;
                    antiAliasing = 1;
                }
                else
                {   // High quality for static
                    targetResolution = 1024;
                    filter = FilterMode.Bilinear;
                    antiAliasing = 4;
                }

                // Calculate aspect-correct dimensions from target resolution
                float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
                int targetW, targetH;

                if (screenAspect >= 1f)
                {   // Landscape: width = target, height derived from aspect
                    targetW = targetResolution;
                    targetH = Mathf.Max(64, Mathf.RoundToInt(targetResolution / screenAspect));
                }
                else
                {   // Portrait: height = target, width derived from aspect
                    targetH = targetResolution;
                    targetW = Mathf.Max(64, Mathf.RoundToInt(targetResolution * screenAspect));
                }

                // Check if RenderTexture needs recreation
                bool needsRecreation = PreviewRT == null ||
                                        PreviewRT.width != targetW ||
                                        PreviewRT.height != targetH ||
                                        PreviewRT.antiAliasing != antiAliasing ||
                                        PreviewRT.filterMode != filter;

                if (!needsRecreation)
                    return;

                if (PreviewRT != null)
                {
                    PreviewRT.Release();
                    UnityEngine.Object.Destroy(PreviewRT);
                    PreviewRT = null;
                }

                var rt = new RenderTexture(targetW, targetH, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = antiAliasing,
                    filterMode = filter
                };
                rt.Create();
                PreviewRT = rt;

                if (PreviewImage != null)
                    PreviewImage.texture = PreviewRT;

                if (World.PreviewCamera != null)
                    World.PreviewCamera.targetTexture = PreviewRT;
            }
            catch (System.Exception ex)
            { Debug.LogWarning($"Adaptive RT setup failed: {ex.Message}"); }
        }

        private void ApplyCameraQualitySettings()
        {   // Schedule camera quality changes using debounce to avoid redundant rapid updates
            if (World.PreviewCamera == null)
                return;

            // If activity hasn't changed and there's no pending update, do nothing
            if (currentActivity == lastAppliedActivity && !qualityApplyScheduled)
                return;

            // Record requested activity and reset debounce timer
            lastRequestedActivity = currentActivity;
            lastQualityRequestTime = Time.unscaledTime;
            qualityApplyScheduled = true;
        }

        private void ApplyCameraQualitySettingsImmediate()
        {   // Immediately apply camera rendering quality settings (called after debounce)
            if (World.PreviewCamera == null)
                return;

            try
            {
                bool isHighQuality = lastRequestedActivity == CameraActivity.Static;

                if (isHighQuality)
                {   // High quality settings for static preview
                    World.PreviewCamera.renderingPath = RenderingPath.DeferredShading;
                    World.PreviewCamera.allowHDR = true;
                    World.PreviewCamera.allowMSAA = true;
                }
                else
                {   // Low quality settings for movement
                    World.PreviewCamera.renderingPath = RenderingPath.Forward;
                    World.PreviewCamera.allowHDR = false;
                    World.PreviewCamera.allowMSAA = false;
                }

                // Update tracking state
                lastAppliedActivity = lastRequestedActivity;
                qualityApplyScheduled = false;
            }
            catch (System.Exception ex)
            { Debug.LogWarning($"Camera quality settings failed: {ex.Message}"); }
        }
    }
}