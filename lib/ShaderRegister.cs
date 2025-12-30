using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;

namespace FrameEmbededState
{
    /// Attach this to shader module classes to declare metadata without central registration.
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ComputeShaderModuleAttribute : Attribute
    {
        public string Name { get; }
        public string KernelName { get; }

        public ComputeShaderModuleAttribute(string name, string kernelName = "CSMain")
        {
            Name = name;
            KernelName = kernelName;
        }
    }

    /// Non-generic interface for registry storage.
    public interface IComputeShaderModule
    {
        string Name { get; }
        string KernelName { get; }
        ComputeShader Shader { get; }
        bool IsLoaded { get; }

        object RunBoxed(object args);

        /// Lightweight checks: platform support, shader loaded, kernel exists.
        bool TryValidate(out string error);

        /// Optional: deeper test (dispatch, sanity checks). Return false to fail.
        bool TrySelfTest(out string report);
    }

    /// Base class for typed shader modules.
    public abstract class ComputeShaderModule<TArgs, TResult> : IComputeShaderModule
    {
        protected ComputeShader _shader;
        private string _name;
        private string _kernelName;

        protected ComputeShaderModule()
        {
            // Pull defaults from attribute if present.
            var attr = GetType().GetCustomAttribute<ComputeShaderModuleAttribute>();
            if (attr != null)
            {
                _name = attr.Name;
                _kernelName = attr.KernelName;
            }
        }

        /// Override if you prefer code-defined metadata instead of attribute.
        public virtual string Name => _name ?? GetType().Name;
        public virtual string KernelName => _kernelName ?? "CSMain";
        public ComputeShader Shader => _shader;
        public bool IsLoaded => _shader != null;

        /// Default loader uses Resources. Override for Addressables / injected refs / custom lookup.
        protected virtual ComputeShader LoadShader()
        {   // No-op: shader is assigned externally
            return null;
        }

        public TResult Run(in TArgs args)
        {   // Run the compute shader with provided arguments, error if not assigned
            if (!SystemInfo.supportsComputeShaders)
                throw new NotSupportedException("Compute shaders are not supported on this platform.");

            var shader = Shader;
            if (shader == null)
                throw new InvalidOperationException($"ComputeShader for '{Name}' is not assigned. This usually means the asset bundle containing the compute shader has not been loaded or the shader name does not match.");

            int kernel = shader.FindKernel(KernelName);

            Bind(shader, kernel, in args);

            var (gx, gy, gz) = GetDispatchGroups(in args);
            shader.Dispatch(kernel, gx, gy, gz);

            return GetResult(in args);
        }

        /// Set textures/buffers/constants.
        protected abstract void Bind(ComputeShader shader, int kernel, in TArgs args);

        /// Compute dispatch group counts.
        protected abstract (int x, int y, int z) GetDispatchGroups(in TArgs args);

        /// Most shaders write into a target in args; default is default(TResult). Override as needed.
        protected virtual TResult GetResult(in TArgs args) => default;

        object IComputeShaderModule.RunBoxed(object args)
        {
            if (args is not TArgs typed)
                throw new ArgumentException($"'{Name}' expected args of type {typeof(TArgs).Name} but got {args?.GetType().Name ?? "null"}.");
            return Run(in typed);
        }

        public virtual bool TryValidate(out string error)
        {   // Validate that the compute shader is assigned and kernel exists
            error = null;

            if (!SystemInfo.supportsComputeShaders)
            {
                error = "SystemInfo.supportsComputeShaders == false";
                return false;
            }

            var shader = Shader;
            if (shader == null)
            {
                error = $"ComputeShader asset not assigned for '{Name}'. This usually means the asset bundle containing the compute shader has not been loaded or the shader name does not match.";
                return false;
            }

            try
            {
                shader.FindKernel(KernelName);
                return true;
            }
            catch (Exception e)
            {
                error = $"Kernel '{KernelName}' not found: {e.Message}";
                return false;
            }
        }

        public virtual bool TrySelfTest(out string report)
        {
            report = "No self-test implemented.";
            return true;
        }
    }

    /// Central registry: auto-discovers any IComputeShaderModule with a public parameterless ctor.
    public static class ComputeShaderRegistry
    {
        private static readonly Dictionary<string, IComputeShaderModule> _byName = new(StringComparer.Ordinal);
        private static readonly object _lock = new();
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInit() => Initialize();

        public static void Initialize(bool force = false)
        {
            lock (_lock)
            {
                if (_initialized && !force) return;
                _initialized = true;
                _byName.Clear();

                foreach (var t in DiscoverModuleTypes())
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                    IComputeShaderModule instance;
                    try
                    {
                        instance = (IComputeShaderModule)Activator.CreateInstance(t);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[ComputeShaderRegistry] Failed to create '{t.FullName}': {e}");
                        continue;
                    }

                    Register(instance);
                }
            }
        }

        public static void Register(IComputeShaderModule module)
        {
            if (module == null) return;
            if (string.IsNullOrWhiteSpace(module.Name))
            {
                Debug.LogWarning($"[ComputeShaderRegistry] Skipping module with empty Name: {module.GetType().FullName}");
                return;
            }

            _byName[module.Name] = module;
        }

        public static IComputeShaderModule Get(string name)
        {
            Initialize();
            return (name != null && _byName.TryGetValue(name, out var m)) ? m : null;
        }

        public static T Get<T>() where T : class, IComputeShaderModule
        {
            Initialize();
            return _byName.Values.OfType<T>().FirstOrDefault();
        }

        public static IReadOnlyCollection<IComputeShaderModule> All
        {
            get { Initialize(); return _byName.Values.ToArray(); }
        }

        public static (int passed, int failed) RunAllValidations(bool log = true)
        {
            Initialize();
            int passed = 0, failed = 0;

            foreach (var m in All)
            {
                if (m.TryValidate(out var err))
                {
                    passed++;
                    if (log) Debug.Log($"[ComputeShaderRegistry] OK: {m.Name}");
                }
                else
                {
                    failed++;
                    if (log) Debug.LogError($"[ComputeShaderRegistry] FAIL: {m.Name} — {err}");
                }
            }
            return (passed, failed);
        }

        public static (int passed, int failed) RunAllSelfTests(bool log = true)
        {
            Initialize();
            int passed = 0, failed = 0;

            foreach (var m in All)
            {
                bool ok;
                string msg;

                try { ok = m.TrySelfTest(out msg); }
                catch (Exception e) { ok = false; msg = e.ToString(); }

                if (ok)
                {
                    passed++;
                    if (log) Debug.Log($"[ComputeShaderRegistry] TEST OK: {m.Name} — {msg}");
                }
                else
                {
                    failed++;
                    if (log) Debug.LogError($"[ComputeShaderRegistry] TEST FAIL: {m.Name} — {msg}");
                }
            }
            return (passed, failed);
        }

        private static IEnumerable<Type> DiscoverModuleTypes()
        {   // Discover all non-abstract, non-generic, non-interface types implementing IComputeShaderModule

            var target = typeof(IComputeShaderModule);

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
}
