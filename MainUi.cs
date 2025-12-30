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
        protected BaseShaderEffect(string name, string description)
        {
            MainUi.RegisterShader(name, description, ApplyEffect);
        }

        protected abstract void ApplyEffect(VisualOverlayManager.VisualOverlaySettings settings);
    }

    public static class MainUi
    {
        private struct ShaderInfo
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
            public readonly Graphic Graphic;
            public readonly Color Normal;

            public ButtonTint(Graphic graphic, Color normal)
            {
                Graphic = graphic;
                Normal = normal;
            }
        }

        private static readonly List<ShaderInfo> shaders = new();
        private static readonly List<ButtonTint> buttonTints = new();

        private static VisualOverlayManager overlayManager;
        private static GameObject holder;
        private static ClosableWindow window;

        private static readonly int windowID = Builder.GetRandomID();
        private static int selectedShader = -1;

        // tint to add to the original color for selected button
        private static readonly Color HighlightAdd = new(0.2f, 0.2f, 0.45f, 0f);

        public static void Init()
        {
            ForceLoadAllShaderEffects();

            SceneHelper.OnWorldSceneLoaded += CreateUI;
            SceneHelper.OnBuildSceneLoaded += CreateUI;
            SceneHelper.OnWorldSceneUnloaded += DestroyUI;
            SceneHelper.OnBuildSceneUnloaded += DestroyUI;
        }

        public static void SetOverlayManager(VisualOverlayManager mgr) => overlayManager = mgr;

        public static void RegisterShader(string name, string description, ShaderEffectDelegate effect)
        {
            shaders.Add(new ShaderInfo(name, description, effect));
            Debug.Log($"[FrameEmbededState] Registered shader: {name} - {description}");

            // Rebuild if the window already exists
            if (window != null) CreateUI();
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

        private static Camera GetWorldCamera() => GameCamerasManager.main?.world_Camera?.camera;

        private static void SetOverlayEnabled(bool enable)
        {
            if (overlayManager == null) return;
            var cam = GetWorldCamera();
            overlayManager.ConfigureOverlay(s =>
            {
                s.TargetCamera = cam;
                s.Enable = enable;
            });
        }

        public static void CreateUI()
        {
            DestroyUI();

            buttonTints.Clear();

            // Ensure overlay is bound to the current camera and disabled by default
            SetOverlayEnabled(false);

            int width = 340;
            int height = 70 + shaders.Count * 44;

            holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "Shader Selector Holder");
            window = UIToolsBuilder.CreateClosableWindow(
                holder.transform, windowID, width, height, 0, 0,
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

            // Clear layout children (defensive)
            foreach (Transform child in layout.transform)
                UnityEngine.Object.Destroy(child.gameObject);

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

            UpdateButtonHighlights();

            // Re-apply selected shader after rebuild
            if (selectedShader >= 0 && selectedShader < shaders.Count)
                ApplyShader(selectedShader, updateSelection: false);
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
            // Prefer BackOverTint image (matches your original intent)
            Graphic g = null;

            var backOverTint = buttonRect.transform.Find("BackOverTint");
            if (backOverTint != null)
                g = backOverTint.GetComponent<Image>();

            // Fallback: any Graphic on button or children
            if (g == null)
                g = buttonRect.GetComponent<Graphic>() ?? buttonRect.GetComponentInChildren<Graphic>();

            // If still null, do nothing
            if (g == null) return;

            buttonTints.Add(new ButtonTint(g, g.color));
        }

        private static void ApplyShader(int idx, bool updateSelection)
        {
            if (overlayManager == null || idx < 0 || idx >= shaders.Count)
                return;

            if (updateSelection)
                selectedShader = idx;

            var cam = GetWorldCamera();
            var effect = shaders[idx].Effect;

            overlayManager.ConfigureOverlay(settings =>
            {
                settings.TargetCamera = cam;
                settings.Enable = true;
                settings.Execute = _ => effect(settings);
            });

            UpdateButtonHighlights();
        }

        private static void ToggleShader(int idx)
        {
            if (overlayManager == null || idx < 0 || idx >= shaders.Count)
                return;

            if (selectedShader == idx)
            {
                selectedShader = -1;
                SetOverlayEnabled(false);
                Lib.Renders.ObjectTarget.Release();
            }
            else
            {
                ApplyShader(idx, updateSelection: true);
            }

            UpdateButtonHighlights();
        }

        private static void UpdateButtonHighlights()
        {
            for (int i = 0; i < buttonTints.Count; i++)
            {
                var tint = buttonTints[i];
                if (tint.Graphic == null) continue;

                if (i == selectedShader)
                {
                    var n = tint.Normal;
                    tint.Graphic.color = new Color(
                        Mathf.Clamp01(n.r + HighlightAdd.r),
                        Mathf.Clamp01(n.g + HighlightAdd.g),
                        Mathf.Clamp01(n.b + HighlightAdd.b),
                        Mathf.Clamp01(n.a + HighlightAdd.a)
                    );
                }
                else
                {
                    tint.Graphic.color = tint.Normal;
                }
            }
        }
    }
}
