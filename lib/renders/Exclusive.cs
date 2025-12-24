using UnityEngine;
using Unity.Collections;
using FrameEmbededState;

namespace FrameEmbededState.Lib.Renders
{
    public static class Exclusive
    {
        public static void Render(VisualOverlayManager.State s, RenderTexture srcRT)
        {   // Render overlay for UI only (OnTop/Exclusive)
            var settings = s.Settings;

            var prev = RenderTexture.active;
            RenderTexture.active = s.UiOutputRT;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prev;

            if (s.GpuMaterial != null)
            {
                Graphics.Blit(srcRT, s.UiOutputRT, s.GpuMaterial);
                return;
            }

            CopyNative(s.Src, s.Dst);
            settings.Execute?.Invoke(new FrameEmbededState.VisualOverlayManager.FrameData
            {
                Source = s.Src,
                Result = s.Dst,
                Width = s.Width,
                Height = s.Height
            });
            s.DstTex.Apply(false, false);
            Graphics.Blit(s.DstTex, s.UiOutputRT);
        }

        private static void CopyNative(NativeArray<Color32> from, NativeArray<Color32> to)
        {
            int n = Mathf.Min(from.Length, to.Length);
            for (int i = 0; i < n; i++)
                to[i] = from[i];
        }
    }
}
