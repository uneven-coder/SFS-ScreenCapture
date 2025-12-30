using UnityEngine;
using Unity.Collections;
using FrameEmbededState;
using FrameEmbededState.Lib.Renders; 

namespace FrameEmbededState.Lib.Renders
{
    public static class Exclusive
    {
        private static RenderTexture _uiRT;

        public static RenderTexture RenderUI(
            VisualOverlayManager.VisualOverlaySettings settings,
            RenderTexture srcRT)
        {   

            if (settings != null && settings.Execute != null)
                settings.Execute(settings);

            if (settings == null || srcRT == null)
            {
                Debug.Log("[Exclusive.RenderUI] settings or srcRT is null, returning null");
                return null;
            }

            if (!settings.BytecodeArgs.HasValue)
            {
                Debug.Log("[Exclusive.RenderUI] No BytecodeArgs present, returning null");
                return null;
            }

            int w = srcRT.width;
            int h = srcRT.height;

            EnsureUiRT(w, h);
            ClearUiRT();

            if (settings.BytecodeArgs.HasValue)
            {   
                Debug.Log("[Exclusive.RenderUI] Dispatching BytecodeVmShader for UI overlay");
                var args = settings.BytecodeArgs.Value;
                var patchedArgs = new BytecodeVmShader.Args(
                    source: srcRT,
                    result: _uiRT,
                    prog: args.Prog,
                    progLen: args.ProgLen,
                    maxSteps: args.MaxSteps,
                    time: Time.unscaledTime,
                    uiPos01: args.UIPos01,
                    const4: args.Const4,
                    const1: args.Const1
                );
                BytecodeVmShader.RunStatic(patchedArgs);
                return _uiRT;
            }

            return null;
        }

        public static void Release()
        {
            if (_uiRT != null)
            {
                _uiRT.Release();
                Object.Destroy(_uiRT);
            }
            _uiRT = null;
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
                filterMode = FilterMode.Bilinear,
                enableRandomWrite = true 
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
    }

    public static class RenderBehindUIRenderer
    {
        private static RenderTexture _cameraBuffer;
        private static RenderTexture _uavRT;

        public static void Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            RenderTexture srcRT,
            RenderTexture dstRT)
        {   // Render overlay using compute shader into a private UAV, then copy to dstRT or backbuffer

            Debug.Log("[RenderBehindUIRenderer] Render called");

            if (srcRT == null)
            {
                Debug.LogError("[RenderBehindUIRenderer] srcRT is null, cannot render");
                return;
            }

            bool toBackbuffer = dstRT == null;
            RenderTexture tempDst = null;
            RenderTexture finalDst = dstRT;

            if (toBackbuffer)
            {
                var desc = srcRT.descriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                tempDst = RenderTexture.GetTemporary(desc);
                tempDst.name = "VisualOverlay.BackbufferTemp";
                finalDst = tempDst;
            }

            try
            {
                if (settings == null || !settings.BytecodeArgs.HasValue)
                {
                    Debug.LogWarning("[RenderBehindUIRenderer] settings null or no BytecodeArgs, copying srcRT");
                    RtCopy.Copy(srcRT, finalDst);
                    return;
                }

                EnsureCameraBuffer(srcRT.width, srcRT.height);
                ClearCameraBuffer();
                RtCopy.Copy(srcRT, _cameraBuffer);

                var args = settings.BytecodeArgs.Value;
                if (args.Prog == null || args.ProgLen <= 0)
                {
                    Debug.LogError("[RenderBehindUIRenderer] BytecodeArgs.Prog is null/empty");
                    RtCopy.Copy(_cameraBuffer, finalDst);
                    return;
                }

                EnsureUav(srcRT.width, srcRT.height);
                ClearUav();

                var patchedArgs = new BytecodeVmShader.Args(
                    source: _cameraBuffer,
                    result: _uavRT,
                    prog: args.Prog,
                    progLen: args.ProgLen,
                    maxSteps: args.MaxSteps,
                    time: Time.unscaledTime,
                    uiPos01: args.UIPos01,
                    const4: args.Const4,
                    const1: args.Const1
                );

                BytecodeVmShader.RunStatic(patchedArgs);
                RtCopy.Copy(_uavRT, finalDst);
            }
            finally
            {
                if (toBackbuffer)
                {
                    Graphics.Blit(finalDst, (RenderTexture)null);
                    RenderTexture.ReleaseTemporary(tempDst);
                }
            }
        }

        public static void Release()
        {   
            if (_uavRT != null)
            {
                _uavRT.Release();
                Object.Destroy(_uavRT);
                _uavRT = null;
            }

            if (_cameraBuffer != null)
            {
                _cameraBuffer.Release();
                Object.Destroy(_cameraBuffer);
                _cameraBuffer = null;
            }
        }

        private static void EnsureCameraBuffer(int w, int h)
        {   
            if (_cameraBuffer != null && _cameraBuffer.width == w && _cameraBuffer.height == h)
                return;

            if (_cameraBuffer != null)
            {
                _cameraBuffer.Release();
                Object.Destroy(_cameraBuffer);
            }

            _cameraBuffer = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "VisualOverlay.CameraBuffer",
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                enableRandomWrite = false
            };
            _cameraBuffer.Create();
            ClearCameraBuffer(); 
        }

        private static void ClearCameraBuffer()
        {   
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = _cameraBuffer;
                GL.Clear(true, true, Color.clear);
            }
            finally
            {
                RenderTexture.active = prev;
            }
        }

        private static void EnsureUav(int w, int h)
        {   
            if (_uavRT != null && _uavRT.width == w && _uavRT.height == h)
                return;

            
            if (_uavRT != null)
            {
                _uavRT.Release();
                Object.Destroy(_uavRT);
                _uavRT = null;
            }

            _uavRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "VisualOverlay.BehindUI.UAV",
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                enableRandomWrite = true
            };
            _uavRT.Create();
        }

        private static void ClearUav()
        {   
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = _uavRT;
                GL.Clear(true, true, Color.clear);
            }
            finally
            {
                RenderTexture.active = prev;
            }
        }
    }

    public static class Inclusive
    {
        public static RenderTexture Render(
            VisualOverlayManager.VisualOverlaySettings settings,
            RenderTexture srcRT,
            RenderTexture dstRT,
            bool renderUI)
        {
            if (settings != null && settings.Execute != null)
                settings.Execute(settings);

            if (srcRT == null || dstRT == null)
                return null;

            if (settings == null)
            {
                Debug.LogWarning("[Inclusive] settings is null, using fallback.");
                RtCopy.Copy(srcRT, dstRT);
                return null;
            }

            if (!settings.BytecodeArgs.HasValue)
            {
                Debug.LogWarning("[Inclusive] BytecodeArgs not set, using fallback.");
                RtCopy.Copy(srcRT, dstRT);
                return null;
            }

            try
            {
                RenderBehindUIRenderer.Render(settings, srcRT, dstRT);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Inclusive] Exception in RenderBehindUIRenderer.Render: " + ex);
                RtCopy.Copy(srcRT, dstRT);
            }

            return renderUI ? dstRT : null;
        }
    }
}