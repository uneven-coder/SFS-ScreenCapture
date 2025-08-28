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

        // Camera tracking for preview optimization with packed state
        private Vector3 lastCameraPosition;
        private Quaternion lastCameraRotation;
        private CameraActivity currentActivity = CameraActivity.Static;
        private CameraActivity lastAppliedActivity = CameraActivity.Static;
        
        // Optimized state flags for branch reduction and CPU prediction
        private float qualityDebounceDelay = 0.03f;
        private float lastQualityRequestTime;
        private bool qualityApplyScheduled = false;
        
        // Pre-calculated quality states for branch prediction
        private static readonly (int res, FilterMode filter, int aa)[] QualityPresets = 
        {
            (128, FilterMode.Point, 1),     // Moving/Low quality - index 0
            (1024, FilterMode.Bilinear, 4)  // Static/High quality - index 1
        };
        
        // RT pool for instant switching without allocation lag
        private RenderTexture[] rtPool = new RenderTexture[2];
        private int activeRTIndex = -1;

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
        {   // Release and destroy preview rendering resources including RT pool
            PreviewImage = null;
            
            // Clean up main RT
            if (PreviewRT != null)
            {
                PreviewRT.Release();
                UnityEngine.Object.Destroy(PreviewRT);
                PreviewRT = null;
            }
            
            // Clean up RT pool to prevent memory leaks
            for (int i = 0; i < rtPool.Length; i++)
            {
                if (rtPool[i] != null)
                {
                    rtPool[i].Release();
                    UnityEngine.Object.Destroy(rtPool[i]);
                    rtPool[i] = null;
                }
            }
            activeRTIndex = -1;
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

        void updateCameraRotation()
        {   // Sync preview camera transform with main camera without overriding zoom-sensitive properties
            if (World.MainCamera == null || World.PreviewCamera == null || 
                GameCamerasManager.main?.world_Camera?.camera == null) return;

            var mainCameraSource = GameCamerasManager.main.world_Camera.camera;
            var mainTransform = mainCameraSource.transform;
            var previewTransform = World.PreviewCamera.transform;

            previewTransform.position = mainTransform.position;
            previewTransform.rotation = mainTransform.rotation;
            previewTransform.localScale = mainTransform.localScale;
        }

        private void Update()
        {   // Handle window minimization and adaptive preview updates with branch optimization
            if (World.MainCamera == null) World.MainCamera = GameCamerasManager.main?.world_Camera?.camera;
            
            CheckForScreenSizeChange();

            if (closableWindow == null) return;

            bool minimized = closableWindow.Minimized;
            ref bool wasMinimized = ref Main.World.wasMinimized;

            if (minimized != wasMinimized)
            {   // Handle state change with ternary for branch reduction
                (minimized ? windowCollapsedAction : windowOpenedAction)?.Invoke();
                wasMinimized = minimized;
                Main.World.wasMinimized = wasMinimized;
                mainWindow?.UpdateBackgroundWindowVisibility();
            }

            if (minimized || PreviewImage == null || World.MainCamera == null || World.PreviewCamera == null) return;

            // Movement cadence gate: schedule preview update when interval elapses
            if (CheckForSignificantChanges(ref lastCameraPosition, ref lastCameraRotation, lastPreviewUpdate,
                VelocityThresholdSq, RotationVelocityThreshold, PositionDeltaThresholdSq, RotationDeltaThreshold,
                UpdateIntervals[0], UpdateIntervals[1], out var activity))
            {   // Update activity and schedule rendering
                currentActivity = activity;
                lastPreviewUpdate = Time.unscaledTime;
                
                // Sync camera once when we decided an update is due
                if (GameCamerasManager.main?.world_Camera?.camera != null)
                    updateCameraRotation();

                qualityApplyScheduled = true;
                lastQualityRequestTime = Time.unscaledTime - qualityDebounceDelay;  // expire debounce
                UpdateAdaptivePreviewSystem();
            }
            else if (currentActivity != lastAppliedActivity || qualityApplyScheduled)
                UpdateAdaptivePreviewSystem();
        }

        private void CheckForScreenSizeChange()
        {   // Monitor screen resolution and schedule adaptive update when it changes
            if (lastScreenWidth != Screen.width || lastScreenHeight != Screen.height)
            {
                lastScreenWidth = Screen.width;
                lastScreenHeight = Screen.height;

                try
                { 
                    OnScreenSizeChanged?.Invoke(); 
                    InvalidateRTPool();
                    SchedulePreviewUpdate(immediate: true);

                    // Ensure the preview image fits inside its box at full scale after resolution change
                    FitPreviewImageToBox(1.0f);
                }
                catch (System.Exception ex)
                { Debug.LogWarning($"Error in screen size change handler: {ex.Message}"); }
            }
        }

        private void InvalidateRTPool()
        {   // Clear RT pool to force recreation with new dimensions
            for (int i = 0; i < rtPool.Length; i++)
            {
                if (rtPool[i] != null)
                {
                    rtPool[i].Release();
                    UnityEngine.Object.Destroy(rtPool[i]);
                    rtPool[i] = null;
                }
            }
            activeRTIndex = -1;
        }

        private void UpdateAdaptivePreviewSystem()
        {   // Unified adaptive preview system with CPU branch prediction optimization
            if (PreviewImage == null || World.PreviewCamera == null) return;

            bool activityChanged = currentActivity != lastAppliedActivity;
            if (!activityChanged && !qualityApplyScheduled) return;  // Fast exit for no-op frames

            float currentTime = Time.unscaledTime;
            bool debounceExpired = float.IsNegativeInfinity(lastQualityRequestTime) ||
                                   (currentTime - lastQualityRequestTime) >= qualityDebounceDelay;
            
            // Only start debounce on the transition edge; do not reset while already scheduled
            if (activityChanged && !qualityApplyScheduled)
            {   // Schedule quality update once per transition
                lastQualityRequestTime = currentTime;
                qualityApplyScheduled = true;
            }

            if (qualityApplyScheduled && debounceExpired)
            {   // Compute target preset and dimensions based on screen aspect (crop applied via UV/RawImage)
                int qualityIndex = currentActivity == CameraActivity.Static ? 1 : 0;
                var (targetRes, filter, antiAliasing) = QualityPresets[qualityIndex];
                
                float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
                int targetW = screenAspect >= 1f ? targetRes : Mathf.Max(64, Mathf.RoundToInt(targetRes * screenAspect));
                int targetH = screenAspect >= 1f ? Mathf.Max(64, Mathf.RoundToInt(targetRes / screenAspect)) : targetRes;

                // Switch or validate RT first to avoid overwriting settings later
                SwitchToPooledRT(qualityIndex, targetW, targetH, filter, antiAliasing);

                // Ensure camera transform is synced to main at render time without touching FOV/ortho
                if (GameCamerasManager.main?.world_Camera?.camera != null)
                    updateCameraRotation();

                // Apply background color for transparent/solid modes
                try { World.PreviewCamera.backgroundColor = CaptureUtilities.GetBackgroundColor(); } catch { }

                // Apply zoom after sync and after RT switch to preserve state
                try { CaptureUtilities.ApplyPreviewZoom(World.MainCamera, World.PreviewCamera, Main.PreviewZoomLevel); } catch { }

                // Apply quality options (HDR/MSAA/etc.)
                ApplyCameraQualityBatch(qualityIndex == 1);
                
                // Ensure UI layout matches new RT and crop
                CaptureUtilities.UpdatePreviewImageLayoutForCurrentRT();

                // Fit RawImage inside its parent box at full scale without changing the box
                FitPreviewImageToBox(1.0f);
                
                // Temporarily toggle scene visibility for background/terrain and hidden rockets
                System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> changedRenderers = null;
                try
                {
                    changedRenderers = CaptureUtilities.ApplySceneVisibilityTemporary(showBackground, showTerrain, Main.HiddenRockets);
                }
                catch { }

                // Render updated frame
                try
                {
                    if (World.PreviewCamera?.targetTexture != null)
                        World.PreviewCamera.Render();
                }
                finally
                {
                    try { CaptureUtilities.RestoreSceneVisibility(changedRenderers); } catch { }
                }
                
                lastAppliedActivity = currentActivity;
                qualityApplyScheduled = false;
            }
        }

        private void FitPreviewImageToBox(float scaleFactor = 1.0f)
        {   // Size RawImage to fit inside its parent box while preserving cropped aspect with proper UI layering
            if (PreviewImage == null)
                return;

            var img = PreviewImage.rectTransform;
            var parent = img != null ? img.parent as RectTransform : null;
            if (img == null || parent == null)
                return;

            // Ensure proper UI hierarchy and prevent hardware overlay rendering
            var imgGO = img.gameObject;
            
            // Remove any Canvas components that might cause independent rendering
            var allComponents = imgGO.GetComponentsInChildren<Component>();
            foreach (var comp in allComponents)
            {
                if (comp != null && comp.GetType().Name == "Canvas")
                    UnityEngine.Object.DestroyImmediate(comp);
            }
            
            // Ensure image follows parent's layer and sorting
            imgGO.layer = parent.gameObject.layer;
            
            // Add mask to image to contain content within bounds
            var mask = imgGO.GetComponent<RectMask2D>() ?? imgGO.AddComponent<RectMask2D>();

            float boxW = Mathf.Max(1f, parent.rect.width);
            float boxH = Mathf.Max(1f, parent.rect.height);

            int rtW = (PreviewRT != null && PreviewRT.IsCreated()) ? PreviewRT.width : Screen.width;
            int rtH = (PreviewRT != null && PreviewRT.IsCreated()) ? PreviewRT.height : Screen.height;

            var (left, top, right, bottom) = CaptureUtilities.GetNormalizedCropValues();
            float cropW = Mathf.Max(1f, rtW * (1f - left - right));
            float cropH = Mathf.Max(1f, rtH * (1f - top - bottom));
            float aspect = cropW / Mathf.Max(1f, cropH);

            float fitW = boxW;
            float fitH = fitW / Mathf.Max(1e-6f, aspect);
            if (fitH > boxH)
            {   // Height-limited; recompute width from height
                fitH = boxH;
                fitW = fitH * aspect;
            }

            float s = Mathf.Clamp01(scaleFactor);
            fitW *= s;
            fitH *= s;

            // Size image to fit within box bounds and center it
            img.anchorMin = new Vector2(0.5f, 0.5f);
            img.anchorMax = new Vector2(0.5f, 0.5f);
            img.pivot = new Vector2(0.5f, 0.5f);
            img.sizeDelta = new Vector2(fitW, fitH);
            img.anchoredPosition = Vector2.zero;
            img.localScale = Vector3.one;

            // Apply UV cropping to show only the cropped portion
            var uvLeft = left;
            var uvBottom = bottom;
            var uvWidth = 1f - left - right;
            var uvHeight = 1f - top - bottom;
            
            try { PreviewImage.uvRect = new Rect(uvLeft, uvBottom, uvWidth, uvHeight); } catch { }

            // Ensure image is properly integrated in UI hierarchy
            PreviewImage.raycastTarget = false;
            PreviewImage.material = null; // Prevent custom materials that might cause overlay issues
            PreviewImage.maskable = true; // Enable masking to respect RectMask2D bounds
            
            // Force immediate layout calculation to ensure proper positioning
            if (parent != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        }

        private void SwitchToPooledRT(int qualityIndex, int targetW, int targetH, FilterMode filter, int antiAliasing)
        {   // Instant RT switching with stable camera alignment to prevent view shifting
            // CPU prediction: check if current RT is already correct (most common case)
            if (activeRTIndex == qualityIndex && PreviewRT != null && 
                PreviewRT.width == targetW && PreviewRT.height == targetH) return;

            try
            {
                // Validate pool RT or create if needed
                if (rtPool[qualityIndex] == null || rtPool[qualityIndex].width != targetW || 
                    rtPool[qualityIndex].height != targetH || rtPool[qualityIndex].filterMode != filter)
                {   // Create new RT with optimized settings
                    rtPool[qualityIndex]?.Release();
                    rtPool[qualityIndex] = new RenderTexture(targetW, targetH, 24, RenderTextureFormat.ARGB32)
                    {   // Inline property setting for CPU cache efficiency
                        antiAliasing = antiAliasing,
                        filterMode = filter,
                        useMipMap = false,
                        autoGenerateMips = false
                    };
                    rtPool[qualityIndex].Create();
                }

                // Use existing utility function to setup preview camera with proper alignment
                World.PreviewCamera = SetupPreviewCamera(World.MainCamera, rtPool[qualityIndex], World.PreviewCamera);

                // Atomic switch without frame gap
                PreviewRT = rtPool[qualityIndex];
                PreviewImage.texture = PreviewRT;
                activeRTIndex = qualityIndex;
            }
            catch (System.Exception ex) { Debug.LogWarning($"RT pool switch failed: {ex.Message}"); }
        }

        private void ApplyCameraQualityBatch(bool isHighQuality)
        {   // Batch camera quality updates to single operation for CPU efficiency
            try
            {
                // Branch prediction: high quality is more common during preview
                if (isHighQuality)
                {   // High quality path - expected by CPU predictor
                    World.PreviewCamera.renderingPath = RenderingPath.DeferredShading;
                    World.PreviewCamera.allowHDR = true;
                    World.PreviewCamera.allowMSAA = true;
                }
                else
                {   // Low quality path - less common, but still optimized
                    World.PreviewCamera.renderingPath = RenderingPath.Forward;
                    World.PreviewCamera.allowHDR = false;
                    World.PreviewCamera.allowMSAA = false;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning($"Camera quality batch update failed: {ex.Message}"); }
        }

        public void SchedulePreviewUpdate(bool immediate = false)
        {   // Schedule an adaptive preview quality update; immediate bypasses debounce
            qualityApplyScheduled = true;
            lastQualityRequestTime = immediate ? float.NegativeInfinity : Time.unscaledTime;

            // Ensure preview image fits properly after any scheduling
            try { FitPreviewImageToBox(1.0f); } catch { }
        }
    }
}