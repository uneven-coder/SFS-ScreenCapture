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
        private static readonly Dictionary<string, object> shaderProvidedArgs = new();
        private static readonly Dictionary<string, Dictionary<string, object>> userArgs = new();
        private static readonly Dictionary<string, string> lastArgSnapshot = new();
        private static readonly Dictionary<string, TextInput> activeInputFields = new(); // Track active input fields for direct updates

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
        {
            var cam = WorldCamera;
            if (idx < 0 || idx >= shaders.Count)
            {   // Disable overlay when no shader selected
                if (selectedShader >= 0)
                {
                    OverlayDispatcher.Render(cam, null, null, null, null, OverlayRenderMode.BehindUI);
                    OverlayDispatcher.SelectedModule = null;
                    OverlayDispatcher.CurrentArgs = null;
                    CustomRender_Render.Release();
                }
                return;
            }
            
            var info = shaders[idx];
            var module = ShaderRegistry.Get(info.Name);
            if (module == null || module.Shader == null) return;

            OverlayRenderMode renderMode = OverlayRenderMode.BehindUI;
            var renderTargetProp = module.GetType().GetProperty("RenderTarget");
            if (renderTargetProp != null)
                renderMode = (OverlayRenderMode)renderTargetProp.GetValue(module);

            OverlayDispatcher.SelectedModule = module;

            var argsType = module.GetType().BaseType?.GetGenericArguments()[0];
            object argsObj = null;
            if (argsType != null)
            {   // Create args object with proper priority: user > current > shader > default
                if (!currentArgs.TryGetValue(info.Name, out argsObj) || argsObj == null)
                    argsObj = Activator.CreateInstance(argsType);

                var fields = argsType.GetFields();
                Dictionary<string, object> userVals = null;
                userArgs.TryGetValue(info.Name, out userVals);

                object shaderProvided = null;
                shaderProvidedArgs.TryGetValue(info.Name, out shaderProvided);

                Material mat = null;
                try { mat = new Material(module.Shader); } catch { }

                foreach (var field in fields)
                {   // Priority hierarchy: user override > shader runtime > material default
                    if (userVals != null && userVals.TryGetValue(field.Name, out var userVal))
                        field.SetValue(argsObj, userVal);
                    else if (shaderProvided != null)
                    {   // Use shader-provided runtime values
                        var shaderField = shaderProvided.GetType().GetField(field.Name);
                        if (shaderField != null)
                            field.SetValue(argsObj, shaderField.GetValue(shaderProvided));
                    }
                    else if (mat != null)
                    {   // Load shader defaults with proper type conversion
                        string propName = "_" + field.Name;
                        if (field.FieldType == typeof(float) && mat.HasProperty(propName))
                            field.SetValue(argsObj, mat.GetFloat(propName));
                        else if (field.FieldType == typeof(int) && mat.HasProperty(propName))
                            field.SetValue(argsObj, (int)mat.GetFloat(propName));
                        else if (field.FieldType == typeof(Color) && mat.HasProperty(propName))
                            field.SetValue(argsObj, mat.GetColor(propName));
                        else if (field.FieldType == typeof(Vector3) && mat.HasProperty(propName))
                        {   // Convert Vector4 to Vector3
                            Vector4 v4 = mat.GetVector(propName);
                            field.SetValue(argsObj, new Vector3(v4.x, v4.y, v4.z));
                        }
                        else if (field.FieldType == typeof(Vector4) && mat.HasProperty(propName))
                            field.SetValue(argsObj, mat.GetVector(propName));
                    }
                }
                if (mat != null) UnityEngine.Object.Destroy(mat);

                currentArgs[info.Name] = argsObj;
            }

            OverlayDispatcher.CurrentArgs = argsObj;

            if (renderMode == OverlayRenderMode.CustomRender)
            {   // Setup custom render effect and trigger module run
                var effect = cam?.GetComponent<FrameEmbededStateOverlayEffect>();
                if (effect == null)
                    effect = cam?.gameObject.AddComponent<FrameEmbededStateOverlayEffect>();
                
                if (effect != null)
                {
                    effect.renderMode = OverlayRenderMode.CustomRender;
                    effect.customRenderKey = info.Name;
                    effect.selectedShader = module.Shader;
                }

                // Trigger module with current args
                var runMethod = module.GetType().GetMethod("Run");
                if (runMethod != null && argsObj != null)
                    runMethod.Invoke(module, new[] { argsObj });
            }
            else
            {   // Apply standard overlay rendering
                OverlayDispatcher.Render(cam, null, null, module.Shader, null, renderMode);

                if (OverlayDispatcher.CurrentMaterial != null && OverlayDispatcher.SelectedModule != null)
                    OverlayDispatcher.SelectedModule.ApplyArgs(OverlayDispatcher.CurrentMaterial, argsObj);
            }
        }

        public static void UpdateShaderProvidedArgs(string shaderName, object args)
        {   // Called by shader module to update runtime-calculated values
            if (args == null) return;

            shaderProvidedArgs[shaderName] = args;

            var argsType = args.GetType();
            var fields = argsType.GetFields();
            var snapshot = string.Join("|", System.Array.ConvertAll(fields, f => $"{f.Name}:{f.GetValue(args)}"));
            
            if (!lastArgSnapshot.TryGetValue(shaderName, out var oldSnapshot) || oldSnapshot != snapshot)
            {   // Values changed, update input fields directly if this shader is selected
                lastArgSnapshot[shaderName] = snapshot;
                if (selectedShader >= 0 && selectedShader < shaders.Count && shaders[selectedShader].Name == shaderName)
                    UpdateInputFieldsFromShaderArgs(shaderName, args);
            }
        }

        private static void UpdateInputFieldsFromShaderArgs(string shaderName, object args)
        {   // Update text input fields directly without rebuilding UI
            if (args == null) return;

            var argsType = args.GetType();
            var fields = argsType.GetFields();

            Dictionary<string, object> userVals = null;
            userArgs.TryGetValue(shaderName, out userVals);

            foreach (var field in fields)
            {   // Skip user-edited fields, only update shader-provided values
                if (userVals != null && userVals.ContainsKey(field.Name)) continue;

                var key = $"{shaderName}_{field.Name}";
                if (activeInputFields.TryGetValue(key, out var input) && input != null)
                {
                    var fieldVal = field.GetValue(args);
                    var newText = FormatFieldValue(fieldVal);
                    if (input.Text != newText)
                        input.Text = newText;
                }
            }
        }

        public static object GetCurrentArgs(string shaderName)
        {   // Allow shader modules to retrieve merged user + default args
            currentArgs.TryGetValue(shaderName, out var args);
            return args;
        }

        public static Dictionary<string, object> GetUserArgs(string shaderName)
        {   // Expose user-edited arguments to shader modules
            userArgs.TryGetValue(shaderName, out var dict);
            return dict;
        }

        public static void RebuildUI()
        {   // Build a fully responsive, dynamic UI with always-visible args area

            DestroyUI();
            buttonTints.Clear();
            activeInputFields.Clear();

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
            {   // Create rows of shader selection buttons
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
                {   // Build argument editing interface
                    var info = shaders[selectedShader];
                    var argsType = module.GetType().BaseType?.GetGenericArguments()[0];
                    object args = null;
                    if (argsType != null)
                    {   // Merge shader-provided, user-edited, and default values
                        args = Activator.CreateInstance(argsType);
                        var fields = argsType.GetFields();

                        Material mat = null;
                        try { mat = new Material(module.Shader); } catch { }

                        object shaderProvided = null;
                        shaderProvidedArgs.TryGetValue(info.Name, out shaderProvided);

                        Dictionary<string, object> userVals = null;
                        userArgs.TryGetValue(info.Name, out userVals);

                        foreach (var field in fields)
                        {   // Priority: user edit > shader runtime > material default
                            if (userVals != null && userVals.TryGetValue(field.Name, out var userVal))
                                field.SetValue(args, userVal);
                            else if (shaderProvided != null)
                            {
                                var shaderField = shaderProvided.GetType().GetField(field.Name);
                                if (shaderField != null)
                                    field.SetValue(args, shaderField.GetValue(shaderProvided));
                            }
                            else if (mat != null)
                            {   // Fallback to material defaults with proper type conversion
                                string propName = "_" + field.Name;
                                if (field.FieldType == typeof(float) && mat.HasProperty(propName))
                                    field.SetValue(args, mat.GetFloat(propName));
                                else if (field.FieldType == typeof(int) && mat.HasProperty(propName))
                                    field.SetValue(args, (int)mat.GetFloat(propName));
                                else if (field.FieldType == typeof(Color) && mat.HasProperty(propName))
                                    field.SetValue(args, mat.GetColor(propName));
                                else if (field.FieldType == typeof(Vector3) && mat.HasProperty(propName))
                                {   // Convert Vector4 to Vector3
                                    Vector4 v4 = mat.GetVector(propName);
                                    field.SetValue(args, new Vector3(v4.x, v4.y, v4.z));
                                }
                                else if (field.FieldType == typeof(Vector4) && mat.HasProperty(propName))
                                    field.SetValue(args, mat.GetVector(propName));
                            }
                        }
                        if (mat != null) UnityEngine.Object.Destroy(mat);

                        currentArgs[info.Name] = args;

                        for (int i = 0; i < fields.Length; i += argColumns)
                        {   // Create input fields for each argument
                            var row = Builder.CreateContainer(argsPanel, 0, 0);
                            row.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperCenter, spacing, null, true);
                            for (int j = 0; j < argColumns && i + j < fields.Length; j++)
                            {
                                var field = fields[i + j];
                                object fieldVal = field.GetValue(args);
                                string fieldStr = FormatFieldValue(fieldVal);
                                
                                Builder.CreateLabel(row, minArgWidth, 38, 0, 0, field.Name);
                                var input = Builder.CreateTextInput(row, minArgWidth, 38, 0, 0, fieldStr, val =>
                                {   // Handle user edits to arguments
                                    try
                                    {
                                        object parsedValue = ParseFieldValue(val, field.FieldType, field.GetValue(args));
                                        field.SetValue(args, parsedValue);

                                        if (!userArgs.TryGetValue(info.Name, out var dict) || dict == null)
                                            userArgs[info.Name] = dict = new Dictionary<string, object>();
                                        dict[field.Name] = parsedValue;

                                        currentArgs[info.Name] = args;
                                    }
                                    catch (Exception ex) { Debug.LogWarning($"Failed to parse {field.Name}: {ex.Message}"); }
                                });

                                activeInputFields[$"{info.Name}_{field.Name}"] = input;
                            }
                        }
                    }
                }
            }
            else
                Builder.CreateLabel(argsPanel, windowWidth - 2 * padding, 38, 0, 0, "Select a shader to edit its arguments.");

            // Add Apply and Restore buttons at the bottom if a shader is selected
            if (selectedShader >= 0 && selectedShader < shaders.Count)
            {   // Control buttons for shader configuration
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
                    {   // Clear user overrides and return to shader-provided values
                        var info = shaders[selectedShader];
                        userArgs.Remove(info.Name);
                        RebuildUI();
                    }
                );
            }

            UpdateButtonHighlights();
        }

        private static string FormatFieldValue(object fieldVal)
        {   // Convert field value to display string with proper formatting for all types
            if (fieldVal is Color c) return $"#{ColorUtility.ToHtmlStringRGBA(c)}";
            if (fieldVal is Vector3 v3) return $"{v3.x:F6},{v3.y:F6},{v3.z:F6}";
            if (fieldVal is Vector4 v4) return $"{v4.x:F6},{v4.y:F6},{v4.z:F6},{v4.w:F6}";
            if (fieldVal is float f) return f.ToString("F6");
            return fieldVal?.ToString() ?? "";
        }

        private static object ParseFieldValue(string val, System.Type fieldType, object currentValue)
        {   // Parse user input string to field type with support for vectors and proper float parsing
            if (fieldType == typeof(float))
                return float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : currentValue;
            
            if (fieldType == typeof(int))
                return int.TryParse(val, out var i) ? i : currentValue;
            
            if (fieldType == typeof(Color))
                return ColorUtility.TryParseHtmlString(val, out var color) ? color : currentValue;
            
            if (fieldType == typeof(Vector3))
            {   // Parse comma-separated vector values with invariant culture
                var parts = val.Split(',');
                if (parts.Length == 3 && 
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) && 
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) && 
                    float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                    return new Vector3(x, y, z);
            }
            
            if (fieldType == typeof(Vector4))
            {   // Parse comma-separated vector4 values with invariant culture
                var parts = val.Split(',');
                if (parts.Length == 4 && 
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) && 
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) && 
                    float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z) && 
                    float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
                    return new Vector4(x, y, z, w);
            }
            
            return currentValue;
        }

        public static void DestroyUI()
        {   // Destroy all UI and clear state
            if (holder != null)
                UnityEngine.Object.Destroy(holder);
            holder = null;
            window = null;
            buttonTints.Clear();
            activeInputFields.Clear();
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
