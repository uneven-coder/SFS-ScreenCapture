using UnityEngine;
using System.Linq;
using FrameEmbededState;

namespace FrameEmbededState.Lib.Renders
{
    public static class ObjectTarget
    {
        public static void Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            RenderTexture srcRT,
            RenderTexture dstRT,
            Renderer[] targets)
        {
            if (settings?.Execute == null || targets == null || targets.Length == 0)
            {
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            var activeRenderers = targets
                .Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy)
                .ToArray();

            if (activeRenderers.Length == 0)
            {
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            var frame = new VisualOverlayManager.FrameData
            {
                Width = srcRT.width,
                Height = srcRT.height,
                Source = default,
                Result = default,
                ObjectVisiblePixels = default,
                MaskBuffer = default,
                MaskIdToRenderer = activeRenderers
            };

            settings.Execute(frame);

            Graphics.Blit(srcRT, dstRT);
        }
    }
}
