using System;
using System.Collections.Generic;
using UnityEngine;
using SFS.UI.ModGUI;
using UITools;
using ModLoader.Helpers;
using UnityEngine.UI;
using SFS.World;  // add to lookup game cameras

namespace FrameEmbededState
{
    public delegate void ShaderEffectDelegate(VisualOverlayManager.VisualOverlaySettings settings);

    public static class MainUi
    {
        static readonly List<(string name, string description, ShaderEffectDelegate effect)> shaders = new List<(string, string, ShaderEffectDelegate)>();
        static VisualOverlayManager overlayManager;
        static GameObject holder;
        static ClosableWindow window;
        static readonly int windowID = Builder.GetRandomID();

        static int selectedShader = -1; // Track currently selected shader
        // store button object + its original (normal) color so we can restore it
        static System.Collections.Generic.List<(GameObject obj, Color normalColor)> shaderButtons = new System.Collections.Generic.List<(GameObject, Color)>();
        // tint to add on top of the normal color when highlighted
        static readonly Color HighlightAdd = new Color(0.2f, 0.2f, 0.45f, 0f);

        public static void Init()
        {   // Attach/detach UI on scene load/unload

            FrameEmbededState.dataMosh.EnsureRegistered();
            FrameEmbededState.oldFilter.EnsureRegistered();
            FrameEmbededState.FrameWatermarkEncoder.EnsureRegistered();
            FrameEmbededState.spaceShader.EnsureRegistered();
            FrameEmbededState.WaterShader.EnsureRegistered();
            FrameEmbededState.ChristmasCozyShader.EnsureRegistered();
            FrameEmbededState.Moebius.EnsureRegistered();
            FrameEmbededState.RgbCycleEffect.EnsureRegistered();

            SceneHelper.OnWorldSceneLoaded += CreateUI;
            SceneHelper.OnBuildSceneLoaded += CreateUI;
            SceneHelper.OnWorldSceneUnloaded += DestroyUI;
            SceneHelper.OnBuildSceneUnloaded += DestroyUI;
        }

        public static void RegisterShader(string name, string description, ShaderEffectDelegate effect)
        {   // Register a shader effect for UI selection and log registration
            shaders.Add((name, description, effect));
            Debug.Log($"[FrameEmbededState] Registered shader: {name} - {description}");
            if (window != null)
                CreateUI();
        }

        public static void SetOverlayManager(VisualOverlayManager mgr)
        {   // Set the overlay manager instance
            overlayManager = mgr;
        }

        public static void CreateUI()
        {   // Create the shader selector window and ensure buttons are visible
            DestroyUI();

            shaderButtons = new System.Collections.Generic.List<(GameObject, Color)>();

            if (overlayManager != null)
            {
                var cam = GameCamerasManager.main?.world_Camera?.camera;
                if (cam != null)
                    overlayManager.ConfigureOverlay(s => { s.TargetCamera = cam; s.Enable = false; });
            }

            Debug.Log($"[FrameEmbededState] Creating Shader Selector UI with {shaders.Count} shaders loaded.");
            for (int i = 0; i < shaders.Count; i++)
                Debug.Log($"[FrameEmbededState] Shader {i}: {shaders[i].name} - {shaders[i].description}");

            int width = 340, height = 70 + shaders.Count * 44;
            holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "Shader Selector Holder");
            window = UIToolsBuilder.CreateClosableWindow(holder.transform, windowID, width, height, 0, 0, true, true, 1f, "Shader Selector", false);

            var layout = window.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 8f, new RectOffset(10, 10, 10, 10), true);
            window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

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
                    text: shaders[i].name,
                    onClick: () => ToggleShader(idx)
                );

                // Find the "BackOverTint" child and get its Image component to change the color
                var backOverTint = btn.rectTransform.transform.Find("BackOverTint");
                if (backOverTint != null)
                {
                    var image = backOverTint.GetComponent<Image>();
                    if (image != null)
                    {
                        // capture the normal color (do not overwrite the existing normal color)
                        shaderButtons.Add((backOverTint.gameObject, image.color));
                    }
                }
                else
                {   // keep a fallback reference to the button itself if tint not found
                    // try to capture a fallback image color if available
                    var fallbackImg = btn.rectTransform.GetComponent<Image>() ?? btn.rectTransform.GetComponentInChildren<Image>();
                    var normal = fallbackImg != null ? fallbackImg.color : Color.white;
                    shaderButtons.Add((btn.gameObject, normal));
                }

                btn.rectTransform.transform.SetParent(layout.transform, false);

            }

            // Highlight the currently selected shader (if any)
            UpdateButtonHighlights();

            // Re-apply selected shader if any
            if (selectedShader >= 0 && selectedShader < shaders.Count)
                ApplyShader(selectedShader, updateSelection: false);
        }

        public static void DestroyUI()
        {   // Destroy the shader selector window and holder
            if (holder != null)
                UnityEngine.Object.Destroy(holder);
            holder = null;
            window = null;
            if (shaderButtons != null)
                shaderButtons.Clear();
            shaderButtons = null;
        }

        static void ApplyShader(int idx, bool updateSelection = true)
        {   // Apply the selected shader to the overlay manager
            if (overlayManager == null || idx < 0 || idx >= shaders.Count)
                return;

            if (updateSelection)
                selectedShader = idx;

            var cam = GameCamerasManager.main?.world_Camera?.camera;
            overlayManager.ConfigureOverlay(settings =>
            {   // bind to camera if available and enable this effect
                settings.TargetCamera = cam;
                settings.Enable = true;
                settings.Execute = frameData => shaders[idx].effect(settings);
            });

            UpdateButtonHighlights();
        }

        static void ToggleShader(int idx)
        {   // Toggle shader on/off and restore materials when disabled
            if (overlayManager == null || idx < 0 || idx >= shaders.Count)
                return;

            if (selectedShader == idx)
            {   // disable and restore materials
                selectedShader = -1;
                var cam = GameCamerasManager.main?.world_Camera?.camera;
                overlayManager.ConfigureOverlay(s => { s.TargetCamera = cam; s.Enable = false; });
                Lib.Renders.ObjectTarget.Release();
            }
            else
                ApplyShader(idx, updateSelection: true);

            UpdateButtonHighlights();
        }

        static void UpdateButtonHighlights()
        {   // Update stored button visuals so only selected shader is highlighted
            if (shaderButtons == null) return;

            for (int i = 0; i < shaderButtons.Count; i++)
            {
                var (obj, normal) = shaderButtons[i];
                if (obj == null) continue;

                var image = obj.GetComponent<Image>() ?? obj.GetComponentInChildren<Image>();
                if (image != null)
                {   // for selected: add highlight tint to the original normal color (clamped)
                    if (i == selectedShader)
                    {
                        var added = new Color(
                            Mathf.Clamp01(normal.r + HighlightAdd.r),
                            Mathf.Clamp01(normal.g + HighlightAdd.g),
                            Mathf.Clamp01(normal.b + HighlightAdd.b),
                            Mathf.Clamp01(normal.a + HighlightAdd.a)
                        );
                        image.color = added;
                    }
                    else
                    {   // restore original normal color
                        image.color = normal;
                    }
                    continue;
                }

                // fallback: try to set text color on a child Text component
                var txt = obj.GetComponentInChildren<UnityEngine.UI.Text>();
                if (txt != null)
                    txt.color = (i == selectedShader) ? Color.cyan : Color.white;
            }
        }
    }
}