using System;
using UnityEngine;
using FrameEmbededState.Lib;
using FrameEmbededState;
using FrameEmbededState.Effects.oldFilter;
using Unity.Collections;
using FrameEmbededState.Lib.Renders;

namespace FrameEmbededState.Effects.oldFilter
{
    [ShaderModule("oldFilter", ShaderType.Shader, "Hidden/FrameEmbededState/OldFilter", OverlayRenderMode.Inclusive)]
    public class OldFilterShaderModule : ShaderModule<OldFilterShaderModule.Args, object>
    {
        public struct Args
        {
            public float Contrast, Exposure, Gamma, GrainAmount, GrainSpeed, FlickerAmt, VignetteAmt, ScanlineAmt, DustChance, ScratchAmt;
            public int ScratchWidth;
            // To make time pass slower, scale it down (e.g., by 0.5f for half speed)
            private float _time;
            public float Time
            {
            get => _time  / 100f; // Increased divisor to slow down time further
            set => _time = value;
            }
            public int FrameSeed;
        }

        public OldFilterShaderModule()
        {   // Only assign metadata, do not load or assign shader/material here
        }

        public override object Run(in Args args)
        {   // Not used for direct rendering; see ApplyConstants
            return null;
        }

        public void ApplyConstants(Material mat, in Args args)
        {   // Set all effect constants on the provided material
            if (mat == null)
                throw new System.ArgumentNullException(nameof(mat));

            mat.SetFloat("_Contrast", args.Contrast);
            mat.SetFloat("_Exposure", args.Exposure);
            mat.SetFloat("_Gamma", args.Gamma);
            mat.SetFloat("_GrainAmount", args.GrainAmount);
            mat.SetFloat("_GrainSpeed", args.GrainSpeed);
            mat.SetFloat("_FlickerAmt", args.FlickerAmt);
            mat.SetFloat("_VignetteAmt", args.VignetteAmt);
            mat.SetFloat("_ScanlineAmt", args.ScanlineAmt);
            mat.SetFloat("_DustChance", args.DustChance);
            mat.SetFloat("_ScratchAmt", args.ScratchAmt);
            mat.SetInt("_ScratchWidth", args.ScratchWidth);
            mat.SetFloat("_Time", args.Time);  // Set custom time for controlled animation speed
        }
    }
}
