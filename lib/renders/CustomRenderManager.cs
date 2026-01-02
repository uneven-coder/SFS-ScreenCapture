using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrameEmbededState.Lib.Renders
{
    public static class ObjectTargetRenderDispatcher
    {   // Dispatch argument updates for object-targeting shader modules
        private static readonly Dictionary<string, IShaderModule> _activeModules = new Dictionary<string, IShaderModule>();
        private static string _currentFrameModule;

        public static void Register(string key, IShaderModule module)
        {   // Register a module for object-targeting rendering
            if (string.IsNullOrEmpty(key) || module == null) return;
            _activeModules[key] = module;
        }

        public static void BeginFrame(string key)
        {   // Mark the start of a render frame for a specific module
            _currentFrameModule = key;
        }

        public static void EndFrame()
        {   // Clear the current frame context
            _currentFrameModule = null;
        }

        public static void UpdateArgs(string key, object args)
        {   // Update shader arguments for the module
            if (!_activeModules.TryGetValue(key, out var module)) return;

            var updateMethod = module.GetType().GetMethod("UpdateArgs");
            updateMethod?.Invoke(module, new[] { args });
        }

        public static void ExecuteModule(string key, object args)
        {   // Execute a module's Run method with the provided arguments
            if (!_activeModules.TryGetValue(key, out var module)) return;

            BeginFrame(key);
            var runMethod = module.GetType().GetMethod("Run");
            runMethod?.Invoke(module, new[] { args });
            EndFrame();
        }

        public static void Restore(string key)
        {   // Restore original materials for the module
            if (!_activeModules.TryGetValue(key, out var module)) return;

            var restoreMethod = module.GetType().GetMethod("RestoreMaterials");
            restoreMethod?.Invoke(module, null);
        }

        public static void RestoreAll()
        {   // Restore all active modules
            foreach (var key in _activeModules.Keys)
                Restore(key);

            _activeModules.Clear();
        }
    }
}
