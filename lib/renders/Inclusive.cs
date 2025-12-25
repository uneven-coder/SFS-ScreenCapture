using UnityEngine;
using FrameEmbededState;

namespace FrameEmbededState.Lib.Renders
{
    /// <summary>
    /// Coordinates UI + screen rendering.
    /// </summary>
    public static class Inclusive
    {
        public static RenderTexture Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            Material gpuMaterial,
            RenderTexture srcRT,
            RenderTexture dstRT,
            bool renderUI)
        {
            if (settings == null)
            {
                Graphics.Blit(srcRT, dstRT);
                return null;
            }

            // Screen (behind UI)
            RenderBehindUIRenderer.Render(settings, gpuMaterial, srcRT, dstRT);

            // UI overlay (optional)
            if (!renderUI)
                return null;

            return Exclusive.RenderUI(settings, gpuMaterial, srcRT);
        }
    }
}
