using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

namespace FrameEmbededState
{
    public static class ComputeShaderNames
    {
        public const string AddTwoNumbers = "AddTwoNumbers";
        public const string CSMain = "CSMain";
        public const string GeneralPixel = "compute"; // Name of the general-purpose compute shader
    }

    public class ComputeShaderArgs : Dictionary<string, object>
    {   // Helper for passing named arguments to compute shader runs
        public T Get<T>(string key) => ContainsKey(key) ? (T)this[key] : default;
    }

    public abstract class BaseComputeShader
    {

        public ComputeShader Shader { get; private set; }
        public string ShaderName { get; private set; }

        protected BaseComputeShader(ComputeShader shader, string shaderName)
        {   // Store shader and register
            Shader = shader;
            ShaderName = shaderName;
            RegisterInstance(this);
        }

        public abstract object Run(ComputeShaderArgs args);


        private static readonly Dictionary<string, BaseComputeShader> registry = new();

        public static void RegisterInstance(BaseComputeShader instance)
        {   // Register or replace a shader instance by name
            if (instance == null || string.IsNullOrEmpty(instance.ShaderName))
                return;
            registry[instance.ShaderName] = instance;
        }

        public static void RegisterFromAsset(ComputeShader shader)
        {   // Register a shader from a ComputeShader asset by name
            if (shader == null || string.IsNullOrEmpty(shader.name))
                return;

            // Add new shader types here as needed
            if (shader.name == ComputeShaderNames.AddTwoNumbers)
                new AddTwoNumbersShader(shader);
            else if (shader.name == ComputeShaderNames.GeneralPixel)
                new GeneralPixelComputeShader(shader);
            // else if (shader.name == ...) new OtherShader(shader);
        }

        public static BaseComputeShader Get(string shaderName) =>
            registry.TryGetValue(shaderName, out var s) ? s : null;

        public static void AutoRegisterAllInMemory()
        {   // Scan all loaded compute shaders and auto-register known types
            foreach (var cs in Resources.FindObjectsOfTypeAll<ComputeShader>())
                RegisterFromAsset(cs);
        }
    }

    public class AddTwoNumbersShader : BaseComputeShader
    {   // AddTwoNumbers compute shader wrapper

        public AddTwoNumbersShader(ComputeShader shader)
            : base(shader, ComputeShaderNames.AddTwoNumbers) { }

        public override object Run(ComputeShaderArgs args)
        {   // Run CSMain with two float arrays "A" and "B"
            float[] a = args.Get<float[]>("A");
            float[] b = args.Get<float[]>("B");

            int kernel = Shader.FindKernel(ComputeShaderNames.CSMain);
            int count = Math.Min(a?.Length ?? 0, b?.Length ?? 0);
            if (count == 0)
                return Array.Empty<float>();

            var inputA = new ComputeBuffer(count, sizeof(float));
            var inputB = new ComputeBuffer(count, sizeof(float));
            var output = new ComputeBuffer(count, sizeof(float));
            var result = new float[count];

            inputA.SetData(a);
            inputB.SetData(b);

            Shader.SetBuffer(kernel, "InputA", inputA);
            Shader.SetBuffer(kernel, "InputB", inputB);
            Shader.SetBuffer(kernel, "Result", output);

            Shader.Dispatch(kernel, 8, 8, 1);

            output.GetData(result);

            inputA.Release();
            inputB.Release();
            output.Release();

            return result;
        }
    }

    public class GeneralPixelComputeShader : BaseComputeShader
    {   // Flexible compute shader for arbitrary pixel operations

        public GeneralPixelComputeShader(ComputeShader shader)
            : base(shader, ComputeShaderNames.GeneralPixel) { }

        public override object Run(ComputeShaderArgs args)
        {   // Run with user-specified parameters and textures
            var srcTex = args.Get<RenderTexture>("Source");
            var dstTex = args.Get<RenderTexture>("Result");
            float time = args.Get<float>("Time");
            Vector2 mouse = args.Get<Vector2>("MouseNorm");
            int mode = args.Get<int>("Mode");
            float paramA = args.Get<float>("ParamA");
            float paramB = args.Get<float>("ParamB");
            float paramC = args.Get<float>("ParamC");

            int kernel = Shader.FindKernel(ComputeShaderNames.CSMain);

            Shader.SetTexture(kernel, "Result", dstTex);
            if (srcTex != null)
                Shader.SetTexture(kernel, "Source", srcTex);

            Shader.SetFloat("Time", time);
            Shader.SetFloats("MouseNorm", mouse.x, mouse.y);
            Shader.SetFloats("Resolution", dstTex.width, dstTex.height);
            Shader.SetFloat("Mode", mode);
            Shader.SetFloat("ParamA", paramA);
            Shader.SetFloat("ParamB", paramB);
            Shader.SetFloat("ParamC", paramC);

            int tx = Mathf.CeilToInt(dstTex.width / 8.0f);
            int ty = Mathf.CeilToInt(dstTex.height / 8.0f);
            Shader.Dispatch(kernel, tx, ty, 1);

            return dstTex;
        }
    }

    public static class ComputeShaderUtil
    {   // Utility for running and logging compute shader tests

        public static void AutoRegisterAll()
        {   // Register all known compute shaders in memory
            BaseComputeShader.AutoRegisterAllInMemory();
        }

        public static void RunAndLogAddTwoNumbersTest()
        {   // Run AddTwoNumbers shader test and log result
            var shader = BaseComputeShader.Get(ComputeShaderNames.AddTwoNumbers) as AddTwoNumbersShader;
            if (shader == null)
            {
                Debug.LogWarning("[FrameEmbededState] Example compute shader NOT found");
                return;
            }

            float[] a = Enumerable.Range(0, 64).Select(i => (float)i).ToArray();
            float[] b = Enumerable.Range(0, 64).Select(i => (float)(i * 2)).ToArray();
            var args = new ComputeShaderArgs { ["A"] = a, ["B"] = b };
            float[] result = shader.Run(args) as float[];

            Debug.Log($"[FrameEmbededState] Compute shader CSMain result: [{string.Join(", ", result.Take(8))} ... {string.Join(", ", result.Skip(56))}]");
        }

        public static void RunAndLogGeneralPixelTest(RenderTexture src, RenderTexture dst, int mode)
        {   // Run general pixel compute shader and log result
            var shader = BaseComputeShader.Get(ComputeShaderNames.GeneralPixel) as GeneralPixelComputeShader;
            if (shader == null)
            {
                Debug.LogWarning("[FrameEmbededState] GeneralPixel compute shader NOT found");
                return;
            }

            var args = new ComputeShaderArgs
            {
                ["Source"] = src,
                ["Result"] = dst,
                ["Time"] = Time.unscaledTime,
                ["MouseNorm"] = new Vector2(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height),
                ["Mode"] = mode,
                ["ParamA"] = 0.35f, // Example: vignette amount or starfield parallax
                ["ParamB"] = 0.02f, // Example: starfield speed
                ["ParamC"] = 14.0f  // Example: star size
            };
            shader.Run(args);

            Debug.Log($"[FrameEmbededState] Ran GeneralPixel compute shader in mode {mode}");
        }
    }
}
