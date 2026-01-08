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



        public override void Early_Load()
        {   // Setup FrameEmbededState, apply Harmony patches, then auto-register shaders and run log tests

            // Apply Harmony patches and library patches first
            // var patcher = new Harmony("mods.FrameEmbededState.Patches");
            // patcher.PatchAll();

            FrameEmbededState.Lib.Patches.ApplyAll();

            // Ensure shader registry is initialized and shaders are loaded after patches
            

            base.Early_Load();
        }

        public override void Load()
        {   // Initialize shader registry and menu entrypoint
            FrameEmbededState.ShaderRegistry.Initialize(force: true);
            MainUi.Init();
        }
    }
}
