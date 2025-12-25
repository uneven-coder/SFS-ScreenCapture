using UnityEngine;
using Unity.Collections;
using FrameEmbededState;

namespace FrameEmbededState.Lib.Renders
{
    /// <summary>
    /// UI-only renderer. Never renders to screen.
    /// </summary>
    public static class Exclusive
    {
        private static Texture2D _srcTex;
        private static Texture2D _dstTex;
        private static NativeArray<Color32> _src;
        private static NativeArray<Color32> _dst;
        private static int _w, _h;
        private static Rect _rect;

        private static RenderTexture _uiRT;

        public static RenderTexture RenderUI(
            VisualOverlayManager.VisualOverlaySettings settings,
            Material gpuMaterial,
            RenderTexture srcRT)
        {
            if (settings == null || srcRT == null)
                return null;

            // If nothing would render, don't allocate or compute
            if (!settings.UseGpuShader && settings.Execute == null)
                return null;

            // If GPU requested but material missing AND no CPU fallback, do nothing
            if (settings.UseGpuShader && gpuMaterial == null && settings.Execute == null)
                return null;

            int w = srcRT.width;
            int h = srcRT.height;

            EnsureUiRT(w, h);
            ClearUiRT();

            // GPU path
            if (settings.UseGpuShader && gpuMaterial != null)
            {
                Graphics.Blit(srcRT, _uiRT, gpuMaterial);
                return _uiRT;
            }

            // CPU path (requires Execute)
            if (settings.Execute == null)
                return null;

            EnsureCpuBuffers(w, h);

            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = srcRT;
                _srcTex.ReadPixels(_rect, 0, 0, false);
            }
            finally
            {
                RenderTexture.active = prev;
            }

            CopyNative(_src, _dst);

            settings.Execute(new VisualOverlayManager.FrameData
            {
                Source = _src,
                Result = _dst,
                Width = _w,
                Height = _h
            });

            _dstTex.Apply(false, false);
            Graphics.Blit(_dstTex, _uiRT);

            return _uiRT;
        }

        public static void Release()
        {
            if (_uiRT != null)
            {
                _uiRT.Release();
                Object.Destroy(_uiRT);
            }

            if (_srcTex != null) Object.Destroy(_srcTex);
            if (_dstTex != null) Object.Destroy(_dstTex);

            _uiRT = null;
            _srcTex = null;
            _dstTex = null;
            _src = default;
            _dst = default;
            _w = _h = 0;
            _rect = default;
        }

        private static void EnsureUiRT(int w, int h)
        {
            if (_uiRT != null && _uiRT.width == w && _uiRT.height == h)
                return;

            if (_uiRT != null)
            {
                _uiRT.Release();
                Object.Destroy(_uiRT);
            }

            _uiRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "VisualOverlay.UI",
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _uiRT.Create();
        }

        private static void ClearUiRT()
        {
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = _uiRT;
                GL.Clear(true, true, Color.clear);
            }
            finally
            {
                RenderTexture.active = prev;
            }
        }

        private static void EnsureCpuBuffers(int w, int h)
        {
            if (_srcTex != null && _w == w && _h == h)
                return;

            if (_srcTex != null) Object.Destroy(_srcTex);
            if (_dstTex != null) Object.Destroy(_dstTex);

            _w = w;
            _h = h;
            _rect = new Rect(0, 0, w, h);

            _srcTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            _dstTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            _src = _srcTex.GetRawTextureData<Color32>();
            _dst = _dstTex.GetRawTextureData<Color32>();
        }

        private static void CopyNative(NativeArray<Color32> from, NativeArray<Color32> to)
        {
            int n = Mathf.Min(from.Length, to.Length);
            if (n <= 0)
                return;

#if UNITY_2018_1_OR_NEWER
            // Uses Unity's internal memcopy (faster than element-by-element)
            NativeArray<Color32>.Copy(from, 0, to, 0, n);
#else
            for (int i = 0; i < n; i++)
                to[i] = from[i];
#endif
        }
    }
}
