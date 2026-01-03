using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;
using UITools;
using ModLoader.Helpers;
using SFS.World;
using FrameEmbededState.Lib.Renders;
using SysType = System.Type;

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

        private struct ArgGroup
        {
            public string ModuleName;
            public string Category;
            public List<KeyValuePair<string, object>> Fields;
        }

        // -------------------------
        // UI layout metrics (single place to tweak/expand)
        // -------------------------
        private readonly struct UiMetrics
        {
            public readonly int WindowWidth;
            public readonly int WindowHeight;

            public readonly int Padding;
            public readonly int Spacing;

            public readonly int MinShaderButtonWidth;
            public readonly int ShaderButtonHeight;

            public readonly int MinArgWidth;
            public readonly int ArgRowHeight;

            public readonly int FoldoutButtonSize;

            public readonly int ShaderGridColumns;
            public readonly int ArgColumns;

            public UiMetrics(
                int windowWidth,
                int windowHeight,
                int padding,
                int spacing,
                int minShaderButtonWidth,
                int shaderButtonHeight,
                int minArgWidth,
                int argRowHeight,
                int foldoutButtonSize,
                int shaderGridColumns,
                int argColumns)
            {
                WindowWidth = windowWidth;
                WindowHeight = windowHeight;
                Padding = padding;
                Spacing = spacing;
                MinShaderButtonWidth = minShaderButtonWidth;
                ShaderButtonHeight = shaderButtonHeight;
                MinArgWidth = minArgWidth;
                ArgRowHeight = argRowHeight;
                FoldoutButtonSize = foldoutButtonSize;
                ShaderGridColumns = shaderGridColumns;
                ArgColumns = argColumns;
            }
        }

        private static readonly UiMetrics UI = new UiMetrics(
            windowWidth: 580,
            windowHeight: 600,
            padding: 8,
            spacing: 6,
            minShaderButtonWidth: 120,
            shaderButtonHeight: 32,
            minArgWidth: 140,
            argRowHeight: 32,
            foldoutButtonSize: 24,
            shaderGridColumns: 4,
            argColumns: 2
        );

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
        private static readonly Dictionary<string, TextInput> activeInputFields = new();
        private static readonly Dictionary<string, bool> groupFoldoutStates = new();
        private static Camera _cachedCamera;
        private static bool _pendingCameraSwitch;

        public static void Init()
        {
            RegisterAllShaders();
            SceneHelper.OnWorldSceneLoaded += RebuildUI;
            SceneHelper.OnBuildSceneLoaded += RebuildUI;
            SceneHelper.OnWorldSceneUnloaded += DestroyUI;
            SceneHelper.OnBuildSceneUnloaded += DestroyUI;
            
            if (SFS.Cameras.ActiveCamera.main?.activeCamera.Value != null)
                SFS.Cameras.ActiveCamera.main.activeCamera.OnChange += OnCameraChange;
        }

        public static void RegisterGpuShader(string name, string description, string shaderSource)
        {
            shaders.Add(new ShaderInfo(name, description, shaderSource));
            Debug.Log($"[FrameEmbededState] Registered GPU shader: {name} - {description}");
            RebuildUI();
        }

        private static void RegisterAllShaders()
        {
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

        private static Camera WorldCamera
        {
            get
            {   // Always get fresh camera reference to handle zoom transitions
                
                Camera activeCam = GameCamerasManager.main?.world_Camera?.camera ?? GameCamerasManager.main?.scaledWorld_Camera?.camera;
                
                if (activeCam != null && activeCam.gameObject.activeInHierarchy && activeCam.enabled)
                {
                    if (_cachedCamera != activeCam)
                    {   // Camera changed, schedule reattachment
                        _cachedCamera = activeCam;
                        _pendingCameraSwitch = true;
                    }
                    return _cachedCamera;
                }

                return _cachedCamera;
            }
        }

        private static void OnCameraChange()
        {   // Force refresh when camera changes ensuring materials update
            
            Camera newCam = GameCamerasManager.main?.world_Camera?.camera ?? GameCamerasManager.main?.scaledWorld_Camera?.camera;

            if (newCam != null && newCam.enabled && newCam.gameObject.activeInHierarchy)
            {
                _cachedCamera = newCam;
                _pendingCameraSwitch = true;

                Debug.Log($"[MainUi] Camera changed to: {newCam?.name ?? "null"}");

                UnityEngine.Object.FindObjectsOfType<FrameEmbededStateOverlayEffect>()
                    .Where(e => e != null && e.gameObject != newCam.gameObject)
                    .ToList()
                    .ForEach(e => UnityEngine.Object.Destroy(e));

                if (selectedShader >= 0)
                    ConfigureOverlay(selectedShader);
            }
        }

        public static void NotifyCameraInactive()
        {   // Called when a camera becomes inactive to trigger switch check
            _pendingCameraSwitch = true;
            
            if (selectedShader >= 0)
            {
                var newCam = WorldCamera;
                if (newCam != null && newCam != _cachedCamera)
                    ConfigureOverlay(selectedShader);
            }
        }

        private static void ConfigureOverlay(int idx)
        {   // Configure overlay for selected shader with current camera
            
            var cam = WorldCamera;

            if (cam == null)
            {
                Debug.LogWarning("[MainUi] No valid camera available for overlay configuration");
                return;
            }

            if (_pendingCameraSwitch)
            {   // Clean up effects on old cameras before switching
                UnityEngine.Object.FindObjectsOfType<FrameEmbededStateOverlayEffect>()
                    .Where(e => e != null && e.gameObject != cam.gameObject)
                    .ToList()
                    .ForEach(e => UnityEngine.Object.Destroy(e));

                _pendingCameraSwitch = false;
            }

            if (idx < 0 || idx >= shaders.Count)
            {
                if (selectedShader >= 0)
                {
                    OverlayDispatcher.Render(cam, null, null, null, null, OverlayRenderMode.BehindUI);
                    OverlayDispatcher.SelectedModule = null;
                    OverlayDispatcher.CurrentArgs = null;
                    CustomRender_Render.Release();
                    
                    var effect = cam?.GetComponent<FrameEmbededStateOverlayEffect>();
                    if (effect != null)
                        UnityEngine.Object.Destroy(effect);
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
            {
                if (!currentArgs.TryGetValue(info.Name, out argsObj) || argsObj == null)
                    argsObj = Activator.CreateInstance(argsType);

                var fields = argsType.GetFields();
                userArgs.TryGetValue(info.Name, out var userVals);
                shaderProvidedArgs.TryGetValue(info.Name, out var shaderProvided);

                Material mat = null;
                try { mat = new Material(module.Shader); } catch { }

                foreach (var field in fields)
                {
                    if (userVals != null && userVals.TryGetValue(field.Name, out var userVal))
                        field.SetValue(argsObj, userVal);
                    else if (shaderProvided != null)
                    {
                        var shaderField = shaderProvided.GetType().GetField(field.Name);
                        if (shaderField != null)
                            field.SetValue(argsObj, shaderField.GetValue(shaderProvided));
                    }
                    else if (mat != null)
                    {
                        string propName = "_" + field.Name;

                        if (field.FieldType == typeof(float) && mat.HasProperty(propName))
                            field.SetValue(argsObj, mat.GetFloat(propName));
                        else if (field.FieldType == typeof(int) && mat.HasProperty(propName))
                            field.SetValue(argsObj, (int)mat.GetFloat(propName));
                        else if (field.FieldType == typeof(Color) && mat.HasProperty(propName))
                            field.SetValue(argsObj, mat.GetColor(propName));
                        else if (field.FieldType == typeof(Vector3) && mat.HasProperty(propName))
                        {
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

            var existingEffect = cam?.GetComponent<FrameEmbededStateOverlayEffect>();

            if (renderMode == OverlayRenderMode.CustomRender)
            {
                if (existingEffect == null)
                    existingEffect = cam?.gameObject.AddComponent<FrameEmbededStateOverlayEffect>();

                if (existingEffect != null)
                {
                    existingEffect.renderMode = OverlayRenderMode.CustomRender;
                    existingEffect.customRenderKey = info.Name;
                    existingEffect.selectedShader = module.Shader;
                }

                var runMethod = module.GetType().GetMethod("Run");
                if (runMethod != null && argsObj != null)
                    runMethod.Invoke(module, new[] { argsObj });
            }
            else
            {
                if (existingEffect != null)
                {   // Update existing effect instead of recreating
                    existingEffect.renderMode = renderMode;
                    existingEffect.selectedShader = module.Shader;
                    existingEffect.customRenderKey = info.Name;
                }

                OverlayDispatcher.Render(cam, null, null, module.Shader, null, renderMode);

                if (OverlayDispatcher.CurrentMaterial != null && OverlayDispatcher.SelectedModule != null)
                    OverlayDispatcher.SelectedModule.ApplyArgs(OverlayDispatcher.CurrentMaterial, argsObj);
            }
        }

        public static void UpdateShaderProvidedArgs(string shaderName, object args)
        {
            if (args == null) return;

            shaderProvidedArgs[shaderName] = args;

            var argsType = args.GetType();
            var fields = argsType.GetFields();
            var snapshot = string.Join("|", Array.ConvertAll(fields, f => $"{f.Name}:{f.GetValue(args)}"));

            if (!lastArgSnapshot.TryGetValue(shaderName, out var oldSnapshot) || oldSnapshot != snapshot)
            {
                lastArgSnapshot[shaderName] = snapshot;

                if (selectedShader >= 0 && selectedShader < shaders.Count && shaders[selectedShader].Name == shaderName)
                    UpdateInputFieldsFromShaderArgs(shaderName, args);
            }
        }

        private static void UpdateInputFieldsFromShaderArgs(string shaderName, object args)
        {
            if (args == null) return;

            userArgs.TryGetValue(shaderName, out var userVals);

            var flatFields = FlattenFields(args, "");
            foreach (var kvp in flatFields)
            {
                if (userVals != null && userVals.ContainsKey(kvp.Key)) continue;

                var key = $"{shaderName}_{kvp.Key}";
                if (activeInputFields.TryGetValue(key, out var input) && input != null)
                {
                    var newText = FormatFieldValue(kvp.Value);
                    if (input.Text != newText)
                        input.Text = newText;
                }
            }
        }

        private static Dictionary<string, object> FlattenFields(object obj, string prefix)
        {   // Recursively flatten nested fields into a dictionary with dot-notation keys
            var result = new Dictionary<string, object>();
            if (obj == null) return result;

            var type = obj.GetType();
            var fields = type.GetFields();

            foreach (var field in fields)
            {   // Process each field and handle nested structures
                var fieldVal = field.GetValue(obj);
                var fieldPath = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";

                if (fieldVal == null || IsSimpleType(field.FieldType))
                    result[fieldPath] = fieldVal;
                else if (field.FieldType.IsArray)
                {   // Handle array fields by indexing each element
                    var arr = fieldVal as Array;
                    if (arr != null)
                    {   // Iterate through array elements
                        for (int i = 0; i < arr.Length; i++)
                        {   // Process each array element
                            var element = arr.GetValue(i);
                            var elementPath = $"{fieldPath}[{i}]";

                            if (element != null && !IsSimpleType(element.GetType()))
                            {   // Recursively flatten complex array elements
                                var nested = FlattenFields(element, elementPath);
                                foreach (var nkvp in nested)
                                    result[nkvp.Key] = nkvp.Value;
                            }
                            else
                                result[elementPath] = element;
                        }
                    }
                }
                else if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
                {   // Recursively flatten nested value types
                    var nested = FlattenFields(fieldVal, fieldPath);
                    foreach (var nkvp in nested)
                        result[nkvp.Key] = nkvp.Value;
                }
                else
                    result[fieldPath] = fieldVal;
            }

            return result;
        }

        private static bool IsSimpleType(SysType type)
        {   // Determine if a type is considered simple for UI display
            return type.IsPrimitive || type.IsEnum || type == typeof(string) ||
                   type == typeof(Color) || type == typeof(Vector3) || type == typeof(Vector4) ||
                   type == typeof(Texture2D);
        }

        public static object GetCurrentArgs(string shaderName)
        {
            currentArgs.TryGetValue(shaderName, out var args);
            return args;
        }

        public static Dictionary<string, object> GetUserArgs(string shaderName)
        {
            userArgs.TryGetValue(shaderName, out var dict);
            return dict;
        }

        // =========================================================
        // UI REBUILD (refactored)
        // =========================================================
        public static void RebuildUI()
        {
            DestroyUI();
            ResetUiCaches();

            CreateUiRoot();
            ConfigureMainWindowLayout();

            // Layout hierarchy is explicit:
            // Window
            //  ├─ Shader grid
            //  ├─ Separator
            //  ├─ Args panel
            //  └─ Footer buttons (Apply / Restore)
            BuildShaderGrid();
            BuildSeparator();
            BuildArgsPanel();
            BuildFooterButtons();

            UpdateButtonHighlights();
        }

        private static void ResetUiCaches()
        {
            buttonTints.Clear();
            activeInputFields.Clear();
        }

        private static void CreateUiRoot()
        {
            holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "Shader Selector Holder");
            window = UIToolsBuilder.CreateClosableWindow(
                holder.transform,
                windowID,
                UI.WindowWidth,
                UI.WindowHeight,
                0,
                0,
                true,
                true,
                1f,
                "Shader Selector",
                false
            );
        }

        private static void ConfigureMainWindowLayout()
        {   // Configure the main window with vertical layout and scrolling
            window.CreateLayoutGroup(
                SFS.UI.ModGUI.Type.Vertical,
                TextAnchor.UpperLeft,
                UI.Spacing,
                new RectOffset(UI.Padding, UI.Padding, UI.Padding, UI.Padding),
                true
            );
            window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);
        }

        // -------------------------
        // Section: Shader selection grid
        // -------------------------
        private static void BuildShaderGrid()
        {
            int shaderCount = shaders.Count;

            for (int start = 0; start < shaderCount; start += UI.ShaderGridColumns)
            {
                var row = CreateHorizontalRow(window);
                for (int col = 0; col < UI.ShaderGridColumns && start + col < shaderCount; col++)
                {
                    int idx = start + col;
                    CreateShaderSelectButton(row, idx);
                }
            }
        }

        private static void CreateShaderSelectButton(RectTransform parentRow, int shaderIndex)
        {
            var btn = Builder.CreateButton(
                parent: parentRow,
                width: UI.MinShaderButtonWidth,
                height: UI.ShaderButtonHeight,
                posX: 0,
                posY: 0,
                text: shaders[shaderIndex].Name,
                onClick: () => ToggleShader(shaderIndex)
            );
            CacheButtonTint(btn.rectTransform);
        }

        // -------------------------
        // Section: Separator
        // -------------------------
        private static void BuildSeparator()
        {
            Builder.CreateSeparator(window, UI.WindowWidth - 2 * UI.Padding, 0, 0);
        }

        // -------------------------
        // Section: Args panel
        // -------------------------
        private static void BuildArgsPanel()
        {   // Build the arguments panel for the selected shader
            var argsPanel = CreateVerticalGroup(window);

            if (selectedShader < 0 || selectedShader >= shaders.Count)
            {
                Builder.CreateLabel(argsPanel, UI.WindowWidth - 2 * UI.Padding, 32, 0, 0, "Select a shader to edit its arguments.");
                return;
            }

            var info = shaders[selectedShader];
            var module = ShaderRegistry.Get(info.Name);
            if (module == null)
            {
                Builder.CreateLabel(argsPanel, UI.WindowWidth - 2 * UI.Padding, 32, 0, 0, "Shader module not found.");
                return;
            }

            Builder.CreateLabel(argsPanel, UI.WindowWidth - 2 * UI.Padding, 28, 0, 0, $"<b>{info.Name}</b> Shader Arguments");

            var groups = BuildArgGroups(module, info);

            foreach (var group in groups)
            {
                BuildArgGroup(argsPanel, info, group);
            }
        }

        private static void BuildArgGroup(RectTransform argsPanel, ShaderInfo selectedInfo, ArgGroup group)
        {
            var groupKey = $"{group.ModuleName}_{group.Category}";
            EnsureFoldoutState(groupKey);

            // Header row (foldout + label)
            var headerRow = CreateHorizontalRow(argsPanel);
            CreateFoldoutButton(headerRow, groupKey);

            var categoryText = group.ModuleName != selectedInfo.Name
                ? $"{group.ModuleName} - {group.Category}"
                : group.Category;

            Builder.CreateLabel(headerRow, UI.WindowWidth - 2 * UI.Padding - (UI.FoldoutButtonSize + UI.Spacing), UI.FoldoutButtonSize, 0, 0, categoryText);

            if (!groupFoldoutStates[groupKey]) return;

            // Fields (grid within this group)
            BuildArgFieldGrid(argsPanel, selectedInfo.Name, group);
        }

        private static void EnsureFoldoutState(string groupKey)
        {
            if (!groupFoldoutStates.ContainsKey(groupKey))
                groupFoldoutStates[groupKey] = true;
        }

        private static void CreateFoldoutButton(RectTransform headerRow, string groupKey)
        {
            // Capture key for closure
            string capturedKey = groupKey;

            Builder.CreateButton(
                parent: headerRow,
                width: UI.FoldoutButtonSize,
                height: UI.FoldoutButtonSize,
                posX: 0,
                posY: 0,
                text: groupFoldoutStates[capturedKey] ? "▼" : "►",
                onClick: () =>
                {
                    groupFoldoutStates[capturedKey] = !groupFoldoutStates[capturedKey];
                    RebuildUI();
                }
            );
        }

        private static void BuildArgFieldGrid(RectTransform argsPanel, string shaderName, ArgGroup group)
        {
            for (int i = 0; i < group.Fields.Count; i += UI.ArgColumns)
            {
                var row = CreateHorizontalRow(argsPanel);

                for (int j = 0; j < UI.ArgColumns && i + j < group.Fields.Count; j++)
                {
                    var kvp = group.Fields[i + j];
                    CreateArgEditorCell(row, shaderName, kvp.Key, kvp.Value);
                }
            }
        }

        private static void CreateArgEditorCell(RectTransform row, string shaderName, string fieldPath, object fieldVal)
        {
            if (fieldVal != null && !IsSimpleType(fieldVal.GetType()))
                return;

            string displayName = fieldPath.Split('.').Last();
            string fieldStr = FormatFieldValue(fieldVal);

            // Keep capture values stable for the callback
            string capturedShader = shaderName;
            string capturedPath = fieldPath;
            object capturedCurrentValue = fieldVal;

            Builder.CreateLabel(row, UI.MinArgWidth, UI.ArgRowHeight, 0, 0, displayName);

            var input = Builder.CreateTextInput(row, UI.MinArgWidth, UI.ArgRowHeight, 0, 0, fieldStr, val =>
            {
                HandleArgEdited(capturedShader, capturedPath, val, capturedCurrentValue);
            });

            activeInputFields[$"{shaderName}_{fieldPath}"] = input;
        }

        private static void HandleArgEdited(string shaderName, string fieldPath, string inputText, object fallbackCurrentValue)
        {   // Parse and apply user-edited arg value immediately to shader
            try
            {
                if (!currentArgs.TryGetValue(shaderName, out var argsObj) || argsObj == null)
                    return;

                var fieldType = fallbackCurrentValue?.GetType() ?? typeof(float);
                object parsed = ParseFieldValue(inputText, fieldType, fallbackCurrentValue);

                SetNestedFieldValue(argsObj, fieldPath, parsed);

                if (!userArgs.TryGetValue(shaderName, out var dict) || dict == null)
                    userArgs[shaderName] = dict = new Dictionary<string, object>();

                dict[fieldPath] = parsed;
                currentArgs[shaderName] = argsObj;

                UpdateShaderProvidedArgs(shaderName, argsObj);

                if (selectedShader >= 0 && selectedShader < shaders.Count)
                {   // Force immediate material update for current shader
                    var selectedInfo = shaders[selectedShader];
                    var selectedModule = ShaderRegistry.Get(selectedInfo.Name);
                    
                    if (selectedModule != null && (selectedInfo.Name == shaderName || selectedModule.Name == shaderName))
                        ConfigureOverlay(selectedShader);
                }
            }
            catch (Exception ex)
            { Debug.LogWarning($"Failed to parse {fieldPath}: {ex.Message}"); }
        }

        // -------------------------
        // Section: Footer buttons
        // -------------------------
        private static void BuildFooterButtons()
        {
            if (selectedShader < 0 || selectedShader >= shaders.Count)
                return;

            var row = CreateHorizontalRow(window);

            Builder.CreateButton(
                parent: row,
                width: UI.MinArgWidth,
                height: UI.ArgRowHeight,
                posX: 0,
                posY: 0,
                text: "Apply",
                onClick: () => ConfigureOverlay(selectedShader)
            );

            Builder.CreateButton(
                parent: row,
                width: UI.MinArgWidth,
                height: UI.ArgRowHeight,
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

        // -------------------------
        // Layout primitives (small reusable helpers)
        // -------------------------
        private static RectTransform CreateHorizontalRow(ClosableWindow parent)
        {   // Create a horizontal layout row within a window
            var row = Builder.CreateContainer(parent, 0, 0);
            row.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, UI.Spacing, null, true);
            return row.rectTransform;
        }

        private static RectTransform CreateHorizontalRow(RectTransform parent)
        {   // Create a horizontal layout row within a transform
            var row = Builder.CreateContainer(parent, 0, 0);
            row.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.UpperLeft, UI.Spacing, null, true);
            return row.rectTransform;
        }

        private static RectTransform CreateVerticalGroup(ClosableWindow parent)
        {   // Create a vertical layout group within a window
            var group = Builder.CreateContainer(parent, 0, 0);
            group.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, UI.Spacing, null, true);
            return group.rectTransform;
        }

        // =========================================================
        // Arg grouping / defaults / nested set (unchanged logic)
        // =========================================================
        private static List<ArgGroup> BuildArgGroups(IShaderModule module, ShaderInfo info)
        {   // Build argument groups by categorizing shader fields
            var groups = new List<ArgGroup>();
            var argsType = module.GetType().BaseType?.GetGenericArguments()[0];
            if (argsType == null) return groups;

            var args = Activator.CreateInstance(argsType);

            Material mat = null;
            try { mat = new Material(module.Shader); } catch { }

            shaderProvidedArgs.TryGetValue(info.Name, out var shaderProvided);
            userArgs.TryGetValue(info.Name, out var userVals);

            LoadArgsFromSources(args, mat, shaderProvided, userVals);
            if (mat != null) UnityEngine.Object.Destroy(mat);

            currentArgs[info.Name] = args;

            var flatFields = FlattenFields(args, "");
            var groupedFields = new Dictionary<string, List<KeyValuePair<string, object>>>();

            foreach (var kvp in flatFields)
            {
                var category = ExtractCategory(argsType, kvp.Key);
                var moduleName = ExtractModuleName(kvp.Key, module.Name);
                var groupKey = $"{moduleName}_{category}";

                if (!groupedFields.ContainsKey(groupKey))
                    groupedFields[groupKey] = new List<KeyValuePair<string, object>>();

                groupedFields[groupKey].Add(kvp);
            }

            foreach (var kvp in groupedFields)
            {
                var parts = kvp.Key.Split('_');
                var moduleName = parts.Length > 0 ? parts[0] : module.Name;
                var category = parts.Length > 1 ? parts[1] : "General";

                groups.Add(new ArgGroup
                {
                    ModuleName = moduleName,
                    Category = category,
                    Fields = kvp.Value
                });
            }

            return groups;
        }

        private static void LoadArgsFromSources(object args, Material mat, object shaderProvided, Dictionary<string, object> userVals)
        {
            var flatUser = userVals ?? new Dictionary<string, object>();
            var flatShader = shaderProvided != null ? FlattenFields(shaderProvided, "") : new Dictionary<string, object>();
            var flatDefaults = mat != null ? ExtractMaterialDefaults(args.GetType(), mat) : new Dictionary<string, object>();
            var flatAttrDefaults = ExtractAttributeDefaults(args.GetType());

            var allPaths = new HashSet<string>();
            foreach (var key in flatUser.Keys) allPaths.Add(key);
            foreach (var key in flatShader.Keys) allPaths.Add(key);
            foreach (var key in flatDefaults.Keys) allPaths.Add(key);
            foreach (var key in flatAttrDefaults.Keys) allPaths.Add(key);

            foreach (var path in allPaths)
            {   // Apply values with priority: user > shader > material > attributes
                object value = null;
                if (flatUser.TryGetValue(path, out value) ||
                    flatShader.TryGetValue(path, out value) ||
                    flatDefaults.TryGetValue(path, out value) ||
                    flatAttrDefaults.TryGetValue(path, out value))
                {
                    try { SetNestedFieldValue(args, path, value); }
                    catch { }
                }
            }
        }

        private static Dictionary<string, object> ExtractMaterialDefaults(SysType argsType, Material mat)
        {   // Extract default values from a material's shader properties
            var result = new Dictionary<string, object>();
            var tempArgs = Activator.CreateInstance(argsType);
            var flatFields = FlattenFields(tempArgs, "");

            foreach (var kvp in flatFields)
            {
                var fieldPath = kvp.Key;
                var lastDot = fieldPath.LastIndexOf('.');
                var fieldName = lastDot >= 0 ? fieldPath.Substring(lastDot + 1) : fieldPath;
                var propName = "_" + fieldName;

                var fieldType = kvp.Value?.GetType() ?? typeof(float);

                if (fieldType == typeof(float) && mat.HasProperty(propName)) result[fieldPath] = mat.GetFloat(propName);
                else if (fieldType == typeof(int) && mat.HasProperty(propName)) result[fieldPath] = (int)mat.GetFloat(propName);
                else if (fieldType == typeof(Color) && mat.HasProperty(propName)) result[fieldPath] = mat.GetColor(propName);
                else if (fieldType == typeof(Vector3) && mat.HasProperty(propName))
                {
                    Vector4 v4 = mat.GetVector(propName);
                    result[fieldPath] = new Vector3(v4.x, v4.y, v4.z);
                }
                else if (fieldType == typeof(Vector4) && mat.HasProperty(propName)) result[fieldPath] = mat.GetVector(propName);
            }

            return result;
        }

        private static Dictionary<string, object> ExtractAttributeDefaults(SysType argsType)
        {   // Extract default values from field attributes
            var result = new Dictionary<string, object>();
            ExtractAttributeDefaultsRecursive(argsType, "", result);
            return result;
        }

        private static void ExtractAttributeDefaultsRecursive(SysType type, string prefix, Dictionary<string, object> result)
        {   // Recursively extract attribute-defined defaults from fields
            var fields = type.GetFields();

            foreach (var field in fields)
            {   // Process each field for attribute defaults
                var fieldPath = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";
                var attr = field.GetCustomAttribute<ShaderArgAttribute>();

                if (attr?.DefaultValue != null) result[fieldPath] = attr.DefaultValue;

                if (field.FieldType.IsArray)
                {   // Handle array element attributes
                    var elementType = field.FieldType.GetElementType();
                    if (elementType != null && elementType.IsValueType && !elementType.IsPrimitive && !elementType.IsEnum)
                    {   // Recursively process array element type
                        var dummyPath = $"{fieldPath}[0]";
                        ExtractAttributeDefaultsRecursive(elementType, dummyPath, result);
                    }
                }
                else if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum &&
                         field.FieldType != typeof(Vector3) && field.FieldType != typeof(Vector4) && field.FieldType != typeof(Color))
                {   // Recursively process nested value types
                    ExtractAttributeDefaultsRecursive(field.FieldType, fieldPath, result);
                }
            }
        }

        private static void SetNestedFieldValue(object obj, string path, object value)
        {   // Set nested field value with automatic array creation and resizing
            if (obj == null || string.IsNullOrEmpty(path)) return;

            var parts = path.Split('.');
            if (parts.Length == 1)
            {   // Direct field assignment
                var field = obj.GetType().GetField(path);
                if (field != null) field.SetValue(obj, value);
                return;
            }

            var chain = new List<object> { obj };
            var fieldChain = new List<FieldInfo>();
            var arrayIndices = new List<int>();
            object current = obj;

            for (int i = 0; i < parts.Length - 1; i++)
            {   // Traverse nested path, creating missing nodes
                var part = parts[i];
                var arrayMatch = System.Text.RegularExpressions.Regex.Match(part, @"(.+)\[(\d+)\]");

                if (arrayMatch.Success)
                {   // Handle array element access
                    var fieldName = arrayMatch.Groups[1].Value;
                    var index = int.Parse(arrayMatch.Groups[2].Value);
                    var field = current.GetType().GetField(fieldName);
                    if (field == null) return;

                    var elemType = field.FieldType.GetElementType();
                    if (elemType == null) return;

                    var arr = field.GetValue(current) as Array;
                    if (arr == null || index >= arr.Length)
                    {   // Create or resize array to accommodate index
                        var oldLen = arr?.Length ?? 0;
                        var newLen = Math.Max(index + 1, oldLen);
                        var newArr = Array.CreateInstance(elemType, newLen);
                        if (arr != null && oldLen > 0) Array.Copy(arr, newArr, oldLen);
                        arr = newArr;
                        field.SetValue(current, arr);
                    }

                    var elem = arr.GetValue(index);
                    if (elem == null && !elemType.IsValueType)
                    {   // Create missing reference-type element
                        elem = Activator.CreateInstance(elemType);
                        arr.SetValue(elem, index);
                    }

                    fieldChain.Add(field);
                    arrayIndices.Add(index);
                    current = elem;
                    chain.Add(current);
                }
                else
                {   // Handle normal field access
                    var field = current.GetType().GetField(part);
                    if (field == null) return;

                    var next = field.GetValue(current);
                    if (next == null && !field.FieldType.IsValueType)
                    {   // Create missing reference-type node
                        next = Activator.CreateInstance(field.FieldType);
                        field.SetValue(current, next);
                    }

                    fieldChain.Add(field);
                    arrayIndices.Add(-1);
                    current = next;
                    chain.Add(current);
                }
            }

            var lastPart = parts[parts.Length - 1];
            var lastField = current.GetType().GetField(lastPart);
            if (lastField == null) return;

            lastField.SetValue(current, value);

            for (int i = chain.Count - 1; i > 0; i--)
            {   // Back-propagate changes through value types and arrays
                var parent = chain[i - 1];
                var field = fieldChain[i - 1];
                var arrIdx = arrayIndices[i - 1];

                if (arrIdx >= 0)
                {   // Update array element
                    var arr = field.GetValue(parent) as Array;
                    if (arr != null) arr.SetValue(chain[i], arrIdx);
                }
                else if (parent.GetType().IsValueType)
                    field.SetValue(parent, chain[i]);
            }
        }

        private static string FormatFieldValue(object fieldVal)
        {
            if (fieldVal is Color c) return $"#{ColorUtility.ToHtmlStringRGBA(c)}";
            if (fieldVal is Vector3 v3) return $"{v3.x:F6},{v3.y:F6},{v3.z:F6}";
            if (fieldVal is Vector4 v4) return $"{v4.x:F6},{v4.y:F6},{v4.z:F6},{v4.w:F6}";
            if (fieldVal is float f) return f.ToString("F6");
            if (fieldVal is bool b) return b.ToString();
            return fieldVal?.ToString() ?? "";
        }

        private static object ParseFieldValue(string val, SysType fieldType, object currentValue)
        {   // Parse user input string into the appropriate field type
            if (fieldType == typeof(float))
                return float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : currentValue;

            if (fieldType == typeof(int))
                return int.TryParse(val, out var i) ? i : currentValue;

            if (fieldType == typeof(bool))
                return bool.TryParse(val, out var b) ? b : currentValue;

            if (fieldType == typeof(Color))
                return ColorUtility.TryParseHtmlString(val, out var color) ? color : currentValue;

            if (fieldType == typeof(Vector3))
            {
                var parts = val.Split(',');
                if (parts.Length == 3 &&
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
                    float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                    return new Vector3(x, y, z);
            }

            if (fieldType == typeof(Vector4))
            {
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

        private static string ExtractCategory(SysType argsType, string fieldPath)
        {   // Extract category from field attributes or path structure
            string baseGroup = GetAttrGroup(argsType, fieldPath) ?? "General";

            if (fieldPath.Contains("[") && fieldPath.Contains("]"))
            {   // Extract array index for grouped naming
                var match = System.Text.RegularExpressions.Regex.Match(fieldPath, @"(.+)\[(\d+)\]");
                if (match.Success)
                {   // Create category with array index for clarity
                    var arrayName = match.Groups[1].Value;
                    var index = match.Groups[2].Value;
                    var friendlyName = arrayName.Replace("Layers", " Layer").Replace("layers", " layer");
                    return $"{friendlyName} {index} - {baseGroup}";
                }
            }

            return baseGroup;

            static string GetAttrGroup(SysType rootType, string path)
            {   // Traverse field path to find attribute-defined group
                var parts = path.Split('.');
                var t = rootType;
                FieldInfo f = null;

                for (int i = 0; i < parts.Length; i++)
                {   // Navigate through nested types
                    var part = parts[i].Split('[')[0];
                    f = t.GetField(part);
                    if (f == null) break;

                    if (i == parts.Length - 1)
                        return f.GetCustomAttribute<ShaderArgAttribute>()?.Group;

                    if (f.FieldType.IsArray) t = f.FieldType.GetElementType();
                    else if (f.FieldType.IsValueType && !f.FieldType.IsPrimitive && !f.FieldType.IsEnum) t = f.FieldType;
                    else break;
                }

                return null;
            }
        }

        private static string ExtractModuleName(string fieldPath, string defaultModule)
        {   // Keep all fields under the selected module without remapping
            return defaultModule;
        }

        public static void DestroyUI()
        {
            if (holder != null)
                UnityEngine.Object.Destroy(holder);

            holder = null;
            window = null;

            buttonTints.Clear();
            activeInputFields.Clear();
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

            selectedShader = (selectedShader == idx) ? -1 : idx;
            ConfigureOverlay(selectedShader);
            RebuildUI();
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
