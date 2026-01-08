using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using SFS.UI.ModGUI;
using UITools;
using ModLoader.Helpers;
using SFS.World;
using FrameEmbededState.Lib.Renders;
using FrameEmbededState.Lib;
using SysType = System.Type;

namespace FrameEmbededState
{
    public static class MainUi
    {
        private sealed class ShaderViewModel
        {   // ViewModel representing shader metadata and state
            public string Name { get; set; }
            public string Description { get; set; }
            public string ShaderSource { get; set; }
        }

        private sealed class ButtonState
        {   // Encapsulates button visual state for highlight management
            public Graphic Graphic { get; set; }
            public Color NormalColor { get; set; }
        }

        private sealed class ArgumentGroup
        {   // Groups related shader arguments for organized UI presentation
            public string ModuleName { get; set; }
            public string Category { get; set; }
            public List<KeyValuePair<string, object>> Fields { get; set; }
        }

        private sealed class ShaderState
        {   // Maintains state for a specific shader including args and user overrides
            public object CurrentArgs { get; set; }
            public Dictionary<string, object> UserOverrides { get; } = new();
            public Dictionary<string, TextInput> ActiveInputs { get; } = new();
            public string LastSnapshot { get; set; }
        }

        private static readonly List<ShaderViewModel> _shaderModels = new(16);
        private static readonly List<ButtonState> _buttonStates = new(32);
        private static readonly Dictionary<string, ShaderState> _shaderStates = new();
        private static readonly Dictionary<string, bool> _groupFoldouts = new();

        private static int _selectedShaderIndex = -1;
        private static Camera _activeCamera;
        private static bool _cameraChanged;

        private static bool _menuRegistered;
        private static GameObject _menuRoot;
        private static Window _leftPaneWindow;
        private static Window _rightPaneWindow;
        private static RectTransform _shaderListPanel;
        private static RectTransform _argsPanel;
        private static int _leftPaneWidth;
        private static int _rightPaneWidth;
        private static RectTransform _menuContentRect;
        private static float _originalMenuWidth;
        private static bool _menuIsActive;

        public static void Init()
        {   // Initialize shader registry, menu, and camera monitoring
            LoadShaderRegistry();
            RegisterMenu();
            SubscribeToCameraEvents();
        }

        public static void RegisterGpuShader(string name, string description, string shaderSource)
        {   // Register or update shader in the registry and refresh UI if active
            if (string.IsNullOrWhiteSpace(name)) return;

            var existingIndex = _shaderModels.FindIndex(s => s.Name == name);
            var model = new ShaderViewModel { Name = name, Description = description, ShaderSource = shaderSource };

            if (existingIndex >= 0) _shaderModels[existingIndex] = model;
            else _shaderModels.Add(model);

            Debug.Log($"[FrameEmbededState] Registered GPU shader: {name}");
            if (_menuRoot != null) RebuildShaderList();
        }

        private static void LoadShaderRegistry()
        {   // Populate shader models from the central registry
            _shaderModels.Clear();
            foreach (var module in ShaderRegistry.AllModules)
                _shaderModels.Add(new ShaderViewModel
                {
                    Name = module.Name,
                    Description = $"Shader module: {module.Name}",
                    ShaderSource = module.Shader?.name
                });
        }

        private static void RegisterMenu()
        {   // Register configuration menu page once
            if (_menuRegistered) return;
            _menuRegistered = true;
            ConfigurationMenu.Add("Shader API", new (string, Func<Transform, GameObject>)[] { ("Shader API", BuildMenuPage) });
        }

        private static void SubscribeToCameraEvents()
        {   // Hook into camera change notifications
            var activeCamera = SFS.Cameras.ActiveCamera.main?.activeCamera.Value;
            if (activeCamera != null) SFS.Cameras.ActiveCamera.main.activeCamera.OnChange += HandleCameraChange;
        }

        private sealed class MenuLifecycleHandler : MonoBehaviour
        {
            private void OnEnable()
            {   // Track menu activation state
                _menuIsActive = true;
            }

            private void OnDisable()
            {   // Handle menu closing and restore original state
                RestoreMenuWidth();
                CleanupMenuState();
            }

            private void OnDestroy()
            {   // Final cleanup when menu is destroyed
                RestoreMenuWidth();
                CleanupMenuState();
            }
        }

        private static void RestoreMenuWidth()
        {   // Restore the original menu content width when closing
            if (_menuContentRect != null && _originalMenuWidth > 0)
                _menuContentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _originalMenuWidth);
        }

        private static void CleanupMenuState()
        {   // Clear all menu references and cached state
            if (!_menuIsActive) return;
            
            _menuIsActive = false;
            _menuRoot = null;
            _leftPaneWindow = null;
            _rightPaneWindow = null;
            _shaderListPanel = null;
            _argsPanel = null;
            _menuContentRect = null;
            _originalMenuWidth = 0;
            _buttonStates.Clear();
            
            foreach (var state in _shaderStates.Values)
                state.ActiveInputs.Clear();
        }

        private static GameObject BuildMenuPage(Transform parent)
        {   // Construct dual-pane configuration menu with proper lifecycle management
            if (_menuIsActive && _menuRoot != null)
            {   // Menu already exists, return existing instance
                return _menuRoot;
            }

            const int widthMultiplier = 700;
            var contentSize = ConfigurationMenu.ContentSize;
            _menuContentRect = parent.parent.parent.GetRect();
            _originalMenuWidth = _menuContentRect.rect.width;
            var scaledWidth = (int)(contentSize.x + widthMultiplier / 1.70f);

            _menuContentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, contentSize.x + widthMultiplier);
            _leftPaneWidth = Mathf.Clamp(scaledWidth / 3, 190, 280);
            _rightPaneWidth = Mathf.Max(220, scaledWidth - _leftPaneWidth - UiHelper.Metrics.Spacing - (UiHelper.Metrics.Padding * 2));
            var paneHeight = Mathf.Max(220, contentSize.y - (UiHelper.Metrics.Padding * 2) - 60);

            var window = Builder.CreateWindow(parent, Builder.GetRandomID(), scaledWidth, contentSize.y, 0, 0, false, false, 1f, "Shader API");
            window.gameObject.AddComponent<MenuLifecycleHandler>();

            window.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, UiHelper.Metrics.Spacing, UiHelper.Layout.StandardPadding, true);
            window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);

            var rootRow = UiHelper.CreateHorizontalContainer(window);
            _leftPaneWindow = UiHelper.CreateScrollableWindow(rootRow, _leftPaneWidth, paneHeight, "Shaders", vertical: true, horizontal: false);
            UiHelper.CreateButton(_leftPaneWindow, _leftPaneWidth - (UiHelper.Metrics.Padding * 2), "Disable Overlay", () => SelectShader(-1));
            _shaderListPanel = UiHelper.CreateVerticalContainer(_leftPaneWindow);

            _rightPaneWindow = UiHelper.CreateScrollableWindow(rootRow, _rightPaneWidth, paneHeight, "Arguments", vertical: true, horizontal: false);
            _argsPanel = UiHelper.CreateVerticalContainer(_rightPaneWindow);

            _menuRoot = window.gameObject;
            _menuIsActive = true;
            
            RebuildShaderList();
            RebuildArgsPanel();

            return window.gameObject;
        }

        private static void RebuildShaderList()
        {   // Rebuild shader selection list only if menu is active
            if (_shaderListPanel == null || !_menuIsActive) return;

            _buttonStates.Clear();
            UiHelper.ClearChildren(_shaderListPanel);

            for (int i = 0; i < _shaderModels.Count; i++)
            {
                int capturedIndex = i;
                var btn = UiHelper.CreateButton(_shaderListPanel, _leftPaneWidth - (UiHelper.Metrics.Padding * 2), _shaderModels[i].Name, () => SelectShader(capturedIndex));
                CacheButtonState(btn.rectTransform);
            }

            UpdateHighlights();
            UnityEngine.Canvas.ForceUpdateCanvases();
        }

        private static void SelectShader(int index)
        {   // Select shader or disable overlay then refresh UI panels
            _selectedShaderIndex = (index >= 0 && index < _shaderModels.Count) ? index : -1;
            UpdateHighlights();
            RebuildArgsPanel();
            ApplyShaderConfiguration(_selectedShaderIndex);
        }

        private static void RebuildArgsPanel()
        {   // Rebuild argument editor panel only if menu is active
            if (_argsPanel == null || !_menuIsActive) return;

            UiHelper.ClearChildren(_argsPanel);

            if (_selectedShaderIndex < 0 || _selectedShaderIndex >= _shaderModels.Count)
            {
                UiHelper.CreateLabel(_argsPanel, _rightPaneWidth - (UiHelper.Metrics.Padding * 2), "Select a shader to edit its arguments.");
                ForceLayoutRefresh();
                return;
            }

            var model = _shaderModels[_selectedShaderIndex];
            var module = ShaderRegistry.Get(model.Name);

            if (module == null)
            {
                UiHelper.CreateLabel(_argsPanel, _rightPaneWidth - (UiHelper.Metrics.Padding * 2), "Shader module not found.");
                ForceLayoutRefresh();
                return;
            }

            var groups = BuildArgumentGroups(module, model);
            foreach (var group in groups)
                BuildArgumentGroupUI(_argsPanel, model, group);
            
            BuildFooterControls(_argsPanel, model);
            ForceLayoutRefresh();
        }

        private static void ForceLayoutRefresh()
        {   // Force Unity layout recalculation with additional safety checks
            if (!_menuIsActive || _rightPaneWindow == null || _argsPanel == null) return;

            UnityEngine.Canvas.ForceUpdateCanvases();
            
            if (_argsPanel != null && _argsPanel.gameObject.activeInHierarchy)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_argsPanel);
            
            if (_rightPaneWindow != null && _rightPaneWindow.gameObject.activeInHierarchy)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(_rightPaneWindow.rectTransform);
        }

        private static void BuildArgumentGroupUI(RectTransform panel, ShaderViewModel model, ArgumentGroup group)
        {   // Create foldout section with proper state validation
            if (!_menuIsActive) return;

            var groupKey = $"{group.ModuleName}_{group.Category}";
            if (!_groupFoldouts.ContainsKey(groupKey))
                _groupFoldouts[groupKey] = true;

            var headerRow = UiHelper.CreateHorizontalContainer(panel);
            var isExpanded = _groupFoldouts[groupKey];
            
            UiHelper.CreateFoldoutButton(headerRow, isExpanded, () =>
            {   // Toggle foldout and rebuild only if menu is still active
                if (!_menuIsActive) return;
                _groupFoldouts[groupKey] = !_groupFoldouts[groupKey];
                RebuildArgsPanel();
            });

            var categoryText = group.ModuleName != model.Name ? $"{group.ModuleName} - {group.Category}" : group.Category;
            var headerWidth = Mathf.Max(0, (_rightPaneWidth - (UiHelper.Metrics.Padding * 2)) - (UiHelper.Metrics.FoldoutButtonSize + UiHelper.Metrics.Spacing));
            UiHelper.CreateLabel(headerRow, headerWidth, categoryText, UiHelper.Metrics.FoldoutButtonSize);

            if (!isExpanded) return;

            var fieldWidth = UiHelper.CalculateFieldWidth(_rightPaneWidth, UiHelper.Metrics.DefaultColumns);
            for (int i = 0; i < group.Fields.Count; i += UiHelper.Metrics.DefaultColumns)
            {
                var row = UiHelper.CreateHorizontalContainer(panel);
                for (int j = 0; j < UiHelper.Metrics.DefaultColumns && i + j < group.Fields.Count; j++)
                {
                    var kvp = group.Fields[i + j];
                    BuildArgumentField(row, model.Name, kvp.Key, kvp.Value, fieldWidth);
                }
            }
        }

        private static void BuildArgumentField(RectTransform row, string shaderName, string fieldPath, object fieldValue, int fieldWidth)
        {   // Create field controls with menu state validation
            if (!_menuIsActive || (fieldValue != null && !IsSimpleType(fieldValue.GetType()))) return;

            var displayName = fieldPath.Split('.').Last();
            var fieldText = FormatValue(fieldValue);
            var labelWidth = Mathf.Clamp(Mathf.RoundToInt(fieldWidth * 0.42f), 70, Mathf.Max(70, fieldWidth - 60));
            var inputWidth = Mathf.Max(60, fieldWidth - labelWidth);

            UiHelper.CreateLabel(row, labelWidth, displayName, UiHelper.Metrics.InputHeight);
            var input = UiHelper.CreateTextInput(row, inputWidth, fieldText, val =>
            {   // Handle input changes only if menu is active
                if (_menuIsActive)
                    HandleArgumentEdit(shaderName, fieldPath, val, fieldValue);
            });

            var state = GetOrCreateShaderState(shaderName);
            state.ActiveInputs[$"{shaderName}_{fieldPath}"] = input;
        }

        private static void BuildFooterControls(RectTransform panel, ShaderViewModel model)
        {   // Create footer buttons with menu state validation
            if (!_menuIsActive) return;

            var row = UiHelper.CreateHorizontalContainer(panel);
            var usableWidth = Mathf.Max(0, _rightPaneWidth - (UiHelper.Metrics.Padding * 2));
            var buttonWidth = Mathf.Max(120, (usableWidth - UiHelper.Metrics.Spacing) / 2);

            UiHelper.CreateButton(row, buttonWidth, "Apply", () =>
            {   // Apply configuration only if menu is active
                if (_menuIsActive)
                    ApplyShaderConfiguration(_selectedShaderIndex);
            });
            
            UiHelper.CreateButton(row, buttonWidth, "Restore to Default", () =>
            {   // Restore defaults only if menu is active
                if (!_menuIsActive) return;
                _shaderStates.Remove(model.Name);
                ApplyShaderConfiguration(_selectedShaderIndex);
                RebuildArgsPanel();
            });
        }

        private static Camera GetActiveWorldCamera()
        {   // Retrieve active world camera with change detection
            var activeCam = GameCamerasManager.main?.world_Camera?.camera ?? GameCamerasManager.main?.scaledWorld_Camera?.camera;
            if (activeCam != null && activeCam.gameObject.activeInHierarchy && activeCam.enabled)
            {
                if (_activeCamera != activeCam) { _activeCamera = activeCam; _cameraChanged = true; }
                return _activeCamera;
            }
            return _activeCamera;
        }

        private static void HandleCameraChange()
        {   // Respond to camera transitions and reapply shader configuration
            var newCamera = GameCamerasManager.main?.world_Camera?.camera ?? GameCamerasManager.main?.scaledWorld_Camera?.camera;
            if (newCamera != null && newCamera.enabled && newCamera.gameObject.activeInHierarchy)
            {
                _activeCamera = newCamera;
                _cameraChanged = true;
                CleanupOrphanedEffects(newCamera);
                if (_selectedShaderIndex >= 0) ApplyShaderConfiguration(_selectedShaderIndex);
            }
        }

        public static void NotifyCameraInactive()
        {   // Handle camera becoming inactive and trigger reconfiguration if needed
            _cameraChanged = true;
            if (_selectedShaderIndex >= 0)
            {
                var newCam = GetActiveWorldCamera();
                if (newCam != null && newCam != _activeCamera) ApplyShaderConfiguration(_selectedShaderIndex);
            }
        }

        private static void CleanupOrphanedEffects(Camera currentCamera)
        {   // Remove overlay effects from inactive cameras
            UnityEngine.Object.FindObjectsOfType<FrameEmbededStateOverlayEffect>()
                .Where(e => e != null && e.gameObject != currentCamera.gameObject)
                .ToList()
                .ForEach(e => UnityEngine.Object.Destroy(e));
        }

        private static void ApplyShaderConfiguration(int index)
        {   // Configure overlay effect with selected shader and current arguments
            var camera = GetActiveWorldCamera();
            if (camera == null) { Debug.LogWarning("[MainUi] No valid camera for overlay"); return; }

            if (_cameraChanged) { CleanupOrphanedEffects(camera); _cameraChanged = false; }

            if (index < 0 || index >= _shaderModels.Count)
            {
                if (_selectedShaderIndex >= 0)
                {
                    OverlayDispatcher.Render(camera, null, null, null, null, OverlayRenderMode.BehindUI);
                    OverlayDispatcher.SelectedModule = null;
                    OverlayDispatcher.CurrentArgs = null;
                    CustomRender_Render.Release();
                    var overlayEffect = camera?.GetComponent<FrameEmbededStateOverlayEffect>();
                    if (overlayEffect != null) UnityEngine.Object.Destroy(overlayEffect);
                }
                return;
            }

            var model = _shaderModels[index];
            var module = ShaderRegistry.Get(model.Name);
            if (module == null || module.Shader == null) return;

            OverlayDispatcher.SelectedModule = module;
            var renderMode = GetRenderMode(module);
            var argsObject = ResolveShaderArguments(module, model.Name);
            OverlayDispatcher.CurrentArgs = argsObject;

            var effect = camera?.GetComponent<FrameEmbededStateOverlayEffect>() ?? camera?.gameObject.AddComponent<FrameEmbededStateOverlayEffect>();
            if (effect != null)
            {
                effect.renderMode = renderMode;
                effect.customRenderKey = model.Name;
                effect.selectedShader = module.Shader;
            }

            if (renderMode == OverlayRenderMode.CustomRender)
            {
                var runMethod = module.GetType().GetMethod("Run");
                runMethod?.Invoke(module, new[] { argsObject });
            }
            else
            {
                OverlayDispatcher.Render(camera, null, null, module.Shader, null, renderMode);
                if (OverlayDispatcher.CurrentMaterial != null && OverlayDispatcher.SelectedModule != null)
                    OverlayDispatcher.SelectedModule.ApplyArgs(OverlayDispatcher.CurrentMaterial, argsObject);
            }
        }

        private static OverlayRenderMode GetRenderMode(IShaderModule module)
        {   // Extract render mode from module property via reflection
            var renderTargetProp = module.GetType().GetProperty("RenderTarget");
            return renderTargetProp != null ? (OverlayRenderMode)renderTargetProp.GetValue(module) : OverlayRenderMode.BehindUI;
        }

        private static object ResolveShaderArguments(IShaderModule module, string shaderName)
        {   // Construct args object from user overrides, shader defaults, and material properties
            var argsType = module.GetType().BaseType?.GetGenericArguments()[0];
            if (argsType == null) return null;

            var state = GetOrCreateShaderState(shaderName);
            var argsObject = state.CurrentArgs ?? Activator.CreateInstance(argsType);

            Material tempMaterial = null;
            try { tempMaterial = new Material(module.Shader); } catch { }

            var flatFields = FlattenFields(argsObject, "");
            var materialDefaults = tempMaterial != null ? ExtractMaterialDefaults(argsType, tempMaterial) : new Dictionary<string, object>();
            var attributeDefaults = ExtractAttributeDefaults(argsType);

            foreach (var path in flatFields.Keys)
            {
                object value = null;
                if (state.UserOverrides.TryGetValue(path, out value) ||
                    materialDefaults.TryGetValue(path, out value) ||
                    attributeDefaults.TryGetValue(path, out value))
                {
                    try { SetNestedField(argsObject, path, value); } catch { }
                }
            }

            if (tempMaterial != null) UnityEngine.Object.Destroy(tempMaterial);
            state.CurrentArgs = argsObject;
            return argsObject;
        }

        public static void UpdateShaderProvidedArgs(string shaderName, object args)
        {   // Update shader-provided arguments and refresh UI inputs if changed
            if (args == null) return;

            var state = GetOrCreateShaderState(shaderName);
            var fields = args.GetType().GetFields();
            var snapshot = string.Join("|", Array.ConvertAll(fields, f => $"{f.Name}:{f.GetValue(args)}"));

            if (state.LastSnapshot == snapshot) return;
            state.LastSnapshot = snapshot;

            if (_selectedShaderIndex >= 0 && _selectedShaderIndex < _shaderModels.Count && _shaderModels[_selectedShaderIndex].Name == shaderName)
                SyncInputFieldsFromArgs(shaderName, args, state);
        }

        private static void SyncInputFieldsFromArgs(string shaderName, object args, ShaderState state)
        {   // Update text inputs to reflect shader-provided argument values
            var flatFields = FlattenFields(args, "");
            foreach (var kvp in flatFields)
            {
                if (state.UserOverrides.ContainsKey(kvp.Key)) continue;
                var inputKey = $"{shaderName}_{kvp.Key}";
                if (state.ActiveInputs.TryGetValue(inputKey, out var input) && input != null)
                {
                    var newText = FormatValue(kvp.Value);
                    if (input.Text != newText) input.Text = newText;
                }
            }
        }

        private static void HandleArgumentEdit(string shaderName, string fieldPath, string inputText, object fallbackValue)
        {   // Parse user input and apply to shader arguments with immediate configuration update
            try
            {
                var state = GetOrCreateShaderState(shaderName);
                if (state.CurrentArgs == null) return;

                var fieldType = fallbackValue?.GetType() ?? typeof(float);
                var parsedValue = ParseValue(inputText, fieldType, fallbackValue);

                SetNestedField(state.CurrentArgs, fieldPath, parsedValue);
                state.UserOverrides[fieldPath] = parsedValue;

                if (_selectedShaderIndex >= 0 && _selectedShaderIndex < _shaderModels.Count)
                {
                    var selectedModel = _shaderModels[_selectedShaderIndex];
                    var selectedModule = ShaderRegistry.Get(selectedModel.Name);
                    if (selectedModule != null && (selectedModel.Name == shaderName || selectedModule.Name == shaderName))
                        ApplyShaderConfiguration(_selectedShaderIndex);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"Failed to parse {fieldPath}: {ex.Message}"); }
        }

        private static List<ArgumentGroup> BuildArgumentGroups(IShaderModule module, ShaderViewModel model)
        {   // Organize shader arguments into categorized groups for UI presentation
            var groups = new List<ArgumentGroup>();
            var argsType = module.GetType().BaseType?.GetGenericArguments()[0];
            if (argsType == null) return groups;

            var state = GetOrCreateShaderState(model.Name);
            var argsObject = state.CurrentArgs ?? Activator.CreateInstance(argsType);
            state.CurrentArgs = argsObject;

            var flatFields = FlattenFields(argsObject, "");
            var groupedFields = new Dictionary<string, List<KeyValuePair<string, object>>>();

            foreach (var kvp in flatFields)
            {
                var category = ExtractCategory(argsType, kvp.Key);
                var moduleName = module.Name;
                var groupKey = $"{moduleName}_{category}";

                if (!groupedFields.ContainsKey(groupKey)) groupedFields[groupKey] = new List<KeyValuePair<string, object>>();
                groupedFields[groupKey].Add(kvp);
            }

            foreach (var kvp in groupedFields)
            {
                var parts = kvp.Key.Split('_');
                groups.Add(new ArgumentGroup
                {
                    ModuleName = parts.Length > 0 ? parts[0] : module.Name,
                    Category = parts.Length > 1 ? parts[1] : "General",
                    Fields = kvp.Value
                });
            }

            return groups;
        }

        private static Dictionary<string, object> FlattenFields(object obj, string prefix)
        {   // Recursively flatten nested fields into dot-notation dictionary
            var result = new Dictionary<string, object>();
            if (obj == null) return result;

            var fields = obj.GetType().GetFields();
            foreach (var field in fields)
            {
                var fieldValue = field.GetValue(obj);
                var fieldPath = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";

                if (fieldValue == null || IsSimpleType(field.FieldType)) result[fieldPath] = fieldValue;
                else if (field.FieldType.IsArray)
                {
                    var arr = fieldValue as Array;
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            var element = arr.GetValue(i);
                            var elementPath = $"{fieldPath}[{i}]";
                            if (element != null && !IsSimpleType(element.GetType()))
                            {
                                var nested = FlattenFields(element, elementPath);
                                foreach (var nkvp in nested) result[nkvp.Key] = nkvp.Value;
                            }
                            else result[elementPath] = element;
                        }
                    }
                }
                else if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
                {
                    var nested = FlattenFields(fieldValue, fieldPath);
                    foreach (var nkvp in nested) result[nkvp.Key] = nkvp.Value;
                }
                else result[fieldPath] = fieldValue;
            }
            return result;
        }

        private static bool IsSimpleType(SysType type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Color) ||
            type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Texture2D);

        private static Dictionary<string, object> ExtractMaterialDefaults(SysType argsType, Material mat)
        {   // Extract default values from material shader properties
            var result = new Dictionary<string, object>();
            var tempArgs = Activator.CreateInstance(argsType);
            var flatFields = FlattenFields(tempArgs, "");

            foreach (var kvp in flatFields)
            {
                var fieldName = kvp.Key.Split('.').Last().Split('[')[0];
                var propName = "_" + fieldName;
                var fieldType = kvp.Value?.GetType() ?? typeof(float);

                if (fieldType == typeof(float) && mat.HasProperty(propName)) result[kvp.Key] = mat.GetFloat(propName);
                else if (fieldType == typeof(int) && mat.HasProperty(propName)) result[kvp.Key] = (int)mat.GetFloat(propName);
                else if (fieldType == typeof(Color) && mat.HasProperty(propName)) result[kvp.Key] = mat.GetColor(propName);
                else if (fieldType == typeof(Vector3) && mat.HasProperty(propName))
                {
                    Vector4 v4 = mat.GetVector(propName);
                    result[kvp.Key] = new Vector3(v4.x, v4.y, v4.z);
                }
                else if (fieldType == typeof(Vector4) && mat.HasProperty(propName)) result[kvp.Key] = mat.GetVector(propName);
            }
            return result;
        }

        private static Dictionary<string, object> ExtractAttributeDefaults(SysType argsType)
        {   // Extract default values defined in field attributes
            var result = new Dictionary<string, object>();
            ExtractAttributeDefaultsRecursive(argsType, "", result);
            return result;
        }

        private static void ExtractAttributeDefaultsRecursive(SysType type, string prefix, Dictionary<string, object> result)
        {   // Recursively traverse type hierarchy to find attribute defaults
            var fields = type.GetFields();
            foreach (var field in fields)
            {
                var fieldPath = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";
                var attr = field.GetCustomAttribute<ShaderArgAttribute>();
                if (attr?.DefaultValue != null) result[fieldPath] = attr.DefaultValue;

                if (field.FieldType.IsArray)
                {
                    var elementType = field.FieldType.GetElementType();
                    if (elementType != null && elementType.IsValueType && !elementType.IsPrimitive && !elementType.IsEnum)
                        ExtractAttributeDefaultsRecursive(elementType, $"{fieldPath}[0]", result);
                }
                else if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum &&
                         field.FieldType != typeof(Vector3) && field.FieldType != typeof(Vector4) && field.FieldType != typeof(Color))
                {
                    ExtractAttributeDefaultsRecursive(field.FieldType, fieldPath, result);
                }
            }
        }

        private static void SetNestedField(object obj, string path, object value)
        {   // Set nested field value with automatic array creation and resizing
            if (obj == null || string.IsNullOrEmpty(path)) return;

            var parts = path.Split('.');
            if (parts.Length == 1)
            {
                var field = obj.GetType().GetField(path);
                field?.SetValue(obj, value);
                return;
            }

            var chain = new List<object> { obj };
            var fieldChain = new List<FieldInfo>();
            var arrayIndices = new List<int>();
            object current = obj;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i];
                var arrayMatch = System.Text.RegularExpressions.Regex.Match(part, @"(.+)\[(\d+)\]");

                if (arrayMatch.Success)
                {
                    var fieldName = arrayMatch.Groups[1].Value;
                    var index = int.Parse(arrayMatch.Groups[2].Value);
                    var field = current.GetType().GetField(fieldName);
                    if (field == null) return;

                    var elemType = field.FieldType.GetElementType();
                    if (elemType == null) return;

                    var arr = field.GetValue(current) as Array;
                    if (arr == null || index >= arr.Length)
                    {
                        var newLen = Math.Max(index + 1, arr?.Length ?? 0);
                        var newArr = Array.CreateInstance(elemType, newLen);
                        if (arr != null) Array.Copy(arr, newArr, arr.Length);
                        arr = newArr;
                        field.SetValue(current, arr);
                    }

                    var elem = arr.GetValue(index);
                    if (elem == null && !elemType.IsValueType) { elem = Activator.CreateInstance(elemType); arr.SetValue(elem, index); }

                    fieldChain.Add(field);
                    arrayIndices.Add(index);
                    current = elem;
                    chain.Add(current);
                }
                else
                {
                    var field = current.GetType().GetField(part);
                    if (field == null) return;

                    var next = field.GetValue(current);
                    if (next == null && !field.FieldType.IsValueType) { next = Activator.CreateInstance(field.FieldType); field.SetValue(current, next); }

                    fieldChain.Add(field);
                    arrayIndices.Add(-1);
                    current = next;
                    chain.Add(current);
                }
            }

            var lastField = current.GetType().GetField(parts[parts.Length - 1]);
            if (lastField == null) return;
            lastField.SetValue(current, value);

            for (int i = chain.Count - 1; i > 0; i--)
            {
                var parent = chain[i - 1];
                var field = fieldChain[i - 1];
                var arrIdx = arrayIndices[i - 1];

                if (arrIdx >= 0) { var arr = field.GetValue(parent) as Array; arr?.SetValue(chain[i], arrIdx); }
                else if (parent.GetType().IsValueType) field.SetValue(parent, chain[i]);
            }
        }

        private static string FormatValue(object value)
        {   // Format field value for display in text input
            if (value is Color c) return $"#{ColorUtility.ToHtmlStringRGBA(c)}";
            if (value is Vector3 v3) return $"{v3.x:F6},{v3.y:F6},{v3.z:F6}";
            if (value is Vector4 v4) return $"{v4.x:F6},{v4.y:F6},{v4.z:F6},{v4.w:F6}";
            if (value is float f) return f.ToString("F6");
            if (value is bool b) return b.ToString();
            return value?.ToString() ?? "";
        }

        private static object ParseValue(string text, SysType fieldType, object fallback)
        {   // Parse user input text into typed field value
            if (fieldType == typeof(float)) return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fallback;
            if (fieldType == typeof(int)) return int.TryParse(text, out var i) ? i : fallback;
            if (fieldType == typeof(bool)) return bool.TryParse(text, out var b) ? b : fallback;
            if (fieldType == typeof(Color)) return ColorUtility.TryParseHtmlString(text, out var c) ? c : fallback;

            if (fieldType == typeof(Vector3))
            {
                var parts = text.Split(',');
                if (parts.Length == 3 &&
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
                    float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                    return new Vector3(x, y, z);
            }

            if (fieldType == typeof(Vector4))
            {
                var parts = text.Split(',');
                if (parts.Length == 4 &&
                    float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
                    float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z) &&
                    float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
                    return new Vector4(x, y, z, w);
            }

            return fallback;
        }

        private static string ExtractCategory(SysType argsType, string fieldPath)
        {   // Extract category from field attributes or infer from path structure
            var baseGroup = GetAttributeGroup(argsType, fieldPath) ?? "General";

            if (fieldPath.Contains("[") && fieldPath.Contains("]"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(fieldPath, @"(.+)\[(\d+)\]");
                if (match.Success)
                {
                    var arrayName = match.Groups[1].Value;
                    var index = match.Groups[2].Value;
                    var friendlyName = arrayName.Replace("Layers", " Layer").Replace("layers", " layer");
                    return $"{friendlyName} {index} - {baseGroup}";
                }
            }

            return baseGroup;

            static string GetAttributeGroup(SysType rootType, string path)
            {
                var parts = path.Split('.');
                var t = rootType;

                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i].Split('[')[0];
                    var f = t.GetField(part);
                    if (f == null) break;
                    if (i == parts.Length - 1) return f.GetCustomAttribute<ShaderArgAttribute>()?.Group;

                    if (f.FieldType.IsArray) t = f.FieldType.GetElementType();
                    else if (f.FieldType.IsValueType && !f.FieldType.IsPrimitive && !f.FieldType.IsEnum) t = f.FieldType;
                    else break;
                }
                return null;
            }
        }

        private static void CacheButtonState(RectTransform buttonRect)
        {   // Cache button graphics state for highlight management
            var graphic = UiHelper.GetButtonGraphic(buttonRect);
            if (graphic == null) return;
            _buttonStates.Add(new ButtonState { Graphic = graphic, NormalColor = graphic.color });
        }

        private static void UpdateHighlights()
        {   // Apply highlight to currently selected shader button
            for (int i = 0; i < _buttonStates.Count; i++)
            {
                var state = _buttonStates[i];
                if (state.Graphic == null) continue;
                UiHelper.HighlightButton(state.Graphic, state.NormalColor, i == _selectedShaderIndex);
            }
        }

        private static ShaderState GetOrCreateShaderState(string shaderName)
        {   // Retrieve or initialize shader state container
            if (!_shaderStates.TryGetValue(shaderName, out var state))
            {
                state = new ShaderState();
                _shaderStates[shaderName] = state;
            }
            return state;
        }

        public static Dictionary<string, object> GetUserArgs(string shaderName)
        {   // Expose user-edited argument overrides for external access
            if (string.IsNullOrWhiteSpace(shaderName)) return null;
            return _shaderStates.TryGetValue(shaderName, out var state) ? state.UserOverrides : null;
        }

        public static object GetCurrentArgs(string shaderName)
        {   // Expose resolved arguments object for external access
            if (string.IsNullOrWhiteSpace(shaderName)) return null;
            return _shaderStates.TryGetValue(shaderName, out var state) ? state.CurrentArgs : null;
        }
    }
}
