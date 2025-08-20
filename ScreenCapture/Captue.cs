using System;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using ModLoader;
using SFS;
using SFS.IO;
using SFS.UI.ModGUI;
using SFS.World;
using SFS.WorldBase;
using UITools;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static ScreenCapture.FileHelper;
using static UITools.UIToolsBuilder;

namespace ScreenCapture
{
    public class Captue : MonoBehaviour
    {
        internal Camera mainCamera;
        internal Camera previewCamera; // Dedicated camera for preview rendering
        internal int resolutionWidth = 1980;
        internal int previewWidth = 384;
        internal float zoomFactor = 1f;
        internal ClosableWindow closableWindow;
        internal GameObject uiHolder;
        internal RawImage previewImage;
        internal RenderTexture previewRT;
        internal bool showBackground = true;
        internal bool showTerrain = true;
        internal int lastScreenWidth;
        internal int lastScreenHeight;

        // Background customization UI/state
        internal Window bgWindow;
        internal float bgR = 0f, bgG = 0f, bgB = 0f;  // Background color (0-255 via UI, stored as 0-1 normalized)
        internal bool bgTransparent = true;          // When true, force alpha 0

        internal Action windowOpenedAction;
        internal Action windowCollapsedAction;
        internal bool wasMinimized;

        private void Awake()
        {   // camera and default callbacks
            if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
                this.mainCamera = GameCamerasManager.main.world_Camera.camera;

            windowOpenedAction = () =>  // Freeze time when window is opened
            {
                if (WorldTime.main != null)
                    WorldTime.main.SetState(0.0, true, false);
            };

            windowCollapsedAction = () =>  // Restore realtime when window is collapsed
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
        {   // Delegate UI creation to CaptureUI using options struct
            var options = new CaptureUIOptions
            {
                Owner = this,
                GetShowBackground = () => showBackground,
                ToggleBackground = () => showBackground = !showBackground,
                GetShowTerrain = () => showTerrain,
                ToggleTerrain = () => showTerrain = !showTerrain,
                CaptureAction = (Action)TakeScreenshot,
                ResolutionInputAction = new UnityEngine.Events.UnityAction<string>(OnResolutionInputChange),
                OnWindowOpened = windowOpenedAction,
                OnWindowCollapsed = windowCollapsedAction,
                UpdatePreviewCulling = UpdatePreviewCulling,
                UpdateBackgroundWindowVisibility = UpdateBackgroundWindowVisibility,
                SetupPreview = SetupPreview
            };
            CaptureUI.ShowUI(options);
        }

        public void HideUI()
        {   // Delegate destruction to CaptureUI
            CaptureUI.HideUI(this);
        }

        private void CreatePreviewRenderTexture()
        {   // Create render texture with exact screen aspect ratio to prevent cropping
            if (previewRT != null)
            {
                previewRT.Release();
                UnityEngine.Object.Destroy(previewRT);
            }

            // Always use exact screen aspect ratio for render texture
            float screenAspect = (float)Screen.width / Screen.height;
            int rtHeight = Mathf.RoundToInt(previewWidth / screenAspect);

            previewRT = new RenderTexture(previewWidth, rtHeight, 0, RenderTextureFormat.ARGB32) { antiAliasing = 1, filterMode = FilterMode.Bilinear };
        }

        private void EnsurePreviewCamera()
        {   // Create or sync a dedicated preview camera cloned from the main camera
            if (mainCamera == null)
                return;

            if (previewCamera == null)
            {
                var go = new GameObject("ScreenCapture_PreviewCamera");
                go.hideFlags = HideFlags.DontSave;
                previewCamera = go.AddComponent<Camera>();
                previewCamera.enabled = false;
            }

            previewCamera.CopyFrom(mainCamera);
            previewCamera.enabled = false;  // Only render manually
            previewCamera.targetTexture = previewRT;
            previewCamera.cullingMask = ComputeCullingMask();

            ApplyBackgroundSettingsToCamera(previewCamera);

            // Mirror transform after CopyFrom to be safe
            previewCamera.transform.position = mainCamera.transform.position;
            previewCamera.transform.rotation = mainCamera.transform.rotation;
            previewCamera.transform.localScale = mainCamera.transform.localScale;
        }

        private void ApplyBackgroundSettingsToCamera(Camera cam)
        {   // Apply background clear flags/color based on toggles and user selection
            if (cam == null)
                return;

            if (!showBackground)
            {   // When background is hidden, use solid color/alpha per user choice
                var color = new Color(bgR / 255f, bgG / 255f, bgB / 255f, bgTransparent ? 0f : 1f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = color;
            }
            else
            {   // Use whatever the camera had (already copied from main for preview)
                // No-op
            }
        }

        private void UpdateBackgroundWindowVisibility()
        {   // Show/hide background settings window based on UI state and background toggle
            bool shouldShow = closableWindow != null && !closableWindow.Minimized && !showBackground;

            if (shouldShow && bgWindow == null)
            {   // Create window with controls
                int id = Builder.GetRandomID();
                bgWindow = Builder.CreateWindow(uiHolder.transform, id, 280, 320, (int)(closableWindow.Position.x + 700), (int)closableWindow.Position.y, draggable: true, savePosition: false, opacity: 1f, titleText: "Background");

                var content = Builder.CreateContainer(bgWindow, 0, 0);
                content.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 8f, new RectOffset(8, 8, 260, 8), true);

                Builder.CreateToggleWithLabel(content, 200, 46, () => bgTransparent, () =>
                {
                    bgTransparent = !bgTransparent;
                    UpdatePreviewCulling();
                }, 0, 0, "Transparent BG");

                // R input
                Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "R", ((int)bgR).ToString(), val =>
                {
                    if (int.TryParse(val, out int r))
                    {
                        bgR = Mathf.Clamp(r, 0, 255);
                        UpdatePreviewCulling();
                    }
                });

                // G input
                Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "G", ((int)bgG).ToString(), val =>
                {
                    if (int.TryParse(val, out int g))
                    {
                        bgG = Mathf.Clamp(g, 0, 255);
                        UpdatePreviewCulling();
                    }
                });

                // B input
                Builder.CreateInputWithLabel(content, 200, 40, 0, 0, "B", ((int)bgB).ToString(), val =>
                {
                    if (int.TryParse(val, out int b))
                    {
                        bgB = Mathf.Clamp(b, 0, 255);
                        UpdatePreviewCulling();
                    }
                });

                Builder.CreateLabel(content, 200, 35, 0, 0, "RGB out of 255");
            }
            else if (!shouldShow && bgWindow != null)
            {   // Destroy when not needed
                UnityEngine.Object.Destroy(bgWindow.gameObject);
                bgWindow = null;
            }
        }

        private int ComputeCullingMask()
        {   // Compute a culling mask for preview/capture; only strip known background layers
            int mask = mainCamera != null ? mainCamera.cullingMask : ~0;

            if (!showBackground)
            {
                int stars = LayerMask.GetMask("Stars");
                if (stars != 0)
                    mask &= ~stars;
            }

            return mask;
        }

        private void ApplyCullingForRender(Camera cam)
        {   // Apply the computed culling mask to the given camera and refresh preview immediately
            if (cam == null)
                return;
            cam.cullingMask = ComputeCullingMask();
        }

        private void UpdatePreviewCulling()
        {   // Re-render preview using the dedicated camera with current visibility toggles
            if (previewRT == null)
                return;

            EnsurePreviewCamera();
            if (previewCamera == null)
                return;

            previewCamera.cullingMask = ComputeCullingMask();
            ApplyBackgroundSettingsToCamera(previewCamera);

            // Apply visibility changes temporarily for this preview frame
            var modified = ApplySceneVisibilityTemporary();

            var prevTarget = previewCamera.targetTexture;
            previewCamera.targetTexture = previewRT;
            previewCamera.Render();
            previewCamera.targetTexture = prevTarget;

            // Restore original renderer enabled states so changes are preview-only
            RestoreSceneVisibility(modified);
        }

        private bool IsAtmosphereObject(GameObject go)
        {   // Detect objects that represent atmosphere either by name or by having an Atmosphere component
            if (go == null)
                return false;
            var name = go.name ?? string.Empty;
            if (name.IndexOf("atmosphere", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Check for component from SFS.World
            return go.GetComponent<Atmosphere>() != null;
        }

        private System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> ApplySceneVisibilityTemporary()
        {   // Temporarily disable renderers according to toggles and return list of changed renderers
            var changed = new System.Collections.Generic.List<(Renderer, bool)>();

            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(includeInactive: true);

            foreach (var r in renderers)
            {
                if (r == null || r.gameObject == null)
                    continue;

                // Skip UI elements (detect by RectTransform parent presence)
                if (r.GetComponentInParent<RectTransform>() != null)
                    continue;

                var go = r.gameObject;
                string layerName = LayerMask.LayerToName(go.layer) ?? string.Empty;

                // Always show Parts layer
                if (string.Equals(layerName, "Parts", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isTerrain = go.GetComponentInParent<SFS.World.StaticWorldObject>() != null ||
                                 go.GetComponentInParent<SFS.World.Terrain.DynamicTerrain>() != null;
                bool isAtmosphere = IsAtmosphereObject(go);
                bool isStars = string.Equals(layerName, "Stars", StringComparison.OrdinalIgnoreCase) || go.name.IndexOf("star", StringComparison.OrdinalIgnoreCase) >= 0;

                bool shouldDisable = false;

                // Background toggle hides stars and atmosphere, but never terrain
                if (!showBackground && (isStars || isAtmosphere))
                    shouldDisable = true;

                // Terrain toggle explicitly hides terrain objects
                if (!showTerrain && isTerrain)
                    shouldDisable = true;

                if (shouldDisable && r.enabled)
                {
                    changed.Add((r, r.enabled));
                    r.enabled = false;
                }
            }

            return changed;
        }

        private void RestoreSceneVisibility(System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> changed)
        {   // Restore renderer enabled states saved by ApplySceneVisibilityTemporary
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

        private void SetupPreview(Container imageContainer)
        {   // Create a preview GameObject under the given container and configure its RawImage

            var previewGO = new GameObject("PreviewImage");
            previewGO.transform.SetParent(imageContainer.rectTransform, false);

            var rect = previewGO.AddComponent<RectTransform>();

            // Calculate preview dimensions using helper method
            var (finalWidth, finalHeight) = CalculatePreviewDimensions();
            rect.sizeDelta = new Vector2(finalWidth, finalHeight);

            // Provide explicit layout sizing so the parent vertical layout places the controls below the preview
            var layout = imageContainer.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ??
                         imageContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.preferredWidth = finalWidth;
            layout.preferredHeight = finalHeight + 8f; // small padding so controls don't touch the preview

            previewImage = previewGO.AddComponent<UnityEngine.UI.RawImage>();
            previewImage.color = Color.white;
            previewImage.maskable = false;
            previewImage.uvRect = new Rect(0, 0, 1, 1);

            // Store initial screen dimensions for change detection
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            // Prepare and assign the render texture
            CreatePreviewRenderTexture();
            if (previewRT != null && !previewRT.IsCreated())
                previewRT.Create();

            previewImage.texture = previewRT;

            // Ensure preview camera exists and render once
            EnsurePreviewCamera();
            UpdatePreviewCulling();
        }

        private void Update()
        {   // Monitor window state, screen changes, and update preview
            if (closableWindow == null)
                return;

            bool minimized = closableWindow.Minimized;
            if (minimized != wasMinimized)
            {
                if (minimized)
                    windowCollapsedAction?.Invoke();
                else
                    windowOpenedAction?.Invoke();

                wasMinimized = minimized;

                UpdateBackgroundWindowVisibility();
            }

            // Check for screen size changes and update render texture accordingly
            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
            {
                lastScreenWidth = Screen.width;
                lastScreenHeight = Screen.height;

                if (previewImage != null)
                {
                    // Update preview image size using consistent calculation
                    var (finalWidth, finalHeight) = CalculatePreviewDimensions();
                    var rect = previewImage.GetComponent<RectTransform>();
                    rect.sizeDelta = new Vector2(finalWidth, finalHeight);

                    // Recreate render texture with new dimensions
                    CreatePreviewRenderTexture();
                    previewImage.texture = previewRT;
                }
            }

            // Update the live preview when window is visible
            if (!minimized && previewImage != null && mainCamera != null)
            {
                if (previewRT == null || !previewRT.IsCreated())
                {
                    CreatePreviewRenderTexture();
                    previewImage.texture = previewRT;
                }

                EnsurePreviewCamera();
                if (previewCamera != null)
                {
                    previewCamera.cullingMask = ComputeCullingMask();
                    ApplyBackgroundSettingsToCamera(previewCamera);

                    // Apply visibility changes temporarily for this preview frame
                    var modified = ApplySceneVisibilityTemporary();

                    var prevTarget = previewCamera.targetTexture;
                    previewCamera.targetTexture = previewRT;
                    previewCamera.Render();
                    previewCamera.targetTexture = prevTarget;

                    // Restore scene state
                    RestoreSceneVisibility(modified);
                }
            }
        }

        private (int width, int height) CalculatePreviewDimensions()
        {   // Calculate preview dimensions that fit container while maintaining screen aspect
            float screenAspect = (float)Screen.width / Screen.height;
            float containerWidth = 520f;
            float containerHeight = 430f;
            float containerAspect = containerWidth / containerHeight;

            // Calculate dimensions that fit within container bounds
            int finalWidth, finalHeight;

            if (screenAspect > containerAspect)
            {   // Screen is wider - scale to fit container width
                finalWidth = Mathf.RoundToInt(containerWidth);
                finalHeight = Mathf.RoundToInt(containerWidth / screenAspect);
            }
            else
            {   // Screen is taller - scale to fit container height
                finalHeight = Mathf.RoundToInt(containerHeight);
                finalWidth = Mathf.RoundToInt(containerHeight * screenAspect);
            }

            // Ensure dimensions never exceed container bounds
            finalWidth = Mathf.Min(finalWidth, Mathf.RoundToInt(containerWidth));
            finalHeight = Mathf.Min(finalHeight, Mathf.RoundToInt(containerHeight));

            return (finalWidth, finalHeight);
        }

        private void OnResolutionInputChange(string newValue)
        {   // Update width when input changes
            resolutionWidth = int.TryParse(newValue, out int num) ? num : resolutionWidth;
        }

        private void TakeScreenshot()
        {   // Capture screenshot at specified resolution
            // Ensure camera is initialized
            if (this.mainCamera == null)
            {
                if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
                    this.mainCamera = GameCamerasManager.main.world_Camera.camera;
                else
                {
                    UnityEngine.Debug.LogError("Cannot take screenshot: Camera not available");
                    return;
                }
            }

            int width = this.resolutionWidth;
            int height = Mathf.RoundToInt((float)width / (float)Screen.width * (float)Screen.height);

            RenderTexture renderTexture = null;
            Texture2D screenshotTexture = null;
            int previousAntiAliasing = QualitySettings.antiAliasing;
            bool previousOrthographic = this.mainCamera.orthographic;
            float previousOrthographicSize = this.mainCamera.orthographicSize;
            float previousFieldOfView = this.mainCamera.fieldOfView;
            var prevClearFlags = this.mainCamera.clearFlags;
            var prevBgColor = this.mainCamera.backgroundColor;

            try
            {
                renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                screenshotTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

                this.mainCamera.orthographic = false;
                this.mainCamera.orthographicSize = previousOrthographicSize / this.zoomFactor;
                this.mainCamera.fieldOfView = previousFieldOfView;
                QualitySettings.antiAliasing = 0;

                // Apply culling mask according to toggles for the final render
                var prevMask = this.mainCamera.cullingMask;
                ApplyCullingForRender(this.mainCamera);

                // Apply background settings (only when background hidden)
                ApplyBackgroundSettingsToCamera(this.mainCamera);

                // Temporarily apply scene visibility rules for this capture only
                var modified = ApplySceneVisibilityTemporary();

                this.mainCamera.targetTexture = renderTexture;
                this.mainCamera.Render();

                // Restore scene renderers and camera settings
                RestoreSceneVisibility(modified);
                this.mainCamera.cullingMask = prevMask;
                this.mainCamera.clearFlags = prevClearFlags;
                this.mainCamera.backgroundColor = prevBgColor;

                RenderTexture.active = renderTexture;

                screenshotTexture.ReadPixels(new Rect(0f, 0f, (float)width, (float)height), 0, 0);
                screenshotTexture.Apply();

                byte[] pngBytes = screenshotTexture.EncodeToPNG();

                // Get current world name at time of screenshot
                var worldFolder = CreateWorldFolder(GetWorldName());

                string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";

                using (var ms = new MemoryStream(pngBytes))
                    InsertIo(fileName, ms, worldFolder);
            }

            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Screenshot capture failed: {ex.Message}\n{ex.StackTrace}");
            }

            finally
            {
                this.mainCamera.targetTexture = null;
                RenderTexture.active = null;

                this.mainCamera.orthographic = previousOrthographic;
                this.mainCamera.orthographicSize = previousOrthographicSize;
                this.mainCamera.fieldOfView = previousFieldOfView;
                QualitySettings.antiAliasing = previousAntiAliasing;

                if (renderTexture != null)
                    UnityEngine.Object.Destroy(renderTexture);

                if (screenshotTexture != null)
                    UnityEngine.Object.Destroy(screenshotTexture);
            }
        }

        private FolderPath CreateWorldFolder(string worldName)
        {   // Create or return a folder for the current world name
            string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" :
                                  new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

            return InsertIo(sanitizedName, Main.ScreenCaptureFolder);
        }

        private string GetWorldName() =>
            (Base.worldBase?.paths?.worldName) ?? "Unknown";  // Get world name directly or use "Unknown" as fallback
    }
}
