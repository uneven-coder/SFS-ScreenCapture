using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace FrameEmbededState.Lib
{
    public static class ObjectUtil
    {
        public readonly struct ShaderKey
        {
            public readonly int Layer;
            public readonly int RenderQueue;
            public readonly float SortDepth;   // material "_Depth" (game custom)
            public readonly float CameraDepth; // linear01 depth from camera (near..far)

            public ShaderKey(int layer, int renderQueue, float sortDepth, float cameraDepth)
            {
                Layer = layer;
                RenderQueue = renderQueue;
                SortDepth = sortDepth;
                CameraDepth = cameraDepth;
            }
        }

        /// <summary>
        /// Enumerates visible renderers using a DEPTH MASK (linear01) + frustum, then sorts by the game's custom sorting
        /// (renderQueue + material "_Depth"), and provides a per-object "shader type" id (NOT an actual shader/material).
        ///
        /// Depth mask requirements:
        /// - Must be a readable Texture2D (Read/Write enabled), containing linear01 depth in .r.
        /// - Same view as the provided camera (same projection / resolution mapping).
        /// </summary>
        public static void GetObjects(
            Camera camera,
            Texture2D depthMaskLinear01,
            Action<Renderer, Bounds, Rect, int> callback,
            Func<ShaderKey, Renderer, int> shaderTypeSelector = null,
            int[] allowedLayers = null,
            float occlusionEpsilon = 0.0025f,
            int samplesPerObject = 5)
        {
            if (camera == null || depthMaskLinear01 == null || callback == null)
                return;

            // Default "custom shader type" mapping:
            // bucket by layer + renderQueue + _Depth (0..1) into a stable int id.
            shaderTypeSelector ??= (key, r) =>
            {
                int depthBucket = Mathf.Clamp(Mathf.FloorToInt(key.SortDepth * 1024f), 0, 1024);
                unchecked
                {
                    int h = 17;
                    h = h * 31 + key.Layer;
                    h = h * 31 + key.RenderQueue;
                    h = h * 31 + depthBucket;
                    return h;
                }
            };

            int texW = depthMaskLinear01.width;
            int texH = depthMaskLinear01.height;

            // Frustum planes (fast coarse cull)
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);

            // Collect first, then sort using the game's custom sorting.
            var list = new List<(Renderer r, Bounds b, Rect rect, int shaderType, int rq, float sortDepth, float camDepth)>();

            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;

                int layer = r.gameObject.layer;
                if (allowedLayers != null && allowedLayers.Length > 0 && Array.IndexOf(allowedLayers, layer) < 0)
                    continue;

                var b = r.bounds;
                if (!GeometryUtility.TestPlanesAABB(planes, b))
                    continue;

                // Compute a screen rect (pixel space) for sampling the depth mask.
                // Use bounds corners for a tighter rect than min/max world points alone.
                if (!TryGetScreenRect(camera, b, out Rect rect))
                    continue;

                // If completely off-screen, skip.
                if (rect.width <= 1f || rect.height <= 1f)
                    continue;

                // Camera depth for object center in linear01 (near..far).
                var vp = camera.WorldToViewportPoint(b.center);
                if (vp.z <= 0f) // behind camera
                    continue;

                float camDepth01 = Linear01FromViewZ(camera, vp.z);

                // Occlusion test vs depth mask.
                if (!PassesDepthMask(depthMaskLinear01, rect, camDepth01, occlusionEpsilon, samplesPerObject, texW, texH))
                    continue;

                // Game custom sorting inputs: renderQueue + "_Depth" float
                int renderQueue = 0;
                float sortDepth = 0f;

                var mat = r.sharedMaterial;
                if (mat != null)
                {
                    renderQueue = mat.renderQueue;
                    if (mat.HasProperty("_Depth"))
                        sortDepth = mat.GetFloat("_Depth");
                }

                var key = new ShaderKey(layer, renderQueue, sortDepth, camDepth01);
                int shaderType = shaderTypeSelector(key, r);

                list.Add((r, b, rect, shaderType, renderQueue, sortDepth, camDepth01));
            }

            // Sort: renderQueue asc, then game "_Depth" asc, then camera depth asc (near->far).
            list.Sort((a, b) =>
            {
                int c = a.rq.CompareTo(b.rq);
                if (c != 0) return c;
                c = a.sortDepth.CompareTo(b.sortDepth);
                if (c != 0) return c;
                return a.camDepth.CompareTo(b.camDepth);
            });

            for (int i = 0; i < list.Count; i++)
                callback(list[i].r, list[i].b, list[i].rect, list[i].shaderType);
        }

        public static void GetVisibleObjectsFiltered(
            Camera camera,
            Action<Renderer, Bounds, Rect> callback,
            int[] allowedLayers = null,
            int[] allowedRenderQueues = null,
            Func<Renderer, bool> extraFilter = null)
        {   // Enumerate visible objects in the camera's frustum, filtered by layer and render queue

            if (camera == null)
                return;

            var planes = GeometryUtility.CalculateFrustumPlanes(camera);

            var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>()
                .Where(r => r.enabled && r.gameObject.activeInHierarchy)
                .Where(r => GeometryUtility.TestPlanesAABB(planes, r.bounds))
                .Where(r => allowedLayers == null || allowedLayers.Length == 0 || allowedLayers.Contains(r.gameObject.layer))
                .Where(r => allowedRenderQueues == null || allowedRenderQueues.Length == 0 || allowedRenderQueues.Contains(r.sharedMaterial != null ? r.sharedMaterial.renderQueue : 0))
                .Where(r => extraFilter == null || extraFilter(r));

            foreach (var renderer in renderers)
            {
                var bounds = renderer.bounds;
                if (!TryGetScreenRect(camera, bounds, out Rect rect))
                    continue;

                if (rect.width <= 1f || rect.height <= 1f)
                    continue;

                callback(renderer, bounds, rect);
            }
        }

        private static float Linear01FromViewZ(Camera cam, float viewZ)
        {
            // viewZ is in world units along camera forward (positive in front).
            float n = cam.nearClipPlane;
            float f = cam.farClipPlane;
            return Mathf.Clamp01((viewZ - n) / Mathf.Max(0.0001f, (f - n)));
        }

        private static bool PassesDepthMask(
            Texture2D depthMaskLinear01,
            Rect screenRect,
            float objectDepth01,
            float eps,
            int samples,
            int texW,
            int texH)
        {
            // Sample points in screenRect (pixel space). Compare objectDepth01 vs depth mask.
            // Visible if objectDepth <= maskDepth + eps at ANY sample point.
            // Assumes depth mask is linear01 depth stored in .r.
            Vector2[] pts;

            // Common sampling layouts
            if (samples <= 1)
            {
                pts = new[] { new Vector2(screenRect.center.x, screenRect.center.y) };
            }
            else if (samples <= 5)
            {
                pts = new[]
                {
                    new Vector2(screenRect.center.x, screenRect.center.y),
                    new Vector2(screenRect.xMin, screenRect.yMin),
                    new Vector2(screenRect.xMax, screenRect.yMin),
                    new Vector2(screenRect.xMin, screenRect.yMax),
                    new Vector2(screenRect.xMax, screenRect.yMax),
                };
            }
            else
            {
                // 3x3 grid
                pts = new Vector2[9];
                int k = 0;
                for (int gy = 0; gy < 3; gy++)
                for (int gx = 0; gx < 3; gx++)
                {
                    float px = Mathf.Lerp(screenRect.xMin, screenRect.xMax, gx / 2f);
                    float py = Mathf.Lerp(screenRect.yMin, screenRect.yMax, gy / 2f);
                    pts[k++] = new Vector2(px, py);
                }
            }

            for (int i = 0; i < pts.Length; i++)
            {
                // Convert screen pixel coords into UV for the depth mask.
                float u = Mathf.Clamp01(pts[i].x / Mathf.Max(1f, Screen.width));
                float v = Mathf.Clamp01(pts[i].y / Mathf.Max(1f, Screen.height));

                // Depth mask assumed to match camera view; y flip not applied (mask should match the same convention).
                float maskDepth01 = depthMaskLinear01.GetPixelBilinear(u, v).r;

                if (objectDepth01 <= maskDepth01 + eps)
                    return true;
            }

            return false;
        }

        private static bool TryGetScreenRect(Camera cam, Bounds b, out Rect rect)
        {
            // 8 corners
            Vector3 c = b.center;
            Vector3 e = b.extents;

            Vector3[] corners = new Vector3[8]
            {
                new Vector3(c.x - e.x, c.y - e.y, c.z - e.z),
                new Vector3(c.x + e.x, c.y - e.y, c.z - e.z),
                new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
                new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
                new Vector3(c.x - e.x, c.y - e.y, c.z + e.z),
                new Vector3(c.x + e.x, c.y - e.y, c.z + e.z),
                new Vector3(c.x - e.x, c.y + e.y, c.z + e.z),
                new Vector3(c.x + e.x, c.y + e.y, c.z + e.z),
            };

            bool anyInFront = false;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;

            for (int i = 0; i < corners.Length; i++)
            {
                var sp = cam.WorldToScreenPoint(corners[i]);
                if (sp.z > 0f) anyInFront = true;

                minX = Mathf.Min(minX, sp.x);
                minY = Mathf.Min(minY, sp.y);
                maxX = Mathf.Max(maxX, sp.x);
                maxY = Mathf.Max(maxY, sp.y);
            }

            if (!anyInFront || float.IsInfinity(minX) || float.IsInfinity(minY))
            {
                rect = default;
                return false;
            }

            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }
    }
}
