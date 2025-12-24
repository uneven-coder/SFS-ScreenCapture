using UnityEngine;
using System.Linq;

namespace FrameEmbededState.Lib.Renders
{
    public static class ObjectTarget
    {
        public static void Render(
            VisualOverlayManager.State s,
            RenderTexture srcRT,
            RenderTexture dstRT,
            Renderer[] targets)
        {   // Apply object-layer effect by modifying materials/renderers only

            var settings = s.Settings;
            if (settings?.Execute == null || targets == null || targets.Length == 0)
            {   // Always blit to avoid breaking the pipeline
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            var activeRenderers = targets
                .Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy)
                .ToArray();

            if (activeRenderers.Length == 0)
            {   // Always blit to avoid breaking the pipeline
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            var mats = activeRenderers
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null)
                .Distinct()
                .ToArray();

            // Only provide renderers/materials, not pixel buffers
            var frame = new VisualOverlayManager.FrameData
            {
                Width = srcRT.width,
                Height = srcRT.height,
                Source = default,
                Result = default,
                ObjectVisiblePixels = default,
                MaskBuffer = default,
                MaskIdToRenderer = activeRenderers,
                // TargetRenderers = activeRenderers,
                // TargetMaterials = mats
            };

            settings.Execute(frame);

            // Always blit srcRT to dstRT, never touch screen pixels directly
            Graphics.Blit(srcRT, dstRT);
        }
    }
}
