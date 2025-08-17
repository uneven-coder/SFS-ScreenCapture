using System;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using ModLoader;
using ModLoader.Helpers;
using SFS.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScreenCapture
{
    public class Main : Mod
    {
        public static FolderPath ScreenCaptureFolder { get; private set; }

        public override string ModNameID
        {
            get { return "ScreenCapture"; }
        }
        public override string DisplayName
        {
            get { return "ScreenCapture"; }
        }
        public override string Author
        {
            get { return "Cratior"; }
        }
        public override string MinimumGameVersionNecessary
        {
            get { return "1.5.10"; }
        }
        public override string ModVersion
        {
            get { return "2.2.3"; } }
        public override string Description
        {
            get { return "Adds a screenshot button to the world scene, allowing you to take screenshots at custom resolutions."; }
        }

        public override void Load()
        {   // Initialize mod components and register scene-specific event handlers
            base.Load();
            
            ScreenCaptureFolder = FileHelper.InsertIo("ScreenCaptures", FileHelper.savingFolder);
            
            SceneHelper.OnWorldSceneLoaded += CreateScreenCaptureUI;
            SceneHelper.OnWorldSceneUnloaded += DestroyScreenCaptureUI;
            
            // Create persistent capture instance
            if (s_captureInstance == null)
            {
                GameObject captureObject = new GameObject("ScreenCapture_Persistent");
                s_captureInstance = captureObject.AddComponent<Captue>();
                UnityEngine.Object.DontDestroyOnLoad(captureObject);
            }
        }

        public override void Early_Load()
        {
            base.Early_Load();
        }

        private static Captue s_captureInstance;

        private void CreateScreenCaptureUI()
        {   // Display the capture UI when entering the world scene
            if (s_captureInstance != null)
                s_captureInstance.ShowUI();
        }

        private void DestroyScreenCaptureUI() 
        {   // Hide the capture UI when leaving the world scene
            if (s_captureInstance != null)
                s_captureInstance.HideUI();
        }
    }

    internal static class FileHelper
    {
        public static FolderPath savingFolder = (FolderPath)typeof(FileLocations).GetProperty("SavingFolder", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        
        private static FolderPath InsertIntoSfS(string relativePath, FolderPath baseFolder, byte[] fileBytes = null, Stream inputStream = null)
        {
            if (inputStream != null && !inputStream.CanRead)
                throw new ArgumentException("inputStream must be readable.", nameof(inputStream));
            
            var baseFull = baseFolder.ToString();
            
            if (!Directory.Exists(baseFull))
                Directory.CreateDirectory(baseFull);

            var combinedFull = Path.Combine(baseFull, relativePath);
            var isFile = (fileBytes != null) || (inputStream != null);
            
            if (!isFile)
            {
                if (!Directory.Exists(combinedFull))
                    Directory.CreateDirectory(combinedFull);

                return new FolderPath(combinedFull);
            }

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

        public static FolderPath InsertIo(string folderName, FolderPath baseFolder) =>
            InsertIntoSfS(folderName, baseFolder);  // Wrapper for folder creation

        public static FolderPath InsertIo(string fileName, Stream inputStream, FolderPath folder) =>
            InsertIntoSfS(fileName, folder, null, inputStream);  // Wrapper for file insertion using stream

        public static FolderPath InsertIo(string fileName, byte[] fileBytes, FolderPath folder) =>
            InsertIntoSfS(fileName, folder, fileBytes, null);  // Wrapper for file insertion using bytes
    }
}
