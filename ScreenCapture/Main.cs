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
    // Token: 0x02000002 RID: 2
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
        {
            base.Load();
            SceneHelper.OnSceneLoaded += new Action<Scene>(this.OnSceneLoadedHandler);
        }

        public override void Early_Load()
        {
            base.Early_Load();
            ScreenCaptureFolder = FileHelper.InsertIo("ScreenCaptures", FileHelper.savingFolder);
        }

        private static Captue s_captureInstance;

        private void OnSceneLoadedHandler(Scene scene)
        {   // Ensure a single persistent Captue exists across scene loads
            if (s_captureInstance == null)
            {
                var s_captureInstance = new GameObject("ScreenCapture_Persistent").AddComponent<Captue>();
                UnityEngine.Object.DontDestroyOnLoad(s_captureInstance);
            }
        }
    }

    internal static class FileHelper
    {
        public static FolderPath savingFolder = (FolderPath)typeof(FileLocations).GetProperty("SavingFolder", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        
        private static FolderPath InsertIntoSfS(string relativePath, FolderPath baseFolder, byte[] fileBytes = null, Stream inputStream = null)
        {   // Core insert function: creates a folder or writes a file inside specified base folder
            // If fileBytes or inputStream is provided the call writes a file; otherwise it creates a folder

            if (inputStream != null && !inputStream.CanRead)
                throw new ArgumentException("inputStream must be readable.", nameof(inputStream));
            
            // Use provided base folder
            var baseFull = baseFolder.ToString();
            
            // Create base directory if it doesn't exist
            if (!Directory.Exists(baseFull))
                Directory.CreateDirectory(baseFull);

            // Simple path combination
            var combinedFull = Path.Combine(baseFull, relativePath);
            var isFile = (fileBytes != null) || (inputStream != null);
            
            if (!isFile)
            {   // Create folder and return its path
                if (!Directory.Exists(combinedFull))
                    Directory.CreateDirectory(combinedFull);

                return new FolderPath(combinedFull);
            }

            // Ensure destination directory exists for file write
            var destinationDir = Path.GetDirectoryName(combinedFull) ?? baseFull;
            if (!Directory.Exists(destinationDir))
                Directory.CreateDirectory(destinationDir);

            // Write file from provided byte[] or Stream
            using (var output = new FileStream(combinedFull, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (fileBytes != null)
                    output.Write(fileBytes, 0, fileBytes.Length);
                else
                {   // inputStream is not null here and is readable
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
