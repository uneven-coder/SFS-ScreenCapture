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
using Resources = UnityEngine.Resources;
using System.Linq;

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
        {   // Setup FrameEmbededState, auto-register shaders, run log tests, and apply Harmony patches early

            // Ensure registry is initialized and shaders are loaded
            FrameEmbededState.ComputeShaderRegistry.Initialize(force: true);

            var patcher = new Harmony("mods.FrameEmbededState.Patches");
            patcher.PatchAll();

            FrameEmbededState.Lib.Patches.ApplyAll();


            base.Early_Load();
        }

        public override void Load()
        {   // Setup overlay and scene hook for edge rendering
            overlay = new VisualOverlayManager();

            MainUi.SetOverlayManager(overlay);
            MainUi.Init();

            // Always show the UI window after load
            MainUi.RebuildUI();

            // Always use the current world camera for overlay
            void SetOverlayToCurrentCamera()
            {   // Set overlay to current world camera

                FrameEmbededState.ComputeShaderRegistry.RunAllValidations();
                FrameEmbededState.ComputeShaderRegistry.RunAllSelfTests();

                // var cam = GameCamerasManager.main?.world_Camera?.camera;
                var cam = GameCamerasManager.main?.world_Camera?.camera;
                if (cam == null) return;
                overlay.ConfigureOverlay(settings =>
                {
                    settings.TargetCamera = cam;
                    settings.Enable = false;
                });
            }

            SceneManager.sceneLoaded += (_, __) => SetOverlayToCurrentCamera();
            SceneHelper.OnWorldSceneLoaded += SetOverlayToCurrentCamera;
            SceneHelper.OnBuildSceneLoaded += SetOverlayToCurrentCamera;
        }
    }
}
