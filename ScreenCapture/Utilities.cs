using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SFS.IO;
using SFS.UI.ModGUI;
using SFS.World;
using UnityEngine;
using SystemType = System.Type;
using static ScreenCapture.Main;

namespace ScreenCapture
{
    // Merged file utilities class combining functionality from FileHelper and FileUtilities
    public static class FileUtilities
    {   // Centralized file operations to avoid duplication across classes
        public static FolderPath savingFolder = (FolderPath)typeof(FileLocations).GetProperty("SavingFolder", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

        public static FolderPath InsertIo(string folderName, FolderPath baseFolder) => 
            InsertIntoSfS(folderName, baseFolder);  // Create subdirectory

        public static FolderPath InsertIo(string fileName, Stream inputStream, FolderPath folder) => 
            InsertIntoSfS(fileName, folder, null, inputStream);  // Save file from stream

        public static FolderPath InsertIo(string fileName, byte[] fileBytes, FolderPath folder) => 
            InsertIntoSfS(fileName, folder, fileBytes, null);  // Save file from bytes

        public static string GetWorldName() => 
            (SFS.Base.worldBase?.paths?.worldName) ?? "Unknown";  // Current world name or fallback

        public static FolderPath CreateWorldFolder(string worldName)
        {   // Create screenshot subfolder for current world
            string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                                  new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            
            return InsertIo(sanitizedName, Main.ScreenCaptureFolder);
        }

        public static FolderPath InsertIntoSfS(string relativePath, FolderPath baseFolder, byte[] fileBytes = null, Stream inputStream = null)
        {   // Core file/folder creation implementation
            if (inputStream != null && !inputStream.CanRead)
                throw new ArgumentException("inputStream must be readable.", nameof(inputStream));

            var baseFull = baseFolder.ToString();

            if (!Directory.Exists(baseFull))
                Directory.CreateDirectory(baseFull);

            var combinedFull = Path.Combine(baseFull, relativePath);
            var isFile = (fileBytes != null) || (inputStream != null);

            if (!isFile)
            {   // Create directory
                if (!Directory.Exists(combinedFull))
                    Directory.CreateDirectory(combinedFull);

                return new FolderPath(combinedFull);
            }

            // Create file
            var destinationDir = Path.GetDirectoryName(combinedFull) ?? baseFull;
            if (!Directory.Exists(destinationDir))
                Directory.CreateDirectory(destinationDir);

            using (var output = new FileStream(combinedFull, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (fileBytes != null)
                    output.Write(fileBytes, 0, fileBytes.Length);
                else
                {
                    if (inputStream.CanSeek)
                        inputStream.Position = 0;
                    inputStream.CopyTo(output);
                }

                output.Flush(true);
            }

            return new FolderPath(destinationDir);
        }
    }

    // Memory-related constants and safety parameters
    public static class MemoryConstants
    {
        public const float SafetyMultiplier = 1.15f; // overhead margin
        public const int GPU_COLOR_BPP = 4; // ARGB32 color buffer
        public const int GPU_DEPTH_BPP = 4; // 24-bit depth typically occupies 32-bit surface
        public const int CPU_BPP = 4; // Texture2D RGBA32 in RAM
        public const double GPU_BUDGET_FRACTION = 0.4; // conservative fraction of reported memory
        public const double CPU_BUDGET_FRACTION = 0.4;
    }
    
    public static class CaptureUtilities
    {

        public static (int width, int height) GetResolutionFromWidth(int width)
        {   // Calculate height from width using current screen aspect
            width = Mathf.Max(16, width);
            int height = Mathf.RoundToInt((float)width / Mathf.Max(1, (float)Screen.width) * (float)Screen.height);
            return (width, Mathf.Max(16, height));
        }

        public static (long gpuBytes, long cpuBytes, long rawBytes) EstimateMemoryForWidth(int width)
        {   // Estimate memory footprints for render/copy paths
            var (w, h) = GetResolutionFromWidth(width);
            long pixels = (long)w * h;
            long gpu = (long)Math.Ceiling(pixels * (MemoryConstants.GPU_COLOR_BPP + MemoryConstants.GPU_DEPTH_BPP) * MemoryConstants.SafetyMultiplier);
            long cpu = (long)Math.Ceiling(pixels * MemoryConstants.CPU_BPP * MemoryConstants.SafetyMultiplier);
            long raw = pixels * MemoryConstants.CPU_BPP;
            return (gpu, cpu, raw);
        }

        public static (long gpuBudget, long cpuBudget) GetMemoryBudgets()
        {   // Compute conservative memory budgets based on device capabilities
            long gpu = (long)(SystemInfo.graphicsMemorySize * 1024L * 1024L * MemoryConstants.GPU_BUDGET_FRACTION);
            long cpu = (long)(SystemInfo.systemMemorySize * 1024L * 1024L * MemoryConstants.CPU_BUDGET_FRACTION);
            return (gpu, cpu);
        }

        public static int ComputeMaxSafeWidth()
        {   // Determine maximum safe width constrained by VRAM/RAM and texture limits
            float aspect = (float)Screen.height / Mathf.Max(1, Screen.width);
            var (gpuBudget, cpuBudget) = GetMemoryBudgets();

            double perPixelGPU = (MemoryConstants.GPU_COLOR_BPP + MemoryConstants.GPU_DEPTH_BPP) * MemoryConstants.SafetyMultiplier;
            double perPixelCPU = MemoryConstants.CPU_BPP * MemoryConstants.SafetyMultiplier;

            double coef = aspect; // pixels = w^2 * aspect
            double maxWgpu = Math.Sqrt(gpuBudget / Math.Max(1e-6, (perPixelGPU * coef)));
            double maxWcpu = Math.Sqrt(cpuBudget / Math.Max(1e-6, (perPixelCPU * coef)));

            int texLimit = SystemInfo.maxTextureSize > 0 ? SystemInfo.maxTextureSize : int.MaxValue;
            int maxW = Mathf.FloorToInt((float)Math.Min(maxWgpu, maxWcpu));
            maxW = Mathf.Clamp(maxW, 16, texLimit);
            return maxW;
        }

        public static string FormatMB(long bytes) =>
            $"{bytes / (1024.0 * 1024.0):0.#} MB";  // Format bytes as megabytes

        public static long EstimatePngSizeBytes(long rawBytes) =>
            (long)Math.Max(1024, rawBytes * 0.30);  // Approximate PNG size assuming moderate compression

        // Create a preview render texture based on a given width and current screen aspect. Returns the new RT.
        public static RenderTexture CreatePreviewRenderTexture(int previewWidth)
        {
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            int rtHeight = Mathf.RoundToInt(previewWidth / Mathf.Max(1e-6f, screenAspect));

            var rt = new RenderTexture(previewWidth, rtHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear
            };

            if (!rt.IsCreated()) rt.Create();
            return rt;
        }

        // Ensure a preview camera exists and is configured based on the main camera. Returns the preview camera.
        public static Camera EnsurePreviewCamera(Camera mainCamera, RenderTexture targetRT, Camera existingPreviewCamera)
        {
            Camera preview = existingPreviewCamera;
            if (preview == null)
            {
                var go = new GameObject("ScreenCapture_PreviewCamera");
                go.hideFlags = HideFlags.DontSave;
                preview = go.AddComponent<Camera>();
                preview.enabled = false;
            }

            if (mainCamera != null)
                preview.CopyFrom(mainCamera);

            preview.enabled = false;
            preview.targetTexture = targetRT;
            preview.cullingMask = mainCamera != null ? mainCamera.cullingMask : ~0;

            // Apply background color from BackgroundUI settings
            preview.clearFlags = CameraClearFlags.SolidColor;
            preview.backgroundColor = GetBackgroundColor();

            if (mainCamera != null)
            {
                preview.transform.position = mainCamera.transform.position;
                preview.transform.rotation = mainCamera.transform.rotation;
                preview.transform.localScale = mainCamera.transform.localScale;
            }

            return preview;
        }

        public static int ComputeCullingMask(bool showBackground)
        {   // Calculate the appropriate culling mask based on current settings
            int mask = World.MainCamera != null ? World.MainCamera.cullingMask : ~0;

            if (!showBackground)
            {
                int stars = LayerMask.GetMask("Stars");
                if (stars != 0)
                    mask &= ~stars;
            }

            return mask;
        }

        public static Color GetBackgroundColor()
        {   // Build background color from BackgroundUI's static values
            return new Color(
                BackgroundUI.R / 255f,
                BackgroundUI.G / 255f,
                BackgroundUI.B / 255f,
                BackgroundUI.Transparent ? 0f : 1f
            );
        }

        public static void ApplyCullingForRender(Camera cam, int mask)
        {   // Apply a provided culling mask to the camera
            if (cam == null)
                return;
            cam.cullingMask = mask;
        }

        public static float SnapZoomLevel(float level)
        {   // Snap to exact 1x (level 0) when multiplier is close to 1
            float factor = Mathf.Exp(level);
            return Mathf.Abs(factor - 1f) <= 0.02f ? 0f : level;
        }

        public static void ApplyPreviewZoom(Camera mainCamera, Camera previewCamera, float zoom)
        {   // Simplified zoom function without dolly adjustments
            if (previewCamera == null)
                return;

            float z = Mathf.Max(Mathf.Exp(zoom), 1e-6f);

            if (previewCamera.orthographic)
            {   // Scale orthographic size inversely with zoom
                float baseSize = mainCamera != null ? Mathf.Max(mainCamera.orthographicSize, 1e-6f) : Mathf.Max(previewCamera.orthographicSize, 1e-6f);
                previewCamera.orthographicSize = Mathf.Clamp(baseSize / z, 1e-6f, 1_000_000f);
            }
            else
            {   // Adjust field of view within valid range
                float baseFov = mainCamera != null ? Mathf.Clamp(mainCamera.fieldOfView, 5f, 120f) : Mathf.Clamp(previewCamera.fieldOfView, 5f, 120f);
                float targetFov = Mathf.Clamp(baseFov / z, 5f, 120f);
                previewCamera.fieldOfView = targetFov;
                if (mainCamera != null)
                    previewCamera.transform.position = mainCamera.transform.position;
            }
        }

        public static System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> ApplySceneVisibilityTemporary(bool showBackground, bool showTerrain, System.Collections.Generic.HashSet<Rocket> hiddenRockets)
        {   // Temporarily modify scene visibility based on provided settings
            var changed = new System.Collections.Generic.List<(Renderer, bool)>();
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(includeInactive: true);

            foreach (var r in renderers)
            {
                if (r == null || r.gameObject == null)
                    continue;

                if (r.GetComponentInParent<RectTransform>() != null)
                    continue;

                var go = r.gameObject;
                string layerName = LayerMask.LayerToName(go.layer) ?? string.Empty;

                bool isTerrain = go.GetComponentInParent<SFS.World.StaticWorldObject>() != null ||
                                 go.GetComponentInParent<SFS.World.Terrain.DynamicTerrain>() != null;
                bool isAtmosphere = IsAtmosphereObject(go);
                bool isStars = string.Equals(layerName, "Stars", StringComparison.OrdinalIgnoreCase) || go.name.IndexOf("star", StringComparison.OrdinalIgnoreCase) >= 0;

                var parentRocket = go.GetComponentInParent<Rocket>();
                bool isRocketHidden = parentRocket != null && hiddenRockets != null && hiddenRockets.Contains(parentRocket);

                bool shouldDisable = false;

                if (!showBackground && (isStars || isAtmosphere)) shouldDisable = true;
                if (!showTerrain && isTerrain) shouldDisable = true;
                if (isRocketHidden) shouldDisable = true;

                if (shouldDisable && r.enabled)
                {
                    changed.Add((r, r.enabled));
                    r.enabled = false;
                }
            }

            return changed;
        }

        public static void RestoreSceneVisibility(System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> changed)
        {   // Restore previously modified scene visibility
            if (changed == null)
                return;

            foreach (var entry in changed)
            {
                try
                {
                    if (entry.renderer != null)
                        entry.renderer.enabled = entry.previousEnabled;
                }
                catch { }
            }
        }

        public static bool IsAtmosphereObject(GameObject go)
        {   // Determine if a GameObject represents atmosphere
            if (go == null)
                return false;

            var name = go.name ?? string.Empty;
            if (name.IndexOf("atmosphere", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return go.GetComponent<SFS.World.Atmosphere>() != null;
        }

        public static void SetupPreview(Container imageContainer)
        {   // Set up the preview image and render texture using World.OwnerInstance
            var previewGO = new GameObject("PreviewImage");
            previewGO.transform.SetParent(imageContainer.rectTransform, false);

            var rect = previewGO.AddComponent<RectTransform>();

            var (finalWidth, finalHeight) = CalculatePreviewDimensions();
            rect.sizeDelta = new Vector2(finalWidth, finalHeight);

            var layout = imageContainer.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ??
                         imageContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.preferredWidth = finalWidth;
            layout.preferredHeight = finalHeight + 8f;

            Captue.PreviewImage = previewGO.AddComponent<UnityEngine.UI.RawImage>();
            Captue.PreviewImage.color = Color.white;
            Captue.PreviewImage.maskable = false;
            Captue.PreviewImage.uvRect = new Rect(0, 0, 1, 1);

            LastScreenWidth = Screen.width;
            LastScreenHeight = Screen.height;

            // Recreate RT
            if (Captue.PreviewRT != null)
            {
                Captue.PreviewRT.Release();
                UnityEngine.Object.Destroy(Captue.PreviewRT);
                Captue.PreviewRT = null;
            }
            Captue.PreviewRT = CreatePreviewRenderTexture(PreviewWidth);
            if (Captue.PreviewRT != null && !Captue.PreviewRT.IsCreated())
                Captue.PreviewRT.Create();

            Captue.PreviewImage.texture = Captue.PreviewRT;

            // Ensure preview camera
            World.PreviewCamera = EnsurePreviewCamera(World.MainCamera, Captue.PreviewRT, World.PreviewCamera);
            World.PreviewCamera.cullingMask = ComputeCullingMask(World.OwnerInstance?.showBackground ?? true);
            World.PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            World.PreviewCamera.backgroundColor = GetBackgroundColor();
        }

        public static (int width, int height) CalculatePreviewDimensions()
        {   // Calculate the appropriate preview dimensions
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float containerWidth = 520f;
            float containerHeight = 430f;
            float containerAspect = containerWidth / containerHeight;

            int finalWidth, finalHeight;

            if (screenAspect > containerAspect)
            {
                finalWidth = Mathf.RoundToInt(containerWidth);
                finalHeight = Mathf.RoundToInt(containerWidth / Mathf.Max(1e-6f, screenAspect));
            }
            else
            {
                finalHeight = Mathf.RoundToInt(containerHeight);
                finalWidth = Mathf.RoundToInt(containerHeight * screenAspect);
            }

            finalWidth = Mathf.Min(finalWidth, Mathf.RoundToInt(containerWidth));
            finalHeight = Mathf.Min(finalHeight, Mathf.RoundToInt(containerHeight));

            return (finalWidth, finalHeight);
        }

        public static void CleanupUI(Captue owner)
        {   // Centralized cleanup of UI resources
            if (owner == null)
                return;

            if (Captue.PreviewRT != null)
            {
                Captue.PreviewRT.Release();
                UnityEngine.Object.Destroy(Captue.PreviewRT);
                Captue.PreviewRT = null;
            }

            if (World.PreviewCamera != null)
            {
                UnityEngine.Object.Destroy(World.PreviewCamera.gameObject);
                World.PreviewCamera = null;
            }

            Captue.PreviewImage = null;

            // Reset state to defaults
            owner.showBackground = true;
            owner.showTerrain = true;
            HiddenRockets.Clear();
        }

        public static void ShowHideWindow<T>(ref T windowInstance, System.Action showAction, System.Action hideAction) where T : UIBase, new()
        {   // Generic method to show/hide a window instance
            if (windowInstance == null)
                windowInstance = new T();

            if (!windowInstance.IsOpen)
                windowInstance.Show();
            else
                windowInstance.Hide();
        }

        public static void OnResolutionInputChange(Captue owner, string newValue)
        {   // Update target width, estimate memory/file sizes, and clamp to a safe maximum if needed (no owner fields used)
            if (!int.TryParse(newValue, out int num))
                return;

            int resolutionWidth = Mathf.Max(16, num);

            var (gpuNeed, cpuNeed, rawBytes) = EstimateMemoryForWidth(resolutionWidth);
            var (gpuBudget, cpuBudget) = GetMemoryBudgets();

            if (gpuNeed > gpuBudget || cpuNeed > cpuBudget)
            {   // Requested resolution is too large; trim and inform
                int maxSafe = ComputeMaxSafeWidth();
                var (ow, oh) = GetResolutionFromWidth(resolutionWidth);
                resolutionWidth = Mathf.Min(resolutionWidth, maxSafe);
                var (nw, nh) = GetResolutionFromWidth(resolutionWidth);

                Debug.LogWarning($"Requested {ow}x{oh} exceeds safe memory budgets. Max safe width on this device is ~{maxSafe} ({nw}x{nh}). GPU budget {FormatMB(gpuBudget)}, CPU budget {FormatMB(cpuBudget)}; needed GPU {FormatMB(gpuNeed)}, CPU {FormatMB(cpuNeed)}.");
            }
            else
            {   // Provide a quick estimate for awareness
                long approxPng = EstimatePngSizeBytes(rawBytes);
                var (w, h) = GetResolutionFromWidth(resolutionWidth);
                Debug.Log($"Estimated sizes for {w}x{h}: Raw {FormatMB(rawBytes)}, GPU {FormatMB(gpuNeed)} (incl. depth), approx PNG {FormatMB(approxPng)}.");
            }
        }

        public static bool IsRocketVisible(Rocket rocket) =>
            rocket != null && !HiddenRockets.Contains(rocket);

        public static void SetRocketVisible(Rocket rocket, bool visible)
        {   // Toggle a rocket's visibility in preview/screenshot
            if (rocket == null)
                return;
            if (visible) HiddenRockets.Remove(rocket); else HiddenRockets.Add(rocket);
        }

        public static void SetAllRocketsVisible(bool visible)
        {   // Set all rockets visible or hidden at once
            HiddenRockets.Clear();
            if (!visible)
            {
                var rockets = UnityEngine.Object.FindObjectsOfType<Rocket>(includeInactive: true);
                foreach (var r in rockets) HiddenRockets.Add(r);
            }
        }

        public static void UpdatePreviewCulling()
        {   // Update the preview camera's culling and background settings
            if (World.PreviewCamera == null || World.OwnerInstance == null)
                return;
            World.PreviewCamera.cullingMask = ComputeCullingMask(World.OwnerInstance.showBackground);
            World.PreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            World.PreviewCamera.backgroundColor = GetBackgroundColor();
        }

    }

    // Time management and frame-history utilities moved out of UI into a dedicated class
    public static class CaptureTime
    {   // Manage saving/loading world saves and perform small time steps via TimeStepHelper
        private static readonly int MaxFrameHistory = 30;
        private static readonly System.Collections.Generic.List<object> frameHistory = new System.Collections.Generic.List<object>(MaxFrameHistory);
        private static int currentFrameIndex = -1;

        public static void SaveCurrentFrame()
        {   // Create a world save and push it onto the history stack
            try
            {
                var gameManagerType = System.Type.GetType("SFS.World.GameManager, Assembly-CSharp");
                if (gameManagerType == null)
                {
                    Debug.LogWarning("GameManager type not found");
                    return;
                }

                var gameManager = UnityEngine.Object.FindObjectOfType(gameManagerType);
                if (gameManager == null)
                {
                    Debug.LogWarning("GameManager instance not found");
                    return;
                }

                var createSaveMethod = gameManagerType.GetMethod("CreateWorldSave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (createSaveMethod == null)
                {
                    Debug.LogWarning("CreateWorldSave method not found");
                    return;
                }

                object worldSave = createSaveMethod.Invoke(gameManager, null);
                if (worldSave == null)
                {
                    Debug.LogWarning("Failed to create world save");
                    return;
                }

                if (currentFrameIndex >= 0 && currentFrameIndex < frameHistory.Count - 1)
                    frameHistory.RemoveRange(currentFrameIndex + 1, frameHistory.Count - currentFrameIndex - 1);

                frameHistory.Add(worldSave);
                if (frameHistory.Count > MaxFrameHistory)
                    frameHistory.RemoveAt(0);

                currentFrameIndex = frameHistory.Count - 1;
                Debug.Log($"Saved frame {currentFrameIndex + 1}/{frameHistory.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error saving frame: {ex.Message}");
            }
        }

        public static void LoadFrame(int index)
        {   // Load a world save from history into the current game
            try
            {
                if (index < 0 || index >= frameHistory.Count)
                {
                    Debug.LogError($"Invalid frame index: {index}");
                    return;
                }

                object worldSave = frameHistory[index];
                if (worldSave == null)
                {
                    Debug.LogError("World save is null");
                    return;
                }

                var gameManagerType = System.Type.GetType("SFS.World.GameManager, Assembly-CSharp");
                var msgDrawerType = System.Type.GetType("SFS.UI.MsgDrawer, Assembly-CSharp");

                if (gameManagerType == null || msgDrawerType == null)
                {
                    Debug.LogError("Required types not found");
                    return;
                }

                var gameManager = UnityEngine.Object.FindObjectOfType(gameManagerType);
                var msgDrawer = UnityEngine.Object.FindObjectOfType(msgDrawerType);

                if (gameManager == null || msgDrawer == null)
                {
                    Debug.LogError("Required instances not found");
                    return;
                }

                var loadSaveMethod = gameManagerType.GetMethod("LoadSave", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (loadSaveMethod == null)
                {
                    Debug.LogError("LoadSave method not found");
                    return;
                }

                loadSaveMethod.Invoke(gameManager, new[] { worldSave, false, msgDrawer });
                currentFrameIndex = index;
                Time.timeScale = 0f;

                Debug.Log($"Loaded frame {index + 1}/{frameHistory.Count}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading frame: {ex.Message}");
                Time.timeScale = 0f;
            }
        }

        public static void StepForwardAndPause()
        {   // Advance a short real-time step and save before/after states
            try
            {
                Debug.Log("Starting time step with coroutine approach");

                SaveCurrentFrame();

                GameObject helperGo = new GameObject("TimeStepHelper");
                var helper = helperGo.AddComponent<TimeStepHelper>();

                helper.OnStepComplete = () =>
                {
                    UnityEngine.Object.Destroy(helperGo);
                    SaveCurrentFrame();
                    Debug.Log("Time step complete via coroutine");

                    try
                    {
                        var worldTimeType = System.Type.GetType("SFS.World.WorldTime, Assembly-CSharp");
                        if (worldTimeType != null)
                        {
                            var mainField = worldTimeType.GetField("main");
                            var worldTime = mainField?.GetValue(null);
                            var worldTimeField = worldTimeType?.GetField("worldTime");

                            if (worldTimeField != null && worldTime != null)
                            {
                                double currentTime = (double)worldTimeField.GetValue(worldTime);
                                Debug.Log($"Final time: {currentTime:F2}s");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Error reading time: {ex.Message}");
                    }
                };

                helper.ConfigureStep(0.1f, 1, frameHistory);
                helper.BeginTimeStep();

                Debug.Log("Time step coroutine started");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Step forward error: {ex.Message}");
                Time.timeScale = 0f;
            }
        }

        public static void StepBackwardInTime()
        {   // Load previous saved frame and perform a tiny settle step
            try
            {
                Time.timeScale = 0f;

                if (frameHistory.Count == 0)
                {
                    Debug.Log("No frame history available");
                    return;
                }

                int nextFrameIndex = -1;

                if (currentFrameIndex > 0)
                    nextFrameIndex = currentFrameIndex - 1;
                else if (currentFrameIndex == 0)
                {
                    Debug.Log("Already at oldest saved frame");
                    return;
                }
                else
                    nextFrameIndex = frameHistory.Count - 1;

                if (nextFrameIndex < 0 || nextFrameIndex >= frameHistory.Count)
                {
                    Debug.LogError("Invalid frame index calculated");
                    return;
                }

                LoadFrame(nextFrameIndex);

                GameObject settleHelper = new GameObject("SettleHelper");
                var settle = settleHelper.AddComponent<TimeStepHelper>();

                settle.OnStepComplete = () =>
                {
                    UnityEngine.Object.Destroy(settleHelper);
                    Time.timeScale = 0f;

                    var worldTimeType = System.Type.GetType("SFS.World.WorldTime, Assembly-CSharp");
                    if (worldTimeType != null)
                    {
                        var mainField = worldTimeType.GetField("main");
                        var worldTime = mainField?.GetValue(null);
                        var setStateMethod = worldTimeType?.GetMethod("SetState");

                        if (setStateMethod != null && worldTime != null)
                            setStateMethod.Invoke(worldTime, new object[] { 0.0, true, false });

                        var worldTimeField = worldTimeType?.GetField("worldTime");
                        if (worldTimeField != null && worldTime != null)
                        {
                            double currentTime = (double)worldTimeField.GetValue(worldTime);
                            Debug.Log($"Current world time after settle: {currentTime:F2}s");
                        }
                    }

                    Debug.Log("Time step settle complete");
                };

                settle.ConfigureStep(0.01f, 0, frameHistory);
                settle.BeginTimeStep();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Step back error: {ex.Message}");
                Time.timeScale = 0f;
            }
        }

        public static double ExtractTimeFromSave(object worldSave)
        {   // Try to extract a numeric time from a world save object
            try
            {
                var t = worldSave.GetType();

                var timeField = t.GetField("time") ?? t.GetField("worldTime") ?? t.GetField("Time");
                if (timeField != null)
                    return (double)Convert.ChangeType(timeField.GetValue(worldSave), typeof(double));

                var gameStateField = t.GetField("gameState") ?? t.GetField("state") ?? t.GetField("worldState");
                if (gameStateField != null)
                {
                    var gameState = gameStateField.GetValue(worldSave);
                    if (gameState != null)
                    {
                        var gt = gameState.GetType();
                        var gameTimeField = gt.GetField("time") ?? gt.GetField("worldTime") ?? gt.GetField("Time");
                        if (gameTimeField != null)
                            return (double)Convert.ChangeType(gameTimeField.GetValue(gameState), typeof(double));
                    }
                }

                return currentFrameIndex * 0.1;
            }
            catch
            {
                return currentFrameIndex * 0.1;
            }
        }
    }

    // Time stepping helper to advance real-time for a small number of steps
    public class TimeStepHelper : UnityEngine.MonoBehaviour
    {   // Coroutine-driven helper to run short real-time steps and notify on completion
        public Action OnStepComplete;

        private float stepSeconds = 0.1f;
        private int stepsToTake = 1;
        private bool running = false;
        private System.Collections.Generic.List<object> frameHistoryRef;

        public void ConfigureStep(float stepLength, int steps, System.Collections.Generic.List<object> frameHistory)
        {   // Configure duration and number of steps for the helper
            stepSeconds = Math.Max(0.0001f, stepLength);
            stepsToTake = Math.Max(0, steps);
            frameHistoryRef = frameHistory;
        }

        public void BeginTimeStep()
        {   // Start the coroutine that runs the configured real-time steps
            if (running)
                return;

            running = true;
            StartCoroutine(RunStepsCoroutine());
        }

        private System.Collections.IEnumerator RunStepsCoroutine()
        {   // Run the requested number of real-time steps, pausing the game's time between steps
            int actualSteps = Math.Max(1, stepsToTake);

            for (int i = 0; i < actualSteps; i++)
            {   // Allow the game to run for a short real-time duration
                Time.timeScale = 1f;
                yield return new UnityEngine.WaitForSecondsRealtime(stepSeconds);
                Time.timeScale = 0f;
            }

            running = false;

            try
            {
                OnStepComplete?.Invoke();
            }
            catch (Exception ex)
            {   // Swallow exceptions to avoid coroutine leaks
                UnityEngine.Debug.LogWarning($"TimeStepHelper completion threw: {ex.Message}");
            }
        }
    }
}
