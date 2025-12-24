using UnityEngine;
using Unity.Collections;
using FrameEmbededState;

namespace FrameEmbededState.Lib.Renders
{
    public static class RenderBehindUIRenderer
    {
        public static void Render(VisualOverlayManager.State s, RenderTexture srcRT, RenderTexture dstRT)
        {   // Render overlay behind UI using CPU or GPU path
            var settings = s.Settings;

            if (settings.UseGpuShader && s.GpuMaterial != null)
            {
                Graphics.Blit(srcRT, dstRT, s.GpuMaterial);
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
            Graphics.Blit(s.DstTex, dstRT);
        }

        private static void CopyNative(NativeArray<Color32> from, NativeArray<Color32> to)
        {
            int n = Mathf.Min(from.Length, to.Length);
            for (int i = 0; i < n; i++)
                to[i] = from[i];
        }
    }
}
