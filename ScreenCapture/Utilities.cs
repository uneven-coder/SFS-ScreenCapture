using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SFS.IO;
using SFS.UI.ModGUI;
using SFS.World;
using UnityEngine;

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

    // UI-related constants for consistent sizing
    public static class UIConstants
    {
        public const float DEFAULT_CONTAINER_WIDTH = 520f;
        public const float DEFAULT_CONTAINER_HEIGHT = 430f;
        public const float DEFAULT_SPACING = 8f;
        public const float DEFAULT_BUTTON_HEIGHT = 46f;
        public const float DEFAULT_INPUT_HEIGHT = 40f;
    }
    
    public static class CaptureUtilities
    {
        #region Memory and Resolution Management

        public static (int width, int height) GetResolutionFromWidth(Captue owner, int width)
        {   // Calculate height from width using current screen aspect
            width = Mathf.Max(16, width);
            int height = Mathf.RoundToInt((float)width / Mathf.Max(1, (float)Screen.width) * (float)Screen.height);
            return (width, Mathf.Max(16, height));
        }

        public static (long gpuBytes, long cpuBytes, long rawBytes) EstimateMemoryForWidth(Captue owner, int width)
        {   // Estimate memory footprints for render/copy paths
            var (w, h) = GetResolutionFromWidth(owner, width);
            long pixels = (long)w * h;
            long gpu = (long)Math.Ceiling(pixels * (MemoryConstants.GPU_COLOR_BPP + MemoryConstants.GPU_DEPTH_BPP) * MemoryConstants.SafetyMultiplier);
            long cpu = (long)Math.Ceiling(pixels * MemoryConstants.CPU_BPP * MemoryConstants.SafetyMultiplier);
            long raw = pixels * MemoryConstants.CPU_BPP;
            return (gpu, cpu, raw);
        }

        public static (long gpuBudget, long cpuBudget) GetMemoryBudgets(Captue owner)
        {   // Compute conservative memory budgets based on device capabilities
            long gpu = (long)(SystemInfo.graphicsMemorySize * 1024L * 1024L * MemoryConstants.GPU_BUDGET_FRACTION);
            long cpu = (long)(SystemInfo.systemMemorySize * 1024L * 1024L * MemoryConstants.CPU_BUDGET_FRACTION);
            return (gpu, cpu);
        }

        public static int ComputeMaxSafeWidth(Captue owner)
        {   // Determine maximum safe width constrained by VRAM/RAM and texture limits
            float aspect = (float)Screen.height / Mathf.Max(1, Screen.width);
            var (gpuBudget, cpuBudget) = GetMemoryBudgets(owner);

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

        #endregion

        #region Preview and Camera Management

        public static void CreatePreviewRenderTexture(Captue owner)
        {   // Create or recreate the preview render texture
            if (owner.previewRT != null)
            {
                owner.previewRT.Release();
                UnityEngine.Object.Destroy(owner.previewRT);
            }

            float screenAspect = (float)Screen.width / Screen.height;
            int rtHeight = Mathf.RoundToInt(owner.previewWidth / screenAspect);

            owner.previewRT = new RenderTexture(owner.previewWidth, rtHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear
            };

            if (!owner.previewRT.IsCreated())
                owner.previewRT.Create();
        }

        public static bool TryAcquireMainCamera(Captue owner)
        {   // Attempt to resolve and cache the world camera
            if (owner.mainCamera != null)
                return true;

            if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
                owner.mainCamera = GameCamerasManager.main.world_Camera.camera;

            return owner.mainCamera != null;
        }

        public static void EnsurePreviewCamera(Captue owner)
        {   // Create and configure the preview camera if needed
            if (!TryAcquireMainCamera(owner))
                return;

            if (owner.previewCamera == null)
            {
                var go = new GameObject("ScreenCapture_PreviewCamera");
                go.hideFlags = HideFlags.DontSave;
                owner.previewCamera = go.AddComponent<Camera>();
                owner.previewCamera.enabled = false;
            }

            owner.previewCamera.CopyFrom(owner.mainCamera);
            owner.previewCamera.enabled = false;
            owner.previewCamera.targetTexture = owner.previewRT;
            owner.previewCamera.cullingMask = ComputeCullingMask(owner);

            ApplyBackgroundSettingsToCamera(owner, owner.previewCamera);

            owner.previewCamera.transform.position = owner.mainCamera.transform.position;
            owner.previewCamera.transform.rotation = owner.mainCamera.transform.rotation;
            owner.previewCamera.transform.localScale = owner.mainCamera.transform.localScale;

            ApplyPreviewZoom(owner);
        }

        public static int ComputeCullingMask(Captue owner)
        {   // Calculate the appropriate culling mask based on current settings
            int mask = owner.mainCamera != null ? owner.mainCamera.cullingMask : ~0;

            if (!owner.showBackground)
            {
                int stars = LayerMask.GetMask("Stars");
                if (stars != 0)
                    mask &= ~stars;
            }

            return mask;
        }

        public static void ApplyBackgroundSettingsToCamera(Captue owner, Camera cam)
        {   // Apply background color and transparency settings to camera
            if (cam == null)
                return;

            // Get color from BackgroundUI's static method
            var color = new Color(
                BackgroundUI.R / 255f,
                BackgroundUI.G / 255f,
                BackgroundUI.B / 255f,
                BackgroundUI.Transparent ? 0f : 1f
            );

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = color;
        }
        
        public static void ApplyCullingForRender(Captue owner, Camera cam)
        {   // Apply the computed culling mask to the camera
            if (cam == null)
                return;
            cam.cullingMask = ComputeCullingMask(owner);
        }

        #endregion

        #region Zoom Management

        public static float SnapZoomLevel(float level)
        {   // Snap to exact 1x (level 0) when multiplier is close to 1
            float factor = Mathf.Exp(level);
            return Mathf.Abs(factor - 1f) <= 0.02f ? 0f : level;
        }

        public static void SetPreviewZoom(Captue owner, float factor)
        {   // Update preview zoom from buttons within safe factor range
            float clamped = Mathf.Clamp(factor, 0.25f, 4f);
            float level = Mathf.Log(Mathf.Max(clamped, 0.001f));
            owner.previewZoomLevel = SnapZoomLevel(level);
            owner.previewZoom = Mathf.Exp(owner.previewZoomLevel);
            ApplyPreviewZoom(owner);
        }

        public static void SetPreviewZoomLevelUnclamped(Captue owner, float level)
        {   // Update preview zoom level from input without clamping to button range
            owner.previewZoomLevel = SnapZoomLevel(level);
            owner.previewZoom = Mathf.Exp(owner.previewZoomLevel);
            ApplyPreviewZoom(owner);
        }

        public static void ApplyPreviewZoom(Captue owner)
        {   // Apply current previewZoomLevel to preview camera preserving main camera as baseline
            if (owner.previewCamera == null)
                return;

            float z = Mathf.Max(Mathf.Exp(owner.previewZoomLevel), 1e-6f);

            if (owner.previewCamera.orthographic)
            {   // Scale orthographic size inversely with zoom and allow large zoom swings
                float baseSize = owner.mainCamera != null ? Mathf.Max(owner.mainCamera.orthographicSize, 1e-6f) : Mathf.Max(owner.previewCamera.orthographicSize, 1e-6f);
                owner.previewCamera.orthographicSize = Mathf.Clamp(baseSize / z, 1e-6f, 1_000_000f);
            }
            else
            {   // Use FOV; when outside [5, 120], dolly toward/away from a pivot along forward axis
                float baseFov = owner.mainCamera != null ? Mathf.Clamp(owner.mainCamera.fieldOfView, 5f, 120f) : Mathf.Clamp(owner.previewCamera.fieldOfView, 5f, 120f);
                float rawFov = baseFov / z; // unbounded target FOV before clamping

                if (rawFov >= 5f && rawFov <= 120f)
                {   // Within FOV range, no dolly needed
                    owner.previewCamera.fieldOfView = rawFov;
                    if (owner.mainCamera != null)
                        owner.previewCamera.transform.position = owner.mainCamera.transform.position;
                }
                else if (rawFov > 120f)
                {   // Zoomed out beyond FOV: fix FOV and dolly back
                    owner.previewCamera.fieldOfView = 120f;

                    if (owner.mainCamera != null)
                    {
                        var forward = owner.mainCamera.transform.forward;
                        var pivot = owner.mainCamera.transform.position + forward * owner.previewBasePivotDistance;
                        float ratio = rawFov / 120f;
                        float newDist = Mathf.Clamp(owner.previewBasePivotDistance * ratio, owner.previewBasePivotDistance, 1_000_000f);
                        owner.previewCamera.transform.position = pivot - forward * newDist;
                    }
                }
                else
                {   // rawFov < 5: Zoomed in beyond FOV: fix FOV and dolly forward toward pivot
                    owner.previewCamera.fieldOfView = 5f;

                    if (owner.mainCamera != null)
                    {
                        var forward = owner.mainCamera.transform.forward;
                        var pivot = owner.mainCamera.transform.position + forward * owner.previewBasePivotDistance;
                        float ratio = 5f / Mathf.Max(rawFov, 1e-6f);
                        float newDist = Mathf.Clamp(owner.previewBasePivotDistance / ratio, 0.001f, owner.previewBasePivotDistance);
                        owner.previewCamera.transform.position = pivot - forward * newDist;
                    }
                }
            }
        }

        #endregion

        #region Scene Visibility Management

        public static System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> ApplySceneVisibilityTemporary(Captue owner)
        {   // Temporarily modify scene visibility based on current settings
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
                bool isRocketHidden = parentRocket != null && owner.hiddenRockets.Contains(parentRocket);

                bool shouldDisable = false;

                if (!owner.showBackground && (isStars || isAtmosphere)) shouldDisable = true;
                if (!owner.showTerrain && isTerrain) shouldDisable = true;
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

        #endregion

        #region UI Setup and Management

        public static void SetupPreview(Captue owner, Container imageContainer)
        {   // Set up the preview image and render texture
            var previewGO = new GameObject("PreviewImage");
            previewGO.transform.SetParent(imageContainer.rectTransform, false);

            var rect = previewGO.AddComponent<RectTransform>();

            var (finalWidth, finalHeight) = CalculatePreviewDimensions(owner);
            rect.sizeDelta = new Vector2(finalWidth, finalHeight);

            var layout = imageContainer.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ??
                         imageContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.preferredWidth = finalWidth;
            layout.preferredHeight = finalHeight + 8f;

            owner.previewImage = previewGO.AddComponent<UnityEngine.UI.RawImage>();
            owner.previewImage.color = Color.white;
            owner.previewImage.maskable = false;
            owner.previewImage.uvRect = new Rect(0, 0, 1, 1);

            owner.lastScreenWidth = Screen.width;
            owner.lastScreenHeight = Screen.height;

            CreatePreviewRenderTexture(owner);
            if (owner.previewRT != null && !owner.previewRT.IsCreated())
                owner.previewRT.Create();

            owner.previewImage.texture = owner.previewRT;

            EnsurePreviewCamera(owner);
            owner.UpdatePreviewCulling();
        }

        public static (int width, int height) CalculatePreviewDimensions(Captue owner)
        {   // Calculate the appropriate preview dimensions
            float screenAspect = (float)Screen.width / Screen.height;
            float containerWidth = UIConstants.DEFAULT_CONTAINER_WIDTH;
            float containerHeight = UIConstants.DEFAULT_CONTAINER_HEIGHT;
            float containerAspect = containerWidth / containerHeight;

            int finalWidth, finalHeight;

            if (screenAspect > containerAspect)
            {
                finalWidth = Mathf.RoundToInt(containerWidth);
                finalHeight = Mathf.RoundToInt(containerWidth / screenAspect);
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

            owner.previewImage = null;

            // Reset state to defaults
            owner.showBackground = true;
            owner.showTerrain = true;
            owner.hiddenRockets.Clear();
        }

        public static void ShowHideWindow<T>(Captue owner, ref T windowInstance, System.Action<Captue> showAction, System.Action hideAction) where T : UIBase, new()
        {   // Generic method to show/hide a window instance
            if (windowInstance == null)
                windowInstance = new T();

            if (!windowInstance.IsOpen)
                windowInstance.Show(owner);
            else
                windowInstance.Hide();
        }

        #endregion

        #region Screenshot and Resolution Management

        public static void OnResolutionInputChange(Captue owner, string newValue)
        {   // Update target width, estimate memory/file sizes, and clamp to a safe maximum if needed
            if (!int.TryParse(newValue, out int num))
                return;

            owner.resolutionWidth = Mathf.Max(16, num);

            var (gpuNeed, cpuNeed, rawBytes) = EstimateMemoryForWidth(owner, owner.resolutionWidth);
            var (gpuBudget, cpuBudget) = GetMemoryBudgets(owner);

            if (gpuNeed > gpuBudget || cpuNeed > cpuBudget)
            {   // Requested resolution is too large; trim and inform
                int maxSafe = ComputeMaxSafeWidth(owner);
                var (ow, oh) = GetResolutionFromWidth(owner, owner.resolutionWidth);
                owner.resolutionWidth = Mathf.Min(owner.resolutionWidth, maxSafe);
                var (nw, nh) = GetResolutionFromWidth(owner, owner.resolutionWidth);

                Debug.LogWarning($"Requested {ow}x{oh} exceeds safe memory budgets. Max safe width on this device is ~{maxSafe} ({nw}x{nh}). GPU budget {FormatMB(gpuBudget)}, CPU budget {FormatMB(cpuBudget)}; needed GPU {FormatMB(gpuNeed)}, CPU {FormatMB(cpuNeed)}.");
            }
            else
            {   // Provide a quick estimate for awareness
                long approxPng = EstimatePngSizeBytes(rawBytes);
                Debug.Log($"Estimated sizes for {GetResolutionFromWidth(owner, owner.resolutionWidth).width}x{GetResolutionFromWidth(owner, owner.resolutionWidth).height}: Raw {FormatMB(rawBytes)}, GPU {FormatMB(gpuNeed)} (incl. depth), approx PNG {FormatMB(approxPng)}.");
            }

            // Update UI labels if visible
            if (owner.mainWindow != null)
                owner.mainWindow.UpdateEstimatesUI();
        }

        public static void TakeScreenshot(Captue owner)
        {   // Capture and save a screenshot at the specified resolution
            if (owner.mainCamera == null)
            {
                if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
                    owner.mainCamera = GameCamerasManager.main.world_Camera.camera;
                else
                {
                    UnityEngine.Debug.LogError("Cannot take screenshot: Camera not available");
                    return;
                }
            }

            int width = owner.resolutionWidth;
            int maxSafe = ComputeMaxSafeWidth(owner);
            if (width > maxSafe)
            {   // Prevent attempting an unsafe resolution
                var (ow, oh) = GetResolutionFromWidth(owner, width);
                width = maxSafe;
                var (sw, sh) = GetResolutionFromWidth(owner, width);
                Debug.LogWarning($"Requested {ow}x{oh} exceeds safe memory budgets. Using {sw}x{sh} as the maximum available on this device.");
            }
            int height = Mathf.RoundToInt((float)width / (float)Screen.width * (float)Screen.height);

            RenderTexture renderTexture = null;
            Texture2D screenshotTexture = null;
            int previousAntiAliasing = QualitySettings.antiAliasing;
            bool previousOrthographic = owner.mainCamera.orthographic;
            float previousOrthographicSize = owner.mainCamera.orthographicSize;
            float previousFieldOfView = owner.mainCamera.fieldOfView;
            var prevClearFlags = owner.mainCamera.clearFlags;
            var prevBgColor = owner.mainCamera.backgroundColor;
            Vector3 previousPosition = owner.mainCamera.transform.position;

            try
            {
                renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                screenshotTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

                // Apply the same zoom model as the preview (unbounded level -> factor)
                float z = Mathf.Max(Mathf.Exp(owner.previewZoomLevel), 1e-6f);

                if (owner.mainCamera.orthographic)
                {   // Orthographic: scale size inversely with zoom
                    owner.mainCamera.orthographicSize = Mathf.Clamp(previousOrthographicSize / z, 1e-6f, 1_000_000f);
                }
                else
                {   // Perspective: use FOV when in range, otherwise dolly relative to a forward pivot
                    float baseFov = Mathf.Clamp(previousFieldOfView, 5f, 120f);
                    float rawFov = baseFov / z;

                    if (rawFov >= 5f && rawFov <= 120f)
                        owner.mainCamera.fieldOfView = rawFov;  // Within FOV range
                    else if (rawFov > 120f)
                    {   // Zoomed out beyond FOV
                        owner.mainCamera.fieldOfView = 120f;

                        var fwd = owner.mainCamera.transform.forward;
                        var pivot = previousPosition + fwd * owner.previewBasePivotDistance;
                        float ratio = rawFov / 120f;
                        float newDist = Mathf.Clamp(owner.previewBasePivotDistance * ratio, owner.previewBasePivotDistance, 1_000_000f);
                        owner.mainCamera.transform.position = pivot - fwd * newDist;
                    }
                    else
                    {   // rawFov < 5f: extreme zoom-in, dolly toward pivot
                        owner.mainCamera.fieldOfView = 5f;

                        var fwd = owner.mainCamera.transform.forward;
                        var pivot = previousPosition + fwd * owner.previewBasePivotDistance;
                        float ratio = 5f / Mathf.Max(rawFov, 1e-6f);
                        float newDist = Mathf.Clamp(owner.previewBasePivotDistance / ratio, 0.001f, owner.previewBasePivotDistance);
                        owner.mainCamera.transform.position = pivot - fwd * newDist;
                    }
                }

                QualitySettings.antiAliasing = 0;

                var prevMask = owner.mainCamera.cullingMask;
                ApplyCullingForRender(owner, owner.mainCamera);

                ApplyBackgroundSettingsToCamera(owner, owner.mainCamera);

                var modified = ApplySceneVisibilityTemporary(owner);

                owner.mainCamera.targetTexture = renderTexture;
                owner.mainCamera.Render();

                RestoreSceneVisibility(modified);
                owner.mainCamera.cullingMask = prevMask;
                owner.mainCamera.clearFlags = prevClearFlags;
                owner.mainCamera.backgroundColor = prevBgColor;

                RenderTexture.active = renderTexture;

                screenshotTexture.ReadPixels(new Rect(0f, 0f, (float)width, (float)height), 0, 0);
                screenshotTexture.Apply();

                byte[] pngBytes = screenshotTexture.EncodeToPNG();

                string worldName = (SFS.Base.worldBase?.paths?.worldName) ?? "Unknown";
                string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                                      new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                var worldFolder = FileUtilities.InsertIo(sanitizedName, Main.ScreenCaptureFolder);

                string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";

                using (var ms = new MemoryStream(pngBytes))
                    FileUtilities.InsertIo(fileName, ms, worldFolder);

                // Log final sizes for user feedback
                var (gpuNeed, cpuNeed, rawBytes) = EstimateMemoryForWidth(owner, width);
                Debug.Log($"Saved {width}x{height}. Approx memory: GPU {FormatMB(gpuNeed)} (incl. depth), CPU {FormatMB(cpuNeed)}; file size {FormatMB(pngBytes.LongLength)} (est PNG {FormatMB(EstimatePngSizeBytes(rawBytes))}).");
            }

            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Screenshot capture failed: {ex.Message}\n{ex.StackTrace}");
            }

            finally
            {
                owner.mainCamera.targetTexture = null;
                RenderTexture.active = null;

                // Restore camera state
                owner.mainCamera.orthographic = previousOrthographic;
                owner.mainCamera.orthographicSize = previousOrthographicSize;
                owner.mainCamera.fieldOfView = previousFieldOfView;
                owner.mainCamera.transform.position = previousPosition;
                QualitySettings.antiAliasing = previousAntiAliasing;

                if (renderTexture != null)
                    UnityEngine.Object.Destroy(renderTexture);

                if (screenshotTexture != null)
                    UnityEngine.Object.Destroy(screenshotTexture);
            }
        }

        #endregion
    }

    // Extension methods for the Captue class
    public static class CaptueExtensions
    {
        public static (int width, int height) GetResolutionFromWidthPublic(this Captue owner, int width) =>
            CaptureUtilities.GetResolutionFromWidth(owner, width);

        public static (long gpuBytes, long cpuBytes, long rawBytes) EstimateMemoryForWidthPublic(this Captue owner, int width) =>
            CaptureUtilities.EstimateMemoryForWidth(owner, width);

        public static (long gpuBudget, long cpuBudget) GetMemoryBudgetsPublic(this Captue owner) =>
            CaptureUtilities.GetMemoryBudgets(owner);

        public static int GetMaxSafeWidth(this Captue owner) =>
            CaptureUtilities.ComputeMaxSafeWidth(owner);

        public static void ApplyBackgroundSettingsToCamera(this Captue owner, Camera cam) =>
            CaptureUtilities.ApplyBackgroundSettingsToCamera(owner, cam);

        public static int ComputeCullingMask(this Captue owner) =>
            CaptureUtilities.ComputeCullingMask(owner);

        public static void ApplyPreviewZoom(this Captue owner) =>
            CaptureUtilities.ApplyPreviewZoom(owner);
    }
}
