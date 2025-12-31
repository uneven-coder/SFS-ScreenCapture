using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;
using UITools;
using ModLoader.Helpers;
using SFS.World;
using FrameEmbededState;
using FrameEmbededState.Lib.Renders; // <-- Add this for OverlayDispatcher and OverlayRenderMode

namespace FrameEmbededState
{
    public static class MainUi
    {
        private readonly struct ShaderInfo
        {
            public readonly string Name;
            public readonly string Description;
            public readonly string ShaderSource; // Shader name or compute shader path

            public ShaderInfo(string name, string description, string shaderSource)
            {
                Name = name;
                Description = description;
                ShaderSource = shaderSource;
            }
        }

        private struct ButtonTint
        {
            public Graphic Graphic;
            public Color Normal;
        }

        private static readonly List<ShaderInfo> shaders = new(16);
        private static readonly List<ButtonTint> buttonTints = new(16);

        private static GameObject holder;
        private static ClosableWindow window;

        private static readonly int windowID = Builder.GetRandomID();
        private static int selectedShader = -1;

        private static readonly Color HighlightAdd = new(0.2f, 0.2f, 0.45f, 0f);

        private const int WindowWidth = 340;
        private const int WindowBaseHeight = 70;
        private const int RowHeight = 44;

        public static void Init()
        {   // Initialize and register all shaders/effects
            RegisterAllShaders();

            SceneHelper.OnWorldSceneLoaded += RebuildUI;
            SceneHelper.OnBuildSceneLoaded += RebuildUI;
            SceneHelper.OnWorldSceneUnloaded += DestroyUI;
            SceneHelper.OnBuildSceneUnloaded += DestroyUI;
        }

        // Register a GPU shader effect (ShaderLab)
        public static void RegisterGpuShader(string name, string description, string shaderSource)
        {
            shaders.Add(new ShaderInfo(name, description, shaderSource));
            Debug.Log($"[FrameEmbededState] Registered GPU shader: {name} - {description}");
            RebuildUI();
        }

        // Register all shaders here
        private static void RegisterAllShaders()
        {   // Discover and register all shader modules automatically

            shaders.Clear();

            foreach (var module in ShaderRegistry.AllModules)
            {
                if (module.Shader != null)
                    Debug.Log($"[MainUi] Found GPU shader: {module.Name} ({module.Shader.name})");
                else
                    Debug.LogWarning($"[MainUi] GPU shader not found: {module.Name}");

                shaders.Add(new ShaderInfo(
                    module.Name,
                    $"Shader module: {module.Name}",
                    module.Shader != null ? module.Shader.name : null
                ));
            }
        }

        private static Camera WorldCamera =>
            GameCamerasManager.main?.world_Camera?.camera;

        /// <summary>
        /// Centralized overlay configuration:
        /// idx < 0 => disable overlay.
        /// idx >= 0 => enable and set Execute to the shader effect.
        /// </summary>
        private static void ConfigureOverlay(int idx)
        {   // Configure overlay for selected shader type

            var cam = WorldCamera;

            if (idx < 0 || idx >= shaders.Count)
            {
                // Only disable overlay if there was a previous shader selected
                if (selectedShader >= 0)
                {
                    Debug.Log("[MainUi] Overlay disabled (no shader selected).");
                    OverlayDispatcher.Render(
                        cam,
                        null,
                        null,
                        null, // Explicitly pass null shader to disable
                        null,
                        OverlayRenderMode.BehindUI
                    );
                }
                return;
            }

            var info = shaders[idx];

            var module = ShaderRegistry.Get(info.Name);
            if (module != null && module.Shader != null)
            {
                OverlayRenderMode renderMode = OverlayRenderMode.BehindUI;
                var moduleType = module.GetType();
                var renderTargetProp = moduleType.GetProperty("RenderTarget");
                if (renderTargetProp != null)
                    renderMode = (OverlayRenderMode)renderTargetProp.GetValue(module);

                Debug.Log($"[MainUi] Selecting shader '{info.Name}' ({module.Shader.name}) with mode '{renderMode}'.");

                // Log UI shader assignment
                Debug.Log($"[MainUi] Setting CurrentUiShader.Value = {module.Shader?.name ?? "null"}");

                OverlayDispatcher.Render(
                    cam,
                    null,
                    null,
                    module.Shader, // Always pass the actual shader instance
                    null,
                    renderMode
                );
            }
            else
                Debug.LogWarning($"[MainUi] Shader module not found or shader missing: {info.Name}");
        }

        public static void RebuildUI()
        {
            DestroyUI();

            buttonTints.Clear();

            ConfigureOverlay(-1);

            holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "Shader Selector Holder");

            var height = WindowBaseHeight + shaders.Count * RowHeight;

            window = UIToolsBuilder.CreateClosableWindow(
                holder.transform, windowID, WindowWidth, height, 0, 0,
                true, true, 1f, "Shader Selector", false
            );

            var layout = window.CreateLayoutGroup(
                SFS.UI.ModGUI.Type.Vertical,
                TextAnchor.UpperCenter,
                8f,
                new RectOffset(10, 10, 10, 10),
                true
            );
            window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

            for (int i = 0; i < shaders.Count; i++)
            {
                int idx = i;
                var btn = Builder.CreateButton(
                    parent: window,
                    width: 320,
                    height: 38,
                    posX: 0,
                    posY: 0,
                    text: shaders[i].Name,
                    onClick: () => ToggleShader(idx)
                );

                CacheButtonTint(btn.rectTransform);
                btn.rectTransform.SetParent(layout.transform, false);
            }

            if (selectedShader >= 0 && selectedShader < shaders.Count)
                ConfigureOverlay(selectedShader);
            else
                selectedShader = -1;

            UpdateButtonHighlights();
        }

        public static void DestroyUI()
        {
            if (holder != null)
                UnityEngine.Object.Destroy(holder);

            holder = null;
            window = null;
            buttonTints.Clear();
        }

        private static void CacheButtonTint(RectTransform buttonRect)
        {
            Graphic g = null;

            var t = buttonRect.Find("BackOverTint");
            if (t != null) g = t.GetComponent<Image>();

            if (g == null)
                g = buttonRect.GetComponent<Graphic>() ?? buttonRect.GetComponentInChildren<Graphic>();

            if (g == null) return;

            buttonTints.Add(new ButtonTint { Graphic = g, Normal = g.color });
        }

        private static void ToggleShader(int idx)
        {
            if (idx < 0 || idx >= shaders.Count)
                return;

            if (selectedShader == idx)
                selectedShader = -1;
            else
                selectedShader = idx;

            ConfigureOverlay(selectedShader);

            UpdateButtonHighlights();
        }

        private static void UpdateButtonHighlights()
        {
            for (int i = 0; i < buttonTints.Count; i++)
            {
                var bt = buttonTints[i];
                if (bt.Graphic == null) continue;

                bt.Graphic.color = (i == selectedShader) ? (bt.Normal + HighlightAdd) : bt.Normal;
            }
        }
    }
}
