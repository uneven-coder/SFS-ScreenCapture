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
    // Token: 0x02000003 RID: 3
    internal class Captue : MonoBehaviour
    {
        private Camera mainCamera;
        private int resolutionWidth = 1980;
        private float zoomFactor = 1f;
        private string currentWorldName = "Unknown";

        // Token: 0x0600000A RID: 10 RVA: 0x000020AB File Offset: 0x000002AB
        private void Awake()
        {
            this.mainCamera = GameCamerasManager.main.world_Camera.camera;
        }

        // Token: 0x0600000B RID: 11 RVA: 0x000020C4 File Offset: 0x000002C4
        private void Start()
        {   // Initialize screenshot UI only in World_PC and remove it otherwise

            string sceneName = SceneManager.GetActiveScene().name;

            if (sceneName == "World_PC")
            {   // Setup UI for world scene
                // Try to get the world name
                if (Base.worldBase != null)
                    currentWorldName = GetWorldName();
                var holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "SFSRecorder");
                Window window = Builder.CreateWindow(holder.transform, Builder.GetRandomID(), 250, 190, 300, 100, true, true, 1f, "ScreenShot");

                // Common layout and controls
                window.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 0f, null, true);
                Builder.CreateTextInput(window, 160, 60, 0, 0, resolutionWidth.ToString(), new UnityAction<string>(OnResolutionInputChange));
                Builder.CreateButton(window, 180, 60, 0, 0, TakeScreenshot, "Screenshot");
                return;
            }

            else
            {   // Destroy any existing holder to remove the menu when not in world
                var existing = GameObject.Find("SFSRecorder");
                if (existing != null)
                    UnityEngine.Object.Destroy(existing);
                return;
            }
        }

        // Token: 0x0600000C RID: 12 RVA: 0x00002164 File Offset: 0x00000364
        private void OnResolutionInputChange(string newValue) =>
            resolutionWidth = int.TryParse(newValue, out var num) ? num : resolutionWidth;  // Update width if valid

        // Token: 0x0600000D RID: 13 RVA: 0x00002182 File Offset: 0x00000382
        private void TakeScreenshot()
        {
            int width = this.resolutionWidth;
            int height = Mathf.RoundToInt((float)width / (float)Screen.width * (float)Screen.height);

            RenderTexture renderTexture = null;
            Texture2D screenshotTexture = null;
            int previousAntiAliasing = QualitySettings.antiAliasing;
            bool previousOrthographic = this.mainCamera.orthographic;
            float previousOrthographicSize = this.mainCamera.orthographicSize;
            float previousFieldOfView = this.mainCamera.fieldOfView;

            try
            {   // Prepare render targets and configure the camera for capture
                renderTexture = new RenderTexture(width, height, 74);
                screenshotTexture = new Texture2D(width, height, TextureFormat.RGB24, false);

                this.mainCamera.orthographic = false;
                this.mainCamera.orthographicSize = previousOrthographicSize / this.zoomFactor;
                this.mainCamera.fieldOfView = previousFieldOfView;
                QualitySettings.antiAliasing = 0;

                this.mainCamera.targetTexture = renderTexture;
                this.mainCamera.Render();

                RenderTexture.active = renderTexture;

                // Read pixels from the active render target into the texture
                screenshotTexture.ReadPixels(new Rect(0f, 0f, (float)width, (float)height), 0, 0);
                screenshotTexture.Apply();

                byte[] pngBytes = screenshotTexture.EncodeToPNG();

                // Get or create world-specific subfolder
                var worldFolder = CreateWorldFolder(currentWorldName);
                
                // Create a filename with timestamp only (since it's in a world-specific folder)
                string fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png";
                
                using (var ms = new MemoryStream(pngBytes))
                    InsertIo(fileName, ms, worldFolder);
            }

            catch (System.Exception ex)
            {   // Log any failure during capture or save
                UnityEngine.Debug.LogError($"Screenshot capture failed: {ex.Message}\n{ex.StackTrace}");
            }

            finally
            {   // Restore camera and quality settings, and release Unity objects
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

        // Creates or gets a world-specific folder for screenshots
        private FolderPath CreateWorldFolder(string worldName)
        {   // Create a subfolder for the world within the main screenshots folder
            string sanitizedName = string.IsNullOrWhiteSpace(worldName) ? "Unknown" : 
                                  new string(worldName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                
            return InsertIo(sanitizedName, Main.ScreenCaptureFolder);
        }

        // Get the name of the current world from the path
        private string GetWorldName()
        {   // Try to extract world name from the world save path
            try
            {
                if (Base.worldBase == null)
                    return "Unknown";

                // Get the current player location path
                var playerLocationPath = Base.worldBase.GetType()
                    .GetMethod("GetPlayerLocationPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?
                    .Invoke(Base.worldBase, null) as string;

                if (!string.IsNullOrEmpty(playerLocationPath))
                {
                    // Extract world name from the path
                    var folderParts = playerLocationPath.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                    if (folderParts.Length >= 2)
                        return folderParts[folderParts.Length - 2];  // The world name should be the second-to-last part
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Failed to get world name: {ex.Message}");
            }
            
            return "Unknown";
        }
    }
}
