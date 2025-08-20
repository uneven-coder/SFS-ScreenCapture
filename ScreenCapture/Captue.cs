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
using static ScreenCapture.CaptureUtilities;

namespace ScreenCapture
{
public class Captue : MonoBehaviour
{
    internal Camera mainCamera;
    internal Camera previewCamera; 
    internal int resolutionWidth = 1980;
    internal int previewWidth = 384;
    
    internal float zoomFactor = 1f;
    internal ClosableWindow closableWindow;
    internal GameObject uiHolder;
    public RawImage previewImage;
    public RenderTexture previewRT;
    internal bool showBackground = true;
    internal bool showTerrain = true;
    internal int lastScreenWidth;
    internal int lastScreenHeight;

    internal Action windowOpenedAction;
    internal Action windowCollapsedAction;
    internal bool wasMinimized;

    private void Awake()
    {   // Initialize camera and actions when component awakens
        if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
            this.mainCamera = GameCamerasManager.main.world_Camera.camera;

        windowOpenedAction = () =>
        {
            if (WorldTime.main != null)
                WorldTime.main.SetState(0.0, true, false);
        };

        windowCollapsedAction = () =>
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
    {   // Open the UI and ensure the main camera is ready
        TryAcquireMainCamera();
        CaptureUI.ShowUI(this);
    }

    public void HideUI() =>
        CaptureUI.HideUI(this);

    private void CreatePreviewRenderTexture()
    {   // Create or recreate the preview render texture
        if (previewRT != null)
        {
            previewRT.Release();
            UnityEngine.Object.Destroy(previewRT);
        }

        float screenAspect = (float)Screen.width / Screen.height;
        int rtHeight = Mathf.RoundToInt(previewWidth / screenAspect);

        previewRT = new RenderTexture(previewWidth, rtHeight, 24, RenderTextureFormat.ARGB32)
        { 
            antiAliasing = 1, 
            filterMode = FilterMode.Bilinear 
        };

        if (!previewRT.IsCreated())
            previewRT.Create();
    }

    private bool TryAcquireMainCamera()
    {   // Attempt to resolve and cache the world camera
        if (mainCamera != null)
            return true;

        if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
            mainCamera = GameCamerasManager.main.world_Camera.camera;

        return mainCamera != null;
    }

    private void EnsurePreviewCamera()
    {   // Create and configure the preview camera if needed
        if (!TryAcquireMainCamera())
            return;

        if (previewCamera == null)
        {
            var go = new GameObject("ScreenCapture_PreviewCamera");
            go.hideFlags = HideFlags.DontSave;
            previewCamera = go.AddComponent<Camera>();
            previewCamera.enabled = false;
        }

        previewCamera.CopyFrom(mainCamera);
        previewCamera.enabled = false;
        previewCamera.targetTexture = previewRT;
        previewCamera.cullingMask = ComputeCullingMask();

        ApplyBackgroundSettingsToCamera(previewCamera);

        previewCamera.transform.position = mainCamera.transform.position;
        previewCamera.transform.rotation = mainCamera.transform.rotation;
        previewCamera.transform.localScale = mainCamera.transform.localScale;
    }

    public void ApplyBackgroundSettingsToCamera(Camera cam)
    {   // Apply background color and transparency settings to camera
        if (cam == null)
            return;

        var color = new Color(CaptureUI.bgR / 255f, CaptureUI.bgG / 255f, CaptureUI.bgB / 255f, CaptureUI.bgTransparent ? 0f : 1f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = color;
    }

    public int ComputeCullingMask()
    {   // Calculate the appropriate culling mask based on current settings
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
    {   // Apply the computed culling mask to the camera
        if (cam == null)
            return;
        cam.cullingMask = ComputeCullingMask();
    }

    public void UpdatePreviewCulling()
    {   // Update the preview camera's culling and background settings
        if (previewCamera == null)
            return;

        previewCamera.cullingMask = ComputeCullingMask();
        ApplyBackgroundSettingsToCamera(previewCamera);
    }

    private System.Collections.Generic.List<(Renderer renderer, bool previousEnabled)> ApplySceneVisibilityTemporary()
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

            if (string.Equals(layerName, "Parts", StringComparison.OrdinalIgnoreCase))
                continue;

            bool isTerrain = go.GetComponentInParent<SFS.World.StaticWorldObject>() != null ||
                             go.GetComponentInParent<SFS.World.Terrain.DynamicTerrain>() != null;
            bool isAtmosphere = CaptureUtilities.IsAtmosphereObject(go);
            bool isStars = string.Equals(layerName, "Stars", StringComparison.OrdinalIgnoreCase) || go.name.IndexOf("star", StringComparison.OrdinalIgnoreCase) >= 0;

            bool shouldDisable = false;

            if (!showBackground && (isStars || isAtmosphere))
                shouldDisable = true;

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

    internal void SetupPreview(Container imageContainer)
    {   // Set up the preview image and render texture
        var previewGO = new GameObject("PreviewImage");
        previewGO.transform.SetParent(imageContainer.rectTransform, false);

        var rect = previewGO.AddComponent<RectTransform>();

        var (finalWidth, finalHeight) = CalculatePreviewDimensions();
        rect.sizeDelta = new Vector2(finalWidth, finalHeight);

        var layout = imageContainer.gameObject.GetComponent<UnityEngine.UI.LayoutElement>() ??
                     imageContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        layout.preferredWidth = finalWidth;
        layout.preferredHeight = finalHeight + 8f;

        previewImage = previewGO.AddComponent<UnityEngine.UI.RawImage>();
        previewImage.color = Color.white;
        previewImage.maskable = false;
        previewImage.uvRect = new Rect(0, 0, 1, 1);

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        CreatePreviewRenderTexture();
        if (previewRT != null && !previewRT.IsCreated())
            previewRT.Create();

        previewImage.texture = previewRT;

        EnsurePreviewCamera();
        UpdatePreviewCulling();
    }

    private void Update()
    {   // Handle window minimization and update the preview
        TryAcquireMainCamera();

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

            CaptureUI.UpdateBackgroundWindowVisibility();
        }

        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            if (previewImage != null)
            {
                var (finalWidth, finalHeight) = CalculatePreviewDimensions();
                var rect = previewImage.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(finalWidth, finalHeight);

                CreatePreviewRenderTexture();
                previewImage.texture = previewRT;
            }
        }

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

                var modified = ApplySceneVisibilityTemporary();

                var prevTarget = previewCamera.targetTexture;
                previewCamera.targetTexture = previewRT;
                previewCamera.Render();
                previewCamera.targetTexture = prevTarget;

                RestoreSceneVisibility(modified);
            }
        }
    }

    private (int width, int height) CalculatePreviewDimensions()
    {   // Calculate the appropriate preview dimensions
        float screenAspect = (float)Screen.width / Screen.height;
        float containerWidth = 520f;
        float containerHeight = 430f;
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

    internal void OnResolutionInputChange(string newValue) =>
        resolutionWidth = int.TryParse(newValue, out int num) ? num : resolutionWidth;

    internal void TakeScreenshot()
    {   // Capture and save a screenshot at the specified resolution
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

            var prevMask = this.mainCamera.cullingMask;
            ApplyCullingForRender(this.mainCamera);

            ApplyBackgroundSettingsToCamera(this.mainCamera);

            var modified = ApplySceneVisibilityTemporary();

            this.mainCamera.targetTexture = renderTexture;
            this.mainCamera.Render();

            RestoreSceneVisibility(modified);
            this.mainCamera.cullingMask = prevMask;
            this.mainCamera.clearFlags = prevClearFlags;
            this.mainCamera.backgroundColor = prevBgColor;

            RenderTexture.active = renderTexture;

            screenshotTexture.ReadPixels(new Rect(0f, 0f, (float)width, (float)height), 0, 0);
            screenshotTexture.Apply();

            byte[] pngBytes = screenshotTexture.EncodeToPNG();

            var worldFolder = CaptureUtilities.CreateWorldFolder(CaptureUtilities.GetWorldName());

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
}
}
