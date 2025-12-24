using ModLoader;
using SFS;
using SFS.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Threading.Tasks;
using FrameEmbededState;
using ModLoader.Helpers;
using HarmonyLib;
using FrameEmbededState.Lib;

namespace FrameEmbededState
{
    public class Main : Mod
    {
        public override string ModNameID => "FrameEmbededState";
        public override string DisplayName => "FrameEmbededState";
        public override string Author => "Cratior";
        public override string ModVersion => "1.8.0";
        public override string Description => "Dynamic visual overlay system (caller-authored effects).";
        public override string MinimumGameVersionNecessary => "1.5.6";

        private VisualOverlayManager overlay;

        

        public override void Early_Load()
        {   // Setup FrameEmbededState and apply Harmony patches early
            var patcher = new Harmony("mods.FrameEmbededState.Patches");
            patcher.PatchAll();

            // SFS.World.Environment.atmosphere

            FrameEmbededState.Lib.Patches.ApplyAll();

            base.Early_Load();
        }

        public override void Load()
        {   // Setup overlay and scene hook for edge rendering
            overlay = new VisualOverlayManager();

            MainUi.SetOverlayManager(overlay);
            MainUi.Init();
            // Camera cam = GameCamerasManager.main?.world_Camera?.camera ?? GameCamerasManager.main?.scaledWorld_Camera?.camera;
            // Camera cam = SFS.Cameras.ActiveCamera.Camera.camera;
            
            SceneManager.sceneLoaded += (_, __) =>
            {
                Camera cam = GameCamerasManager.main?.world_Camera?.camera;
                if (cam == null) return;

                overlay.VisualManager(settings =>
                {
                    settings.TargetCamera = cam;
                    settings.Enable = false; // No shader enabled by default
                });
            };

            // Ensure overlay target camera is set on SceneHelper-driven loads as well
            SceneHelper.OnWorldSceneLoaded += () =>
            {   // Bind overlay to current world camera when world scene finishes loading
            Camera cam = GameCamerasManager.main?.world_Camera?.camera;
                if (cam == null) return;

                overlay.VisualManager(settings =>
                {
                    settings.TargetCamera = cam;
                    settings.Enable = false;
                });
            };

            SceneHelper.OnBuildSceneLoaded += () =>
            {   // Bind overlay to current build camera when build scene finishes loading
                Camera cam = GameCamerasManager.main?.world_Camera?.camera;
                if (cam == null) return;

                overlay.VisualManager(settings =>
                {
                    settings.TargetCamera = cam;
                    settings.Enable = false;
                });
            };
        }
    }
}
