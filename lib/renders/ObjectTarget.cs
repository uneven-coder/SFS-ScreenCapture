// // ObjectTarget.cs
// // Updated to provide a *normalized Cartesian (disc)* AtmosphereWorldPos grid (no warp/bend),
// // while the applied texture space remains the mesh's (polar) UVs.
// // Based on your current ObjectTarget implementation :contentReference[oaicite:0]{index=0}

// using System;
// using System.Collections.Generic;
// using Unity.Collections;
// using UnityEngine;
// using Object = UnityEngine.Object;

// namespace FrameEmbededState.Lib.Renders
// {
//     public static class ObjectTarget
//     {
//         // Quality/perf knobs (fixed size UV->world buffer; keep small for speed)
//         const int UV_WORLD_W = 256;
//         const int UV_WORLD_H = 256;

//         // Normalized atmosphere grid (CARTESIAN disc UV, centered at 0.5/0.5, radius 0.5)
//         // This is what gets put into FrameData.AtmosphereWorldPos (no polar warp).
//         const int ATM_CART_W = 256;
//         const int ATM_CART_H = 256;

//         // Assumption: atmosphere mesh UVs are POLAR-UNWRAPPED:
//         //   U = theta in [0..1)  (wraps)
//         //   V = radius in [0..1] (center->edge)
//         // If your mesh uses a different scheme, adjust these conversions.
//         const float CART_CENTER = 0.5f;
//         const float CART_RADIUS = 0.5f;

//         static readonly int _MainTexId = Shader.PropertyToID("_MainTex");
//         static readonly int _ColorTexId = Shader.PropertyToID("_ColorTexture");

//         sealed class Cache
//         {
//             public int NextRebuildFrame;
//             public int TargetsKey;

//             public Renderer[] Renderers = Array.Empty<Renderer>();
//             public Material[] Materials = Array.Empty<Material>();
//             public VisualOverlayManager.RendererMaterialGroup[] Groups = Array.Empty<VisualOverlayManager.RendererMaterialGroup>();
//             public VisualOverlayManager.ModelTextureData[] Models = Array.Empty<VisualOverlayManager.ModelTextureData>();

//             // Original state (captured once)
//             public readonly Dictionary<int, MaterialBackup> OriginalByRendererId = new Dictionary<int, MaterialBackup>(256);
//             public readonly Dictionary<int, Renderer> RendererById = new Dictionary<int, Renderer>(256);

//             // Cached UV->world buffer (rasterized once per mesh/transform change) in *mesh UV space* (normalized to [0,1])
//             public Vector3[] UvWorldPos;
//             public float[] UvWorldDepth;
//             public int UvWorldW, UvWorldH;
//             public int UvWorldKey;

//             // Cached normalized CARTESIAN atmosphere world grid (disc space)
//             public Vector3[] AtmosphereWorldCart;
//             public int AtmosphereCartKey;
//             public Vector3 AtmosphereCenter;
//             public float AtmosphereRadius;

//             // Reused output texture + MPB (avoid per-frame allocations & material instancing)
//             public Texture2D OutputTex;
//             public MaterialPropertyBlock Mpb;
//         }

//         sealed class MaterialBackup
//         {
//             public Texture ColorTexture;
//             public Texture NormalTexture;
//             public MaterialPropertyBlock[] PropertyBlocks;
//             public Material[] OriginalMaterials;
//         }

//         static readonly Dictionary<int, Cache> _cacheBySettingsId = new Dictionary<int, Cache>();

//         static Texture2D _cpuSrcTex;
//         static Texture2D _cpuDstTex;
//         static NativeArray<Color32> _cpuSrc;
//         static NativeArray<Color32> _cpuDst;
//         static int _w, _h;
//         static Rect _rect;

//         public static void Render(
//             VisualOverlayManager.VisualOverlaySettings settings,
//             RenderTexture srcRT,
//             Renderer[] objectRenderers)
//         {
//             if (settings?.Execute == null || srcRT == null) return;

//             var roots = ResolveRoots(objectRenderers);
//             Render(settings, srcRT, roots);
//         }

//         static void Render(
//             VisualOverlayManager.VisualOverlaySettings settings,
//             RenderTexture srcRT,
//             GameObject[] roots)
//         {
//             EnsureCpuBuffers(srcRT.width, srcRT.height);

//             int sid = settings.GetHashCode();
//             bool firstSetup = !_cacheBySettingsId.TryGetValue(sid, out var cache);
//             if (firstSetup)
//             {
//                 cache = new Cache();
//                 _cacheBySettingsId[sid] = cache;
//             }

//             int frameCount = Time.frameCount;
//             int key = ComputeTargetsKey(roots);

//             if (frameCount >= cache.NextRebuildFrame || cache.TargetsKey != key)
//             {
//                 cache.TargetsKey = key;
//                 cache.NextRebuildFrame = frameCount + 30;
//                 RebuildCache(cache, roots, captureOriginals: firstSetup);
//             }

//             // Read source RT to CPU
//             var prev = RenderTexture.active;
//             RenderTexture.active = srcRT;
//             _cpuSrcTex.ReadPixels(_rect, 0, 0, false);
//             RenderTexture.active = prev;

//             // Start result as a copy of source
//             CopyNative(_cpuSrc, _cpuDst);

//             // Build/cache UV->world buffer (mesh UV space) and then normalized CART atmosphere grid (disc space)
//             var uvWorld = GetOrBuildUvWorld(cache, UV_WORLD_W, UV_WORLD_H, out int uvW, out int uvH);
//             var atmWorldCart = GetOrBuildAtmosphereWorldCart(cache, uvWorld, uvW, uvH, ATM_CART_W, ATM_CART_H);

//             // Choose a stable basis from the atmosphere renderer transform (recommended)
//             var atmRenderer = cache.Models != null && cache.Models.Length > 0 ? cache.Models[0].Renderer : null;
//             var t = atmRenderer != null ? atmRenderer.transform : null;
//             Vector3 axisU = t ? t.right.normalized : Vector3.right;
//             Vector3 axisV = t ? t.forward.normalized : Vector3.forward;

//             var frame = new VisualOverlayManager.FrameData
//             {
//                 Source = _cpuSrc,
//                 Result = _cpuDst,
//                 Width = _w,
//                 Height = _h,

//                 Renderers = cache.Renderers,
//                 Materials = cache.Materials,
//                 RendererMaterials = cache.Groups,
//                 ModelTextures = cache.Models,

//                 MaterialsDirty = false,

//                 // Normalized (unwarped) atmosphere frame data:
//                 AtmosphereWorldPos = atmWorldCart,      // legacy/compat: cartesian grid
//                 AtmosphereWorldW = ATM_CART_W,
//                 AtmosphereWorldH = ATM_CART_H,
//                 AtmosphereCenter = cache.AtmosphereCenter,
//                 AtmosphereRadius = cache.AtmosphereRadius,

//                 // --- Added for world-anchored painting ---
//                 AtmosphereUvWorldPos = uvWorld,
//                 AtmosphereUvWorldW = uvW,
//                 AtmosphereUvWorldH = uvH,
//                 AtmosphereCartWorldPos = atmWorldCart,
//                 AtmosphereCartWorldW = ATM_CART_W,
//                 AtmosphereCartWorldH = ATM_CART_H,
//                 AtmosphereAxisU = axisU,
//                 AtmosphereAxisV = axisV
//             };

//             settings.Execute(frame);

//             // ObjectLayer: push output into a reusable texture + MPB (no material array churn)
//             if (settings.RenderMode == OverlayRenderMode.ObjectLayer &&
//                 frame.Renderers != null && frame.Renderers.Length > 0)
//             {
//                 EnsureOutputTexture(cache, _w, _h);
//                 cache.OutputTex.SetPixelData(frame.Result, 0);
//                 cache.OutputTex.Apply(false, false);

//                 ApplyTextureViaPropertyBlocks(cache, frame.Renderers, cache.OutputTex);
//             }

//             if (frame.MaterialsDirty && frame.RendererMaterials != null)
//             {
//                 var groups = frame.RendererMaterials;
//                 for (int i = 0; i < groups.Length; i++)
//                 {
//                     var g = groups[i];
//                     if (g.Renderer != null && g.Materials != null)
//                         g.Renderer.materials = g.Materials;
//                 }
//             }
//         }

//         static void EnsureOutputTexture(Cache cache, int w, int h)
//         {
//             if (cache.Mpb == null) cache.Mpb = new MaterialPropertyBlock();

//             if (cache.OutputTex != null && cache.OutputTex.width == w && cache.OutputTex.height == h)
//                 return;

//             if (cache.OutputTex != null) Object.Destroy(cache.OutputTex);

//             cache.OutputTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
//             {
//                 // NOTE: if your atmosphere shader expects theta wrap, consider TextureWrapMode.Repeat.
//                 // Leaving Clamp as-is to avoid behavior changes elsewhere.
//                 wrapMode = TextureWrapMode.Clamp,
//                 filterMode = FilterMode.Bilinear
//             };
//         }

//         static void ApplyTextureViaPropertyBlocks(Cache cache, Renderer[] renderers, Texture tex)
//         {
//             var mpb = cache.Mpb;
//             for (int ri = 0; ri < renderers.Length; ri++)
//             {
//                 var r = renderers[ri];
//                 if (!r) continue;

//                 // Apply per-submesh to support multiple materials
//                 int submeshCount = r.sharedMaterials != null ? r.sharedMaterials.Length : 1;
//                 for (int si = 0; si < submeshCount; si++)
//                 {
//                     mpb.Clear();
//                     r.GetPropertyBlock(mpb, si);

//                     mpb.SetTexture(_MainTexId, tex);
//                     mpb.SetTexture(_ColorTexId, tex);

//                     r.SetPropertyBlock(mpb, si);
//                 }
//             }
//         }

//         static void RebuildCache(Cache cache, GameObject[] roots, bool captureOriginals)
//         {
//             var renderers = new List<Renderer>(256);
//             var groups = new List<VisualOverlayManager.RendererMaterialGroup>(256);
//             var materials = new List<Material>(512);
//             var models = new List<VisualOverlayManager.ModelTextureData>(256);

//             var rendererSeen = new HashSet<int>();
//             var materialSeen = new HashSet<int>();

//             if (captureOriginals)
//                 cache.OriginalByRendererId.Clear();

//             cache.RendererById.Clear();

//             if (roots != null)
//             {
//                 for (int ri = 0; ri < roots.Length; ri++)
//                 {
//                     var root = roots[ri];
//                     if (!root) continue;

//                     // 1) Atmospheres (collect explicitly)
//                     var atmos = root.GetComponentsInChildren<SFS.World.Atmosphere>(true);
//                     for (int ai = 0; ai < atmos.Length; ai++)
//                     {
//                         var mr = atmos[ai] ? atmos[ai].GetComponent<MeshRenderer>() : null;
//                         if (!mr || !mr.enabled || !mr.gameObject.activeInHierarchy) continue;
//                         CollectRenderer(cache, mr, renderers, groups, materials, models, rendererSeen, materialSeen, captureOriginals);
//                     }

//                     // 2) All renderers (rendererSeen avoids duplicates including atmosphere)
//                     var rs = root.GetComponentsInChildren<Renderer>(true);
//                     for (int i = 0; i < rs.Length; i++)
//                     {
//                         var r = rs[i];
//                         if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
//                         CollectRenderer(cache, r, renderers, groups, materials, models, rendererSeen, materialSeen, captureOriginals);
//                     }
//                 }
//             }

//             cache.Renderers = renderers.Count == 0 ? Array.Empty<Renderer>() : renderers.ToArray();
//             cache.Groups = groups.Count == 0 ? Array.Empty<VisualOverlayManager.RendererMaterialGroup>() : groups.ToArray();
//             cache.Materials = materials.Count == 0 ? Array.Empty<Material>() : materials.ToArray();
//             cache.Models = models.Count == 0 ? Array.Empty<VisualOverlayManager.ModelTextureData>() : models.ToArray();

//             // Force rebuild next frame (targets changed)
//             cache.UvWorldKey = 0;
//             cache.AtmosphereCartKey = 0;
//         }

//         static void CollectRenderer(
//             Cache cache,
//             Renderer r,
//             List<Renderer> renderers,
//             List<VisualOverlayManager.RendererMaterialGroup> groups,
//             List<Material> materials,
//             List<VisualOverlayManager.ModelTextureData> models,
//             HashSet<int> rendererSeen,
//             HashSet<int> materialSeen,
//             bool captureOriginals)
//         {
//             int rid = r.GetInstanceID();
//             if (!rendererSeen.Add(rid)) return;

//             var mr = r as MeshRenderer;
//             if (mr == null) return;

//             var mats = mr.sharedMaterials;
//             if (mats == null || mats.Length == 0) return;

//             bool isAtmosphere = mr.GetComponent<SFS.World.Atmosphere>() != null;
//             bool hasAtmosphereShader = false;
//             Shader unlitShader = Shader.Find("Unlit/Texture") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");

//             for (int m = 0; m < mats.Length; m++)
//             {
//                 var mat = mats[m];
//                 if (mat != null && mat.shader != null && mat.shader.name == "SFS/Atmosphere")
//                 {
//                     hasAtmosphereShader = true;
//                     if (unlitShader != null)
//                         mat.shader = unlitShader;
//                 }
//             }

//             if (!isAtmosphere && !hasAtmosphereShader)
//                 return;

//             renderers.Add(mr);
//             cache.RendererById[rid] = mr;
//             groups.Add(new VisualOverlayManager.RendererMaterialGroup { Renderer = mr, Materials = mats });

//             for (int m = 0; m < mats.Length; m++)
//                 if (mats[m] != null && materialSeen.Add(mats[m].GetInstanceID()))
//                     materials.Add(mats[m]);

//             if (captureOriginals && !cache.OriginalByRendererId.ContainsKey(rid))
//                 cache.OriginalByRendererId[rid] = BackupOriginal(mr, mats);

//             var md = BuildModelTextureData(cache, rid, mr, mats);
//             if (md.Renderer != null) models.Add(md);
//         }

//         static MaterialBackup BackupOriginal(Renderer r, Material[] mats)
//         {
//             var mpb = new MaterialPropertyBlock[mats.Length];
//             var originalMats = new Material[mats.Length];

//             for (int i = 0; i < mats.Length; i++)
//             {
//                 mpb[i] = new MaterialPropertyBlock();
//                 r.GetPropertyBlock(mpb[i], i);
//                 originalMats[i] = mats[i];
//             }

//             var backup = new MaterialBackup
//             {
//                 PropertyBlocks = mpb,
//                 OriginalMaterials = originalMats
//             };

//             var m0 = mats[0];
//             if (m0)
//             {
//                 if (m0.HasProperty("_ColorTexture")) backup.ColorTexture = m0.GetTexture("_ColorTexture");
//                 if (m0.HasProperty("_NormalMap")) backup.NormalTexture = m0.GetTexture("_NormalMap");
//             }

//             return backup;
//         }

//         static VisualOverlayManager.ModelTextureData BuildModelTextureData(Cache cache, int rid, MeshRenderer mr, Material[] mats)
//         {
//             var m0 = (mats != null && mats.Length > 0) ? mats[0] : null;
//             if (!m0) return default;

//             Texture2D colorTex = null, normalTex = null;

//             if (cache.OriginalByRendererId.TryGetValue(rid, out var b))
//             {
//                 colorTex = b.ColorTexture as Texture2D;
//                 normalTex = b.NormalTexture as Texture2D;
//             }
//             else
//             {
//                 if (m0.HasProperty("_ColorTexture")) colorTex = m0.GetTexture("_ColorTexture") as Texture2D;
//                 if (m0.HasProperty("_NormalMap")) normalTex = m0.GetTexture("_NormalMap") as Texture2D;
//             }

//             return new VisualOverlayManager.ModelTextureData
//             {
//                 Renderer = mr,
//                 ColorTexture = colorTex,
//                 NormalTexture = normalTex,
//                 UseNormals = normalTex != null,
//                 Smoothness = m0.HasProperty("_Smoothness") ? m0.GetFloat("_Smoothness") : 0.5f
//             };
//         }

//         // ======= NORMALIZATION HELPERS (PUBLIC) =======

//         // Convert mesh polar UV (U=theta, V=radius) into normalized CARTESIAN disc UV.
//         public static Vector2 PolarUvToCartesianUv(float uTheta01, float vRadius01)
//         {
//             float theta = uTheta01 * (Mathf.PI * 2f);
//             float r = Mathf.Clamp01(vRadius01) * CART_RADIUS;
//             return new Vector2(
//                 CART_CENTER + r * Mathf.Cos(theta),
//                 CART_CENTER + r * Mathf.Sin(theta));
//         }

//         // Convert normalized CARTESIAN disc UV into mesh polar UV (U=theta, V=radius).
//         public static void CartesianUvToPolarUv(float uCart01, float vCart01, out float uTheta01, out float vRadius01, out bool insideDisc)
//         {
//             float dx = (uCart01 - CART_CENTER);
//             float dy = (vCart01 - CART_CENTER);

//             float r = Mathf.Sqrt(dx * dx + dy * dy) / CART_RADIUS; // 0..1 at edge
//             insideDisc = r <= 1f;

//             float theta = Mathf.Atan2(dy, dx); // -pi..pi
//             float u = theta / (Mathf.PI * 2f); // -0.5..0.5
//             u = u - Mathf.Floor(u);            // wrap to [0,1)

//             uTheta01 = u;
//             vRadius01 = Mathf.Clamp01(r);
//         }

//         // ======= FAST ATMOSPHERE PATH =======

//         // Instead of sampling mesh UVs, sample a regular Euclidean grid in world XZ space and project onto the mesh
//         static Vector3[] GetOrBuildAtmosphereWorldCart(Cache cache, Vector3[] uvWorld, int uvW, int uvH, int outW, int outH)
//         {   // Build a normalized disc grid in world space by sampling UV world buffer at polar UV equivalents

//             // Key now depends on UV world buffer for alignment
//             int key = unchecked(uvW * 486187739 ^ uvH * 73856093 ^ outW * 19349663 ^ outH * 83492791);
//             if (cache.AtmosphereWorldCart != null &&
//                 cache.AtmosphereWorldCart.Length == outW * outH &&
//                 cache.AtmosphereCartKey == key)
//                 return cache.AtmosphereWorldCart;

//             int n = outW * outH;
//             if (cache.AtmosphereWorldCart == null || cache.AtmosphereWorldCart.Length != n)
//                 cache.AtmosphereWorldCart = new Vector3[n];

//             // For each output pixel, convert Cartesian disc UV to polar UV, then sample UV world buffer
//             System.Threading.Tasks.Parallel.For(0, outH, yIdx =>
//             {   // Each row
//                 float vNorm = (float)yIdx / (outH - 1);
//                 for (int xIdx = 0; xIdx < outW; xIdx++)
//                 {   // Each column
//                     float uNorm = (float)xIdx / (outW - 1);

//                     // Convert Cartesian disc UV to polar UV
//                     CartesianUvToPolarUv(uNorm, vNorm, out float uTheta01, out float vRadius01, out bool insideDisc);

//                     Vector3 worldPos;
//                     if (insideDisc && uvWorld != null)
//                     {   // Sample UV world buffer at polar UV
//                         int uvX = Mathf.Clamp(Mathf.RoundToInt(uTheta01 * (uvW - 1)), 0, uvW - 1);
//                         int uvY = Mathf.Clamp(Mathf.RoundToInt(vRadius01 * (uvH - 1)), 0, uvH - 1);
//                         int uvIdx = uvY * uvW + uvX;
//                         worldPos = uvWorld[uvIdx];
//                     }
//                     else
//                         worldPos = new Vector3(float.NaN, float.NaN, float.NaN);

//                     cache.AtmosphereWorldCart[yIdx * outW + xIdx] = worldPos;
//                 }
//             });

//             cache.AtmosphereCartKey = key;
//             return cache.AtmosphereWorldCart;
//         }

//         // Compute barycentric coordinates for point p in triangle (a, b, c) in 2D
//         static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
//         {   // Returns barycentric coordinates (u,v,w) for p with respect to triangle (a,b,c)
//             Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
//             float d00 = Vector2.Dot(v0, v0), d01 = Vector2.Dot(v0, v1), d11 = Vector2.Dot(v1, v1);
//             float d20 = Vector2.Dot(v2, v0), d21 = Vector2.Dot(v2, v1);
//             float denom = d00 * d11 - d01 * d01;
//             if (Mathf.Abs(denom) < 1e-8f) return new Vector3(-1, -1, -1);
//             float v = (d11 * d20 - d01 * d21) / denom;
//             float w = (d00 * d21 - d01 * d20) / denom;
//             float u = 1.0f - v - w;
//             return new Vector3(u, v, w);
//         }

//         // Find the closest point on triangle (a, b, c) in 2D to point p
//         static Vector2 ClosestPointOnTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
//         {   // Returns closest point in 2D triangle to p
//             Vector2 ab = ClosestPointOnSegment2D(p, a, b);
//             Vector2 bc = ClosestPointOnSegment2D(p, b, c);
//             Vector2 ca = ClosestPointOnSegment2D(p, c, a);

//             float dab = (p - ab).sqrMagnitude;
//             float dbc = (p - bc).sqrMagnitude;
//             float dca = (p - ca).sqrMagnitude;

//             if (dab < dbc && dab < dca) return ab;
//             else if (dbc < dca) return bc;
//             else return ca;
//         }

//         // Find the closest point on segment ab to point p in 2D
//         static Vector2 ClosestPointOnSegment2D(Vector2 p, Vector2 a, Vector2 b)
//         {   // Returns closest point on segment ab to p
//             Vector2 ab = b - a;
//             float t = Vector2.Dot(p - a, ab) / ab.sqrMagnitude;
//             t = Mathf.Clamp01(t);
//             return a + ab * t;
//         }

//         // ======= MISC =======

//         static GameObject[] ResolveRoots(Renderer[] renderers)
//         {
//             if (renderers == null || renderers.Length == 0)
//                 return Array.Empty<GameObject>();

//             var roots = new List<GameObject>(8);
//             var seen = new HashSet<int>();

//             for (int i = 0; i < renderers.Length; i++)
//             {
//                 var r = renderers[i];
//                 if (!r) continue;

//                 var t = r.transform;
//                 var root = (t && t.root) ? t.root.gameObject : r.gameObject;
//                 if (!root) continue;

//                 int id = root.GetInstanceID();
//                 if (seen.Add(id)) roots.Add(root);
//             }

//             return roots.Count == 0 ? Array.Empty<GameObject>() : roots.ToArray();
//         }

//         static int ComputeTargetsKey(GameObject[] roots)
//         {
//             unchecked
//             {
//                 int h = 17;
//                 if (roots != null)
//                 {
//                     for (int i = 0; i < roots.Length; i++)
//                         h = h * 31 + (roots[i] ? roots[i].GetInstanceID() : 0);
//                 }
//                 return h;
//             }
//         }

//         static void EnsureCpuBuffers(int w, int h)
//         {
//             if (_cpuSrcTex != null && _w == w && _h == h)
//                 return;

//             if (_cpuSrcTex != null) Object.Destroy(_cpuSrcTex);
//             if (_cpuDstTex != null) Object.Destroy(_cpuDstTex);

//             _w = w; _h = h;
//             _rect = new Rect(0, 0, w, h);

//             _cpuSrcTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
//             _cpuDstTex = new Texture2D(w, h, TextureFormat.RGBA32, false);

//             _cpuSrc = _cpuSrcTex.GetRawTextureData<Color32>();
//             _cpuDst = _cpuDstTex.GetRawTextureData<Color32>();
//         }

//         static void CopyNative(NativeArray<Color32> from, NativeArray<Color32> to)
//         {
//             int n = from.Length < to.Length ? from.Length : to.Length;
//             for (int i = 0; i < n; i++) to[i] = from[i];
//         }

//         public static void Release()
//         {
//             foreach (var kvp in _cacheBySettingsId)
//             {
//                 RestoreMaterials(kvp.Value);

//                 if (kvp.Value.OutputTex != null)
//                     Object.Destroy(kvp.Value.OutputTex);
//                 kvp.Value.OutputTex = null;
//                 kvp.Value.Mpb = null;
//             }

//             if (_cpuSrcTex != null) Object.Destroy(_cpuSrcTex);
//             if (_cpuDstTex != null) Object.Destroy(_cpuDstTex);

//             _cpuSrcTex = null;
//             _cpuDstTex = null;
//             _cpuSrc = default;
//             _cpuDst = default;
//             _w = _h = 0;

//             _cacheBySettingsId.Clear();
//         }

//         static void RestoreMaterials(Cache cache)
//         {
//             if (cache == null) return;

//             foreach (var kvp in cache.OriginalByRendererId)
//             {
//                 int rid = kvp.Key;
//                 var backup = kvp.Value;

//                 if (!cache.RendererById.TryGetValue(rid, out var renderer) || !renderer) continue;

//                 if (backup.OriginalMaterials != null && backup.OriginalMaterials.Length > 0)
//                     renderer.materials = backup.OriginalMaterials;

//                 var blocks = backup.PropertyBlocks;
//                 if (blocks != null)
//                 {
//                     for (int i = 0; i < blocks.Length; i++)
//                         if (blocks[i] != null)
//                             renderer.SetPropertyBlock(blocks[i], i);
//                 }
//             }
//         }

//         static Vector3[] GetOrBuildUvWorld(Cache cache, int outW, int outH, out int w, out int h)
//         {   // Build a UV-to-world position buffer for the mesh in normalized UV space

//             w = outW;
//             h = outH;

//             var meshData = new List<(MeshRenderer mr, Mesh mesh, Matrix4x4 localToWorld, Vector2 uvMin, Vector2 uvMax)>(64);
//             if (cache.Models != null)
//                 for (int i = 0; i < cache.Models.Length; i++)
//                 {
//                     var r = cache.Models[i].Renderer as MeshRenderer;
//                     if (!r) continue;
//                     var mf = r.GetComponent<MeshFilter>();
//                     if (!mf || !mf.sharedMesh) continue;
//                     var mesh = mf.sharedMesh;
//                     var uvs = mesh.uv;
//                     if (uvs == null || uvs.Length == 0) continue;

//                     Vector2 uvMin = uvs[0], uvMax = uvs[0];
//                     for (int j = 1; j < uvs.Length; j++)
//                     {
//                         uvMin = Vector2.Min(uvMin, uvs[j]);
//                         uvMax = Vector2.Max(uvMax, uvs[j]);
//                     }
//                     meshData.Add((r, mesh, r.localToWorldMatrix, uvMin, uvMax));
//                 }

//             if (meshData.Count == 0)
//                 return null;

//             int key = ComputeUvWorldKey(meshData, outW, outH);
//             if (cache.UvWorldPos != null &&
//                 cache.UvWorldDepth != null &&
//                 cache.UvWorldW == outW &&
//                 cache.UvWorldH == outH &&
//                 cache.UvWorldKey == key)
//                 return cache.UvWorldPos;

//             int n = outW * outH;
//             if (cache.UvWorldPos == null || cache.UvWorldPos.Length != n)
//                 cache.UvWorldPos = new Vector3[n];
//             if (cache.UvWorldDepth == null || cache.UvWorldDepth.Length != n)
//                 cache.UvWorldDepth = new float[n];

//             for (int i = 0; i < n; i++)
//             {
//                 cache.UvWorldPos[i] = default;
//                 cache.UvWorldDepth[i] = float.NegativeInfinity;
//             }

//             for (int mi = 0; mi < meshData.Count; mi++)
//             {
//                 var (mr, mesh, localToWorld, uvMin, uvMax) = meshData[mi];
//                 Vector3 center = mr.bounds.center;

//                 RasterizeMeshWorldPos_UVSpace_Normalized(
//                     mesh,
//                     localToWorld,
//                     center,
//                     outW, outH,
//                     cache.UvWorldPos,
//                     cache.UvWorldDepth,
//                     uvMin,
//                     uvMax);
//             }

//             cache.UvWorldW = outW;
//             cache.UvWorldH = outH;
//             cache.UvWorldKey = key;

//             // UV-world changed => rebuild cart grid next time
//             cache.AtmosphereCartKey = 0;

//             return cache.UvWorldPos;
//         }

//         static int ComputeUvWorldKey(List<(MeshRenderer mr, Mesh mesh, Matrix4x4 localToWorld, Vector2 uvMin, Vector2 uvMax)> meshData, int w, int h)
//         {   // Compute a hash key for the UV world buffer based on mesh and transform state
//             unchecked
//             {
//                 int hash = 17;
//                 hash = hash * 31 + w;
//                 hash = hash * 31 + h;

//                 for (int i = 0; i < meshData.Count; i++)
//                 {
//                     var mr = meshData[i].mr;
//                     var mesh = meshData[i].mesh;

//                     hash = hash * 31 + (mesh ? mesh.GetInstanceID() : 0);

//                     var p = mr.transform.position;
//                     var s = mr.transform.lossyScale;
//                     var q = mr.transform.rotation;

//                     hash = hash * 31 + Mathf.RoundToInt(p.x * 100f);
//                     hash = hash * 31 + Mathf.RoundToInt(p.y * 100f);
//                     hash = hash * 31 + Mathf.RoundToInt(p.z * 100f);

//                     hash = hash * 31 + Mathf.RoundToInt(s.x * 100f);
//                     hash = hash * 31 + Mathf.RoundToInt(s.y * 100f);
//                     hash = hash * 31 + Mathf.RoundToInt(s.z * 100f);

//                     hash = hash * 31 + Mathf.RoundToInt(q.x * 1000f);
//                     hash = hash * 31 + Mathf.RoundToInt(q.y * 1000f);
//                     hash = hash * 31 + Mathf.RoundToInt(q.z * 1000f);
//                     hash = hash * 31 + Mathf.RoundToInt(q.w * 1000f);
//                 }

//                 return hash;
//             }
//         }

//         static void RasterizeMeshWorldPos_UVSpace_Normalized(
//             Mesh mesh,
//             Matrix4x4 localToWorld,
//             Vector3 center,
//             int texW,
//             int texH,
//             Vector3[] worldOut,
//             float[] depthOut,
//             Vector2 uvMin,
//             Vector2 uvMax)
//         {   // Rasterize mesh triangles into a UV grid, storing world positions and depth

//             var verts = mesh.vertices;
//             var uvs = mesh.uv;
//             var tris = mesh.triangles;

//             if (verts == null || uvs == null || tris == null) return;
//             if (uvs.Length != verts.Length) return;

//             int w = texW, h = texH;
//             Vector2 uvRange = uvMax - uvMin;
//             if (uvRange.x == 0f) uvRange.x = 1f;
//             if (uvRange.y == 0f) uvRange.y = 1f;

//             for (int t = 0; t < tris.Length; t += 3)
//             {
//                 int i0 = tris[t];
//                 int i1 = tris[t + 1];
//                 int i2 = tris[t + 2];

//                 if ((uint)i0 >= (uint)verts.Length || (uint)i1 >= (uint)verts.Length || (uint)i2 >= (uint)verts.Length)
//                     continue;

//                 Vector2 uv0 = (uvs[i0] - uvMin);
//                 Vector2 uv1 = (uvs[i1] - uvMin);
//                 Vector2 uv2 = (uvs[i2] - uvMin);

//                 uv0.x /= uvRange.x; uv0.y /= uvRange.y;
//                 uv1.x /= uvRange.x; uv1.y /= uvRange.y;
//                 uv2.x /= uvRange.x; uv2.y /= uvRange.y;

//                 float denom = (uv1.y - uv2.y) * (uv0.x - uv2.x) + (uv2.x - uv1.x) * (uv0.y - uv2.y);
//                 if (Mathf.Abs(denom) < 1e-12f) continue;
//                 float invDen = 1f / denom;

//                 float uMinN = Mathf.Clamp01(Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x)));
//                 float uMaxN = Mathf.Clamp01(Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x)));
//                 float vMinN = Mathf.Clamp01(Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y)));
//                 float vMaxN = Mathf.Clamp01(Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y)));

//                 int xMin = Mathf.Clamp(Mathf.FloorToInt(uMinN * (w - 1)), 0, w - 1);
//                 int xMax = Mathf.Clamp(Mathf.CeilToInt(uMaxN * (w - 1)), 0, w - 1);
//                 int yMin = Mathf.Clamp(Mathf.FloorToInt(vMinN * (h - 1)), 0, h - 1);
//                 int yMax = Mathf.Clamp(Mathf.CeilToInt(vMaxN * (h - 1)), 0, h - 1);

//                 Vector3 v0 = verts[i0];
//                 Vector3 v1 = verts[i1];
//                 Vector3 v2 = verts[i2];

//                 for (int y = yMin; y <= yMax; y++)
//                 {
//                     float vv = (y + 0.5f) / h;
//                     for (int x = xMin; x <= xMax; x++)
//                     {
//                         float uu = (x + 0.5f) / w;

//                         float w0 = ((uv1.y - uv2.y) * (uu - uv2.x) + (uv2.x - uv1.x) * (vv - uv2.y)) * invDen;
//                         if (w0 < 0f) continue;

//                         float w1 = ((uv2.y - uv0.y) * (uu - uv2.x) + (uv0.x - uv2.x) * (vv - uv2.y)) * invDen;
//                         if (w1 < 0f) continue;

//                         float w2 = 1f - w0 - w1;
//                         if (w2 < 0f) continue;

//                         Vector3 localPos = v0 * w0 + v1 * w1 + v2 * w2;
//                         Vector3 worldPos = localToWorld.MultiplyPoint3x4(localPos);

//                         float depth = (worldPos - center).sqrMagnitude;

//                         int idx = y * w + x;
//                         if (depth <= depthOut[idx]) continue;

//                         depthOut[idx] = depth;
//                         worldOut[idx] = worldPos;
//                     }
//                 }
//             }
//         }
//     }
// }
