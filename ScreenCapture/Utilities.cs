using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SFS.IO;
using SFS.UI.ModGUI;
using SFS.World;
using UnityEngine;
using UnityEngine.UI;
using SystemType = System.Type;
using static ScreenCapture.Main;

namespace ScreenCapture
{
    public static class CacheManager
    {
        private static (int width, long gpu, long cpu, long raw) memoryCache = (-1, 0, 0, 0);
        private static (int origW, int origH, int cropW, int cropH) dimensionsCache = (-1, -1, -1, -1);
        private static (float left, float top, float right, float bottom) lastCropValues = (-1, -1, -1, -1);
        private static (long gpuBudget, long cpuBudget) budgetCache = (-1, -1);
        private static int maxSafeWidthCache = -1;
        private static int lastScreenWForMaxWidth = -1, lastScreenHForMaxWidth = -1;

        public static void InvalidateMemoryCache() => memoryCache = (-1, 0, 0, 0);
        public static void InvalidateCropCache() => dimensionsCache = (-1, -1, -1, -1);
        public static void InvalidateMaxWidthCache() => maxSafeWidthCache = -1;

        public static void InvalidateScreenCache()
        {   // Invalidate all screen-dependent caches
            InvalidateMemoryCache();
            InvalidateCropCache();
            InvalidateMaxWidthCache();
        }

        public static (long gpu, long cpu, long raw) GetCachedMemoryEstimate(int width)
        {   // Return cached memory estimate or compute and cache new one
            if (memoryCache.width == width)
                return (memoryCache.gpu, memoryCache.cpu, memoryCache.raw);

            var result = CaptureUtilities.EstimateMemoryForWidthUncached(width);
            memoryCache = (width, result.gpuBytes, result.cpuBytes, result.rawBytes);
            return result;
        }

        public static (long gpuBudget, long cpuBudget) GetCachedMemoryBudgets()
        {   // Return cached memory budgets or compute and cache new ones
            if (budgetCache.gpuBudget != -1)
                return budgetCache;

            budgetCache = CaptureUtilities.GetMemoryBudgetsUncached();
            return budgetCache;
        }

        public static int GetCachedMaxSafeWidth()
        {   // Return cached max width or compute and cache new one if screen changed
            if (maxSafeWidthCache != -1 && lastScreenWForMaxWidth == Screen.width && lastScreenHForMaxWidth == Screen.height)
                return maxSafeWidthCache;

            maxSafeWidthCache = CaptureUtilities.ComputeMaxSafeWidthUncached();
            lastScreenWForMaxWidth = Screen.width;
            lastScreenHForMaxWidth = Screen.height;
            return maxSafeWidthCache;
        }

        public static (int cropW, int cropH) GetCachedCroppedDimensions(int origW, int origH)
        {   // Return cached cropped dimensions or compute and cache new ones if crop settings changed
            bool cropChanged = lastCropValues.left != Main.CropLeft || lastCropValues.top != Main.CropTop ||
                              lastCropValues.right != Main.CropRight || lastCropValues.bottom != Main.CropBottom;

            if (dimensionsCache.origW == origW && dimensionsCache.origH == origH && !cropChanged)
                return (dimensionsCache.cropW, dimensionsCache.cropH);

            var result = CaptureUtilities.GetCroppedResolutionUncached(origW, origH);
            dimensionsCache = (origW, origH, result.width, result.height);
            lastCropValues = (Main.CropLeft, Main.CropTop, Main.CropRight, Main.CropBottom);
            return result;
        }
    }

    public static class FileUtilities
    {
        public static FolderPath savingFolder = (FolderPath)typeof(FileLocations).GetProperty("SavingFolder", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

        public static FolderPath InsertIo(string folderName, FolderPath baseFolder) => InsertIntoSfS(folderName, baseFolder);
        public static FolderPath InsertIo(string fileName, Stream inputStream, FolderPath folder) => InsertIntoSfS(fileName, folder, null, inputStream);
        public static FolderPath InsertIo(string fileName, byte[] fileBytes, FolderPath folder) => InsertIntoSfS(fileName, folder, fileBytes, null);
        public static string GetWorldName() => (SFS.Base.worldBase?.paths?.worldName) ?? "Unknown";

        public static FolderPath CreateWorldFolder(string worldName) =>
            InsertIo(SanitizeFileName(worldName), Main.ScreenCaptureFolder);

        public static void OpenCurrentWorldCapturesFolder()
        {   // Open the current world's capture folder in file explorer
            try
            {
                var folder = InsertIo(SanitizeFileName(GetWorldName()), Main.ScreenCaptureFolder);
                string url = "file:///" + folder.ToString().Replace('\\', '/');
                Application.OpenURL(url);
            }
            catch { }
        }

        private static string SanitizeFileName(string worldName) =>
            string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
            new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

        public static FolderPath InsertIntoSfS(string relativePath, FolderPath baseFolder, byte[] fileBytes = null, Stream inputStream = null)
        {   // Create folder or file in SFS directory structure with proper error handling
            if (inputStream != null && !inputStream.CanRead)
                throw new ArgumentException("inputStream must be readable.", nameof(inputStream));

            var baseFull = baseFolder.ToString();
            if (!Directory.Exists(baseFull)) Directory.CreateDirectory(baseFull);

            var combinedFull = Path.Combine(baseFull, relativePath);
            var isFile = fileBytes != null || inputStream != null;

            if (!isFile)
            {   // Create directory
                if (!Directory.Exists(combinedFull)) Directory.CreateDirectory(combinedFull);
                return new FolderPath(combinedFull);
            }

            // Create file
            var destinationDir = Path.GetDirectoryName(combinedFull) ?? baseFull;
            if (!Directory.Exists(destinationDir)) Directory.CreateDirectory(destinationDir);

            using (var output = new FileStream(combinedFull, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            {
                if (fileBytes != null)
                    output.Write(fileBytes, 0, fileBytes.Length);
                else
                {
                    if (inputStream.CanSeek) inputStream.Position = 0;
                    inputStream.CopyTo(output, 65536);
                }
                output.Flush(true);
            }
            return new FolderPath(destinationDir);
        }
        
        public static IEnumerator VerifyAndShowResult(string worldFolderPath, string fileName, byte[] pngBytes, 
                                                     int renderWidth, int outWidth, int outHeight, System.Action<bool> onComplete)
        {   // Wait for file system to flush, verify save, then show result
            yield return new WaitForSecondsRealtime(0.1f);

            string fullPath = Path.Combine(worldFolderPath, fileName);
            bool saveSuccess = false;

            try
            {
                if (File.Exists(fullPath))
                {   // Verify file size matches expected
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Length > 1024 && Math.Abs(fileInfo.Length - pngBytes.Length) < 100)
                    {
                        saveSuccess = true;
                        var (gpuNeed, cpuNeed, rawBytes) = CaptureUtilities.EstimateMemoryForWidth(renderWidth);
                        Debug.Log($"Verified save {outWidth}x{outHeight}. Memory (render): GPU {CaptureUtilities.FormatMB(gpuNeed)}, CPU {CaptureUtilities.FormatMB(cpuNeed)}; file {CaptureUtilities.FormatMB(pngBytes.LongLength)}");
                    }
                    else Debug.LogError($"File size mismatch. Expected: {pngBytes.Length}, Actual: {fileInfo.Length}");
                }
                else Debug.LogError("Screenshot file does not exist after save");
            }
            catch (System.Exception ex) { Debug.LogError($"File verification failed: {ex.Message}"); }

            yield return new WaitForSecondsRealtime(0.1f);
            onComplete?.Invoke(saveSuccess);
        }
    }

    public static class MemoryConstants
    {
        public const float SafetyMultiplier = 1.15f;
        public const int GPU_COLOR_BPP = 4;
        public const int GPU_DEPTH_BPP = 4;
        public const int CPU_BPP = 4;
        public const double GPU_BUDGET_FRACTION = 0.4;
        public const double CPU_BUDGET_FRACTION = 0.4;
    }

    public static class CaptureUtilities
    {
        // Caching and pooling
        private static readonly System.Collections.Generic.Dictionary<string, (int width, int height)> elementSizeCache =
            new System.Collections.Generic.Dictionary<string, (int, int)>(64);
        private static readonly System.Collections.Queue rendererListPool = new System.Collections.Queue();
        private static int cachedInteriorLayerMask = -1;

        // Object pooling for renderer lists
        private static System.Collections.Generic.List<(Renderer, bool)> GetPooledRendererList() =>
            rendererListPool.Count > 0 ? 
                (System.Collections.Generic.List<(Renderer, bool)>)rendererListPool.Dequeue() : 
                new System.Collections.Generic.List<(Renderer, bool)>(64);

        private static void ReturnPooledRendererList(System.Collections.Generic.List<(Renderer, bool)> list)
        {   // Return list to pool with size limit
            if (list != null && rendererListPool.Count < 4)
            {
                list.Clear();
                rendererListPool.Enqueue(list);
            }
        }

        // Interior visibility management
        public static bool PreviewInteriorVisible
        {
            get => SFS.InteriorManager.main?.interiorView.Value ?? true;
            set { if (SFS.InteriorManager.main != null) SFS.InteriorManager.main.interiorView.Value = value; }
        }

        private static bool IsInteriorElement(GameObject go) =>
            go != null && (go.name.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          go.GetComponentsInParent<Component>(true).Any(c => 
                              c?.GetType()?.Name?.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0));

        private static int GetCachedInteriorLayerMask()
        {   // Detect and cache interior layer bitmask
            if (cachedInteriorLayerMask != -1) return cachedInteriorLayerMask;

            cachedInteriorLayerMask = 0;
            var interiorLayerNames = new[] { "Interior", "Interiors", "interior", "interiors" };
            
            // Check all 32 layers for 'interior' substring
            for (int i = 0; i < 32; i++)
            {
                string lname = LayerMask.LayerToName(i) ?? string.Empty;
                if (lname.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0)
                    cachedInteriorLayerMask |= (1 << i);
            }

            // Check specific interior layer names
            foreach (var layerName in interiorLayerNames)
            {
                int layerIndex = LayerMask.NameToLayer(layerName);
                if (layerIndex >= 0 && layerIndex < 32)
                    cachedInteriorLayerMask |= (1 << layerIndex);
            }

            return cachedInteriorLayerMask;
        }

        // Resolution and sizing utilities
        public static (int width, int height) GetResolutionFromWidth(int width)
        {   // Get cached or compute resolution from width maintaining aspect ratio
            string cacheKey = $"{width}_{Screen.width}_{Screen.height}";
            if (elementSizeCache.TryGetValue(cacheKey, out var cached)) return cached;

            width = Mathf.Max(16, width);
            int height = Mathf.RoundToInt((float)width / Mathf.Max(1, (float)Screen.width) * (float)Screen.height);
            var result = (width, Mathf.Max(16, height));
            elementSizeCache[cacheKey] = result;
            return result;
        }

        public static (int width, int height) CalculatePreviewDimensions()
        {   // Calculate preview dimensions fitting container while preserving screen aspect
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            const float containerWidth = 520f, containerHeight = 430f;
            float containerAspect = containerWidth / containerHeight;

            return screenAspect > containerAspect ?
                (Mathf.RoundToInt(containerWidth), Mathf.RoundToInt(containerWidth / Mathf.Max(1e-6f, screenAspect))) :
                (Mathf.RoundToInt(containerHeight * screenAspect), Mathf.RoundToInt(containerHeight));
        }

        // Memory estimation functions
        public static (long gpuBytes, long cpuBytes, long rawBytes) EstimateMemoryForWidth(int width) =>
            CacheManager.GetCachedMemoryEstimate(width);

        public static (long gpuBytes, long cpuBytes, long rawBytes) EstimateMemoryForWidthUncached(int width)
        {   // Calculate memory requirements for given width
            var (w, h) = GetResolutionFromWidth(width);
            long pixels = (long)w * h;
            long gpu = (long)Math.Ceiling(pixels * (MemoryConstants.GPU_COLOR_BPP + MemoryConstants.GPU_DEPTH_BPP) * MemoryConstants.SafetyMultiplier);
            long cpu = (long)Math.Ceiling(pixels * MemoryConstants.CPU_BPP * MemoryConstants.SafetyMultiplier);
            return (gpu, cpu, pixels * MemoryConstants.CPU_BPP);
        }

        public static (long gpuBudget, long cpuBudget) GetMemoryBudgets() => CacheManager.GetCachedMemoryBudgets();

        public static (long gpuBudget, long cpuBudget) GetMemoryBudgetsUncached() =>
            ((long)(SystemInfo.graphicsMemorySize * 1024L * 1024L * MemoryConstants.GPU_BUDGET_FRACTION),
             (long)(SystemInfo.systemMemorySize * 1024L * 1024L * MemoryConstants.CPU_BUDGET_FRACTION));

        public static int ComputeMaxSafeWidth() => CacheManager.GetCachedMaxSafeWidth();

        public static int ComputeMaxSafeWidthUncached()
        {   // Calculate maximum safe width based on memory constraints
            float aspect = (float)Screen.height / Mathf.Max(1, Screen.width);
            var (gpuBudget, cpuBudget) = GetMemoryBudgetsUncached();

            double perPixelGPU = (MemoryConstants.GPU_COLOR_BPP + MemoryConstants.GPU_DEPTH_BPP) * MemoryConstants.SafetyMultiplier;
            double perPixelCPU = MemoryConstants.CPU_BPP * MemoryConstants.SafetyMultiplier;
            
            // Calculate max render dimensions based on memory alone
            double maxRenderWgpu = Math.Sqrt(gpuBudget / Math.Max(1e-6, perPixelGPU * aspect));
            double maxRenderWcpu = Math.Sqrt(cpuBudget / Math.Max(1e-6, perPixelCPU * aspect));
            
            // Take the smaller of GPU/CPU limits for render resolution
            double maxRenderW = Math.Min(maxRenderWgpu, maxRenderWcpu);

            int texLimit = SystemInfo.maxTextureSize > 0 ? SystemInfo.maxTextureSize : int.MaxValue;
            
            // Apply texture size limit to render dimensions
            double maxRenderWWithTexLimit = Math.Min(maxRenderW, texLimit);
            
            // Return the render width limit (not target width)
            return Mathf.Clamp(Mathf.FloorToInt((float)maxRenderWWithTexLimit), 16, int.MaxValue);
        }

        // Utility functions
        public static string FormatMB(long bytes) => $"{bytes / (1024.0 * 1024.0):0.#} MB";
        public static long EstimatePngSizeBytes(long rawBytes) => (long)Math.Max(1024, rawBytes * 0.30);
        public static int CalculateTargetRTWidth(int previewWidth) => previewWidth;

        public static (int renderWidth, int renderHeight) CalculateRenderDimensions(int targetWidth, int targetHeight)
        {   // Calculate render dimensions that produce the target output size after cropping
            // The render size should be the same as target size - cropping happens via ReadPixels
            // This ensures we render at the requested resolution and crop the specific area
            return (targetWidth, targetHeight);
        }

        public static (int targetWidth, int targetHeight) CalculateTargetDimensions(int renderWidth, int renderHeight)
        {   // Calculate final output dimensions after cropping from render dimensions
            var (left, top, right, bottom) = GetNormalizedCropValues();
            int targetWidth = Mathf.Max(1, Mathf.RoundToInt(renderWidth * (1f - left - right)));
            int targetHeight = Mathf.Max(1, Mathf.RoundToInt(renderHeight * (1f - top - bottom)));
            return (targetWidth, targetHeight);
        }

        // Cropping utilities
        public static (float left, float top, float right, float bottom) GetNormalizedCropValues()
        {   // Get normalized crop values with constraint handling
            float left = Mathf.Clamp01(Main.CropLeft / 100f);
            float top = Mathf.Clamp01(Main.CropTop / 100f);
            float right = Mathf.Clamp01(Main.CropRight / 100f);
            float bottom = Mathf.Clamp01(Main.CropBottom / 100f);

            // Constrain horizontal and vertical totals
            float totalH = left + right;
            float totalV = top + bottom;

            if (totalH >= 1f) { float s = 0.99f / totalH; left *= s; right *= s; }
            if (totalV >= 1f) { float s = 0.99f / totalV; top *= s; bottom *= s; }

            return (left, top, right, bottom);
        }

        public static (int width, int height) GetCroppedResolution(int originalWidth, int originalHeight) =>
            CacheManager.GetCachedCroppedDimensions(originalWidth, originalHeight);

        public static (int width, int height) GetCroppedResolutionUncached(int originalWidth, int originalHeight)
        {   // Calculate cropped resolution
            var (left, top, right, bottom) = GetNormalizedCropValues();
            return (Mathf.Max(1, Mathf.RoundToInt(originalWidth * (1f - left - right))),
                    Mathf.Max(1, Mathf.RoundToInt(originalHeight * (1f - top - bottom))));
        }

        public static Rect GetCroppedReadRect(int renderWidth, int renderHeight)
        {   // Calculate pixel rect for cropped reading with proper coordinate conversion
            var (left, top, right, bottom) = GetNormalizedCropValues();
            
            // Convert normalized crop values to pixel coordinates
            // ReadPixels uses bottom-left origin, UV uses bottom-left origin
            // BUT crop values are defined as distances from edges in screen space (top-left origin)
            int leftPixels = Mathf.RoundToInt(left * renderWidth);
            int rightPixels = Mathf.RoundToInt(right * renderWidth);
            int topPixels = Mathf.RoundToInt(top * renderHeight);
            int bottomPixels = Mathf.RoundToInt(bottom * renderHeight);

            // For ReadPixels: Y=0 is bottom of texture, so top crop becomes bottom offset in ReadPixels space
            int cropX = leftPixels;
            int cropY = topPixels;  // Top crop becomes Y offset from bottom (flipped)
            int cropWidth = Mathf.Max(1, renderWidth - leftPixels - rightPixels);
            int cropHeight = Mathf.Max(1, renderHeight - topPixels - bottomPixels);

            return new Rect(cropX, cropY, cropWidth, cropHeight);
        }

        public static void ApplyCroppingToCamera(Camera camera)
        {   // Apply crop settings to camera viewport
            if (camera == null) return;
            var (left, top, right, bottom) = GetNormalizedCropValues();
            camera.rect = new Rect(left, bottom, 1f - left - right, 1f - top - bottom);
        }

        // Camera and rendering utilities
        public static RenderTexture CreatePreviewRenderTexture(int previewWidth, int antiAliasing = 1)
        {   // Create render texture matching preview dimensions
            var (finalWidth, finalHeight) = CalculatePreviewDimensions();
            var rt = new RenderTexture(Mathf.Max(1, finalWidth), Mathf.Max(1, finalHeight), 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = Mathf.Clamp(antiAliasing, 1, 8),
                filterMode = antiAliasing > 1 ? FilterMode.Trilinear : FilterMode.Bilinear
            };
            if (!rt.IsCreated()) rt.Create();
            return rt;
        }

        public static Camera SetupPreviewCamera(Camera mainCamera, RenderTexture targetRT, Camera existingPreviewCamera = null)
        {   // Setup or create preview camera with main camera settings
            Camera preview = existingPreviewCamera;
            if (preview == null)
            {   // Create new preview camera
                var go = new GameObject("ScreenCapture_PreviewCamera") { hideFlags = HideFlags.DontSave };
                preview = go.AddComponent<Camera>();
            }

            if (mainCamera != null)
            {   // Copy main camera settings
                preview.CopyFrom(mainCamera);
                preview.transform.position = mainCamera.transform.position;
                preview.transform.rotation = mainCamera.transform.rotation;
                preview.transform.localScale = mainCamera.transform.localScale;
            }

            preview.enabled = false;
            preview.targetTexture = targetRT;
            preview.cullingMask = mainCamera?.cullingMask ?? ~0;
            preview.clearFlags = CameraClearFlags.SolidColor;
            preview.backgroundColor = BackgroundUI.GetBackgroundColor();
            return preview;
        }

        public static int ComputeCullingMask(bool showBackground)
        {   // Compute culling mask based on visibility settings
            int mask = World.MainCamera?.cullingMask ?? ~0;

            if (!showBackground)
            {
                int stars = LayerMask.GetMask("Stars");
                if (stars != 0) mask &= ~stars;
            }

            if (!PreviewInteriorVisible)
            {
                int interiorMask = GetCachedInteriorLayerMask();
                if (interiorMask != 0) mask &= ~interiorMask;
            }

            return mask;
        }

        public static void ApplyPreviewZoom(Camera mainCamera, Camera previewCamera, float zoomLevel)
        {   // Apply zoom to preview camera based on level
            if (previewCamera == null) return;

            float z = Mathf.Max(Mathf.Exp(zoomLevel), 1e-6f);

            if (previewCamera.orthographic)
            {   // Orthographic zoom
                float baseSize = mainCamera?.orthographicSize ?? previewCamera.orthographicSize;
                previewCamera.orthographicSize = Mathf.Clamp(Mathf.Max(baseSize, 1e-6f) / z, 1e-6f, 1_000_000f);
            }
            else
            {   // Perspective zoom
                float baseFov = mainCamera?.fieldOfView ?? previewCamera.fieldOfView;
                previewCamera.fieldOfView = Mathf.Clamp(Mathf.Clamp(baseFov, 5f, 120f) / z, 5f, 120f);
                if (mainCamera != null) previewCamera.transform.position = mainCamera.transform.position;
            }
        }

        // Scene visibility management
        public static System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> ApplySceneVisibilityTemporary(bool showBackground, bool showTerrain, System.Collections.Generic.HashSet<Rocket> hiddenRockets)
        {   // Temporarily disable renderers based on visibility settings
            var changed = GetPooledRendererList();
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(includeInactive: true);

            foreach (var r in renderers)
            {
                if (r?.gameObject == null || r.GetComponentInParent<RectTransform>() != null) continue;

                var go = r.gameObject;
                string layerName = LayerMask.LayerToName(go.layer) ?? string.Empty;

                bool shouldDisable = (!showBackground && IsBackgroundElement(go, layerName)) ||
                                   (!showTerrain && IsTerrainElement(go)) ||
                                   (hiddenRockets?.Contains(go.GetComponentInParent<Rocket>()) == true);

                if (shouldDisable && r.enabled) { changed.Add((r, r.enabled)); r.enabled = false; }
            }

            return changed;
        }

        private static bool IsBackgroundElement(GameObject go, string layerName) =>
            string.Equals(layerName, "Stars", System.StringComparison.OrdinalIgnoreCase) ||
            go.name.IndexOf("star", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            go.GetComponent<SFS.World.Atmosphere>() != null ||
            go.name.IndexOf("atmosphere", System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsTerrainElement(GameObject go) =>
            go.GetComponentInParent<SFS.World.StaticWorldObject>() != null ||
            go.GetComponentInParent<SFS.World.Terrain.DynamicTerrain>() != null;

        public static void RestoreSceneVisibility(System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> changed)
        {   // Restore renderer states and return list to pool
            if (changed == null) return;

            foreach (var (renderer, previousEnabled) in changed)
            {
                try { if (renderer != null) renderer.enabled = previousEnabled; }
                catch { }
            }

            ReturnPooledRendererList(changed);
        }

        // Rocket visibility management
        public static bool IsRocketVisible(Rocket rocket) => rocket != null && !Main.HiddenRockets.Contains(rocket);

        public static void SetRocketVisible(Rocket rocket, bool visible)
        {   // Set individual rocket visibility
            if (rocket == null) return;
            if (visible) Main.HiddenRockets.Remove(rocket); else Main.HiddenRockets.Add(rocket);
        }

        public static void SetAllRocketsVisible(bool visible)
        {   // Set all rockets visibility
            Main.HiddenRockets.Clear();
            if (!visible)
            {
                var rockets = UnityEngine.Object.FindObjectsOfType<Rocket>(includeInactive: true);
                foreach (var rocket in rockets) Main.HiddenRockets.Add(rocket);
            }
        }

        // Camera change detection
        public static bool CheckForSignificantChanges(ref Vector3 lastCameraPosition, ref Quaternion lastCameraRotation, float lastPreviewUpdate,
                                                      float velocityThresholdSq, float rotationVelocityThreshold, float positionDeltaThresholdSq, float rotationDeltaThreshold,
                                                      float movingInterval, float staticInterval, out CameraActivity activity)
        {   // Detect camera movement and determine update timing
            activity = CameraActivity.Static;
            if (World.MainCamera?.transform == null || World.PreviewCamera == null) return false;

            var currentTransform = World.MainCamera.transform;
            float currentTime = Time.unscaledTime;
            float timeSinceLastUpdate = currentTime - lastPreviewUpdate;
            float deltaTime = Mathf.Max(timeSinceLastUpdate, 0.001f);

            Vector3 positionDelta = currentTransform.position - lastCameraPosition;
            float positionDeltaSq = positionDelta.sqrMagnitude;
            float rotationDelta = Quaternion.Angle(currentTransform.rotation, lastCameraRotation);

            bool isMoving = (positionDeltaSq / (deltaTime * deltaTime)) > velocityThresholdSq ||
                           (rotationDelta / deltaTime) > rotationVelocityThreshold ||
                           positionDeltaSq > positionDeltaThresholdSq ||
                           rotationDelta > rotationDeltaThreshold;

            activity = isMoving ? CameraActivity.Moving : CameraActivity.Static;
            float updateInterval = activity == CameraActivity.Moving ? movingInterval : staticInterval;
            bool timeForUpdate = timeSinceLastUpdate >= updateInterval;

            if (timeForUpdate)
            {   // Update tracking state
                lastCameraPosition = currentTransform.position;
                lastCameraRotation = currentTransform.rotation;
                return true;
            }

            return false;
        }

        // UI helper utilities
        public static Container CreateNestedHorizontal(Container parent, float spacing, RectOffset padding, TextAnchor alignment, UIBase.ContainerContentDelegate contentCreator)
        {   // Create horizontal container with specified layout
            var container = Builder.CreateContainer(parent, 0, 0);
            container.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, alignment, spacing, padding, true);
            contentCreator?.Invoke(container);
            return container;
        }

        public static void ToggleInteriorView()
        {   // Toggle global interior visibility using InteriorManager
            try
            {
                if (SFS.InteriorManager.main != null)
                {
                    SFS.InteriorManager.main.ToggleInteriorView();
                    // Debug.Log($"Global interior visibility toggled to: {SFS.InteriorManager.main.interiorView.Value}");
                }
                else Debug.LogWarning("InteriorManager.main is null - cannot toggle interior view");
            }
            catch (Exception ex) { Debug.LogWarning($"Failed to toggle interior visibility: {ex.Message}"); }
        }

        public static void ShowHideWindow<T>(ref T windowInstance, System.Action showAction, System.Action hideAction) where T : UIBase, new()
        {   // Show or hide window instance with type safety
            if (windowInstance == null) windowInstance = new T();
            if (!windowInstance.IsOpen) windowInstance.Show(); else windowInstance.Hide();
        }
    }

    public static class UIUtilities
    {
        public static void CreateCompactToggle(Container parent, string label, System.Func<bool> getter, System.Action action)
        {   // Create a small toggle control that invokes a shared action and refreshes the preview
            int width = 200, height = 39;

            var toggle = Builder.CreateToggleWithLabel(parent, width, height, getter, () =>
            {
                try
                {
                    action?.Invoke();
                    Main.World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
                }
                catch (Exception ex) { Debug.LogError($"Toggle '{label}' action failed: {ex.Message}"); }
            }, 0, 0, label);

            // Resize toggle for compact layout
            try
            {
                float sizeFactor = 0.85f;
                if (toggle?.toggle?.rectTransform != null)
                {
                    toggle.toggle.rectTransform.localScale = new Vector3(sizeFactor, sizeFactor, 0f);
                    if (toggle.label?.rectTransform != null)
                    {
                        var lrt = toggle.label.rectTransform;
                        lrt.sizeDelta = new Vector2(lrt.sizeDelta.x * sizeFactor, lrt.sizeDelta.y);
                        lrt.anchoredPosition = new Vector2(lrt.anchoredPosition.x * sizeFactor, lrt.anchoredPosition.y);
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"Failed to resize toggle: {ex.Message}"); }
        }

        public static void CreateCropControls(Container parent, System.Action onCropChange)
        {   // Create crop input controls in two rows
            Builder.CreateLabel(parent, 390, 36, 0, 0, "Crop");

            CaptureUtilities.CreateNestedHorizontal(parent, 10f, null, TextAnchor.UpperRight, row1 =>
            {
                CreateCropInput(row1, () => Main.CropLeft, val => { Main.CropLeft = val; onCropChange?.Invoke(); });
                CreateCropInput(row1, () => Main.CropTop, val => { Main.CropTop = val; onCropChange?.Invoke(); });
            });

            CaptureUtilities.CreateNestedHorizontal(parent, 10f, null, TextAnchor.UpperRight, row2 =>
            {
                CreateCropInput(row2, () => Main.CropBottom, val => { Main.CropBottom = val; onCropChange?.Invoke(); });
                CreateCropInput(row2, () => Main.CropRight, val => { Main.CropRight = val; onCropChange?.Invoke(); });
            });
        }

        private static void CreateCropInput(Container parent, System.Func<float> getValue, System.Action<float> setValue)
        {   // Create input field for crop values with validation
            Builder.CreateTextInput(parent, 200, 42, 0, 0, Mathf.Clamp(getValue(), 0f, 100f).ToString("0"), val =>
            {
                if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float crop))
                {
                    setValue(Mathf.Clamp(crop, 0f, 100f));
                    CacheManager.InvalidateCropCache();
                    UpdatePreviewCropping();
                }
            });
        }

        public static void CreateTimeControls(Container parent)
        {   // Create time control buttons for frame stepping
            CaptureUtilities.CreateNestedHorizontal(parent, 5f, null, TextAnchor.MiddleCenter, timeControlRow =>
            {
                CreateTimeButton(timeControlRow, "||", () => Time.timeScale == 0 ? 1f : 0f, CaptureTime.SaveCurrentFrame);
                CreateTimeButton(timeControlRow, "<<", null, CaptureTime.StepBackwardInTime);
                CreateTimeButton(timeControlRow, ">>", null, CaptureTime.StepForwardAndPause);
            });
        }

        private static void CreateTimeButton(Container parent, string text, System.Func<float> getTimeScale, System.Action action)
        {   // Create time control button with optional time scale setting
            Builder.CreateButton(parent, 80, 58, 0, 0, () =>
            {
                try
                {
                    if (getTimeScale != null) Time.timeScale = getTimeScale();
                    action?.Invoke();
                }
                catch (System.Exception ex) { Debug.LogError($"Time control error: {ex.Message}"); }
            }, text);
        }

        private static void UpdatePreviewCropping()
        {   // Apply crop via UVs matching ReadPixels coordinate conversion
            if (World.PreviewCamera == null || Captue.PreviewImage == null) return;

            Main.LastScreenWidth = Screen.width;
            Main.LastScreenHeight = Screen.height;

            var (left, top, right, bottom) = CaptureUtilities.GetNormalizedCropValues();

            // Preview camera renders full scene without viewport cropping
            World.PreviewCamera.rect = new Rect(0, 0, 1, 1);
            
            // UV coordinates: (0,0) = bottom-left, (1,1) = top-right
            // Crop values: left/right from edges, top/bottom from edges
            // Convert to UV space: uvY = 1 - normalizedTopDistance, uvHeight = 1 - topCrop - bottomCrop
            float uvLeft = left;
            float uvBottom = bottom;  // Bottom crop stays as bottom offset
            float uvWidth = 1f - left - right;
            float uvHeight = 1f - top - bottom;
            
            Captue.PreviewImage.uvRect = new Rect(uvLeft, uvBottom, uvWidth, uvHeight);

            PreviewUtilities.UpdatePreviewImageLayoutForCurrentRT();
            World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
        }
    }

    public static class PreviewUtilities
    {
        // RT pool for instant switching without allocation lag
        private static RenderTexture[] rtPool = new RenderTexture[2];
        private static int activeRTIndex = -1;

        public static RawImage FitPreviewImageToBox(RawImage previewImage, RenderTexture PreviewRT, float scaleFactor = 1.0f)
        {   // Fit preview image within parent bounds while preserving aspect ratio and applying crop
            if (previewImage?.rectTransform?.parent == null) return null;

            var img = previewImage.rectTransform;
            var parent = img.parent as RectTransform;
            var imgGO = img.gameObject;

            // Clean and configure image object
            imgGO.GetComponentsInChildren<Component>()
                .Where(comp => comp?.GetType().Name == "Canvas")
                .ToList()
                .ForEach(comp => UnityEngine.Object.DestroyImmediate(comp));

            imgGO.layer = parent.gameObject.layer;
            var mask = imgGO.GetComponent<RectMask2D>() ?? imgGO.AddComponent<RectMask2D>();

            // Calculate dimensions
            float boxW = Mathf.Max(1f, parent.rect.width);
            float boxH = Mathf.Max(1f, parent.rect.height);
            int rtW = PreviewRT?.IsCreated() == true ? PreviewRT.width : Screen.width;
            int rtH = PreviewRT?.IsCreated() == true ? PreviewRT.height : Screen.height;

            var (left, top, right, bottom) = CaptureUtilities.GetNormalizedCropValues();
            float cropW = Mathf.Max(1f, rtW * (1f - left - right));
            float cropH = Mathf.Max(1f, rtH * (1f - top - bottom));
            float aspect = cropW / Mathf.Max(1f, cropH);

            // Calculate fit size
            float fitW = boxW;
            float fitH = fitW / Mathf.Max(1e-6f, aspect);
            if (fitH > boxH)
            {   // Fit by height instead
                fitH = boxH;
                fitW = fitH * aspect;
            }

            float s = Mathf.Clamp01(scaleFactor);
            fitW *= s;
            fitH *= s;

            // Configure image transform and cropping
            img.anchorMin = img.anchorMax = img.pivot = new Vector2(0.5f, 0.5f);
            img.sizeDelta = new Vector2(fitW, fitH);
            img.anchoredPosition = Vector2.zero;
            img.localScale = Vector3.one;

            // Apply consistent UV cropping matching ReadPixels coordinate system  
            try 
            { 
                float uvLeft = left;
                float uvBottom = bottom;  // Bottom crop as bottom offset
                float uvWidth = 1f - left - right;
                float uvHeight = 1f - top - bottom;
                
                // Safety clamp
                uvWidth = Mathf.Clamp(uvWidth, 0.01f, 1f - uvLeft);
                uvHeight = Mathf.Clamp(uvHeight, 0.01f, 1f - uvBottom);
                
                previewImage.uvRect = new Rect(uvLeft, uvBottom, uvWidth, uvHeight); 
            } 
            catch { }

            // Configure UI properties
            previewImage.raycastTarget = false;
            previewImage.material = null;
            previewImage.maskable = true;

            if (parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            return previewImage;
        }

        public static bool CleanupRTPool()
        {   // Release and destroy all pooled render textures
            bool anyDestroyed = false;

            for (int i = 0; i < rtPool.Length; i++)
            {
                if (rtPool[i] != null)
                {   // Clean up render texture
                    rtPool[i].Release();
                    UnityEngine.Object.Destroy(rtPool[i]);
                    rtPool[i] = null;
                    anyDestroyed = true;
                }
            }

            activeRTIndex = -1;
            return anyDestroyed;
        }

        public static int InvalidateRTPool()
        {   // Clear RT pool to force recreation with new dimensions
            int invalidatedCount = rtPool.Count(rt => rt != null);

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
            return invalidatedCount;
        }

        public static (RenderTexture renderTexture, bool wasCreated, int poolIndex) SwitchToPooledRT(int poolIndex, int width, int height, FilterMode filterMode, int antiAliasing)
        {   // Switch to pooled RT or create new one if needed
            poolIndex = Mathf.Clamp(poolIndex, 0, rtPool.Length - 1);

            // Return existing RT if already correct
            if (activeRTIndex == poolIndex && rtPool[poolIndex] != null &&
                rtPool[poolIndex].width == width && rtPool[poolIndex].height == height)
                return (rtPool[poolIndex], false, poolIndex);

            bool wasCreated = false;

            // Create new RT if needed
            if (rtPool[poolIndex] == null || rtPool[poolIndex].width != width || rtPool[poolIndex].height != height)
            {   // Clean up old RT and create new one
                if (rtPool[poolIndex] != null)
                {
                    rtPool[poolIndex].Release();
                    UnityEngine.Object.Destroy(rtPool[poolIndex]);
                }

                rtPool[poolIndex] = new RenderTexture(width, height, 24)
                {   // Configure RT properties
                    filterMode = filterMode,
                    antiAliasing = antiAliasing
                };

                rtPool[poolIndex].Create();
                wasCreated = true;
            }

            activeRTIndex = poolIndex;
            return (rtPool[poolIndex], wasCreated, poolIndex);
        }

        public static void UpdatePreviewImageLayoutForCurrentRT()
        {   // Update RawImage size using current RT and crop to avoid stretching
            if (Captue.PreviewImage == null) return;

            int rtW = (Captue.PreviewRT?.IsCreated() == true) ? Captue.PreviewRT.width : CaptureUtilities.CalculatePreviewDimensions().width;
            int rtH = (Captue.PreviewRT?.IsCreated() == true) ? Captue.PreviewRT.height : CaptureUtilities.CalculatePreviewDimensions().height;

            var (left, top, right, bottom) = CaptureUtilities.GetNormalizedCropValues();
            int cropW = Mathf.Max(1, Mathf.RoundToInt(rtW * (1f - left - right)));
            int cropH = Mathf.Max(1, Mathf.RoundToInt(rtH * (1f - top - bottom)));

            UpdatePreviewImageSize(cropW, cropH);
        }

        private static void UpdatePreviewImageSize(int cropW, int cropH)
        {   // Fit inside 520x430 while preserving aspect
            const float containerMaxWidth = 520f;
            const float containerMaxHeight = 430f;

            float aspect = (float)cropW / Mathf.Max(1, cropH);
            float widthByWidth = Mathf.Min(containerMaxWidth, cropW);
            float heightFromWidth = widthByWidth / Mathf.Max(1e-6f, aspect);
            float heightByHeight = Mathf.Min(containerMaxHeight, cropH);
            float widthFromHeight = heightByHeight * aspect;

            float finalWidth = widthFromHeight <= containerMaxWidth ? widthFromHeight : widthByWidth;
            float finalHeight = widthFromHeight <= containerMaxWidth ? heightByHeight : heightFromWidth;

            if (Captue.PreviewImage?.rectTransform != null)
                Captue.PreviewImage.rectTransform.sizeDelta = new Vector2(finalWidth, finalHeight);

            UpdateParentLayout(finalHeight);
        }

        private static void UpdateParentLayout(float height)
        {   // Update parent layout to match new height
            var parent = Captue.PreviewImage.transform.parent as RectTransform;
            if (parent?.GetComponent<LayoutElement>() is LayoutElement parentLayout)
            {
                parentLayout.preferredWidth = 520f;
                parentLayout.preferredHeight = height;
                LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            }
        }

        public static void SetupPreview(Container imageContainer)
        {   // Setup preview image with proper layout and render texture
            var previewGO = new GameObject("PreviewImage");
            previewGO.transform.SetParent(imageContainer.rectTransform, false);

            var rect = previewGO.AddComponent<RectTransform>();
            var (finalWidth, finalHeight) = CaptureUtilities.CalculatePreviewDimensions();
            rect.sizeDelta = new Vector2(finalWidth, finalHeight);

            SetupContainerLayout(imageContainer, finalWidth, finalHeight);

            Captue.PreviewImage = previewGO.AddComponent<UnityEngine.UI.RawImage>();
            Captue.PreviewImage.color = Color.white;
            Captue.PreviewImage.maskable = false;
            Captue.PreviewImage.uvRect = new Rect(0, 0, 1, 1);

            InitializeRenderTexture();
            Captue.PreviewImage.texture = Captue.PreviewRT;

            World.PreviewCamera = CaptureUtilities.SetupPreviewCamera(World.MainCamera, Captue.PreviewRT, World.PreviewCamera);
            World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
        }

        private static void SetupContainerLayout(Container imageContainer, float width, float height)
        {   // Configure container layout element
            var layout = imageContainer.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ??
                         imageContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minWidth = layout.preferredWidth = 520f;
            layout.flexibleWidth = 0f;
            layout.preferredHeight = height;
        }

        private static void InitializeRenderTexture()
        {   // Create and initialize render texture for preview
            Main.LastScreenWidth = Screen.width;
            Main.LastScreenHeight = Screen.height;

            if (Captue.PreviewRT != null)
            {
                Captue.PreviewRT.Release();
                UnityEngine.Object.Destroy(Captue.PreviewRT);
            }

            Captue.PreviewRT = CaptureUtilities.CreatePreviewRenderTexture(Main.PreviewWidth);
            if (!Captue.PreviewRT.IsCreated()) Captue.PreviewRT.Create();
        }

        public static void CleanupUI(Captue owner)
        {   // Clean up UI resources for the given owner
            if (owner == null) return;

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
            owner.showBackground = owner.showTerrain = true;
            Main.HiddenRockets.Clear();
        }
    }

    public enum CameraActivity
    {
        Moving = 0,  // Camera is moving - use low quality with fewer updates
        Static = 1   // Camera is static - use high quality with medium updates
    }

    public static class CaptureTime
    {
        private static readonly int MaxFrameHistory = 30;
        private static readonly System.Collections.Generic.List<SFS.World.WorldSave> frameHistory = new System.Collections.Generic.List<SFS.World.WorldSave>(MaxFrameHistory);
        private static int currentFrameIndex = -1;

        public static void SaveCurrentFrame()
        {   // Create and store world save using reflection to access private CreateWorldSave method
            try
            {
                if (GameManager.main == null) return;

                var createSaveMethod = typeof(GameManager).GetMethod("CreateWorldSave", BindingFlags.NonPublic | BindingFlags.Instance);
                var worldSave = createSaveMethod?.Invoke(GameManager.main, null) as SFS.World.WorldSave;
                if (worldSave == null) return;

                // Drop forward history if stepping back to maintain linear timeline
                if (currentFrameIndex >= 0 && currentFrameIndex < frameHistory.Count - 1)
                    frameHistory.RemoveRange(currentFrameIndex + 1, frameHistory.Count - currentFrameIndex - 1);

                frameHistory.Add(worldSave);
                
                // Trim to maximum capacity
                if (frameHistory.Count > MaxFrameHistory)
                    frameHistory.RemoveRange(0, frameHistory.Count - MaxFrameHistory);

                currentFrameIndex = frameHistory.Count - 1;
                // Debug.Log($"Saved frame {currentFrameIndex + 1}/{frameHistory.Count}");
            }
            catch (Exception ex) { Debug.LogError($"Error saving frame: {ex.Message}"); }
        }

        public static void LoadFrame(int index, bool saveFirst = true)
        {   // Load a saved frame by index with optional saving beforehand
            try
            {
                if (index < 0 || index >= frameHistory.Count || frameHistory[index] == null) return;

                if (saveFirst) SaveCurrentFrame();

                GameManager.main.LoadSave(frameHistory[index], false, SFS.UI.MsgDrawer.main);
                currentFrameIndex = index;
                Time.timeScale = 0f;
            }
            catch (Exception ex) { Debug.LogError($"Error loading frame: {ex.Message}"); Time.timeScale = 0f; }
        }

        public static void StepForwardAndPause()
        {   // Save current state then advance time by 0.1s and pause
            try
            {
                SaveCurrentFrame();

                var helperGo = new GameObject("TimeStepHelper");
                var helper = helperGo.AddComponent<TimeStepHelper>();
                
                helper.OnStepComplete = () => UnityEngine.Object.Destroy(helperGo);
                helper.ConfigureStep(0.1f, 1, frameHistory);
                helper.BeginTimeStep();
            }
            catch (Exception ex) { Debug.LogError($"Step forward error: {ex.Message}"); Time.timeScale = 0f; }
        }

        public static void StepBackwardInTime()
        {   // Step back to previous saved frame without saving current state
            try
            {
                Time.timeScale = 0f;
                if (frameHistory.Count == 0) return;

                int nextFrameIndex = currentFrameIndex > 0 ? currentFrameIndex - 1 :
                                    currentFrameIndex == 0 ? -1 :
                                    frameHistory.Count > 1 ? frameHistory.Count - 2 : -1;

                if (nextFrameIndex < 0 || nextFrameIndex >= frameHistory.Count) return;

                LoadFrame(nextFrameIndex, false);
            }
            catch (Exception ex) { Debug.LogError($"Step back error: {ex.Message}"); Time.timeScale = 0f; }
        }

        public static void SaveOnPause()
        {   // Save frame when pausing if time was running
            if (Time.timeScale > 0f) SaveCurrentFrame();
        }

        public static void SaveOnResumeAndUnpause()
        {   // Save frame then resume time
            SaveCurrentFrame();
            Time.timeScale = 1f;
        }
    }

    public class TimeStepHelper : UnityEngine.MonoBehaviour
    {
        public Action OnStepComplete;
        private float stepSeconds = 0.1f;
        private int stepsToTake = 1;
        private bool running = false;

        public void ConfigureStep(float stepLength, int steps, System.Collections.Generic.List<SFS.World.WorldSave> frameHistory)
        {   // Configure step parameters with safety bounds
            stepSeconds = Math.Max(0.0001f, stepLength);
            stepsToTake = Math.Max(0, steps);
        }

        public void BeginTimeStep()
        {   // Start the time step coroutine if not already running
            if (running) return;
            
            running = true;
            StartCoroutine(RunStepsCoroutine());
        }

        private System.Collections.IEnumerator RunStepsCoroutine()
        {   // Execute time steps with proper timing control
            if (stepsToTake == 0)
                yield return null;
            else
            {   // Run actual steps with time scale control
                int actualSteps = Math.Max(1, stepsToTake);

                for (int i = 0; i < actualSteps; i++)
                {
                    Time.timeScale = 1f;
                    yield return new UnityEngine.WaitForSecondsRealtime(stepSeconds);
                    Time.timeScale = 0f;
                    yield return new UnityEngine.WaitForSecondsRealtime(0.02f);
                }
            }

            running = false;
            try { OnStepComplete?.Invoke(); }
            catch (Exception ex) { UnityEngine.Debug.LogWarning($"TimeStepHelper completion threw: {ex.Message}"); }
        }
    }

    public class UIAnimationHost : MonoBehaviour
    {   
    }
 
    public static class AnimationUtilities
    {
        private static readonly Dictionary<UITools.ClosableWindow, MonoBehaviour> animationHosts = new Dictionary<UITools.ClosableWindow, MonoBehaviour>();
        private static readonly Dictionary<UITools.ClosableWindow, Coroutine> activeAnimations = new Dictionary<UITools.ClosableWindow, Coroutine>();
        private static readonly Dictionary<UITools.ClosableWindow, Color> originalColors = new Dictionary<UITools.ClosableWindow, Color>();

        public static IEnumerator AnimateWindowColor(UITools.ClosableWindow closableWindow, bool success = true)
        {   // Animate window background using sine wave over 0.8 seconds with result-based color
            if (closableWindow == null) yield break;

            var graphics = closableWindow.gameObject.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            var targetGraphic = graphics.FirstOrDefault(g =>
                g.name.ToLower().Contains("back") ||
                g.name.ToLower().Contains("background") ||
                g.name.ToLower().Contains("game")) ?? graphics.FirstOrDefault();

            if (targetGraphic == null)
            {
                Debug.LogWarning("Could not find background component to animate");
                yield break;
            }

            // Store original color if not already stored
            if (!originalColors.ContainsKey(closableWindow))
                originalColors[closableWindow] = targetGraphic.color;

            Color originalColor = originalColors[closableWindow];
            Color effectColor = success ?
                new Color(0.0018f, 0.6902f, 0.0804f, 1f) :
                new Color(0.8f, 0.1f, 0.1f, 1f);

            float duration = 0.8f;
            float elapsed = 0f;

            // Force reset to original color regardless of current state
            targetGraphic.color = originalColor;
            yield return null;

            while (elapsed < duration)
            {   // Use sine wave pattern for smooth color transition that returns to original
                float t = elapsed / duration;
                float sineWave = Mathf.Sin(t * Mathf.PI);
                targetGraphic.color = Color.Lerp(originalColor, effectColor, sineWave);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            // Ensure final color is exactly original and cleanup tracking
            targetGraphic.color = originalColor;
            activeAnimations.Remove(closableWindow);
        }

        public static void StartWindowColorAnimation(UITools.ClosableWindow closableWindow, bool success = true)
        {   // Start window color animation with proper cleanup of existing animations
            if (closableWindow?.gameObject == null) return;

            // Get or create animation host MonoBehaviour
            MonoBehaviour host = null;
            if (!animationHosts.TryGetValue(closableWindow, out host) || host == null)
            {
                host = closableWindow.gameObject.GetComponent<MonoBehaviour>() ?? 
                       closableWindow.gameObject.AddComponent<UIAnimationHost>();
                animationHosts[closableWindow] = host;
            }

            // Stop any existing animation for this window
            if (activeAnimations.TryGetValue(closableWindow, out var existingCoroutine) && existingCoroutine != null && host != null)
            {
                host.StopCoroutine(existingCoroutine);
                
                // Reset to original color immediately
                if (originalColors.TryGetValue(closableWindow, out var originalColor))
                {
                    var graphics = closableWindow.gameObject.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                    var targetGraphic = graphics.FirstOrDefault(g =>
                        g.name.ToLower().Contains("back") ||
                        g.name.ToLower().Contains("background") ||
                        g.name.ToLower().Contains("game")) ?? graphics.FirstOrDefault();
                    
                    if (targetGraphic != null)
                        targetGraphic.color = originalColor;
                }
            }

            // Start new animation
            if (host != null)
            {
                var newCoroutine = host.StartCoroutine(AnimateWindowColor(closableWindow, success));
                activeAnimations[closableWindow] = newCoroutine;
            }
        }

        public static void StopAllAnimations()
        {   // Stop all active window color animations and reset colors
            foreach (var kvp in activeAnimations.ToList())
            {
                if (kvp.Key?.gameObject != null && kvp.Value != null && animationHosts.TryGetValue(kvp.Key, out var host) && host != null)
                {
                    host.StopCoroutine(kvp.Value);
                    
                    if (originalColors.TryGetValue(kvp.Key, out var originalColor))
                    {
                        var graphics = kvp.Key.gameObject.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                        var targetGraphic = graphics.FirstOrDefault(g =>
                            g.name.ToLower().Contains("back") ||
                            g.name.ToLower().Contains("background") ||
                            g.name.ToLower().Contains("game")) ?? graphics.FirstOrDefault();
                        
                        if (targetGraphic != null)
                            targetGraphic.color = originalColor;
                    }
                }
            }
            
            activeAnimations.Clear();
        }

        public static IEnumerator DelayedLayoutRefresh(Container previewContainer, System.Action updateBorderCallback = null)
        {   // Wait a frame then refresh layouts to ensure proper rendering
            yield return null;

            if (previewContainer != null)
            {   // Force immediate layout recalculation
                LayoutRebuilder.ForceRebuildLayoutImmediate(previewContainer.rectTransform);

                if (previewContainer.rectTransform.parent != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(previewContainer.rectTransform.parent as RectTransform);

                PreviewUtilities.UpdatePreviewImageLayoutForCurrentRT();

                if (Captue.PreviewImage?.rectTransform != null)
                {   // Update parent layout to match image size
                    var imgSize = Captue.PreviewImage.rectTransform.sizeDelta;
                    var parentLayout = previewContainer.gameObject.GetComponent<LayoutElement>() ?? 
                                      previewContainer.gameObject.AddComponent<LayoutElement>();
                    parentLayout.preferredHeight = imgSize.y + 12f;
                    parentLayout.minHeight = imgSize.y + 12f;
                }

                updateBorderCallback?.Invoke();
            }

            // Re-render preview with cropping applied
            if (World.PreviewCamera != null && Captue.PreviewRT != null)
            {
                World.PreviewCamera.targetTexture = Captue.PreviewRT;
                World.PreviewCamera.Render();
            }
        }

        public static IEnumerator SyncPreviewVisibility(GameObject previewBorder, GameObject windowGameObject)
        {   // Continuously sync preview visibility with window state
            while (previewBorder != null && windowGameObject != null)
            {
                var windowActive = windowGameObject.activeInHierarchy;
                var windowMinimized = windowGameObject.GetComponent<UITools.ClosableWindow>()?.Minimized ?? false;
                bool shouldShow = windowActive && !windowMinimized;

                if (previewBorder.activeSelf != shouldShow)
                    previewBorder.SetActive(shouldShow);

                yield return new WaitForSecondsRealtime(0.1f);
            }
        }
    }

    public static class PreviewHierarchyUtilities
    {
        public static void ForceWindowHierarchyCompliance(GameObject borderGO, GameObject windowGameObject)
        {   // Ensure preview follows window visibility and is properly constrained within border
            if (borderGO == null || windowGameObject == null) return;

            // Remove Canvas components to prevent independent rendering
            var canvasComponents = borderGO.GetComponentsInChildren<UnityEngine.Component>(true)
                .Where(c => c != null && c.GetType().Name == "Canvas")
                .ToArray();
            foreach (var canvas in canvasComponents)
            {
                try { UnityEngine.Object.Destroy(canvas); } catch { }
            }

            // Disable UI raycasting so preview follows window interaction
            var allGraphics = borderGO.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            foreach (var graphic in allGraphics)
                graphic.raycastTarget = false;

            // Match layers exactly to ensure same rendering order as window
            borderGO.layer = windowGameObject.layer;
            var allChildren = borderGO.GetComponentsInChildren<Transform>(true);
            foreach (var child in allChildren)
                child.gameObject.layer = windowGameObject.layer;

            // Configure preview image constraints
            if (Captue.PreviewImage?.rectTransform != null)
            {   // Force image to fill border exactly without overflow
                var imageRect = Captue.PreviewImage.rectTransform;
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.sizeDelta = Vector2.zero;
                imageRect.anchoredPosition = Vector2.zero;
                imageRect.localScale = Vector3.one;

                var imgLE = imageRect.GetComponent<LayoutElement>() ?? imageRect.gameObject.AddComponent<LayoutElement>();
                imgLE.ignoreLayout = true;

                try { Captue.PreviewImage.uvRect = new Rect(0f, 0f, 1f, 1f); } catch { }
                if (Captue.PreviewRT != null && Captue.PreviewImage.texture != Captue.PreviewRT)
                    Captue.PreviewImage.texture = Captue.PreviewRT;

                Captue.PreviewImage.raycastTarget = false;
            }

            // Ensure border has proper clipping mask
            var mask = borderGO.GetComponent<RectMask2D>() ?? borderGO.AddComponent<RectMask2D>();
            borderGO.gameObject.SetActive(windowGameObject.activeInHierarchy);
        }

        public static void SyncPreviewLayerAndMask(Container previewContainer, Box previewBorder)
        {   // Enforce strict window hierarchy so preview follows menu visibility and draw order
            if (previewContainer == null) return;

            int parentLayer = previewContainer.gameObject.layer;

            if (previewBorder != null)
            {   // Configure border layer and hierarchy
                var borderGO = previewBorder.gameObject;
                borderGO.layer = parentLayer;

                var mask = borderGO.GetComponent<RectMask2D>() ?? borderGO.AddComponent<RectMask2D>();

                // Remove Canvas components to prevent override
                var canvasComponents = borderGO.GetComponents<UnityEngine.Component>()
                    .Where(c => c.GetType().Name == "Canvas")
                    .ToArray();
                foreach (var canvas in canvasComponents)
                    UnityEngine.Object.Destroy(canvas);

                if (borderGO.transform.parent != previewContainer.gameObject.transform)
                    borderGO.transform.SetParent(previewContainer.gameObject.transform, false);

                var graphics = borderGO.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                foreach (var graphic in graphics)
                    graphic.raycastTarget = false;
            }

            if (Captue.PreviewImage != null)
            {   // Configure image layer and hierarchy
                var imgGO = Captue.PreviewImage.gameObject;
                imgGO.layer = parentLayer;

                var imgCanvasComponents = imgGO.GetComponents<UnityEngine.Component>()
                    .Where(c => c.GetType().Name == "Canvas")
                    .ToArray();
                foreach (var canvas in imgCanvasComponents)
                    UnityEngine.Object.Destroy(canvas);

                if (previewBorder != null && imgGO.transform.parent != previewBorder.gameObject.transform)
                    imgGO.transform.SetParent(previewBorder.gameObject.transform, false);

                var imgGraphics = imgGO.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                foreach (var graphic in imgGraphics)
                    graphic.raycastTarget = false;

                // Normalize transform settings
                var imageRect = Captue.PreviewImage.rectTransform;
                if (imageRect != null)
                {
                    imageRect.localScale = Vector3.one;
                    imageRect.anchorMin = Vector2.zero;
                    imageRect.anchorMax = Vector2.one;
                    imageRect.sizeDelta = Vector2.zero;
                    imageRect.anchoredPosition = Vector2.zero;
                    try { Captue.PreviewImage.uvRect = new Rect(0f, 0f, 1f, 1f); } catch { }
                }
            }
        }

        public static void UpdatePreviewBorderSize(Container previewContainer, Box previewBorder)
        {   // Size border to match RawImage and apply UV cropping; image drives the box
            if (previewContainer == null || Captue.PreviewImage == null || previewBorder == null) return;

            try
            {
                var imageRect = Captue.PreviewImage.rectTransform;
                var borderRect = previewBorder.gameObject.GetComponent<RectTransform>();
                if (imageRect == null || borderRect == null) return;

                // Ensure image is laid out according to RT+crop and then mirror size to border
                PreviewUtilities.UpdatePreviewImageLayoutForCurrentRT();
                var imgSize = imageRect.sizeDelta;
                borderRect.sizeDelta = imgSize;

                // Apply consistent UV cropping matching ReadPixels coordinate system
                var (leftCrop, topCrop, rightCrop, bottomCrop) = CaptureUtilities.GetNormalizedCropValues();
                
                // Convert crop values to UV coordinates consistently with ReadPixels
                float uvLeft = leftCrop;
                float uvBottom = bottomCrop;  // Bottom crop as bottom offset
                float uvWidth = 1f - leftCrop - rightCrop;
                float uvHeight = 1f - topCrop - bottomCrop;

                // Safety clamp to texture bounds
                uvWidth = Mathf.Clamp(uvWidth, 0.01f, 1f - uvLeft);
                uvHeight = Mathf.Clamp(uvHeight, 0.01f, 1f - uvBottom);

                try { Captue.PreviewImage.uvRect = new Rect(uvLeft, uvBottom, uvWidth, uvHeight); } catch { }

                if (Captue.PreviewRT != null && Captue.PreviewImage.texture != Captue.PreviewRT)
                    Captue.PreviewImage.texture = Captue.PreviewRT;

                // Update parent layout to follow border height
                var parentLayout = previewContainer.gameObject.GetComponent<LayoutElement>() ?? previewContainer.gameObject.AddComponent<LayoutElement>();
                parentLayout.preferredHeight = imgSize.y + 12f;
                parentLayout.minHeight = imgSize.y + 12f;
            }
            catch (Exception ex) { Debug.LogWarning($"Could not update preview border size: {ex.Message}"); }
        }

        public static void SetupPreviewWithBorder(Container parent, ref Box previewBorder, ref bool previewInitialized)
        {   // Create preview image first with border sized exactly by RawImage size (RT+crop)
            if (parent == null) return;

            UIBase.DisableRaycastsInChildren(parent.gameObject);

            // Create RawImage if missing directly under the provided container
            if (Captue.PreviewImage == null)
                PreviewUtilities.SetupPreview(parent);

            // Size image from current RT and crop, then create border to match
            PreviewUtilities.UpdatePreviewImageLayoutForCurrentRT();
            var imgSize = Captue.PreviewImage?.rectTransform != null ? 
                         Captue.PreviewImage.rectTransform.sizeDelta : 
                         new Vector2(520f, 430f);

            previewBorder = Builder.CreateBox(parent, Mathf.RoundToInt(imgSize.x), Mathf.RoundToInt(imgSize.y), 0, 0, 0.2f);
            previewBorder.Color = new Color(0.4f, 0.48f, 0.6f, 0.6f);

            var borderGO = previewBorder.gameObject;
            borderGO.layer = parent.gameObject.layer;

            var mask = borderGO.GetComponent<RectMask2D>() ?? borderGO.AddComponent<RectMask2D>();

            if (Captue.PreviewImage != null)
            {   // Parent image to border and fill exactly
                Captue.PreviewImage.transform.SetParent(borderGO.transform, false);
                Captue.PreviewImage.gameObject.layer = borderGO.layer;

                var imageRect = Captue.PreviewImage.rectTransform;
                imageRect.anchorMin = Vector2.zero;
                imageRect.anchorMax = Vector2.one;
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.sizeDelta = Vector2.zero;
                imageRect.anchoredPosition = Vector2.zero;
                imageRect.localScale = Vector3.one;

                var imgLE = Captue.PreviewImage.gameObject.GetComponent<LayoutElement>() ??
                            Captue.PreviewImage.gameObject.AddComponent<LayoutElement>();
                imgLE.ignoreLayout = true;

                Captue.PreviewImage.raycastTarget = false;

                // Ensure RT assigned
                if (Captue.PreviewRT != null && Captue.PreviewImage.texture != Captue.PreviewRT)
                    Captue.PreviewImage.texture = Captue.PreviewRT;
            }

            var le = parent.gameObject.GetComponent<LayoutElement>() ?? parent.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 520f;
            le.minWidth = 520f;
            le.flexibleWidth = 0f;
            le.preferredHeight = imgSize.y + 12f;
            le.minHeight = imgSize.y + 12f;

            previewInitialized = true;
            // Debug.Log($"Preview setup: Image {imgSize.x}x{imgSize.y}");
        }
    }

}