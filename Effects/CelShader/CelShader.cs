using System;
using UnityEngine;
using FrameEmbededState.Lib;
using FrameEmbededState;
using FrameEmbededState.Lib.Renders;

namespace FrameEmbededState.Effects.CelShader
{
    [ShaderModule("CelShader", ShaderType.Shader, "Hidden/FrameEmbededState/CelShader", OverlayRenderMode.Inclusive)]
    public class CelShaderModule : ShaderModule<CelShaderModule.Args, object>
    {
        public struct Args
        {   // Holds all cel shader parameters for post-process effect
            public float EdgeThreshold, ShadowThreshold, HighlightThreshold, ShadowIntensity, HighlightIntensity;
            public Color ShadowColor, HighlightColor;
            public int Steps;
            public float Saturation;
            public Color OutlineColor;
            public float OutlineThickness;
        }

        public CelShaderModule()
        {   // Initialize metadata for cel shader
        }

        public override object Run(in Args args)
        {   // Not used for direct rendering; constants applied via ApplyConstants
            return null;
        }

        public void ApplyConstants(Material mat, in Args args)
        {   // Apply cel shading parameters to the material

            if (mat == null)
                throw new ArgumentNullException(nameof(mat));

            mat.SetFloat("_EdgeThreshold", args.EdgeThreshold);
            mat.SetFloat("_ShadowThreshold", args.ShadowThreshold);
            mat.SetFloat("_HighlightThreshold", args.HighlightThreshold);
            mat.SetFloat("_ShadowIntensity", args.ShadowIntensity);
            mat.SetFloat("_HighlightIntensity", args.HighlightIntensity);
            mat.SetColor("_ShadowColor", args.ShadowColor);
            mat.SetColor("_HighlightColor", args.HighlightColor);
            mat.SetInt("_Steps", args.Steps);
            mat.SetFloat("_Saturation", args.Saturation);
            mat.SetColor("_OutlineColor", args.OutlineColor);
            mat.SetFloat("_OutlineThickness", args.OutlineThickness);
        }
    }
}
