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
            private float _time;
            public float Time
            {
                get => _time / 100000f;  // Increased divisor to slow down time further
                set => _time = value;
            }
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
            mat.SetFloat("_Time", args.Time);  // Set custom time for controlled animation speed
        }
    }
}
