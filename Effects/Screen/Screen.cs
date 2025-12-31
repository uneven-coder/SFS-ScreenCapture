using System;
using UnityEngine;
using FrameEmbededState.Lib;
using FrameEmbededState;
using FrameEmbededState.Effects.Screen;
using Unity.Collections;
using FrameEmbededState.Lib.Renders;

namespace FrameEmbededState.Effects.Screen {
    [ShaderModule("UIHologram", ShaderType.Shader, "Hidden/FrameEmbededState/UIHologram", OverlayRenderMode.Exclusive)]
    public class UIHologramShaderModule : ShaderModule<UIHologramShaderModule.Args, object>
    {
        public struct Args
        {
            public float DistortionAmt, ColorShiftAmt, FlickerSpeed, HoloIntensity;
            public int ScanLines;
            public float TimeSpeed;
            public int Seed;
        }

        public UIHologramShaderModule()
        {   // Initialize metadata for UI hologram shader
        }

        public override object Run(in Args args)
        {   // Not used for direct rendering; constants applied via ApplyConstants
            return null;
        }

        public void ApplyConstants(Material mat, in Args args)
        {   // Apply hologram effect parameters to the material
            if (mat == null)
                throw new System.ArgumentNullException(nameof(mat));

            mat.SetFloat("_DistortionAmt", args.DistortionAmt);
            mat.SetFloat("_ColorShiftAmt", args.ColorShiftAmt);
            mat.SetFloat("_FlickerSpeed", args.FlickerSpeed);
            mat.SetFloat("_HoloIntensity", args.HoloIntensity);
            mat.SetInt("_ScanLines", args.ScanLines);
            mat.SetFloat("_TimeSpeed", args.TimeSpeed);
        }

        public override void ApplyArgs(Material mat, object args)
        {   // Apply the arguments to the material
            if (args is Args _args)
                ApplyConstants(mat, _args);
        }
    }
}
