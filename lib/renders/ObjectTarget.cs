using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrameEmbededState.Lib.Renders
{
    public static class ObjectTarget
    {
        private static Texture2D _cpuSrcTex;
        private static Texture2D _cpuDstTex;
        private static NativeArray<Color32> _cpuSrc;
        private static NativeArray<Color32> _cpuDst;
        private static int _cpuWidth;
        private static int _cpuHeight;
        private static Rect _cpuRect;
        private static int _frameCount;
        private static int _lastLogFrame;

        public static void Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            RenderTexture srcRT,
            Renderer[] objectRenderers)
        {
            // Convert the renderer list into root objects so we can collect:
            // - all child renderers
            // - custom mesh modules (PipeMesh/PolygonMesh/BaseMesh)
            // - ModelSetup components + their renderers
            if (objectRenderers == null || objectRenderers.Length == 0)
            {
                Render(settings, srcRT, Array.Empty<object>());
                return;
            }

            object[] roots = objectRenderers
                .Where(r => r != null)
                .Select(r =>
                {
                    var t = r.transform;
                    return (object)((t != null && t.root != null) ? t.root.gameObject : r.gameObject);
                })
                .Distinct()
                .ToArray();

            Render(settings, srcRT, roots.Length > 0 ? roots : (object[])objectRenderers);
        }

        public static void Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            RenderTexture srcRT,
            object[] targets)
        {   // Main render entry point for ObjectTarget mode with material manipulation, runs every frame to maintain effect
            // Collects ALL renderers from hierarchy regardless of component type to catch custom SFS part systems
            if (settings == null || settings.Execute == null)
            {
                if (_frameCount % 300 == 0) Debug.LogWarning("[ObjectTarget] Settings or Execute callback is null");
                _frameCount++;
                return;
            }

            var groups = new List<VisualOverlayManager.RendererMaterialGroup>();
            var modelDataList = new List<VisualOverlayManager.ModelTextureData>();
            var seenRenderers = new HashSet<Renderer>();

            var resolvedRenderers = ResolveRenderers(targets);
            if (resolvedRenderers != null)
            {
                foreach (var renderer in resolvedRenderers)
                {
                    if (renderer == null || !seenRenderers.Add(renderer)) continue;

                    var materials = renderer.materials;
                    if (materials == null || materials.Length == 0) continue;

                    groups.Add(new VisualOverlayManager.RendererMaterialGroup { Renderer = renderer, Materials = materials });
                }
            }

            var meshModules = ResolveMeshModules(targets);
            if (meshModules != null)
            {
                foreach (var module in meshModules)
                {
                    if (module == null) continue;

                    var moduleRenderer = module.GetComponent<Renderer>();
                    if (moduleRenderer == null || !moduleRenderer.enabled || !moduleRenderer.gameObject.activeInHierarchy || !seenRenderers.Add(moduleRenderer)) continue;

                    var materials = moduleRenderer.materials;
                    if (materials == null || materials.Length == 0) continue;

                    groups.Add(new VisualOverlayManager.RendererMaterialGroup { Renderer = moduleRenderer, Materials = materials });
                }
            }

            var modelSetups = ResolveModelSetups(targets);
            if (modelSetups != null)
            {
                foreach (var setup in modelSetups)
                {
                    if (setup.MeshRenderers == null) continue;

                    foreach (var meshRenderer in setup.MeshRenderers)
                    {
                        if (meshRenderer == null || !meshRenderer.enabled || !meshRenderer.gameObject.activeInHierarchy || !seenRenderers.Add(meshRenderer)) continue;

                        modelDataList.Add(new VisualOverlayManager.ModelTextureData
                        {
                            Renderer = meshRenderer, ColorTexture = setup.ColorTex, NormalTexture = setup.NormalTex,
                            UseNormals = setup.UseNormals, Smoothness = setup.Smoothness
                        });

                        var materials = meshRenderer.materials;
                        if (materials != null && materials.Length > 0)
                            groups.Add(new VisualOverlayManager.RendererMaterialGroup { Renderer = meshRenderer, Materials = materials });
                    }
                }
            }

            var partComponents = ResolvePartComponents(targets);
            if (partComponents != null)
            {
                foreach (var part in partComponents)
                {
                    if (part == null) continue;

                    var partRenderers = part.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in partRenderers)
                    {
                        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !seenRenderers.Add(renderer)) continue;

                        var materials = renderer.materials;
                        if (materials != null && materials.Length > 0)
                            groups.Add(new VisualOverlayManager.RendererMaterialGroup { Renderer = renderer, Materials = materials });
                    }
                }
            }

            var allRootObjects = CollectRootGameObjects(targets);
            foreach (var rootObj in allRootObjects)
            {
                if (rootObj == null) continue;

                var allRenderers = rootObj.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in allRenderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !seenRenderers.Add(renderer)) continue;

                    var materials = renderer.materials;
                    if (materials != null && materials.Length > 0)
                        groups.Add(new VisualOverlayManager.RendererMaterialGroup { Renderer = renderer, Materials = materials });
                }
            }

            bool shouldLog = _frameCount - _lastLogFrame >= 60;
            if (shouldLog)
            {
                Debug.Log($"[ObjectTarget] Frame {_frameCount}: {groups.Count} renderer groups ({seenRenderers.Count} unique), {modelDataList.Count} models, {partComponents?.Length ?? 0} parts");
                var sampleCount = Mathf.Min(5, groups.Count);
                for (int i = 0; i < sampleCount; i++)
                {
                    var g = groups[i];
                    if (g.Renderer != null)
                        Debug.Log($"[ObjectTarget] Sample {i}: {g.Renderer.gameObject.name} | materials={g.Materials?.Length ?? 0} | shader={g.Materials?[0]?.shader?.name ?? "null"}");
                }
            }

            _frameCount++;

            if (groups.Count == 0 && modelDataList.Count == 0)
            {
                if (shouldLog) Debug.LogWarning("[ObjectTarget] No valid renderers or models found");
                return;
            }

            if (srcRT == null)
            {
                if (shouldLog) Debug.LogWarning("[ObjectTarget] Source RenderTexture is null");
                return;
            }

            EnsureCpuBuffers(srcRT.width, srcRT.height);

            var prev = RenderTexture.active;
            RenderTexture.active = srcRT;
            _cpuSrcTex.ReadPixels(_cpuRect, 0, 0, false);
            RenderTexture.active = prev;

            CopyNative(_cpuSrc, _cpuDst);

            var frameData = new VisualOverlayManager.FrameData
            {
                Source = _cpuSrc, Result = _cpuDst, Width = _cpuWidth, Height = _cpuHeight,
                RendererMaterials = groups.ToArray(), ModelTextures = modelDataList.ToArray()
            };

            settings.Execute(frameData);

            var updatedGroups = frameData.RendererMaterials;
            if (updatedGroups != null)
            {
                for (int i = 0; i < updatedGroups.Length; i++)
                {
                    var g = updatedGroups[i];
                    if (g.Renderer == null || g.Materials == null) continue;

                    g.Renderer.materials = g.Materials;
                }

                if (shouldLog) Debug.Log($"[ObjectTarget] Materials applied to {updatedGroups.Length} renderers");
            }
        }

        public static void Release()
        {   // Clean up CPU buffers and reset frame counter
            if (_cpuSrcTex != null) Object.Destroy(_cpuSrcTex);
            if (_cpuDstTex != null) Object.Destroy(_cpuDstTex);

            _cpuSrcTex = null;
            _cpuDstTex = null;
            _cpuSrc = default;
            _cpuDst = default;
            _cpuWidth = _cpuHeight = 0;
            _frameCount = 0;
            _lastLogFrame = 0;
        }

        private static void EnsureCpuBuffers(int w, int h)
        {
            if (_cpuSrcTex != null && _cpuWidth == w && _cpuHeight == h)
                return;

            if (_cpuSrcTex != null)
                Object.Destroy(_cpuSrcTex);

            if (_cpuDstTex != null)
                Object.Destroy(_cpuDstTex);

            _cpuWidth = w;
            _cpuHeight = h;
            _cpuRect = new Rect(0, 0, w, h);

            _cpuSrcTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _cpuDstTex = new Texture2D(w, h, TextureFormat.RGBA32, false);

            _cpuSrc = _cpuSrcTex.GetRawTextureData<Color32>();
            _cpuDst = _cpuDstTex.GetRawTextureData<Color32>();
        }

        private static void CopyNative(NativeArray<Color32> from, NativeArray<Color32> to)
        {
            int n = Mathf.Min(from.Length, to.Length);
            for (int i = 0; i < n; i++)
                to[i] = from[i];
        }

        private static Renderer[] ResolveRenderers(object[] targets)
        {
            if (targets == null || targets.Length == 0)
                return Array.Empty<Renderer>();

            var resolved = new List<Renderer>();
            var seen = new HashSet<Renderer>();
            foreach (var target in targets)
            {
                if (target == null)
                    continue;

                foreach (var renderer in EnumerateRenderers(target))
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        continue;

                    if (seen.Add(renderer))
                        resolved.Add(renderer);
                }
            }

            return resolved.Count == 0 ? Array.Empty<Renderer>() : resolved.ToArray();
        }

        private static IEnumerable<Renderer> EnumerateRenderers(object target)
        {
            if (target is Renderer renderer)
            {
                yield return renderer;
                yield break;
            }

            if (target is IEnumerable<Renderer> rendererEnumerable)
            {
                foreach (var ren in rendererEnumerable)
                    yield return ren;
                yield break;
            }

            if (target is IEnumerable enumerable && !(target is string))
            {
                foreach (var entry in enumerable)
                {
                    foreach (var child in EnumerateRenderers(entry))
                        yield return child;
                }
                yield break;
            }

            if (target is GameObject gameObject)
            {
                foreach (var childRenderer in gameObject.GetComponentsInChildren<Renderer>(true))
                    yield return childRenderer;
                yield break;
            }

            if (target is Component component)
            {
                foreach (var childRenderer in component.GetComponentsInChildren<Renderer>(true))
                    yield return childRenderer;
            }
        }

        private static Component[] ResolveMeshModules(object[] targets)
        {
            if (targets == null || targets.Length == 0)
                return Array.Empty<Component>();

            var resolved = new List<Component>();
            var seen = new HashSet<Component>();

            foreach (var target in targets)
            {
                if (target == null)
                    continue;

                foreach (var module in EnumerateMeshModules(target))
                {
                    if (module == null || !module.gameObject.activeInHierarchy)
                        continue;

                    if (seen.Add(module))
                        resolved.Add(module);
                }
            }

            return resolved.Count == 0 ? Array.Empty<Component>() : resolved.ToArray();
        }

        private static IEnumerable<Component> EnumerateMeshModules(object target)
        {
            if (target == null)
                yield break;

            var targetType = target.GetType();
            if (targetType.Name == "PipeMesh" || targetType.Name == "PolygonMesh" ||
                targetType.Name == "SimplePipe" || targetType.BaseType?.Name == "BaseMesh")
            {
                yield return target as Component;
                yield break;
            }

            if (target is IEnumerable enumerable && !(target is string))
            {
                foreach (var entry in enumerable)
                {
                    foreach (var child in EnumerateMeshModules(entry))
                        yield return child;
                }
                yield break;
            }

            if (target is GameObject gameObject)
            {
                foreach (var module in gameObject.GetComponentsInChildren<Component>(true))
                {
                    if (module == null)
                        continue;

                    var moduleType = module.GetType();
                    if (moduleType.Name == "PipeMesh" || moduleType.Name == "PolygonMesh" ||
                        moduleType.Name == "SimplePipe" || moduleType.BaseType?.Name == "BaseMesh")
                        yield return module;
                }
                yield break;
            }

            if (target is Component component)
            {
                foreach (var module in component.GetComponentsInChildren<Component>(true))
                {
                    if (module == null)
                        continue;

                    var moduleType = module.GetType();
                    if (moduleType.Name == "PipeMesh" || moduleType.Name == "PolygonMesh" ||
                        moduleType.Name == "SimplePipe" || moduleType.BaseType?.Name == "BaseMesh")
                        yield return module;
                }
            }
        }

        private static ModelSetupInfo[] ResolveModelSetups(object[] targets)
        {
            if (targets == null || targets.Length == 0)
                return Array.Empty<ModelSetupInfo>();

            var resolved = new List<ModelSetupInfo>();
            var seen = new HashSet<Component>();

            foreach (var target in targets)
            {
                if (target == null)
                    continue;

                foreach (var setup in EnumerateModelSetups(target))
                {
                    if (setup == null || !setup.gameObject.activeInHierarchy)
                        continue;

                    if (!seen.Add(setup))
                        continue;

                    var info = ExtractModelSetupInfo(setup);
                    if (info.MeshRenderers != null && info.MeshRenderers.Length > 0)
                        resolved.Add(info);
                }
            }

            return resolved.Count == 0 ? Array.Empty<ModelSetupInfo>() : resolved.ToArray();
        }

        private static IEnumerable<Component> EnumerateModelSetups(object target)
        {
            if (target == null)
                yield break;

            var targetType = target.GetType();
            if (targetType.Name == "ModelSetup")
            {
                yield return target as Component;
                yield break;
            }

            if (target is IEnumerable enumerable && !(target is string))
            {
                foreach (var entry in enumerable)
                {
                    foreach (var child in EnumerateModelSetups(entry))
                        yield return child;
                }
                yield break;
            }

            if (target is GameObject gameObject)
            {
                foreach (var comp in gameObject.GetComponentsInChildren<Component>(true))
                {
                    if (comp != null && comp.GetType().Name == "ModelSetup")
                        yield return comp;
                }
                yield break;
            }

            if (target is Component component)
            {
                foreach (var comp in component.GetComponentsInChildren<Component>(true))
                {
                    if (comp != null && comp.GetType().Name == "ModelSetup")
                        yield return comp;
                }
            }
        }

        private static ModelSetupInfo ExtractModelSetupInfo(Component modelSetup)
        {
            var info = new ModelSetupInfo();
            var type = modelSetup.GetType();

            var renderersField = type.GetField("meshRenderers");
            if (renderersField != null)
                info.MeshRenderers = renderersField.GetValue(modelSetup) as MeshRenderer[];

            var colorTexField = type.GetField("colorTex");
            if (colorTexField != null)
                info.ColorTex = colorTexField.GetValue(modelSetup) as Texture2D;

            var normalTexField = type.GetField("normalTex");
            if (normalTexField != null)
                info.NormalTex = normalTexField.GetValue(modelSetup) as Texture2D;

            var smoothnessField = type.GetField("smoothness");
            if (smoothnessField != null)
                info.Smoothness = (float)smoothnessField.GetValue(modelSetup);

            var useNormalsField = type.GetField("useNormals");
            if (useNormalsField != null)
                info.UseNormals = (bool)useNormalsField.GetValue(modelSetup);

            return info;
        }

        private static Component[] ResolvePartComponents(object[] targets)
        {   // Resolve SFS Part components from target hierarchy
            if (targets == null || targets.Length == 0)
                return Array.Empty<Component>();

            var resolved = new List<Component>();
            var seen = new HashSet<Component>();

            foreach (var target in targets)
            {
                if (target == null) continue;

                foreach (var part in EnumeratePartComponents(target))
                {
                    if (part == null || !part.gameObject.activeInHierarchy) continue;

                    if (seen.Add(part))
                        resolved.Add(part);
                }
            }

            return resolved.Count == 0 ? Array.Empty<Component>() : resolved.ToArray();
        }

        private static IEnumerable<Component> EnumeratePartComponents(object target)
        {   // Find Part components in GameObject hierarchy using reflection
            if (target == null)
                yield break;

            var targetType = target.GetType();
            if (targetType.Name == "Part" || targetType.Namespace == "SFS.Parts")
            {
                yield return target as Component;
                yield break;
            }

            if (target is IEnumerable enumerable && !(target is string))
            {
                foreach (var entry in enumerable)
                {
                    foreach (var child in EnumeratePartComponents(entry))
                        yield return child;
                }
                yield break;
            }

            if (target is GameObject gameObject)
            {
                foreach (var comp in gameObject.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;

                    var compType = comp.GetType();
                    if (compType.Name == "Part" || compType.Namespace == "SFS.Parts")
                        yield return comp;
                }
                yield break;
            }

            if (target is Component component)
            {
                foreach (var comp in component.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;

                    var compType = comp.GetType();
                    if (compType.Name == "Part" || compType.Namespace == "SFS.Parts")
                        yield return comp;
                }
            }
        }

        private static GameObject[] CollectRootGameObjects(object[] targets)
        {
            if (targets == null || targets.Length == 0)
                return Array.Empty<GameObject>();

            var roots = new HashSet<GameObject>();

            foreach (var target in targets)
            {
                if (target == null) continue;

                if (target is GameObject go) { roots.Add(go.transform.root.gameObject); continue; }
                if (target is Component comp) { roots.Add(comp.transform.root.gameObject); continue; }
                if (target is IEnumerable enumerable && !(target is string))
                {
                    foreach (var entry in enumerable)
                    {
                        if (entry is GameObject goEntry) roots.Add(goEntry.transform.root.gameObject);
                        else if (entry is Component compEntry) roots.Add(compEntry.transform.root.gameObject);
                    }
                }
            }

            return roots.ToArray();
        }

        private struct ModelSetupInfo
        {
            public MeshRenderer[] MeshRenderers;
            public Texture2D ColorTex;
            public Texture2D NormalTex;
            public float Smoothness;
            public bool UseNormals;
        }
    }
}
