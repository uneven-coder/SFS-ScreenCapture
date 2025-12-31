Shader "Hidden/FrameEmbededState/OldFilter"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Contrast ("Contrast", Float) = 1.25    // >1 = punchier
        _Exposure ("Exposure", Float) = 1.05    // overall brightness
        _Gamma ("Gamma", Float) = 0.95          // <1 lifts mids a bit
        _GrainAmount ("Grain Amount", Float) = 0.18   // 0..0.4 typical
        _GrainSpeed ("Grain Speed", Float) = 24       // noise changes per second
        _FlickerAmt ("Flicker Amount", Float) = 0.06  // 0..0.15
        _VignetteAmt ("Vignette Amount", Float) = 0.35    // 0..0.6
        _ScanlineAmt ("Scanline Amount", Float) = 0.06    // subtle
        _DustChance ("Dust Chance", Float) = 0.0022   // per-pixel probability
        _ScratchAmt ("Scratch Amount", Float) = 0.22   // 0..0.5
        _ScratchWidth ("Scratch Width", Int) = 1       // px
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Contrast, _Exposure, _Gamma, _GrainAmount, _GrainSpeed, _FlickerAmt, _VignetteAmt, _ScanlineAmt, _DustChance, _ScratchAmt;
            int _ScratchWidth;
            float _Time;

            float2 _MainTex_TexelSize;

            float Hash12(float2 p)
            {
                float3 p3  = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 19.19);
                return frac((p3.x + p3.y) * p3.z);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float4 c = tex2D(_MainTex, uv);
                float l = dot(c.rgb, float3(0.299, 0.587, 0.114));

                float t = _Time;
                float flickerRand = (Hash12(float2(t, 0)) * 2.0 - 1.0) * _FlickerAmt;
                float flickerSine = sin(t * 6.2) * (_FlickerAmt * 0.35);
                float flicker = 1.0 + flickerRand + flickerSine;

                l *= _Exposure * flicker;
                l = (l - 0.5) * _Contrast + 0.5;
                l = pow(saturate(l), _Gamma);

                float2 px = uv / _MainTex_TexelSize.xy;
                float n = Hash12(px + t * _GrainSpeed) - 0.5;
                float n2 = Hash12(px + float2(131, -71) + t * _GrainSpeed) - 0.5;
                l += (n * 0.75 + n2 * 0.25) * _GrainAmount;

                float d = Hash12(px + float2(-19, 23) + t * 3.7);
                if (d > 1.0 - _DustChance)
                    l = (Hash12(px + float2(5, 9) + t * 11.0) > 0.5) ? 1.0 : 0.0;

                float w = 1.0 / _MainTex_TexelSize.x;
                float h = 1.0 / _MainTex_TexelSize.y;
                float cx = (w - 1.0) * 0.5;
                float cy = (h - 1.0) * 0.5;
                float2 pix = px;
                float rx = pix.x - cx, ry = pix.y - cy;
                float r = sqrt(rx * rx + ry * ry) / sqrt(cx * cx + cy * cy);
                float vig = 1.0 - _VignetteAmt * (r * r);
                l *= vig;

                float scan = 1.0 - _ScanlineAmt * (0.5 + 0.5 * sin((pix.y + t * 18.0) * 3.14159));
                l *= scan;

                // Scratches
                float seg = floor(t * 0.5);
                float scratchX1 = Hash12(float2(seg * 17.0 + 1.0, 0.0)) * (w - 1.0);
                float scratchX2 = Hash12(float2(seg * 17.0 + 2.0, 0.0)) * (w - 1.0);
                bool twoScratches = Hash12(float2(seg * 17.0 + 3.0, 0.0)) > 0.55;
                float dist1 = abs(pix.x - scratchX1);
                float dist2 = abs(pix.x - scratchX2);
                bool hitScratch = dist1 <= _ScratchWidth || (twoScratches && dist2 <= _ScratchWidth);
                if (hitScratch)
                {
                    float s = 1.0 - (min(dist1, dist2) / float(_ScratchWidth + 1));
                    l = lerp(l, 1.0, _ScratchAmt * s);
                }

                float q = saturate(l);
                return float4(q, q, q, c.a);
            }
            ENDCG
        }
    }
}
