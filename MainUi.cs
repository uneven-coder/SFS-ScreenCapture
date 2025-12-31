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
using FrameEmbededState.Lib.Renders;

namespace FrameEmbededState
{
    public static class MainUi
    {
        private readonly struct ShaderInfo
        {
            public readonly string Name;
            public readonly string Description;
            public readonly string ShaderSource;
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
        private static readonly Dictionary<string, object> currentArgs = new();
        private static readonly Dictionary<string, Dictionary<string, object>> userArgs = new(); // Store per-shader user overrides

        public static void Init()
        {   // Register shaders and hook scene events
            RegisterAllShaders();
            SceneHelper.OnWorldSceneLoaded += RebuildUI;
            SceneHelper.OnBuildSceneLoaded += RebuildUI;
            SceneHelper.OnWorldSceneUnloaded += DestroyUI;
            SceneHelper.OnBuildSceneUnloaded += DestroyUI;
        }

        public static void RegisterGpuShader(string name, string description, string shaderSource)
        {
            shaders.Add(new ShaderInfo(name, description, shaderSource));
            Debug.Log($"[FrameEmbededState] Registered GPU shader: {name} - {description}");
            RebuildUI();
        }

        private static void RegisterAllShaders()
        {   // Discover and register all shader modules
            shaders.Clear();
            foreach (var module in ShaderRegistry.AllModules)
            {
                shaders.Add(new ShaderInfo(
                    module.Name,
                    $"Shader module: {module.Name}",
                    module.Shader != null ? module.Shader.name : null
                ));
            }
        }

        private static Camera WorldCamera =>
            GameCamerasManager.main?.world_Camera?.camera;

        private static void ConfigureOverlay(int idx)
        {   // Set overlay for selected shader and apply arguments to material
            var cam = WorldCamera;
            if (idx < 0 || idx >= shaders.Count)
            {
                if (selectedShader >= 0)
                {
                    OverlayDispatcher.Render(cam, null, null, null, null, OverlayRenderMode.BehindUI);
                    OverlayDispatcher.SelectedModule = null;
                    OverlayDispatcher.CurrentArgs = null;
                }
                return;
            }
            var info = shaders[idx];
            var module = ShaderRegistry.Get(info.Name);
            if (module != null && module.Shader != null)
            {
                OverlayRenderMode renderMode = OverlayRenderMode.BehindUI;
                var renderTargetProp = module.GetType().GetProperty("RenderTarget");
                if (renderTargetProp != null)
                    renderMode = (OverlayRenderMode)renderTargetProp.GetValue(module);

                OverlayDispatcher.SelectedModule = module;

                // --- Load args: prefer user-edited, else from shader defaults ---
                var argsType = module.GetType().BaseType?.GetGenericArguments()[0];
                object argsObj = null;
                if (argsType != null)
                {
                    // Try to get user-edited values
                    if (!currentArgs.TryGetValue(info.Name, out argsObj) || argsObj == null)
                        argsObj = Activator.CreateInstance(argsType);

                    // Overlay from userArgs if present, else from shader defaults
                    var fields = argsType.GetFields();
                    Dictionary<string, object> userVals = null;
                    userArgs.TryGetValue(info.Name, out userVals);

                    // Try to get defaults from shader/material
                    Material mat = null;
                    try { mat = new Material(module.Shader); } catch { }

                    foreach (var field in fields)
                    {
                        object val = null;
                        if (userVals != null && userVals.TryGetValue(field.Name, out val))
                        {
                            field.SetValue(argsObj, val);
                        }
                        else if (mat != null)
                        {
                            // Try to get from material property (prefix with "_" for common shader naming)
                            string propName = "_" + field.Name;
                            if (field.FieldType == typeof(float) && mat.HasProperty(propName))
                                field.SetValue(argsObj, mat.GetFloat(propName));
                            else if (field.FieldType == typeof(int) && mat.HasProperty(propName))
                                field.SetValue(argsObj, (int)mat.GetFloat(propName));
                            else if (field.FieldType == typeof(Color) && mat.HasProperty(propName))
                                field.SetValue(argsObj, mat.GetColor(propName));
                        }
                        // else leave as default
                    }
                    if (mat != null) UnityEngine.Object.Destroy(mat);

                    currentArgs[info.Name] = argsObj;
                }

                OverlayDispatcher.CurrentArgs = argsObj;
                OverlayDispatcher.Render(cam, null, null, module.Shader, null, renderMode);

                // Reapply arguments to the existing material if it exists
                if (OverlayDispatcher.CurrentMaterial != null && OverlayDispatcher.SelectedModule != null)
                    OverlayDispatcher.SelectedModule.ApplyArgs(OverlayDispatcher.CurrentMaterial, argsObj);
            }
        }

        public static void RebuildUI()
        {   // Build a fully responsive, dynamic UI with always-visible args area

            DestroyUI();
            buttonTints.Clear();

            holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "Shader Selector Holder");

            int minButtonWidth = 160, minArgWidth = 160, padding = 10, spacing = 8;
            int windowWidth = Mathf.Max(600, Screen.width / 2);
            int maxColumns = Mathf.Max(1, (windowWidth - 2 * padding) / (minButtonWidth + spacing));
            int argColumns = Mathf.Max(1, (windowWidth - 2 * padding) / (minArgWidth * 2 + spacing));

            window = UIToolsBuilder.CreateClosableWindow(
                holder.transform, windowID, windowWidth, 600, 0, 0,
                true, true, 1f, "Shader Selector", false
            );
            var layout = window.CreateLayoutGroup(
                SFS.UI.ModGUI.Type.Vertical,
                TextAnchor.UpperCenter,
                spacing,
                new RectOffset(padding, padding, padding, padding),
                true
            );
            window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

            // Shader buttons (responsive rows/columns)
            int shaderCount = shaders.Count;
            for (int i = 0; i < shaderCount; i += maxColumns)
            {
                var row = Builder.CreateContainer(window, 0, 0);
                row.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, spacing, null, true);
                for (int j = 0; j < maxColumns && i + j < shaderCount; j++)
                {
                    int idx = i + j;
                    var btn = Builder.CreateButton(
                        parent: row,
                        width: minButtonWidth,
                        height: 38,
                        posX: 0,
                        posY: 0,
                        text: shaders[idx].Name,
                        onClick: () => ToggleShader(idx)
                    );
                    CacheButtonTint(btn.rectTransform);
                }
            }

            Builder.CreateSeparator(window, windowWidth - 2 * padding, 0, 0);

            // Always show the argument area, even if no shader is selected
            var argsPanel = Builder.CreateContainer(window, 0, 0);
            argsPanel.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, spacing, null, true);

            if (selectedShader >= 0 && selectedShader < shaders.Count)
            {   // Show and apply args for selected shader
                var module = ShaderRegistry.Get(shaders[selectedShader].Name);
                if (module != null)
                {
                    // Ensure args exist and are loaded from user/shader defaults
                    var info = shaders[selectedShader];
                    var argsType = module.GetType().BaseType?.GetGenericArguments()[0];
                    object args = null;
                    if (argsType != null)
                    {
                        if (!currentArgs.TryGetValue(info.Name, out args) || args == null)
                            args = Activator.CreateInstance(argsType);

                        // Overlay from userArgs if present, else from shader defaults
                        var fields = argsType.GetFields();
                        Dictionary<string, object> userVals = null;
                        userArgs.TryGetValue(info.Name, out userVals);

                        Material mat = null;
                        try { mat = new Material(module.Shader); } catch { }

                        foreach (var field in fields)
                        {
                            object val = null;
                            if (userVals != null && userVals.TryGetValue(field.Name, out val))
                                field.SetValue(args, val);
                            else if (mat != null)
                            {
                                if (field.FieldType == typeof(float) && mat.HasProperty(field.Name))
                                    field.SetValue(args, mat.GetFloat(field.Name));
                                else if (field.FieldType == typeof(int) && mat.HasProperty(field.Name))
                                    field.SetValue(args, (int)mat.GetFloat(field.Name));
                                else if (field.FieldType == typeof(Color) && mat.HasProperty(field.Name))
                                    field.SetValue(args, mat.GetColor(field.Name));
                            }
                        }
                        if (mat != null) UnityEngine.Object.Destroy(mat);

                        currentArgs[info.Name] = args;

                        // Apply effect immediately
                        ConfigureOverlay(selectedShader);

                        for (int i = 0; i < fields.Length; i += argColumns)
                        {
                            var row = Builder.CreateContainer(argsPanel, 0, 0);
                            row.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, spacing, null, true);
                            for (int j = 0; j < argColumns && i + j < fields.Length; j++)
                            {
                                var field = fields[i + j];
                                object fieldVal = field.GetValue(args);
                                string fieldStr = fieldVal is Color c ? $"#{ColorUtility.ToHtmlStringRGBA(c)}" : fieldVal?.ToString() ?? "";
                                Builder.CreateLabel(row, minArgWidth, 38, 0, 0, field.Name);
                                Builder.CreateTextInput(row, minArgWidth, 38, 0, 0, fieldStr, val =>
                                {
                                    try
                                    {
                                        object parsedValue = val;
                                        if (field.FieldType == typeof(float))
                                            parsedValue = float.TryParse(val, out var f) ? f : field.GetValue(args);
                                        else if (field.FieldType == typeof(int))
                                            parsedValue = int.TryParse(val, out var i) ? i : field.GetValue(args);
                                        else if (field.FieldType == typeof(Color))
                                            parsedValue = ColorUtility.TryParseHtmlString(val, out var color) ? color : field.GetValue(args);

                                        field.SetValue(args, parsedValue);

                                        // Store user-edited value for this shader/field
                                        if (!userArgs.TryGetValue(info.Name, out var dict) || dict == null)
                                            userArgs[info.Name] = dict = new Dictionary<string, object>();
                                        dict[field.Name] = parsedValue;

                                        // Update current args (apply only on Apply button press)
                                        currentArgs[info.Name] = args;
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogWarning($"Failed to parse {field.Name}: {ex.Message}");
                                    }
                                });
                            }
                        }
                    }
                }
            }
            else
            {   // No shader selected: show a message or empty area
                var label = Builder.CreateLabel(argsPanel, windowWidth - 2 * padding, 38, 0, 0, "Select a shader to edit its arguments.");
            }

            // Add Apply and Restore buttons at the bottom if a shader is selected
            if (selectedShader >= 0 && selectedShader < shaders.Count)
            {
                var buttonRow = Builder.CreateContainer(window, 0, 0);
                buttonRow.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, spacing, null, true);
                Builder.CreateButton(
                    parent: buttonRow,
                    width: minArgWidth,
                    height: 38,
                    posX: 0,
                    posY: 0,
                    text: "Apply",
                    onClick: () => ConfigureOverlay(selectedShader)
                );
                Builder.CreateButton(
                    parent: buttonRow,
                    width: minArgWidth,
                    height: 38,
                    posX: 0,
                    posY: 0,
                    text: "Restore to Default",
                    onClick: () =>
                    {
                        var info = shaders[selectedShader];
                        userArgs.Remove(info.Name);
                        RebuildUI();
                    }
                );
            }

            UpdateButtonHighlights();
        }

        public static void DestroyUI()
        {   // Destroy all UI and clear state
            if (holder != null)
                UnityEngine.Object.Destroy(holder);
            holder = null;
            window = null;
            buttonTints.Clear();
            // Do NOT clear userArgs or currentArgs here, so user edits persist
        }

        private static void CacheButtonTint(RectTransform buttonRect)
        {   // Cache button graphics for highlighting
            Graphic g = null;
            var t = buttonRect.Find("BackOverTint");
            if (t != null) g = t.GetComponent<Image>();
            if (g == null)
                g = buttonRect.GetComponent<Graphic>() ?? buttonRect.GetComponentInChildren<Graphic>();
            if (g == null) return;
            buttonTints.Add(new ButtonTint { Graphic = g, Normal = g.color });
        }

        private static void ToggleShader(int idx)
        {   // Toggle shader selection and update overlay
            if (idx < 0 || idx >= shaders.Count)
                return;
            selectedShader = (selectedShader == idx) ? -1 : idx;
            ConfigureOverlay(selectedShader);
            RebuildUI();
        }

        private static void UpdateButtonHighlights()
        {   // Highlight selected shader button
            for (int i = 0; i < buttonTints.Count; i++)
            {
                var bt = buttonTints[i];
                if (bt.Graphic == null) continue;
                bt.Graphic.color = (i == selectedShader) ? (bt.Normal + HighlightAdd) : bt.Normal;
            }
        }
    }
}
