using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;
using UITools;
using ModLoader.Helpers;
using SFS.World;

namespace FrameEmbededState
{
    public delegate void ShaderEffectDelegate(VisualOverlayManager.VisualOverlaySettings settings);

    // Base class for auto-registering shader effects
    public abstract class BaseShaderEffect
    {
        protected BaseShaderEffect(string name, string description) =>
            MainUi.RegisterShader(name, description, ApplyEffect);

        protected abstract void ApplyEffect(VisualOverlayManager.VisualOverlaySettings settings);
    }

    public static class MainUi
    {
        private readonly struct ShaderInfo
        {
            public readonly string Name;
            public readonly string Description;
            public readonly ShaderEffectDelegate Effect;

            public ShaderInfo(string name, string description, ShaderEffectDelegate effect)
            {
                Name = name;
                Description = description;
                Effect = effect;
            }
        }

        private struct ButtonTint
        {
            public Graphic Graphic;
            public Color Normal;
        }

        private static readonly List<ShaderInfo> shaders = new(16);
        private static readonly List<ButtonTint> buttonTints = new(16);

        private static VisualOverlayManager overlayManager;
        private static GameObject holder;
        private static ClosableWindow window;

        private static readonly int windowID = Builder.GetRandomID();
        private static int selectedShader = -1;

        private static readonly Color HighlightAdd = new(0.2f, 0.2f, 0.45f, 0f);

        private const int WindowWidth = 340;
        private const int WindowBaseHeight = 70;
        private const int RowHeight = 44;

        public static void Init()
        {
            ForceLoadAllShaderEffects();

            SceneHelper.OnWorldSceneLoaded += RebuildUI;
            SceneHelper.OnBuildSceneLoaded += RebuildUI;
            SceneHelper.OnWorldSceneUnloaded += DestroyUI;
            SceneHelper.OnBuildSceneUnloaded += DestroyUI;
        }

        public static void SetOverlayManager(VisualOverlayManager mgr)
        {
            overlayManager = mgr;
            ConfigureOverlay(-1);

            // Always rebuild UI when overlay manager is set to ensure window is visible
            RebuildUI();
        }

        public static void RegisterShader(string name, string description, ShaderEffectDelegate effect)
        {
            shaders.Add(new ShaderInfo(name, description, effect));
            Debug.Log($"[FrameEmbededState] Registered shader: {name} - {description}");

            // Always rebuild UI after registering a shader to ensure new shaders appear
            RebuildUI();
        }

        private static void ForceLoadAllShaderEffects()
        {
            var baseType = typeof(BaseShaderEffect);
            foreach (var t in baseType.Assembly.GetTypes())
            {
                if (t.IsClass && !t.IsAbstract && baseType.IsAssignableFrom(t))
                    RuntimeHelpers.RunClassConstructor(t.TypeHandle);
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
        {
            if (overlayManager == null) return;

            var cam = WorldCamera;

            if (idx < 0 || idx >= shaders.Count)
            {
                overlayManager.ConfigureOverlay(s =>
                {
                    s.TargetCamera = cam;
                    s.Enable = false;
                    s.Execute = null;
                });
                Debug.Log("[MainUi] Overlay disabled.");
                return;
            }

            var effect = shaders[idx].Effect;

            overlayManager.ConfigureOverlay(s =>
            {   // Let effect set up all settings, including RenderMode
                s.TargetCamera = cam;
                s.Enable = true;
                effect(s); // The effect sets RenderMode, BytecodeArgs, etc.
            });
        }

        public static void RebuildUI()
        {
            DestroyUI();

            buttonTints.Clear();

            // Always start disabled on rebuild; selection (if any) will be re-applied below
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

            // Re-apply selection after rebuild (if still valid)
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
            // Prefer the intended element if present; otherwise fall back.
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
            if (overlayManager == null || idx < 0 || idx >= shaders.Count)
                return;

            if (selectedShader == idx)
            {
                selectedShader = -1;
                ConfigureOverlay(-1);
                Lib.Renders.ObjectTarget.Release();
            }
            else
            {
                selectedShader = idx;
                ConfigureOverlay(idx);
            }

            UpdateButtonHighlights();
        }

        private static void UpdateButtonHighlights()
        {
            for (int i = 0; i < buttonTints.Count; i++)
            {
            var bt = buttonTints[i];
            if (bt.Graphic == null) continue;

            // Inline conditional to set highlight
                bt.Graphic.color = (i == selectedShader) ? (bt.Normal + HighlightAdd) : bt.Normal;
            }
        
        }
    }
}
