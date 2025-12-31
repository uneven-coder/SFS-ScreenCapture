Shader "Hidden/FrameEmbededState/UIHologram"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _DistortionAmt ("Distortion Amount", Float) = 0.05
        _ColorShiftAmt ("Color Shift Amount", Float) = 0.1
        _FlickerSpeed ("Flicker Speed", Float) = 10.0
        _HoloIntensity ("Hologram Intensity", Float) = 0.8
        _ScanLines ("Scan Lines", Int) = 100
        _TimeSpeed ("Time Speed", Float) = 0.1
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
            float _DistortionAmt, _ColorShiftAmt, _FlickerSpeed, _HoloIntensity;
            int _ScanLines;
            float _TimeSpeed;

            float2 _MainTex_TexelSize;

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 19.19);
                return frac((p3.x + p3.y) * p3.z);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y * _TimeSpeed;

                // Distortion
                float distort = sin(uv.y * 10.0 + t * 2.0) * _DistortionAmt;
                uv.x += distort;

                float4 c = tex2D(_MainTex, uv);

                // Color shift
                float shift = sin(t * _FlickerSpeed) * _ColorShiftAmt;
                float r = tex2D(_MainTex, uv + float2(shift, 0)).r;
                float b = tex2D(_MainTex, uv - float2(shift, 0)).b;
                c = float4(r, c.g, b, c.a);

                // Flickering
                float flicker = 1.0 + sin(t * _FlickerSpeed * 2.0) * 0.1;
                c.rgb *= flicker;

                // Scan lines
                float scan = sin(uv.y * _ScanLines + t * 5.0) * 0.5 + 0.5;
                c.rgb *= (1.0 - _HoloIntensity) + _HoloIntensity * scan;

                // Holographic tint
                c.rgb = lerp(c.rgb, float3(0.2, 0.8, 1.0), _HoloIntensity * 0.3);

                return c;
            }
            ENDCG
        }
    }
}
