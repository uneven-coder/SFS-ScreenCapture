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
    internal class Captue : MonoBehaviour
    {
        private Camera mainCamera;
        private Camera previewCamera; // Dedicated camera for preview rendering
        private int resolutionWidth = 1980;
        private int previewWidth = 384;
        private float zoomFactor = 1f;
        private ClosableWindow closableWindow;
        private GameObject uiHolder;
        private RawImage previewImage;
        private RenderTexture previewRT;
        private bool showBackground = true;
        private bool showTerrain = true;
        private int lastScreenWidth;
        private int lastScreenHeight;

        private Action windowOpenedAction;
        private Action windowCollapsedAction;
        private bool wasMinimized;

        private void Awake()
        {   // Initialize camera and default callbacks
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
        {   // Create and display the collapsible screenshot UI with preview and controls
            if (uiHolder != null)
                return;  // UI already exists

            uiHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "SFSRecorder");
            closableWindow = CreateClosableWindow(uiHolder.transform, Builder.GetRandomID(), 870, 500, 300, 100, true, true, 1f, "ScreenShot", minimized: false);
            wasMinimized = closableWindow.Minimized;

            // Root vertical layout to host top and bottom sections
            closableWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 25f, new RectOffset(6, 6, 10, 6), false);

            var toolsContainer = Builder.CreateContainer(closableWindow, 0, 0);
            toolsContainer.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 12f, null, true);

            var ImageContainer = Builder.CreateContainer(toolsContainer, 0, 0);
            SetupPreview(ImageContainer);

            var Hierarchy  = Builder.CreateBox(toolsContainer, 310, 300, 0, 0, 0.5f);
            Hierarchy.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 4f, new RectOffset(3, 3, 6, 4), true);

            Builder.CreateLabel(Hierarchy, 200, 30, 0, 0, "Hierarchy");



            Builder.CreateSeparator(closableWindow, 80, 0, 0);
            
            // Bottom row (70x670): Capture button (left) and resolution input (right)
            var bottom = Builder.CreateContainer(closableWindow, 0, 0);
            // bottom.Size = new Vector2(670f, 70f);
            bottom.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 10f, null, true);

            // Add toggles to control which layers are visible in preview and final capture
            Builder.CreateToggleWithLabel(bottom, 160, 30, () => showBackground, () => { showBackground = !showBackground; UpdatePreviewCulling(); }, 0, 0, "Show Background");
            Builder.CreateToggleWithLabel(bottom, 160, 30, () => showTerrain, () => { showTerrain = !showTerrain; UpdatePreviewCulling(); }, 0, 0, "Show Terrain");

            Builder.CreateButton(bottom, 180, 60, 0, 0, TakeScreenshot, "Capture");
            Builder.CreateTextInput(bottom, 160, 60, 0, 0, resolutionWidth.ToString(), new UnityEngine.Events.UnityAction<string>(OnResolutionInputChange));

            // Apply open behavior immediately if window starts expanded
            if (!closableWindow.Minimized)
                windowOpenedAction?.Invoke();
        }

        public void HideUI()
        {   // Remove the screenshot UI and clear references
            // If window is open, treat hide as a collapse to resume time
            if (closableWindow != null && !closableWindow.Minimized)
                windowCollapsedAction?.Invoke();

            if (uiHolder != null)
            {
                UnityEngine.Object.Destroy(uiHolder);
                uiHolder = null;
                closableWindow = null;
            }

            if (previewRT != null)
            {
                previewRT.Release();
                UnityEngine.Object.Destroy(previewRT);
                previewRT = null;
            }

            if (previewCamera != null)
            {   // Destroy the preview camera to avoid lingering state
                UnityEngine.Object.Destroy(previewCamera.gameObject);
                previewCamera = null;
            }

            previewImage = null;

            // Clean up any orphaned UI elements
            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null)
                UnityEngine.Object.Destroy(existing);
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
            
            previewRT = new RenderTexture(previewWidth, rtHeight, 0) { antiAliasing = 1, filterMode = FilterMode.Bilinear };
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

            // Mirror transform and camera settings
            previewCamera.transform.position = mainCamera.transform.position;
            previewCamera.transform.rotation = mainCamera.transform.rotation;
            previewCamera.transform.localScale = mainCamera.transform.localScale;

            previewCamera.CopyFrom(mainCamera);
            previewCamera.enabled = false;  // Only render manually
            previewCamera.targetTexture = previewRT;
            previewCamera.cullingMask = ComputeCullingMask();
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
            // previewGO.transform.localPosition = new Vector3(270, -120, 0);

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

            try
            {
                renderTexture = new RenderTexture(width, height, 24);
                screenshotTexture = new Texture2D(width, height, TextureFormat.RGB24, false);

                this.mainCamera.orthographic = false;
                this.mainCamera.orthographicSize = previousOrthographicSize / this.zoomFactor;
                this.mainCamera.fieldOfView = previousFieldOfView;
                QualitySettings.antiAliasing = 0;

                // Apply culling mask according to toggles for the final render
                var prevMask = this.mainCamera.cullingMask;
                ApplyCullingForRender(this.mainCamera);

                // Temporarily apply scene visibility rules for this capture only
                var modified = ApplySceneVisibilityTemporary();

                this.mainCamera.targetTexture = renderTexture;
                this.mainCamera.Render();

                // Restore scene renderers and camera mask
                RestoreSceneVisibility(modified);
                this.mainCamera.cullingMask = prevMask;

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
