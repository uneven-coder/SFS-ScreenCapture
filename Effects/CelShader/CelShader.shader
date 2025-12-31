Shader "Hidden/FrameEmbededState/CelShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _EdgeThreshold ("Edge Threshold", Float) = 0.34
        _ShadowThreshold ("Shadow Threshold", Float) = 0.4
        _HighlightThreshold ("Highlight Threshold", Float) = 0.8
        _ShadowIntensity ("Shadow Intensity", Float) = 0.5
        _HighlightIntensity ("Highlight Intensity", Float) = 1.2
        _ShadowColor ("Shadow Color", Color) = (0,0,0,1)
        _HighlightColor ("Highlight Color", Color) = (1,1,1,1)
        _Steps ("Steps", Int) = 10
        _Saturation ("Saturation", Float) = 1.0
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineThickness ("Outline Thickness", Float) = 1.0
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
            float _EdgeThreshold;
            float _ShadowThreshold;
            float _HighlightThreshold;
            float _ShadowIntensity;
            float _HighlightIntensity;
            float4 _ShadowColor;
            float4 _HighlightColor;
            int _Steps;
            float _Saturation;
            float4 _OutlineColor;
            float _OutlineThickness;
            float2 _MainTex_TexelSize;

            // Quantize luminance into N bands
            float Quantize(float value, int steps)
            {   // Quantize input value into discrete bands
                return floor(value * steps) / (steps - 1);
            }

            // Adjust color saturation
            float3 AdjustSaturation(float3 color, float saturation)
            {   // Adjust color saturation using luminance
                float luminance = dot(color, float3(0.299, 0.587, 0.114));
                return lerp(luminance.xxx, color, saturation);
            }

            // Sobel edge detection in screen space
            float SobelOutline(float2 uv, float thickness)
            {   // Compute outline using Sobel filter
                float2 texel = _MainTex_TexelSize * thickness;
                float3 sample[9];
                int idx = 0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++, idx++)
                    sample[idx] = tex2D(_MainTex, uv + float2(x, y) * texel).rgb;

                float3 gx = sample[2] + 2*sample[5] + sample[8] - (sample[0] + 2*sample[3] + sample[6]);
                float3 gy = sample[0] + 2*sample[1] + sample[2] - (sample[6] + 2*sample[7] + sample[8]);
                float edge = length(gx + gy);
                return edge;
            }

            float4 frag(v2f_img i) : SV_Target
            {   // Cel shade the camera image and overlay outlines

                float4 albedo = tex2D(_MainTex, i.uv);
                float3 baseColor = albedo.rgb;

                // Use luminance for banding (like a posterize)
                float luminance = dot(baseColor, float3(0.299, 0.587, 0.114));
                float band = Quantize(luminance, _Steps);

                float3 celColor = baseColor;
                if (band < _ShadowThreshold)
                    celColor = lerp(baseColor, _ShadowColor.rgb, _ShadowIntensity);
                else if (band > _HighlightThreshold)
                    celColor = lerp(baseColor, _HighlightColor.rgb, _HighlightIntensity);

                celColor = AdjustSaturation(celColor, _Saturation);

                float outline = SobelOutline(i.uv, _OutlineThickness);
                float edgeAlpha = smoothstep(_EdgeThreshold, _EdgeThreshold + 0.05, outline);

                float3 finalColor = lerp(celColor, _OutlineColor.rgb, edgeAlpha);

                return float4(finalColor, albedo.a);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
