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
    public class Main : Mod, IUpdatable
    {
        public static int PreviewWidth { get; set; } = 256;
        public static FolderPath ScreenCaptureFolder { get; private set; }

        private static Captue s_captureInstance;
        private static int s_resolutionWidth = 1980;

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

        private static float s_cropLeft, s_cropTop, s_cropRight, s_cropBottom;
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
        public override string ModVersion => "1.6.10"; // release, updates, fixes/changes
        public override string Description => "Adds a screenshot button, allowing you to take screenshots at custom resolutions. With many features for customization that would make any youtuber proud.";

        public Dictionary<string, FilePath> UpdatableFiles
		{
			get
			{
				return new Dictionary<string, FilePath>
				{
					{
						"https://github.com/uneven-coder/SFS-ScreenCapture/releases/latest/download/ScreenCapture.dll",
						new FolderPath(base.ModFolder).ExtendToFile("ScreenCapture.dll")
					}
				};
			}
		}

        public override void Load()
        {
            ScreenCaptureFolder = FileUtilities.InsertIo("ScreenCaptures", FileUtilities.savingFolder);
            SceneHelper.OnSceneLoaded += ManageUI;

            if (s_captureInstance == null)
                CreateCaptureInstance();
        }

        private void CreateCaptureInstance()
        {
            var captureObject = new GameObject("ScreenCapture_Persistent");
            s_captureInstance = captureObject.AddComponent<Captue>();
            UnityEngine.Object.DontDestroyOnLoad(captureObject);
        }

        private void ManageUI(UnityEngine.SceneManagement.Scene scene)
        {
            bool world = string.Equals(scene.name ?? string.Empty, "World_PC", StringComparison.Ordinal);

            // do nothing if capture instance isn't created yet
            if (s_captureInstance == null)
                return;

            if (world)
            {   // Entered world scene
                try
                {
                    s_captureInstance.OnSceneEntered();
                    s_captureInstance.ShowUI();
                }
                catch (Exception ex)
                { UnityEngine.Debug.LogWarning($"ManageUI: error entering scene: {ex.Message}"); }
            }
            else
            {   // Left world scene
                try { s_captureInstance.OnSceneExited(); }
                catch (Exception ex)
                { UnityEngine.Debug.LogWarning($"ManageUI: error exiting scene: {ex.Message}"); }
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
                ClearUiStates();
            }
        }

        private void CleanupMainWindow()
        {
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
            bool poolCleaned = PreviewUtilities.CleanupRTPool();
            if (!poolCleaned)
                Debug.Log("RT pool clean up failed");
        }

        private void CleanupUIHolder()
        {   // Destroy the UI holder to ensure fresh creation on next scene entry
            if (World.UIHolder == null)
                return;

            UnityEngine.Object.Destroy(World.UIHolder);
            World.UIHolder = null;
        }

        private void ClearUiStates()
        {
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

            // Optimized movement detection thresholds
            float VelocityThresholdSq = 0.01f;
            float RotationVelocityThreshold = 2f;
            float PositionDeltaThresholdSq = 0.0001f;
            float RotationDeltaThreshold = 1f;
            float[] UpdateIntervals = { 0.05f, 0.12f }; // Moving: 20fps, Static: 8.3fps

            // Movement cadence gate, schedule preview update when interval elapses
            if (CheckForSignificantChanges(ref lastCameraPosition, ref lastCameraRotation, lastPreviewUpdate,
                VelocityThresholdSq, RotationVelocityThreshold, PositionDeltaThresholdSq, RotationDeltaThreshold,
                UpdateIntervals[0], UpdateIntervals[1], out var activity))
            {   // Update activity and schedule rendering
                currentActivity = activity;
                lastPreviewUpdate = Time.unscaledTime;

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
                    int invalidatedCount = PreviewUtilities.InvalidateRTPool();
                    if (invalidatedCount > 0)
                        Debug.Log($"Invalidated {invalidatedCount} render textures from pool");
                    SchedulePreviewUpdate(immediate: true);

                    // Ensure the preview image fits inside its box at full scale after resolution change
                    PreviewImage = PreviewUtilities.FitPreviewImageToBox(PreviewImage, PreviewRT, 1f);
                }
                catch (System.Exception ex)
                { Debug.LogWarning($"Error in screen size change handler: {ex.Message}"); }
            }
        }

        private void UpdateAdaptivePreviewSystem()
        {   // adaptive preview system
            if (PreviewImage == null || World.PreviewCamera == null) return;

            bool activityChanged = currentActivity != lastAppliedActivity;
            if (!activityChanged && !qualityApplyScheduled) return;  // Fast exit for no-op frames

            float currentTime = Time.unscaledTime;
            bool debounceExpired = float.IsNegativeInfinity(lastQualityRequestTime) ||
                                   (currentTime - lastQualityRequestTime) >= qualityDebounceDelay;

            // Only start debounce on the transition edge; do not reset while already scheduled
            // this caused the state to get stuck if the user kept moving the camera
            if (activityChanged && !qualityApplyScheduled)
            {   // Schedule quality update once per transition
                lastQualityRequestTime = currentTime;
                qualityApplyScheduled = true;
            }

            // Pre-calculated quality states for branch prediction
            (int res, FilterMode filter, int aa)[] QualityPresets =
            {
                (128, FilterMode.Point, 1),     // Moving/Low quality - index 0
                (1024, FilterMode.Bilinear, 4)  // Static/High quality - index 1
            };

            if (qualityApplyScheduled && debounceExpired)
            {   // Compute target preset and dimensions based on screen aspect (crop applied via UV/RawImage)
                int qualityIndex = currentActivity == CameraActivity.Static ? 1 : 0;
                var (targetRes, filter, antiAliasing) = QualityPresets[qualityIndex];

                float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
                int targetW = screenAspect >= 1f ? targetRes : Mathf.Max(64, Mathf.RoundToInt(targetRes * screenAspect));
                int targetH = screenAspect >= 1f ? Mathf.Max(64, Mathf.RoundToInt(targetRes / screenAspect)) : targetRes;

                // Switch or validate RT first to avoid overwriting settings later
                var (renderTexture, wasCreated, poolIndex) = PreviewUtilities.SwitchToPooledRT(qualityIndex, targetW, targetH, filter, antiAliasing);

                // Apply the returned RT to camera and preview components
                if (World.PreviewCamera != null)
                    World.PreviewCamera.targetTexture = renderTexture;

                if (PreviewImage != null)
                    PreviewImage.texture = renderTexture;

                PreviewRT = renderTexture;

                // Apply background color && zoom after RT switch to preserve state
                try { World.PreviewCamera.backgroundColor = BackgroundUI.GetBackgroundColor(); } catch { }
                try { CaptureUtilities.ApplyPreviewZoom(World.MainCamera, World.PreviewCamera, Main.PreviewZoomLevel); } catch { }

                try
                {   // Apply quality options (HDR/MSAA/etc.)
                    // high quality is more common during preview
                    if (qualityIndex == 1)
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

                // Ensure UI layout matches new RT and crop
                PreviewUtilities.UpdatePreviewImageLayoutForCurrentRT();

                PreviewImage = PreviewUtilities.FitPreviewImageToBox(PreviewImage, PreviewRT, 1f);

                // Temporarily toggle scene visibility for background/terrain and hidden rockets
                System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> changedRenderers = null;
                try { changedRenderers = CaptureUtilities.ApplySceneVisibilityTemporary(showBackground, showTerrain, Main.HiddenRockets); }
                catch { }


                try
                {   // Render updated frame
                    if (World.PreviewCamera?.targetTexture != null)
                        World.PreviewCamera.Render();
                }
                finally
                { try { CaptureUtilities.RestoreSceneVisibility(changedRenderers); } catch { } }

                lastAppliedActivity = currentActivity;
                qualityApplyScheduled = false;
            }
        }

        public void SchedulePreviewUpdate(bool immediate = false)
        {   // Schedule an adaptive preview quality update; immediate bypasses debounce
            qualityApplyScheduled = true;
            lastQualityRequestTime = immediate ? float.NegativeInfinity : Time.unscaledTime;

            // Ensure preview image fits properly after any scheduling
            try { PreviewImage = PreviewUtilities.FitPreviewImageToBox(PreviewImage, PreviewRT, 1f); ; } catch { }
        }
    }
}