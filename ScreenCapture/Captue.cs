using System;
using System.IO;
using System.Linq;
using ModLoader;
using SFS;
using SFS.IO;
using SFS.UI.ModGUI;
using SFS.World;
using SFS.WorldBase;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using static ScreenCapture.FileHelper;

namespace ScreenCapture
{
    internal class Captue : MonoBehaviour
    {
        private Camera mainCamera;
        private int resolutionWidth = 1980;
        private float zoomFactor = 1f;
        private Window screenCaptureWindow;
        private GameObject uiHolder;

        private void Awake()
        {   // Initialize camera safely with null checks
            if (GameCamerasManager.main != null && GameCamerasManager.main.world_Camera != null)
                this.mainCamera = GameCamerasManager.main.world_Camera.camera;
        }
        
        public void ShowUI()
        {   // Create and display the screenshot UI
            if (uiHolder != null) 
                return;  // UI already exists
                
            // Create UI elements
            uiHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "SFSRecorder");
            screenCaptureWindow = Builder.CreateWindow(uiHolder.transform, Builder.GetRandomID(), 250, 190, 300, 100, true, true, 1f, "ScreenShot");

            screenCaptureWindow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 0f, null, true);
            Builder.CreateTextInput(screenCaptureWindow, 160, 60, 0, 0, resolutionWidth.ToString(), new UnityAction<string>(OnResolutionInputChange));
            Builder.CreateButton(screenCaptureWindow, 180, 60, 0, 0, TakeScreenshot, "Screenshot");
        }
        
        public void HideUI()
        {   // Remove the screenshot UI
            if (uiHolder != null)
            {
                UnityEngine.Object.Destroy(uiHolder);
                uiHolder = null;
                screenCaptureWindow = null;
            }
            
            // Clean up any orphaned UI elements
            GameObject existing = GameObject.Find("SFSRecorder");
            if (existing != null)
                UnityEngine.Object.Destroy(existing);
        }
        

        private void OnResolutionInputChange(string newValue)
        {
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

                this.mainCamera.targetTexture = renderTexture;
                this.mainCamera.Render();

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
        {
            string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" : 
                                  new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                
            return InsertIo(sanitizedName, Main.ScreenCaptureFolder);
        }

        private string GetWorldName() =>
            (Base.worldBase?.paths?.worldName) ?? "Unknown";  // Get world name directly or use "Unknown" as fallback
    }
}
