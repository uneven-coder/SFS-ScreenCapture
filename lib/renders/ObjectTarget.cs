using System;
using System.Collections.Generic;
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

            public readonly Dictionary<int, MaterialBackup> OriginalByRendererId = new Dictionary<int, MaterialBackup>(256);
            public readonly Dictionary<int, Renderer> RendererById = new Dictionary<int, Renderer>(256);

            // Full-frame UV buffer aligned with pixels (x + y*w)
            public Vector2[] AtmosphereUv;
            public int AtmosphereUvW, AtmosphereUvH;
            public float AtmosphereRMin;
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
        {   // Entry point for rendering overlays; applies effect to objects or screen as needed

            if (settings?.Execute == null) return;

            // If this is an object effect, apply directly to materials
            if (settings.RenderMode == OverlayRenderMode.ObjectLayer && objectRenderers != null && objectRenderers.Length > 0)
            {   // Apply effect directly to object materials

                var roots = ResolveRoots(objectRenderers);

                int sid = settings.GetHashCode();
                bool firstSetup = !_cacheBySettingsId.TryGetValue(sid, out var cache);
                if (firstSetup)
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
                    RebuildCache(cache, roots, captureOriginals: firstSetup);
                }

                // Prepare frame data for Execute callback
                var frame = new VisualOverlayManager.FrameData
                {
                    Renderers = cache.Renderers,
                    Materials = cache.Materials,
                    RendererMaterials = cache.Groups,
                    ModelTextures = cache.Models,
                    MaterialsDirty = false,
                    // No screen buffer for object overlays
                    Source = default,
                    Result = default,
                    Width = 0,
                    Height = 0,
                    CorrectedUvBuffer = null
                };

                settings.Execute(frame);

                // If materials were changed, update them
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

                return;
            }

            // ...existing code for screen/camera buffer overlays...
            if (srcRT == null) return;

            var rootsScreen = ResolveRoots(objectRenderers);
            RenderScreenOverlay(settings, srcRT, rootsScreen);
        }

        static void RenderScreenOverlay(
            VisualOverlayManager.VisualOverlaySettings settings,
            RenderTexture srcRT,
            GameObject[] roots)
        {   // Handles screen/camera buffer overlays

            EnsureCpuBuffers(srcRT.width, srcRT.height);

            int sid = settings.GetHashCode();
            bool firstSetup = !_cacheBySettingsId.TryGetValue(sid, out var cache);
            if (firstSetup)
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
                RebuildCache(cache, roots, captureOriginals: firstSetup);
            }

            // Read srcRT -> CPU src texture
            var prevActive = RenderTexture.active;
            RenderTexture.active = srcRT;
            _cpuSrcTex.ReadPixels(_rect, 0, 0, false);
            RenderTexture.active = prevActive;

            // Start result as a copy of source
            CopyNative(_cpuSrc, _cpuDst);

            // Full-frame UV buffer
            var correctedUvBuffer = GetOrBuildAtmosphereUvFullFrame(cache, _w, _h);

            var frame = new VisualOverlayManager.FrameData
            {
                Source = _cpuSrc,
                Result = _cpuDst,
                Width = _w,
                Height = _h,

                Renderers = cache.Renderers,
                Materials = cache.Materials,
                RendererMaterials = cache.Groups,
                ModelTextures = cache.Models,

                MaterialsDirty = false,
                CorrectedUvBuffer = correctedUvBuffer
            };

            settings.Execute(frame);

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

            // Push CPU result -> GPU and write back into srcRT so the effect is visible
            _cpuDstTex.Apply(false, false);

            var prev = RenderTexture.active;
            RenderTexture.active = srcRT;
            Graphics.Blit(_cpuDstTex, srcRT);
            RenderTexture.active = prev;
        }

        static void RebuildCache(Cache cache, GameObject[] roots, bool captureOriginals)
        {
            var renderers = new List<Renderer>(256);
            var groups = new List<VisualOverlayManager.RendererMaterialGroup>(256);
            var materials = new List<Material>(512);
            var models = new List<VisualOverlayManager.ModelTextureData>(256);

            var rendererSeen = new HashSet<int>();
            var materialSeen = new HashSet<int>();

            if (captureOriginals)
                cache.OriginalByRendererId.Clear();

            cache.RendererById.Clear();

            if (roots != null)
            {
                for (int ri = 0; ri < roots.Length; ri++)
                {
                    var root = roots[ri];
                    if (!root) continue;

                    var atmos = root.GetComponentsInChildren<SFS.World.Atmosphere>(true);
                    for (int ai = 0; ai < atmos.Length; ai++)
                    {
                        var mr = atmos[ai] ? atmos[ai].GetComponent<MeshRenderer>() : null;
                        if (!mr || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                        CollectRenderer(cache, mr, renderers, groups, materials, models, rendererSeen, materialSeen, captureOriginals);
                    }

                    var rs = root.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < rs.Length; i++)
                    {
                        var r = rs[i];
                        if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                        CollectRenderer(cache, r, renderers, groups, materials, models, rendererSeen, materialSeen, captureOriginals);
                    }
                }
            }

            cache.Renderers = renderers.Count == 0 ? Array.Empty<Renderer>() : renderers.ToArray();
            cache.Groups = groups.Count == 0 ? Array.Empty<VisualOverlayManager.RendererMaterialGroup>() : groups.ToArray();
            cache.Materials = materials.Count == 0 ? Array.Empty<Material>() : materials.ToArray();
            cache.Models = models.Count == 0 ? Array.Empty<VisualOverlayManager.ModelTextureData>() : models.ToArray();
        }

        static void CollectRenderer(
            Cache cache,
            Renderer r,
            List<Renderer> renderers,
            List<VisualOverlayManager.RendererMaterialGroup> groups,
            List<Material> materials,
            List<VisualOverlayManager.ModelTextureData> models,
            HashSet<int> rendererSeen,
            HashSet<int> materialSeen,
            bool captureOriginals)
        {
            int rid = r.GetInstanceID();
            if (!rendererSeen.Add(rid)) return;

            renderers.Add(r);
            cache.RendererById[rid] = r;

            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return;

            groups.Add(new VisualOverlayManager.RendererMaterialGroup { Renderer = r, Materials = mats });

            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (!mat) continue;
                int mid = mat.GetInstanceID();
                if (materialSeen.Add(mid)) materials.Add(mat);
            }

            if (captureOriginals && !cache.OriginalByRendererId.ContainsKey(rid))
                cache.OriginalByRendererId[rid] = BackupOriginal(r, mats);

            if (r is MeshRenderer mr)
            {
                var md = BuildModelTextureData(cache, rid, mr, mats);
                if (md.Renderer != null) models.Add(md);
            }
        }

        static MaterialBackup BackupOriginal(Renderer r, Material[] mats)
        {
            var mpb = new MaterialPropertyBlock[mats.Length];
            var originalMats = new Material[mats.Length];

            for (int i = 0; i < mats.Length; i++)
            {
                mpb[i] = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb[i], i);
                originalMats[i] = mats[i];
            }

            var backup = new MaterialBackup
            {
                PropertyBlocks = mpb,
                OriginalMaterials = originalMats
            };

            var m0 = mats[0];
            if (m0)
            {
                if (m0.HasProperty("_ColorTexture")) backup.ColorTexture = m0.GetTexture("_ColorTexture");
                if (m0.HasProperty("_NormalMap")) backup.NormalTexture = m0.GetTexture("_NormalMap");
            }

            return backup;
        }

        static VisualOverlayManager.ModelTextureData BuildModelTextureData(Cache cache, int rid, MeshRenderer mr, Material[] mats)
        {
            var m0 = (mats != null && mats.Length > 0) ? mats[0] : null;
            if (!m0) return default;

            Texture2D colorTex = null, normalTex = null;

            if (cache.OriginalByRendererId.TryGetValue(rid, out var b))
            {
                colorTex = b.ColorTexture as Texture2D;
                normalTex = b.NormalTexture as Texture2D;
            }
            else
            {
                if (m0.HasProperty("_ColorTexture")) colorTex = m0.GetTexture("_ColorTexture") as Texture2D;
                if (m0.HasProperty("_NormalMap")) normalTex = m0.GetTexture("_NormalMap") as Texture2D;
            }

            return new VisualOverlayManager.ModelTextureData
            {
                Renderer = mr,
                ColorTexture = colorTex,
                NormalTexture = normalTex,
                UseNormals = normalTex != null,
                Smoothness = m0.HasProperty("_Smoothness") ? m0.GetFloat("_Smoothness") : 0.5f
            };
        }

        static Vector2[] GetOrBuildAtmosphereUvFullFrame(Cache cache, int w, int h)
        {
            // Build a full-frame UV buffer where each pixel gets (u,v) if it lies within the ring.
            // Outside ring => (-1,-1).
            // Cached by (w,h,rMin).

            SFS.World.Atmosphere atmos = null;

            var models = cache.Models;
            if (models != null)
            {
                for (int i = 0; i < models.Length; i++)
                {
                    var r = models[i].Renderer as MeshRenderer;
                    if (!r) continue;
                    var a = r.GetComponent<SFS.World.Atmosphere>();
                    if (!a) continue;
                    atmos = a;
                    break;
                }
            }

            if (!atmos || atmos.planet == null || atmos.planet.data?.basics == null)
                return null;

            float planetRadius = (float)atmos.planet.data.basics.radius;
            float atmosphereHeight = atmos.planet.data?.atmospherePhysics != null
                ? (float)atmos.planet.data.atmospherePhysics.height
                : 1f;

            float outerRadius = planetRadius + atmosphereHeight;
            float rMin = (outerRadius > 1e-6f) ? (planetRadius / outerRadius) : 0f;

            if (cache.AtmosphereUv != null &&
                cache.AtmosphereUvW == w &&
                cache.AtmosphereUvH == h &&
                Mathf.Abs(cache.AtmosphereRMin - rMin) < 1e-6f)
                return cache.AtmosphereUv;

            float cx = w * 0.5f, cy = h * 0.5f;
            float pxOuter, pxInner;

            // Approximate apparent size using camera distance + FOV
            if (Camera.main != null && atmos.planet.transform != null)
            {
                float distance = Vector3.Distance(Camera.main.transform.position, atmos.planet.transform.position);
                if (distance > 1e-6f)
                {
                    float fovRad = Camera.main.fieldOfView * Mathf.Deg2Rad * 0.5f;
                    float focalLength = h / (2f * Mathf.Tan(fovRad));
                    pxOuter = outerRadius * focalLength / distance;
                    pxInner = pxOuter * rMin;
                }
                else
                {
                    pxOuter = Mathf.Min(w, h) * 0.5f;
                    pxInner = pxOuter * rMin;
                }
            }
            else
            {
                pxOuter = Mathf.Min(w, h) * 0.5f;
                pxInner = pxOuter * rMin;
            }

            float invTwoPi = 1f / (2f * Mathf.PI);
            float invRing = (pxOuter - pxInner) > 1e-6f ? 1f / (pxOuter - pxInner) : 0f;

            var uv = new Vector2[w * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f - cx);
                float dy = (y + 0.5f - cy);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist < pxInner || dist > pxOuter)
                {
                    uv[y * w + x] = new Vector2(-1f, -1f);
                    continue;
                }

                float theta = Mathf.Atan2(dy, dx);
                float u = theta < 0f ? (theta + 2f * Mathf.PI) * invTwoPi : theta * invTwoPi;
                if (u >= 1f) u -= 1f;

                float v = Mathf.Clamp01((dist - pxInner) * invRing);
                uv[y * w + x] = new Vector2(u, v);
            }

            cache.AtmosphereUv = uv;
            cache.AtmosphereUvW = w;
            cache.AtmosphereUvH = h;
            cache.AtmosphereRMin = rMin;

            return uv;
        }

        static GameObject[] ResolveRoots(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                return Array.Empty<GameObject>();

            var roots = new List<GameObject>(8);
            var seen = new HashSet<int>();

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (!r) continue;

                var t = r.transform;
                var root = (t && t.root) ? t.root.gameObject : r.gameObject;
                if (!root) continue;

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
        {
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
        {
            if (cache == null) return;

            foreach (var kvp in cache.OriginalByRendererId)
            {
                int rid = kvp.Key;
                var backup = kvp.Value;

                if (!cache.RendererById.TryGetValue(rid, out var renderer) || !renderer) continue;

                if (backup.OriginalMaterials != null && backup.OriginalMaterials.Length > 0)
                    renderer.materials = backup.OriginalMaterials;

                var blocks = backup.PropertyBlocks;
                if (blocks != null)
                {
                    for (int i = 0; i < blocks.Length; i++)
                        if (blocks[i] != null)
                            renderer.SetPropertyBlock(blocks[i], i);
                }
            }
        }
    }
}
