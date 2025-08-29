using System;
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

        // Additional caching for expensive calculations
        private static (long gpuBudget, long cpuBudget) budgetCache = (-1, -1);
        private static int maxSafeWidthCache = -1;
        private static int lastScreenWForMaxWidth = -1, lastScreenHForMaxWidth = -1;

        public static void InvalidateMemoryCache() => memoryCache = (-1, 0, 0, 0);
        public static void InvalidateCropCache() => dimensionsCache = (-1, -1, -1, -1);
        public static void InvalidateMaxWidthCache() => maxSafeWidthCache = -1;

        public static void InvalidateScreenCache()
        {
            InvalidateMemoryCache();
            InvalidateCropCache();
            InvalidateMaxWidthCache();
        }

        public static (long gpu, long cpu, long raw) GetCachedMemoryEstimate(int width)
        {
            if (memoryCache.width == width)
                return (memoryCache.gpu, memoryCache.cpu, memoryCache.raw);

            var result = CaptureUtilities.EstimateMemoryForWidthUncached(width);
            memoryCache = (width, result.gpuBytes, result.cpuBytes, result.rawBytes);
            return result;
        }

        public static (long gpuBudget, long cpuBudget) GetCachedMemoryBudgets()
        {
            if (budgetCache.gpuBudget != -1)
                return budgetCache;

            budgetCache = CaptureUtilities.GetMemoryBudgetsUncached();
            return budgetCache;
        }

        public static int GetCachedMaxSafeWidth()
        {
            if (maxSafeWidthCache != -1 && lastScreenWForMaxWidth == Screen.width && lastScreenHForMaxWidth == Screen.height)
                return maxSafeWidthCache;

            maxSafeWidthCache = CaptureUtilities.ComputeMaxSafeWidthUncached();
            lastScreenWForMaxWidth = Screen.width;
            lastScreenHForMaxWidth = Screen.height;
            return maxSafeWidthCache;
        }

        public static (int cropW, int cropH) GetCachedCroppedDimensions(int origW, int origH)
        {
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

        public static FolderPath CreateWorldFolder(string worldName)
        {
            string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                                  new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
            return InsertIo(sanitizedName, Main.ScreenCaptureFolder);
        }

        public static void OpenCurrentWorldCapturesFolder()
        {
            try
            {
                string worldName = GetWorldName();
                string sanitized = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                                   new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                var folder = InsertIo(sanitized, Main.ScreenCaptureFolder);
                string url = "file:///" + folder.ToString().Replace('\\', '/');
                Application.OpenURL(url);
            }
            catch { }
        }

        public static FolderPath InsertIntoSfS(string relativePath, FolderPath baseFolder, byte[] fileBytes = null, Stream inputStream = null)
        {
            if (inputStream != null && !inputStream.CanRead)
                throw new ArgumentException("inputStream must be readable.", nameof(inputStream));

            var baseFull = baseFolder.ToString();
            if (!Directory.Exists(baseFull)) Directory.CreateDirectory(baseFull);

            var combinedFull = Path.Combine(baseFull, relativePath);
            var isFile = fileBytes != null || inputStream != null;

            if (!isFile)
            {
                if (!Directory.Exists(combinedFull)) Directory.CreateDirectory(combinedFull);
                return new FolderPath(combinedFull);
            }

            var destinationDir = Path.GetDirectoryName(combinedFull) ?? baseFull;
            if (!Directory.Exists(destinationDir)) Directory.CreateDirectory(destinationDir);

            using (var output = new FileStream(combinedFull, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            {
                if (fileBytes != null) output.Write(fileBytes, 0, fileBytes.Length);
                else
                {
                    if (inputStream.CanSeek) inputStream.Position = 0;
                    inputStream.CopyTo(output, 65536);
                }
                output.Flush(true);
            }
            return new FolderPath(destinationDir);
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
        private static readonly System.Collections.Generic.Dictionary<string, (int width, int height)> elementSizeCache =
            new System.Collections.Generic.Dictionary<string, (int, int)>(64); // Preallocate capacity

        // Object pool for performance-critical operations
        private static readonly System.Collections.Queue rendererListPool = new System.Collections.Queue();
        private static readonly System.Collections.Queue gameObjectListPool = new System.Collections.Queue();

        private static System.Collections.Generic.List<(Renderer, bool)> GetPooledRendererList()
        {
            if (rendererListPool.Count > 0)
            {
                var list = (System.Collections.Generic.List<(Renderer, bool)>)rendererListPool.Dequeue();
                list.Clear();
                return list;
            }
            return new System.Collections.Generic.List<(Renderer, bool)>(64);
        }

        private static void ReturnPooledRendererList(System.Collections.Generic.List<(Renderer, bool)> list)
        {
            if (list != null && rendererListPool.Count < 4) // Limit pool size
                rendererListPool.Enqueue(list);
        }

        // private static System.Collections.Generic.List<(GameObject, bool)> GetPooledGameObjectList()
        // {
        //     if (gameObjectListPool.Count > 0)
        //     {
        //         var list = (System.Collections.Generic.List<(GameObject, bool)>)gameObjectListPool.Dequeue();
        //         list.Clear();
        //         return list;
        //     }
        //     return new System.Collections.Generic.List<(GameObject, bool)>(32);
        // }

        public static bool PreviewInteriorVisible
        {
            get
            {
                try
                {
                    var im = SFS.InteriorManager.main;
                    if (im != null)
                        return im.interiorView.Value;
                }
                catch { }
                return true; // Default to visible if InteriorManager not available
            }
            set
            {
                try
                {
                    var im = SFS.InteriorManager.main;
                    if (im != null)
                        im.interiorView.Value = value;
                }
                catch { }
            }
        }

        private static bool IsInteriorElement(GameObject go)
        {   // Heuristic: detect interior objects by name or by any component type containing 'Interior' to avoid hard references
            if (go == null) return false;
            try
            {
                if (go.name.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var comps = go.GetComponentsInParent<Component>(true);
                for (int i = 0; i < comps.Length; i++)
                {
                    var tname = comps[i]?.GetType()?.Name;
                    if (!string.IsNullOrEmpty(tname) && tname.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static int cachedInteriorLayerMask = -1;

        private static int GetCachedInteriorLayerMask()
        {   // Detect any layers with names containing 'interior' (case-insensitive) and cache bitmask
            if (cachedInteriorLayerMask != -1)
                return cachedInteriorLayerMask;

            cachedInteriorLayerMask = 0;
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    string lname = LayerMask.LayerToName(i) ?? string.Empty;
                    if (lname.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cachedInteriorLayerMask |= (1 << i);
                        Debug.Log($"Found interior layer '{lname}' at index {i}");
                    }
                }

                if (cachedInteriorLayerMask == 0)
                    Debug.Log("No interior layers found by name. Checking for 'Interior' layer specifically...");

                // Try specific interior layer names that might be used
                string[] interiorLayerNames = { "Interior", "Interiors", "interior", "interiors" };
                foreach (var layerName in interiorLayerNames)
                {
                    int layerIndex = LayerMask.NameToLayer(layerName);
                    if (layerIndex >= 0 && layerIndex < 32)
                    {
                        cachedInteriorLayerMask |= (1 << layerIndex);
                        Debug.Log($"Found interior layer '{layerName}' at index {layerIndex}");
                    }
                }

                Debug.Log($"Interior layer mask: {cachedInteriorLayerMask} (binary: {Convert.ToString(cachedInteriorLayerMask, 2)})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error detecting interior layers: {ex.Message}");
                cachedInteriorLayerMask = 0;
            }

            return cachedInteriorLayerMask;
        }

        public static (int width, int height) GetResolutionFromWidth(int width)
        {
            string cacheKey = $"{width}_{Screen.width}_{Screen.height}";
            if (elementSizeCache.TryGetValue(cacheKey, out var cached))
                return cached;

            width = Mathf.Max(16, width);
            int height = Mathf.RoundToInt((float)width / Mathf.Max(1, (float)Screen.width) * (float)Screen.height);
            var result = (width, Mathf.Max(16, height));

            elementSizeCache[cacheKey] = result;
            return result;
        }

        public static void CreateCompactToggle(Container parent, string label, System.Func<bool> getter, System.Action action)
        {   // Create a small toggle control that invokes a shared action and refreshes the preview
            // Use a compact fixed width so two toggles fit side-by-side
            int width = 200;
            int height = 39;

            var toggle = Builder.CreateToggleWithLabel(parent, width, height, getter, () =>
            {
                try
                {
                    action?.Invoke();
                    Main.World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Toggle '{label}' action failed: {ex.Message}");
                }
            }, 0, 0, label);

            // Ensure the toggle visual size is reduced to half for compact layout
            try
            {
                float sizeFactor = 0.85f;
                if (toggle?.toggle != null)
                {   // Adjust RectTransform size if available
                    var rt = toggle?.toggle.rectTransform;
                    if (rt != null)
                        rt.localScale = new Vector3(sizeFactor, sizeFactor, 0f);
                    if (toggle?.label != null)
                    {
                        var lrt = toggle?.label.rectTransform;
                        if (lrt != null)
                            lrt.sizeDelta = new Vector2(lrt.sizeDelta.x * sizeFactor, lrt.sizeDelta.y);
                        lrt.anchoredPosition = new Vector2(lrt.anchoredPosition.x * sizeFactor, lrt.anchoredPosition.y);
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"Failed to resize toggle: {ex.Message}"); }
        }

        public static void CreateCropControls(Container parent, System.Action onCropChange)
        {
            Builder.CreateLabel(parent, 390, 36, 0, 0, "Crop");

            CreateNestedHorizontal(parent, 10f, null, TextAnchor.UpperRight, row1 =>
            {
                CreateCropInput(row1, () => Main.CropLeft, val => { Main.CropLeft = val; onCropChange?.Invoke(); });
                CreateCropInput(row1, () => Main.CropTop, val => { Main.CropTop = val; onCropChange?.Invoke(); });
            });

            CreateNestedHorizontal(parent, 10f, null, TextAnchor.UpperRight, row2 =>
            {
                CreateCropInput(row2, () => Main.CropBottom, val => { Main.CropBottom = val; onCropChange?.Invoke(); });
                CreateCropInput(row2, () => Main.CropRight, val => { Main.CropRight = val; onCropChange?.Invoke(); });
            });
        }

        private static void CreateCropInput(Container parent, System.Func<float> getValue, System.Action<float> setValue)
        {
            Builder.CreateTextInput(parent, 200, 42, 0, 0, Mathf.Clamp(getValue(), 0f, 100f).ToString("0"), val =>
            {
                if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float crop))
                {
                    float clampedCrop = Mathf.Clamp(crop, 0f, 100f);
                    setValue(clampedCrop);
                    CacheManager.InvalidateCropCache();
                    UpdatePreviewCropping();
                }
            });
        }

        public static void CreateTimeControls(Container parent)
        {
            CreateNestedHorizontal(parent, 5f, null, TextAnchor.MiddleCenter, timeControlRow =>
            {
                CreateTimeButton(timeControlRow, "||", () => Time.timeScale == 0 ? 1f : 0f, CaptureTime.SaveCurrentFrame);
                CreateTimeButton(timeControlRow, "<<", null, CaptureTime.StepBackwardInTime);
                CreateTimeButton(timeControlRow, ">>", null, CaptureTime.StepForwardAndPause);
            });
        }

        private static void CreateTimeButton(Container parent, string text, System.Func<float> getTimeScale, System.Action action)
        {
            Builder.CreateButton(parent, 80, 58, 0, 0, () =>
            {
                try
                {
                    if (getTimeScale != null)
                        Time.timeScale = getTimeScale();
                    action?.Invoke();
                }
                catch (System.Exception ex) { Debug.LogError($"Time control error: {ex.Message}"); }
            }, text);
        }

        public static void ToggleInteriorView()
        {
            try
            {
                if (SFS.InteriorManager.main != null)
                {
                    SFS.InteriorManager.main.ToggleInteriorView();
                    Debug.Log($"Global interior visibility toggled to: {SFS.InteriorManager.main.interiorView.Value}");
                }
                else
                {
                    Debug.LogWarning("InteriorManager.main is null - cannot toggle interior view");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to toggle interior visibility: {ex.Message}");
            }
        }


        public static Container CreateNestedHorizontal(Container parent, float spacing, RectOffset padding, TextAnchor alignment, UIBase.ContainerContentDelegate contentCreator)
        {
            var container = Builder.CreateContainer(parent, 0, 0);
            container.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, alignment, spacing, padding, true);
            contentCreator?.Invoke(container);
            return container;
        }

        public static (long gpuBytes, long cpuBytes, long rawBytes) EstimateMemoryForWidth(int width) =>
            CacheManager.GetCachedMemoryEstimate(width);

        public static (long gpuBytes, long cpuBytes, long rawBytes) EstimateMemoryForWidthUncached(int width)
        {
            var (w, h) = GetResolutionFromWidth(width);
            long pixels = (long)w * h;
            long gpu = (long)Math.Ceiling(pixels * (MemoryConstants.GPU_COLOR_BPP + MemoryConstants.GPU_DEPTH_BPP) * MemoryConstants.SafetyMultiplier);
            long cpu = (long)Math.Ceiling(pixels * MemoryConstants.CPU_BPP * MemoryConstants.SafetyMultiplier);
            long raw = pixels * MemoryConstants.CPU_BPP;
            return (gpu, cpu, raw);
        }

        public static (long gpuBudget, long cpuBudget) GetMemoryBudgets() =>
            CacheManager.GetCachedMemoryBudgets();

        public static (long gpuBudget, long cpuBudget) GetMemoryBudgetsUncached()
        {
            long gpu = (long)(SystemInfo.graphicsMemorySize * 1024L * 1024L * MemoryConstants.GPU_BUDGET_FRACTION);
            long cpu = (long)(SystemInfo.systemMemorySize * 1024L * 1024L * MemoryConstants.CPU_BUDGET_FRACTION);
            return (gpu, cpu);
        }

        public static int ComputeMaxSafeWidth() =>
            CacheManager.GetCachedMaxSafeWidth();

        public static int ComputeMaxSafeWidthUncached()
        {
            float aspect = (float)Screen.height / Mathf.Max(1, Screen.width);
            var (gpuBudget, cpuBudget) = GetMemoryBudgetsUncached();

            double perPixelGPU = (MemoryConstants.GPU_COLOR_BPP + MemoryConstants.GPU_DEPTH_BPP) * MemoryConstants.SafetyMultiplier;
            double perPixelCPU = MemoryConstants.CPU_BPP * MemoryConstants.SafetyMultiplier;

            double coef = aspect;
            double maxWgpu = Math.Sqrt(gpuBudget / Math.Max(1e-6, (perPixelGPU * coef)));
            double maxWcpu = Math.Sqrt(cpuBudget / Math.Max(1e-6, (perPixelCPU * coef)));

            int texLimit = SystemInfo.maxTextureSize > 0 ? SystemInfo.maxTextureSize : int.MaxValue;
            return Mathf.Clamp(Mathf.FloorToInt((float)Math.Min(maxWgpu, maxWcpu)), 16, texLimit);
        }

        public static string FormatMB(long bytes) =>
            $"{bytes / (1024.0 * 1024.0):0.#} MB";

        public static long EstimatePngSizeBytes(long rawBytes) =>
            (long)Math.Max(1024, rawBytes * 0.30);

        public static RenderTexture CreatePreviewRenderTexture(int previewWidth, int antiAliasing = 1)
        {
            // Match the RenderTexture to the dynamic preview container size to avoid stretching
            var (finalWidth, finalHeight) = CalculatePreviewDimensions();
            int rtWidth = Mathf.Max(1, finalWidth);
            int rtHeight = Mathf.Max(1, finalHeight);

            var rt = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = Mathf.Clamp(antiAliasing, 1, 8),
                filterMode = antiAliasing > 1 ? FilterMode.Trilinear : FilterMode.Bilinear
            };

            if (!rt.IsCreated())
                rt.Create();
            return rt;
        }

        public static int CalculateTargetRTWidth(int previewWidth) =>
            previewWidth; // Width is directly specified, height is calculated from aspect

        public static Camera SetupPreviewCamera(Camera mainCamera, RenderTexture targetRT, Camera existingPreviewCamera = null)
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
            {
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
        {
            int mask = World.MainCamera?.cullingMask ?? ~0;

            if (!showBackground)
            {
                int stars = LayerMask.GetMask("Stars");
                if (stars != 0)
                    mask &= ~stars;
            }

            // Handle interior visibility through culling mask
            if (!PreviewInteriorVisible)
            {
                int interiorMask = GetCachedInteriorLayerMask();
                if (interiorMask != 0)
                    mask &= ~interiorMask;
            }

            return mask;
        }


        public static void ApplyPreviewZoom(Camera mainCamera, Camera previewCamera, float zoomLevel)
        {
            if (previewCamera == null)
                return;

            float z = Mathf.Max(Mathf.Exp(zoomLevel), 1e-6f);

            if (previewCamera.orthographic)
            {
                float baseSize = mainCamera?.orthographicSize ?? previewCamera.orthographicSize;
                previewCamera.orthographicSize = Mathf.Clamp(Mathf.Max(baseSize, 1e-6f) / z, 1e-6f, 1_000_000f);
            }
            else
            {
                float baseFov = mainCamera?.fieldOfView ?? previewCamera.fieldOfView;
                previewCamera.fieldOfView = Mathf.Clamp(Mathf.Clamp(baseFov, 5f, 120f) / z, 5f, 120f);
                if (mainCamera != null)
                    previewCamera.transform.position = mainCamera.transform.position;
            }
        }

        public static System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> ApplySceneVisibilityTemporary(bool showBackground, bool showTerrain, System.Collections.Generic.HashSet<Rocket> hiddenRockets)
        {
            var changed = GetPooledRendererList();
            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(includeInactive: true);

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r?.gameObject == null || r.GetComponentInParent<RectTransform>() != null)
                    continue;

                var go = r.gameObject;
                string layerName = LayerMask.LayerToName(go.layer) ?? string.Empty;

                bool shouldDisable = (!showBackground && IsBackgroundElement(go, layerName)) ||
                                   (!showTerrain && IsTerrainElement(go)) ||
                                   (hiddenRockets?.Contains(go.GetComponentInParent<Rocket>()) == true);

                if (shouldDisable && r.enabled)
                {
                    changed.Add((r, r.enabled));
                    r.enabled = false;
                }
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
        {
            if (changed == null)
                return;

            for (int i = 0; i < changed.Count; i++)
            {
                try
                {
                    if (changed[i].renderer != null)
                        changed[i].renderer.enabled = changed[i].previousEnabled;
                }
                catch { }
            }

            // Return the list to the pool for reuse
            ReturnPooledRendererList(changed);
        }

        public static void SetupPreview(Container imageContainer)
        {
            var previewGO = new GameObject("PreviewImage");
            previewGO.transform.SetParent(imageContainer.rectTransform, false);

            var rect = previewGO.AddComponent<RectTransform>();
            var (finalWidth, finalHeight) = CalculatePreviewDimensions();
            rect.sizeDelta = new Vector2(finalWidth, finalHeight);

            SetupContainerLayout(imageContainer, finalWidth, finalHeight);

            Captue.PreviewImage = previewGO.AddComponent<UnityEngine.UI.RawImage>();
            Captue.PreviewImage.color = Color.white;
            Captue.PreviewImage.maskable = false;
            Captue.PreviewImage.uvRect = new Rect(0, 0, 1, 1);

            InitializeRenderTexture();
            Captue.PreviewImage.texture = Captue.PreviewRT;

            World.PreviewCamera = SetupPreviewCamera(World.MainCamera, Captue.PreviewRT, World.PreviewCamera);

            // Initial render: schedule via adaptive system
            World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
        }

        private static void SetupContainerLayout(Container imageContainer, float width, float height)
        {
            var layout = imageContainer.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ??
                         imageContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minWidth = layout.preferredWidth = 520f;
            layout.flexibleWidth = 0f;
            layout.preferredHeight = height;
        }

        private static void InitializeRenderTexture()
        {
            Main.LastScreenWidth = Screen.width;
            Main.LastScreenHeight = Screen.height;

            if (Captue.PreviewRT != null)
            {
                Captue.PreviewRT.Release();
                UnityEngine.Object.Destroy(Captue.PreviewRT);
            }

            // Create RT sized to current preview container dimensions
            Captue.PreviewRT = CreatePreviewRenderTexture(Main.PreviewWidth);
            if (!Captue.PreviewRT.IsCreated())
                Captue.PreviewRT.Create();
        }

        private static void UpdatePreviewSettings()
        {   // Removed automatic culling update - now handled by manual requests only
            // This prevents conflicts with the main preview rendering system
        }

        public static (int width, int height) CalculatePreviewDimensions()
        {
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            const float containerWidth = 520f;
            const float containerHeight = 430f;
            float containerAspect = containerWidth / containerHeight;

            return screenAspect > containerAspect ?
                (Mathf.RoundToInt(containerWidth), Mathf.RoundToInt(containerWidth / Mathf.Max(1e-6f, screenAspect))) :
                (Mathf.RoundToInt(containerHeight * screenAspect), Mathf.RoundToInt(containerHeight));
        }

        public static void CleanupUI(Captue owner)
        {
            if (owner == null) return;

            if (Captue.PreviewRT != null)
            {
                Captue.PreviewRT.Release(); UnityEngine.Object.Destroy(Captue.PreviewRT); Captue.PreviewRT = null;
            }

            if (World.PreviewCamera != null)
            {
                UnityEngine.Object.Destroy(World.PreviewCamera.gameObject); World.PreviewCamera = null;
            }

            Captue.PreviewImage = null;
            owner.showBackground = owner.showTerrain = true;
            Main.HiddenRockets.Clear();
        }

        public static void ShowHideWindow<T>(ref T windowInstance, System.Action showAction, System.Action hideAction) where T : UIBase, new()
        {
            if (windowInstance == null) windowInstance = new T();

            if (!windowInstance.IsOpen) windowInstance.Show(); else windowInstance.Hide();
        }

        public static bool IsRocketVisible(Rocket rocket) =>
            rocket != null && !Main.HiddenRockets.Contains(rocket);

        public static void SetRocketVisible(Rocket rocket, bool visible)
        {
            if (rocket == null) return;

            if (visible) Main.HiddenRockets.Remove(rocket); else Main.HiddenRockets.Add(rocket);
        }

        public static void SetAllRocketsVisible(bool visible)
        {
            Main.HiddenRockets.Clear();
            if (!visible)
            {
                var rockets = UnityEngine.Object.FindObjectsOfType<Rocket>(includeInactive: true);
                for (int i = 0; i < rockets.Length; i++) Main.HiddenRockets.Add(rockets[i]);
            }
        }

        public static void UpdatePreviewCulling()
        {
            // Deprecated - preview updates now handled automatically by adaptive system
        }

        public static void UpdatePreviewCropping()
        {   // Apply crop via UVs and resize from current RT + crop to avoid stretching, then schedule update
            if (World.PreviewCamera == null || Captue.PreviewImage == null)
                return;

            Main.LastScreenWidth = Screen.width;
            Main.LastScreenHeight = Screen.height;

            var (left, top, right, bottom) = GetNormalizedCropValues();

            // Keep camera rect full; crop via UVs only
            World.PreviewCamera.rect = new Rect(0, 0, 1, 1);
            Captue.PreviewImage.uvRect = new Rect(left, bottom, 1f - left - right, 1f - top - bottom);

            // Resize RawImage using current RT size and crop to avoid double-aspect application
            UpdatePreviewImageLayoutForCurrentRT();

            // Schedule adaptive update rather than forcing a render
            World.OwnerInstance?.SchedulePreviewUpdate(immediate: true);
        }

        public static void UpdatePreviewImageLayoutForCurrentRT()
        {   // Update RawImage size using current RT and crop to avoid stretching
            if (Captue.PreviewImage == null)
                return;

            int rtW = (Captue.PreviewRT != null && Captue.PreviewRT.IsCreated()) ? Captue.PreviewRT.width : CalculatePreviewDimensions().width;
            int rtH = (Captue.PreviewRT != null && Captue.PreviewRT.IsCreated()) ? Captue.PreviewRT.height : CalculatePreviewDimensions().height;

            var (left, top, right, bottom) = GetNormalizedCropValues();
            int cropW = Mathf.Max(1, Mathf.RoundToInt(rtW * (1f - left - right)));
            int cropH = Mathf.Max(1, Mathf.RoundToInt(rtH * (1f - top - bottom)));

            UpdatePreviewImageSize(cropW, cropH);
        }

        public static (float left, float top, float right, float bottom) GetNormalizedCropValues()
        {
            float left = Mathf.Clamp01(Main.CropLeft / 100f);
            float top = Mathf.Clamp01(Main.CropTop / 100f);
            float right = Mathf.Clamp01(Main.CropRight / 100f);
            float bottom = Mathf.Clamp01(Main.CropBottom / 100f);

            float totalH = left + right;
            float totalV = top + bottom;

            if (totalH >= 1f)
            {
                float s = 0.99f / totalH;
                left *= s;
                right *= s;
            }

            if (totalV >= 1f)
            {
                float s = 0.99f / totalV;
                top *= s;
                bottom *= s;
            }

            return (left, top, right, bottom);
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
        {
            var parent = Captue.PreviewImage.transform.parent as RectTransform;
            if (parent != null)
            {
                var parentLayout = parent.GetComponent<LayoutElement>();
                if (parentLayout != null)
                {
                    parentLayout.preferredWidth = 520f;
                    parentLayout.preferredHeight = height;
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
                }
            }
        }

        private static void RefreshRenderTextureIfNeeded(int width, int height)
        {
            if (Captue.PreviewRT == null || !Captue.PreviewRT.IsCreated() ||
                Captue.PreviewRT.width != width || Captue.PreviewRT.height != height)
            {
                if (Captue.PreviewRT != null)
                {
                    Captue.PreviewRT.Release();
                    UnityEngine.Object.Destroy(Captue.PreviewRT);
                }

                Captue.PreviewRT = CreatePreviewRenderTexture(Main.PreviewWidth);
                if (Captue.PreviewImage != null)
                    Captue.PreviewImage.texture = Captue.PreviewRT;
                if (World.PreviewCamera != null)
                    World.PreviewCamera.targetTexture = Captue.PreviewRT;
            }
        }

        public static void ApplyCroppingToCamera(Camera camera)
        {
            if (camera == null)
                return;

            var (left, top, right, bottom) = GetNormalizedCropValues();

            camera.rect = new Rect(left, bottom, 1f - left - right, 1f - top - bottom);
        }

        public static (int width, int height) GetCroppedResolution(int originalWidth, int originalHeight) =>
            CacheManager.GetCachedCroppedDimensions(originalWidth, originalHeight);

        public static (int width, int height) GetCroppedResolutionUncached(int originalWidth, int originalHeight)
        {
            var (left, top, right, bottom) = GetNormalizedCropValues();

            int croppedWidth = Mathf.RoundToInt(originalWidth * (1f - left - right));
            int croppedHeight = Mathf.RoundToInt(originalHeight * (1f - top - bottom));

            return (Mathf.Max(1, croppedWidth), Mathf.Max(1, croppedHeight));
        }

        public static Rect GetCroppedReadRect(int renderWidth, int renderHeight)
        {
            var (left, top, right, bottom) = GetNormalizedCropValues();

            int leftPixels = Mathf.RoundToInt(left * renderWidth);
            int rightPixels = Mathf.RoundToInt(right * renderWidth);
            int topPixels = Mathf.RoundToInt(top * renderHeight);
            int bottomPixels = Mathf.RoundToInt(bottom * renderHeight);

            int croppedWidth = Mathf.Max(1, renderWidth - leftPixels - rightPixels);
            int croppedHeight = Mathf.Max(1, renderHeight - topPixels - bottomPixels);

            return new Rect(leftPixels, bottomPixels, croppedWidth, croppedHeight);
        }

        public static bool CheckForSignificantChanges(ref Vector3 lastCameraPosition, ref Quaternion lastCameraRotation, float lastPreviewUpdate,
                                                      float velocityThresholdSq, float rotationVelocityThreshold, float positionDeltaThresholdSq, float rotationDeltaThreshold,
                                                      float movingInterval, float staticInterval, out CameraActivity activity)
        {   // Compute camera activity state and decide if a preview update is due
            activity = CameraActivity.Static;

            if (World.MainCamera?.transform == null || World.PreviewCamera == null)
                return false;

            var currentTransform = World.MainCamera.transform;
            float currentTime = Time.unscaledTime;
            float timeSinceLastUpdate = currentTime - lastPreviewUpdate;
            float deltaTime = Mathf.Max(timeSinceLastUpdate, 0.001f);

            Vector3 positionDelta = currentTransform.position - lastCameraPosition;
            float positionDeltaSq = positionDelta.sqrMagnitude;
            float rotationDelta = Quaternion.Angle(currentTransform.rotation, lastCameraRotation);

            float positionVelocitySq = positionDeltaSq / (deltaTime * deltaTime);
            float rotationVelocity = rotationDelta / deltaTime;

            bool isMoving = positionVelocitySq > velocityThresholdSq ||
                            rotationVelocity > rotationVelocityThreshold ||
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
    }

    public static class PreviewUtilities
    {
        public static RawImage FitPreviewImageToBox(RawImage previewImage, RenderTexture PreviewRT, float scaleFactor = 1.0f)
        {
            if (previewImage == null)
                return null;

            var img = previewImage.rectTransform;
            var parent = img?.parent as RectTransform;
            if (img == null || parent == null)
                return null;

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
            {
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

            // Apply UV cropping
            var uvLeft = left;
            var uvBottom = bottom;
            var uvWidth = 1f - left - right;
            var uvHeight = 1f - top - bottom;

            try { previewImage.uvRect = new Rect(uvLeft, uvBottom, uvWidth, uvHeight); } catch { }

            // Ensure image is properly integrated in UI hierarchy
            previewImage.raycastTarget = false;
            previewImage.material = null;
            previewImage.maskable = true;

            // Force layout update
            if (parent != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(parent);

            return previewImage;
        }

        // RT pool for instant switching without allocation lag
        private static RenderTexture[] rtPool = new RenderTexture[2];
        private static int activeRTIndex = -1;

        public static bool CleanupRTPool()
        {   // Release and destroy all pooled render textures and return success status
            bool anyDestroyed = false;

            for (int i = 0; i < rtPool.Length; i++)
            {
                if (rtPool[i] != null)
                {
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
        {   // Clear RT pool to force recreation with new dimensions and return count invalidated
            int invalidatedCount = 0;

            for (int i = 0; i < rtPool.Length; i++)
            {
                if (rtPool[i] != null)
                {
                    rtPool[i].Release();
                    UnityEngine.Object.Destroy(rtPool[i]);
                    rtPool[i] = null;
                    invalidatedCount++;
                }
            }
            activeRTIndex = -1;
            return invalidatedCount;
        }

        public static (RenderTexture renderTexture, bool wasCreated, int poolIndex) SwitchToPooledRT(int poolIndex, int width, int height, FilterMode filterMode, int antiAliasing)
        {   // Switch to pooled RT or create new one if needed and return RT data
            poolIndex = Mathf.Clamp(poolIndex, 0, rtPool.Length - 1);
            bool wasCreated = false;

            if (activeRTIndex == poolIndex && rtPool[poolIndex] != null &&
                rtPool[poolIndex].width == width && rtPool[poolIndex].height == height)
                return (rtPool[poolIndex], false, poolIndex); // Already using correct RT

            // Create new RT if needed
            if (rtPool[poolIndex] == null || rtPool[poolIndex].width != width || rtPool[poolIndex].height != height)
            {
                if (rtPool[poolIndex] != null)
                {
                    rtPool[poolIndex].Release();
                    UnityEngine.Object.Destroy(rtPool[poolIndex]);
                }

                rtPool[poolIndex] = new RenderTexture(width, height, 24)
                {
                    filterMode = filterMode,
                    antiAliasing = antiAliasing
                };
                rtPool[poolIndex].Create();
                wasCreated = true;
            }

            activeRTIndex = poolIndex;
            return (rtPool[poolIndex], wasCreated, poolIndex);
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
                Debug.Log($"Saved frame {currentFrameIndex + 1}/{frameHistory.Count}");
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

}