using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;
using FrameEmbededState; // <-- Fix: ensure ShaderAssetRegistry is visible


namespace FrameEmbededState
{
    /// Attach this to shader module classes to declare metadata for normal shaders.
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ShaderModuleAttribute : Attribute
    {
        public string Name { get; }
        public ShaderType Type { get; }
        public string LoadBy { get; }
        public FrameEmbededState.Lib.Renders.OverlayRenderMode RenderTarget { get; }

        public ShaderModuleAttribute(string name, ShaderType type, string loadBy, FrameEmbededState.Lib.Renders.OverlayRenderMode renderTarget)
        {
            Name = name;
            Type = type;
            LoadBy = loadBy;
            RenderTarget = renderTarget;
        }
    }

    /// Non-generic interface for normal shader registry storage.
    public interface IShaderModule
    {
        string Name { get; }
        Shader Shader { get; }
        bool IsLoaded { get; }

        /// Lightweight checks: platform support, shader loaded.
        bool TryValidate(out string error);

        /// Optional: deeper test (sanity checks). Return false to fail.
        bool TrySelfTest(out string report);

        // New: Apply arguments to a material
        void ApplyArgs(Material mat, object args);
    }

    /// Base class for typed normal shader modules.
    public abstract class ShaderModule<TArgs, TResult> : IShaderModule
    {
        protected Shader _shader;
        private string _name;
        private ShaderType _type;
        private string _loadBy;
        private FrameEmbededState.Lib.Renders.OverlayRenderMode _renderTarget;
        private bool _loggedMissing;

        protected ShaderModule()
        {
            var attr = GetType().GetCustomAttribute<ShaderModuleAttribute>();
            if (attr != null)
            {
                _name = attr.Name;
                _type = attr.Type;
                _loadBy = attr.LoadBy;
                _renderTarget = attr.RenderTarget;
            }
            // Do NOT load or assign _shader here; lazy loading is handled in LoadShader().
        }

        public virtual string Name => _name ?? GetType().Name;
        public FrameEmbededState.Lib.Renders.OverlayRenderMode RenderTarget => _renderTarget;

        // Lazy load: allows AssetBundle-discovered shaders to be used after load.
        public Shader Shader => _shader != null ? _shader : LoadShader();
        public bool IsLoaded => Shader != null;

        protected virtual Shader LoadShader()
        {   // Load shader from registry, built-in, or resources
            if (_shader != null) return _shader;
            if (_type != ShaderType.Shader) return null;

            // 1) Try registry (AssetBundle-discovered)
            if (!string.IsNullOrEmpty(_loadBy) && ShaderAssetRegistry.TryGetShader(_loadBy, out var fromRegistry) && fromRegistry != null)
                return _shader = fromRegistry;

            // 2) Try Shader.Find (built-in/global)
            if (!string.IsNullOrEmpty(_loadBy))
                _shader = UnityEngine.Shader.Find(_loadBy);

            // 3) Try Resources (custom asset in Resources/)
            if (_shader == null && !string.IsNullOrEmpty(_loadBy))
                _shader = Resources.Load<Shader>(_loadBy);

            if (_shader == null && !_loggedMissing && !string.IsNullOrEmpty(_loadBy))
            {   // Log a detailed warning only once per module per session
                _loggedMissing = true;
                Debug.LogWarning(
                    $"[ShaderModule] Shader not found for module '{Name}'.\n" +
                    $"  Attempted load key: '{_loadBy}'\n" +
                    $"  - AssetBundle registry: {(ShaderAssetRegistry.TryGetShader(_loadBy, out var reg) && reg != null ? "FOUND" : "NOT FOUND")}\n" +
                    $"  - Shader.Find: {(UnityEngine.Shader.Find(_loadBy) != null ? "FOUND" : "NOT FOUND")}\n" +
                    $"  - Resources.Load: {(Resources.Load<Shader>(_loadBy) != null ? "FOUND" : "NOT FOUND")}\n" +
                    $"  This warning appears only once per session for this module. " +
                    $"If the shader is loaded via AssetBundle, ensure the bundle is loaded before this module is initialized."
                ); // end of warning
            }

            return _shader;
        }

        // Material management is handled externally; this class does not create or manage materials.

        public abstract TResult Run(in TArgs args);

        public virtual bool TryValidate(out string error)
        {
            error = null;
            var s = Shader;
            if (s == null)
            {
                error = $"Shader asset not assigned/loaded for '{Name}' (key: '{_loadBy ?? "null"}').";
                return false;
            }
            return true;
        }

        public virtual bool TrySelfTest(out string report)
        {
            report = "No self-test implemented.";
            return true;
        }

        // New: Default implementation for applying arguments; override in subclasses
        public virtual void ApplyArgs(Material mat, object args)
        {   // Base implementation does nothing; subclasses should override to apply specific args
        }
    }

    /// Central registry: auto-discovers any IShaderModule with a public parameterless ctor.
    public static class ShaderRegistry
    {
        private static readonly Dictionary<string, IShaderModule> _byName = new(StringComparer.Ordinal);
        private static readonly object _lock = new();
        private static bool _initialized;

        public static bool IsInitialized => _initialized;

        // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        // private static void AutoInit() => Initialize();

        public static void Initialize(bool force = false)
        {   // Explicitly initialize the registry; no longer auto-initialized
            lock (_lock)
            {
                if (_initialized && !force) return;
                _initialized = true;

                Debug.Log("[ShaderRegistry] Initializing shader module registry.");

                _byName.Clear();

                foreach (var t in DiscoverModuleTypes(typeof(IShaderModule)))
                {
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                    try
                    {
                        var instance = (IShaderModule)Activator.CreateInstance(t);
                        Register(instance);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[ShaderRegistry] Failed to create '{t.FullName}': {e}");
                    }
                }
            }
        }

        public static void Register(IShaderModule module)
        {
            if (module == null) return;
            if (string.IsNullOrWhiteSpace(module.Name))
            {
                Debug.LogWarning($"[ShaderRegistry] Skipping module with empty Name: {module.GetType().FullName}");
                return;
            }

            _byName[module.Name] = module;
        }

        public static IShaderModule Get(string name)
        {   // Only return if already initialized; never auto-initialize
            return (_initialized && name != null && _byName.TryGetValue(name, out var m)) ? m : null;
        }

        public static IReadOnlyCollection<IShaderModule> AllModules
        {   // Only return if already initialized; never auto-initialize
            get => _initialized ? _byName.Values.ToArray() : Array.Empty<IShaderModule>();
        }

        private static IEnumerable<Type> DiscoverModuleTypes(Type target)
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(SafeGetTypes)
                .Where(t =>
                    t != null &&
                    !t.IsAbstract &&
                    !t.IsInterface &&
                    !t.IsGenericTypeDefinition &&
                    target.IsAssignableFrom(t)
                );
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(x => x != null); }
            catch { return Array.Empty<Type>(); }
        }
    }

    public enum ShaderType
    {   // Type of shader module (normal only, compute removed)
        Shader
    }
}
