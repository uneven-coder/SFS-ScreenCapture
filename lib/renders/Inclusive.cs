using UnityEngine;
using FrameEmbededState;

namespace FrameEmbededState.Lib.Renders
{
    public static class Inclusive
    {
        public static void Render(VisualOverlayManager.State s, RenderTexture srcRT, RenderTexture dstRT)
        {   // Render overlay inclusive (processed output for both camera and UI)
            var settings = s.Settings;

            if (s.GpuMaterial != null)
                s.GpuMaterial.renderQueue = TryGetRenderQueue("UIOverlayInclusive", 3100);

            // produce the UI output (Exclusive path produces s.UiOutputRT)
            Exclusive.Render(s, srcRT);

            Graphics.Blit(s.UiOutputRT, dstRT);
        }

        private static int TryGetRenderQueue(string layer, int fallback)
        {   // Call VisualOverlayManager.TryGetRenderQueue via reflection
            var method = typeof(FrameEmbededState.VisualOverlayManager)
                .GetMethod("TryGetRenderQueue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method != null)
                return (int)method.Invoke(null, new object[] { layer, fallback });
            return fallback;
        }
    }
}
