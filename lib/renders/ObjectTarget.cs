using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrameEmbededState.Lib.Renders
{
    public static class ObjectTarget
    {
        sealed class Cache
        {
            public int NextRebuildFrame;
            public int TargetsKey;
            public Renderer[] Renderers = Array.Empty<Renderer>();
            public Material[] Materials = Array.Empty<Material>();
            public VisualOverlayManager.RendererMaterialGroup[] Groups = Array.Empty<VisualOverlayManager.RendererMaterialGroup>();
            public VisualOverlayManager.ModelTextureData[] Models = Array.Empty<VisualOverlayManager.ModelTextureData>();
            public Dictionary<int, MaterialBackup> OriginalMaterialData = new Dictionary<int, MaterialBackup>();
        }

        sealed class MaterialBackup
        {
            public Texture ColorTexture;
            public Texture NormalTexture;
            public MaterialPropertyBlock[] PropertyBlocks;
            public Material[] OriginalMaterials;
        }

        static readonly Dictionary<int, Cache> _cacheBySettingsId = new Dictionary<int, Cache>();   

        static Texture2D _cpuSrcTex;
        static Texture2D _cpuDstTex;
        static NativeArray<Color32> _cpuSrc;
        static NativeArray<Color32> _cpuDst;
        static int _w, _h;
        static Rect _rect;

        public static void Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            RenderTexture srcRT,
            Renderer[] objectRenderers)
        {
            if (settings == null || settings.Execute == null || srcRT == null)
                return;

            var roots = ResolveRoots(objectRenderers);
            Render(settings, srcRT, roots);
        }

        static void Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            RenderTexture srcRT,
            GameObject[] roots)
        {   // Execute render pipeline and track if this is first-time setup
            EnsureCpuBuffers(srcRT.width, srcRT.height);

            int sid = settings.GetHashCode();
            bool isFirstSetup = !_cacheBySettingsId.TryGetValue(sid, out var cache);
            
            if (isFirstSetup)
            {
                cache = new Cache();
                _cacheBySettingsId[sid] = cache;
            }

            int frameCount = Time.frameCount;
            int key = ComputeTargetsKey(roots);

            if (frameCount >= cache.NextRebuildFrame || cache.TargetsKey != key)
            {
                cache.TargetsKey = key;
                cache.NextRebuildFrame = frameCount + 30;
                RebuildCache(cache, roots, isFirstSetup);
            }

            // Read src to CPU
            var prev = RenderTexture.active;
            RenderTexture.active = srcRT;
            _cpuSrcTex.ReadPixels(_rect, 0, 0, false);
            RenderTexture.active = prev;

            CopyNative(_cpuSrc, _cpuDst);

            var frame = new VisualOverlayManager.FrameData
            {
                Source = _cpuSrc,
                Result = _cpuDst,
                Width = _w,
                Height = _h,

                // compiled target data
                Renderers = cache.Renderers,
                Materials = cache.Materials,
                RendererMaterials = cache.Groups,
                ModelTextures = cache.Models,

                MaterialsDirty = false
            };

            settings.Execute(frame);

            // Only re-apply if an effect actually changed materials
            if (frame.MaterialsDirty && frame.RendererMaterials != null)
            {
                var groups = frame.RendererMaterials;
                for (int i = 0; i < groups.Length; i++)
                {
                    var g = groups[i];
                    if (g.Renderer != null && g.Materials != null)
                        g.Renderer.materials = g.Materials;
                }
            }

            // NOTE: Writing frame.Result back to GPU depends on how VisualOverlayManager composites.
            // If you need it here, you’d upload _cpuDstTex and blit; otherwise leave as your pipeline already does.
        }

        static GameObject[] ResolveRoots(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                return Array.Empty<GameObject>();

            // no LINQ (allocation-free)
            var roots = new List<GameObject>(8);
            var seen = new HashSet<int>();

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var root = r.transform != null && r.transform.root != null
                    ? r.transform.root.gameObject
                    : r.gameObject;

                if (root == null) continue;
                int id = root.GetInstanceID();
                if (seen.Add(id)) roots.Add(root);
            }

            return roots.Count == 0 ? Array.Empty<GameObject>() : roots.ToArray();
        }

        static int ComputeTargetsKey(GameObject[] roots)
        {
            unchecked
            {
                int h = 17;
                if (roots != null)
                {
                    for (int i = 0; i < roots.Length; i++)
                        h = h * 31 + (roots[i] ? roots[i].GetInstanceID() : 0);
                }
                return h;
            }
        }

        static void RebuildCache(Cache cache, GameObject[] roots, bool captureOriginals)
        {   // Rebuild cache and optionally capture original material state on first run only
            var renderers = new List<Renderer>(128);
            var rendererSeen = new HashSet<int>();

            var groups = new List<VisualOverlayManager.RendererMaterialGroup>(128);

            var materials = new List<Material>(256);
            var materialSeen = new HashSet<int>();

            var models = new List<VisualOverlayManager.ModelTextureData>(128);
            
            if (captureOriginals)
                cache.OriginalMaterialData.Clear();

            if (roots != null)
            {
                for (int ri = 0; ri < roots.Length; ri++)
                {
                    var root = roots[ri];
                    if (!root) continue;

                    var rs = root.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < rs.Length; i++)
                    {
                        var r = rs[i];
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

                        int rid = r.GetInstanceID();
                        if (!rendererSeen.Add(rid)) continue;

                        renderers.Add(r);

                        var mats = r.materials;
                        if (mats != null && mats.Length > 0)
                        {
                            groups.Add(new VisualOverlayManager.RendererMaterialGroup { Renderer = r, Materials = mats });

                            for (int m = 0; m < mats.Length; m++)
                            {
                                var mat = mats[m];
                                if (mat == null) continue;
                                int mid = mat.GetInstanceID();
                                if (materialSeen.Add(mid)) materials.Add(mat);
                            }

                            if (captureOriginals && !cache.OriginalMaterialData.ContainsKey(rid))
                            {   // Only capture on first setup, never overwrite existing backups
                                var mpbBackup = new MaterialPropertyBlock[mats.Length];
                                var originalMats = new Material[mats.Length];
                                
                                for (int m = 0; m < mats.Length; m++)
                                {
                                    mpbBackup[m] = new MaterialPropertyBlock();
                                    r.GetPropertyBlock(mpbBackup[m], m);
                                    originalMats[m] = mats[m];
                                }

                                var backup = new MaterialBackup { PropertyBlocks = mpbBackup, OriginalMaterials = originalMats };

                                if (mats[0] != null && mats[0].HasProperty("_ColorTexture"))
                                    backup.ColorTexture = mats[0].GetTexture("_ColorTexture");
                                if (mats[0] != null && mats[0].HasProperty("_NormalMap"))
                                    backup.NormalTexture = mats[0].GetTexture("_NormalMap");

                                cache.OriginalMaterialData[rid] = backup;
                            }

                            if (r is MeshRenderer mr && mats[0] != null)
                            {   // Use backed-up textures if available, otherwise current
                                Texture2D colorTex = null;
                                Texture2D normalTex = null;
                                
                                if (cache.OriginalMaterialData.TryGetValue(rid, out var existing))
                                {
                                    colorTex = existing.ColorTexture as Texture2D;
                                    normalTex = existing.NormalTexture as Texture2D;
                                }
                                else if (mats[0] != null)
                                {
                                    if (mats[0].HasProperty("_ColorTexture"))
                                        colorTex = mats[0].GetTexture("_ColorTexture") as Texture2D;
                                    if (mats[0].HasProperty("_NormalMap"))
                                        normalTex = mats[0].GetTexture("_NormalMap") as Texture2D;
                                }

                                models.Add(new VisualOverlayManager.ModelTextureData
                                {
                                    Renderer = mr,
                                    ColorTexture = colorTex,
                                    NormalTexture = normalTex,
                                    UseNormals = normalTex != null,
                                    Smoothness = mats[0].HasProperty("_Smoothness") ? mats[0].GetFloat("_Smoothness") : 0.5f
                                });
                            }
                        }
                    }
                }
            }

            cache.Renderers = renderers.Count == 0 ? Array.Empty<Renderer>() : renderers.ToArray();
            cache.Groups = groups.Count == 0 ? Array.Empty<VisualOverlayManager.RendererMaterialGroup>() : groups.ToArray();
            cache.Materials = materials.Count == 0 ? Array.Empty<Material>() : materials.ToArray();
            cache.Models = models.Count == 0 ? Array.Empty<VisualOverlayManager.ModelTextureData>() : models.ToArray();
        }

        static void EnsureCpuBuffers(int w, int h)
        {
            if (_cpuSrcTex != null && _w == w && _h == h)
                return;

            if (_cpuSrcTex != null) Object.Destroy(_cpuSrcTex);
            if (_cpuDstTex != null) Object.Destroy(_cpuDstTex);

            _w = w; _h = h;
            _rect = new Rect(0, 0, w, h);

            _cpuSrcTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _cpuDstTex = new Texture2D(w, h, TextureFormat.RGBA32, false);

            _cpuSrc = _cpuSrcTex.GetRawTextureData<Color32>();
            _cpuDst = _cpuDstTex.GetRawTextureData<Color32>();
        }

        static void CopyNative(NativeArray<Color32> from, NativeArray<Color32> to)
        {
            int n = from.Length < to.Length ? from.Length : to.Length;
            for (int i = 0; i < n; i++) to[i] = from[i];
        }

        public static void Release()
        {   // Release resources and restore materials to original state
            foreach (var kvp in _cacheBySettingsId)
                RestoreMaterials(kvp.Value);

            if (_cpuSrcTex != null) Object.Destroy(_cpuSrcTex);
            if (_cpuDstTex != null) Object.Destroy(_cpuDstTex);

            _cpuSrcTex = null;
            _cpuDstTex = null;
            _cpuSrc = default;
            _cpuDst = default;
            _w = _h = 0;

            _cacheBySettingsId.Clear();
        }

        static void RestoreMaterials(Cache cache)
        {   // Restore renderers to their original material state using saved property blocks
            if (cache == null || cache.OriginalMaterialData == null) return;

            foreach (var kvp in cache.OriginalMaterialData)
            {
                int rid = kvp.Key;
                var backup = kvp.Value;
                var renderer = cache.Renderers.FirstOrDefault(r => r != null && r.GetInstanceID() == rid);

                if (renderer != null)
                {
                    if (backup.OriginalMaterials != null && backup.OriginalMaterials.Length > 0)
                        renderer.materials = backup.OriginalMaterials;

                    if (backup.PropertyBlocks != null)
                    {
                        for (int i = 0; i < backup.PropertyBlocks.Length; i++)
                        {
                            if (backup.PropertyBlocks[i] != null)
                                renderer.SetPropertyBlock(backup.PropertyBlocks[i], i);
                        }
                    }
                }
            }
        }
    }
}
