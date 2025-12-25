using UnityEngine;
using Unity.Collections;
using FrameEmbededState;

namespace FrameEmbededState.Lib.Renders
{
    public static class RenderBehindUIRenderer
    {
        private static Texture2D _srcTex;
        private static Texture2D _dstTex;
        private static NativeArray<Color32> _src;
        private static NativeArray<Color32> _dst;
        private static int _w;
        private static int _h;
        private static Rect _rect;

        public static void Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            Material gpuMaterial,
            RenderTexture srcRT,
            RenderTexture dstRT)
        {
            if (settings == null)
            {
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            if (settings.UseGpuShader && gpuMaterial != null)
            {
                Graphics.Blit(srcRT, dstRT, gpuMaterial);
                return;
            }

            if (settings.Execute == null)
            {
                Graphics.Blit(srcRT, dstRT);
                return;
            }

            EnsureCpuBuffers(srcRT.width, srcRT.height);

            var prev = RenderTexture.active;
            RenderTexture.active = srcRT;
            _srcTex.ReadPixels(_rect, 0, 0, false);
            RenderTexture.active = prev;

            CopyNative(_src, _dst);

            settings.Execute(new VisualOverlayManager.FrameData
            {
                Source = _src,
                Result = _dst,
                Width = _w,
                Height = _h
            });

            _dstTex.Apply(false, false);
            Graphics.Blit(_dstTex, dstRT);
        }

        public static void Release()
        {
            if (_srcTex != null) UnityEngine.Object.Destroy(_srcTex);
            if (_dstTex != null) UnityEngine.Object.Destroy(_dstTex);
            _srcTex = null;
            _dstTex = null;
            _w = 0;
            _h = 0;
        }

        private static void EnsureCpuBuffers(int w, int h)
        {
            if (_srcTex != null && _w == w && _h == h)
                return;

            Release();

            _w = w;
            _h = h;
            _rect = new Rect(0, 0, w, h);

            _srcTex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            _dstTex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };

            _src = _srcTex.GetRawTextureData<Color32>();
            _dst = _dstTex.GetRawTextureData<Color32>();
        }

        private static void CopyNative(NativeArray<Color32> from, NativeArray<Color32> to)
        {
            int n = Mathf.Min(from.Length, to.Length);
            for (int i = 0; i < n; i++)
                to[i] = from[i];
        }
    }
}
