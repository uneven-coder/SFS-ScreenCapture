using UnityEngine;
using FrameEmbededState;

namespace FrameEmbededState.Lib.Renders
{
    public static class Inclusive
    {
        public static RenderTexture Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            Material gpuMaterial,
            RenderTexture srcRT,
            RenderTexture dstRT)
        {
            if (settings == null)
            {
                Graphics.Blit(srcRT, dstRT);
                return null;
            }

            if (gpuMaterial != null)
                gpuMaterial.renderQueue = 3100;

            var ui = Exclusive.Render(settings, gpuMaterial, srcRT);

            if (ui != null)
                Graphics.Blit(ui, dstRT);
            else
                Graphics.Blit(srcRT, dstRT);

            return ui;
        }
    }
}
