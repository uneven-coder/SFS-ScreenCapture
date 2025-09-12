using UnityEngine;
using System.Linq;

namespace ScreenCapture
{
    public static class Compatibility
    {   // Centralized GPU and platform compatibility detection and workarounds
        
        private static bool? _isProblematicGPU;
        private static bool? _requiresMultipleClear;
        private static bool? _supportsAdvancedFormats;

        public static bool IsProblematicGPU
        {   // Detect GPUs with known rendering issues requiring workarounds
            get
            {
                if (_isProblematicGPU.HasValue) return _isProblematicGPU.Value;

                string gpu = SystemInfo.graphicsDeviceName.ToLower();
                string version = SystemInfo.graphicsDeviceVersion.ToLower();
                
                _isProblematicGPU = gpu.Contains("intel") || gpu.Contains("amd") || gpu.Contains("radeon") || 
                                   gpu.Contains("apple") || gpu.Contains("mali") || gpu.Contains("adreno") ||
                                   gpu.Contains("powervr") || gpu.Contains("vivante") ||
                                   version.Contains("opengl es") || version.Contains("opengl 2.") ||
                                   version.Contains("vulkan") && gpu.Contains("qualcomm");
                
                return _isProblematicGPU.Value;
            }
        }

        public static bool RequiresMultipleClear
        {   // Check if GPU requires multiple clearing passes for proper background rendering
            get
            {
                if (_requiresMultipleClear.HasValue) return _requiresMultipleClear.Value;

                string gpu = SystemInfo.graphicsDeviceName.ToLower();
                string version = SystemInfo.graphicsDeviceVersion.ToLower();
                
                _requiresMultipleClear = (gpu.Contains("intel") && !gpu.Contains("iris xe")) ||
                                        (gpu.Contains("amd") && version.Contains("opengl")) ||
                                        gpu.Contains("mali-g") || gpu.Contains("adreno 5") ||
                                        (Application.platform == RuntimePlatform.Android && gpu.Contains("qualcomm"));
                
                return _requiresMultipleClear.Value;
            }
        }

        public static bool SupportsAdvancedFormats
        {   // Check if platform supports advanced texture formats
            get
            {
                if (_supportsAdvancedFormats.HasValue) return _supportsAdvancedFormats.Value;

                _supportsAdvancedFormats = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) &&
                                          SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32) &&
                                          SystemInfo.maxTextureSize >= 4096 &&
                                          SystemInfo.graphicsMemorySize >= 512;
                
                return _supportsAdvancedFormats.Value;
            }
        }

        public static RenderTextureFormat GetOptimalRenderTextureFormat()
        {   // Select best render texture format based on platform capabilities and compatibility
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
                return RenderTextureFormat.ARGB32;  // Mobile standard

            if (SupportsAdvancedFormats && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32))
                return RenderTextureFormat.ARGB32;

            if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB565))
                return RenderTextureFormat.RGB565;  // Older hardware fallback

            return RenderTextureFormat.Default;
        }

        public static TextureFormat GetOptimalTexture2DFormat()
        {   // Select optimal texture format for screenshot capture
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
                return TextureFormat.RGBA32;  // Mobile standard

            if (SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32))
                return TextureFormat.RGBA32;

            if (SystemInfo.SupportsTextureFormat(TextureFormat.ARGB32))
                return TextureFormat.ARGB32;

            return TextureFormat.RGB24;  // Last resort fallback
        }

        public static RenderTextureFormat[] GetFallbackRenderTextureFormats()
        {   // Ordered list of fallback render texture formats for problematic hardware
            return new[]
            {
                RenderTextureFormat.RGB565,
                RenderTextureFormat.ARGB4444,
                RenderTextureFormat.ARGB1555,
                RenderTextureFormat.Default
            }.Where(SystemInfo.SupportsRenderTextureFormat).ToArray();
        }

        public static TextureFormat[] GetFallbackTexture2DFormats()
        {   // Ordered list of fallback texture formats for pixel reading
            return new[]
            {
                TextureFormat.RGB24,
                TextureFormat.ARGB4444,
                TextureFormat.RGB565,
                TextureFormat.ARGB32
            }.Where(SystemInfo.SupportsTextureFormat).ToArray();
        }

        public static void ApplyPlatformSpecificRTSettings(RenderTexture rt)
        {   // Apply platform-specific render texture optimizations and workarounds
            switch (Application.platform)
            {
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    rt.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
                    if (SystemInfo.graphicsDeviceName.ToLower().Contains("apple"))
                        rt.antiAliasing = 1;  // Apple Silicon compatibility
                    break;

                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    rt.enableRandomWrite = false;  // Linux OpenGL compatibility
                    if (SystemInfo.graphicsDeviceVersion.ToLower().Contains("mesa"))
                        rt.filterMode = FilterMode.Point;  // Mesa driver workaround
                    break;

                case RuntimePlatform.Android:
                    rt.memorylessMode = RenderTextureMemoryless.None;  // Ensure data persistence
                    if (SystemInfo.graphicsDeviceName.ToLower().Contains("adreno"))
                        rt.autoGenerateMips = false;  // Adreno stability
                    break;

                case RuntimePlatform.IPhonePlayer:
                    rt.memorylessMode = RenderTextureMemoryless.None;
                    if (SystemInfo.graphicsDeviceName.ToLower().Contains("apple"))
                        rt.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
                    break;

                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    if (SystemInfo.graphicsDeviceName.ToLower().Contains("intel"))
                        rt.wrapMode = TextureWrapMode.Clamp;  // Intel driver stability
                    break;
            }
        }

        public static void ApplyGPUSpecificClear(RenderTexture rt, Color color)
        {   // Apply GPU-specific clearing strategies for maximum compatibility
            RenderTexture.active = rt;

            if (RequiresMultipleClear && color.a > 0.01f)
            {   // Multiple clear passes for problematic drivers but only for opaque colors
                GL.Clear(true, true, color);
                GL.Clear(false, true, color);  // Depth-only clear
                GL.Clear(true, false, color);  // Color-only clear
            }
            else
                GL.Clear(true, true, color);

            RenderTexture.active = null;
        }

        public static void ApplyTransparentClear(RenderTexture rt)
        {   // Apply transparent clearing for GPUs that need explicit transparent setup
            RenderTexture.active = rt;
            
            if (IsProblematicGPU)
            {   // Force transparent clear for problematic drivers
                GL.Clear(true, true, new Color(0, 0, 0, 0));
                GL.Clear(true, false, new Color(0, 0, 0, 0));  // Color-only transparent clear
            }
            
            RenderTexture.active = null;
        }

        public static bool RequiresPixelReadRetry()
        {   // Check if pixel reading requires retry logic for stability
            return IsProblematicGPU || 
                   Application.platform == RuntimePlatform.Android ||
                   SystemInfo.graphicsDeviceVersion.ToLower().Contains("opengl es");
        }

        public static bool SupportsPNGEncoding()
        {   // Check if platform supports reliable PNG encoding
            return SystemInfo.graphicsMemorySize >= 256 && 
                   !SystemInfo.graphicsDeviceName.ToLower().Contains("mali-4") &&
                   !SystemInfo.graphicsDeviceName.ToLower().Contains("adreno 2");
        }

        public static int GetOptimalRetryCount()
        {   // Get optimal retry count based on platform stability
            if (Application.platform == RuntimePlatform.Android)
                return 5;
            if (IsProblematicGPU)
                return 3;
            return 1;
        }

        public static int GetOptimalThreadSleepMs()
        {   // Get optimal sleep duration between retries based on platform
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
                return 20;
            if (SystemInfo.graphicsDeviceName.ToLower().Contains("intel"))
                return 15;
            return 10;
        }

        public static bool RequiresFallbackSavePath()
        {   // Check if platform requires fallback save location due to permission issues
            return Application.platform == RuntimePlatform.Android ||
                   Application.platform == RuntimePlatform.IPhonePlayer ||
                   Application.platform == RuntimePlatform.WebGLPlayer;
        }

        public static string GetDebugInfo()
        {   // Generate comprehensive compatibility debug information
            return $"GPU: {SystemInfo.graphicsDeviceName} | " +
                   $"Version: {SystemInfo.graphicsDeviceVersion} | " +
                   $"Memory: {SystemInfo.graphicsMemorySize}MB | " +
                   $"MaxTexture: {SystemInfo.maxTextureSize} | " +
                   $"Platform: {Application.platform} | " +
                   $"Problematic: {IsProblematicGPU} | " +
                   $"MultipleClear: {RequiresMultipleClear} | " +
                   $"AdvancedFormats: {SupportsAdvancedFormats}";
        }
    }
}