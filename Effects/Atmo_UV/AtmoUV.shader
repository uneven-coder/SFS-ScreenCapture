Shader "Hidden/FrameEmbededState/AtmoUVShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ColorR ("Color R", Color) = (1,0,0,1)
        _ColorG ("Color G", Color) = (0,1,0,1)
        _ColorB ("Color B", Color) = (0,0,1,1)
        _ColorW ("Color W", Color) = (1,1,1,1)
        _BoundsMin ("Bounds Min", Vector) = (-1,-1,0,0)
        _BoundsMax ("Bounds Max", Vector) = (1,1,0,0)
        _GridDensity ("Grid Density", Float) = 8
        _GridLineColor ("Grid Line Color", Color) = (1,1,1,1)
        _GridLineThickness ("Grid Line Thickness", Range(0.005,0.1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 objectPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ColorR;
            float4 _ColorG;
            float4 _ColorB;
            float4 _ColorW;
            float2 _BoundsMin;
            float2 _BoundsMax;
            float _GridDensity;
            float4 _GridLineColor;
            float _GridLineThickness;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.objectPos = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {   // Render textured gradient while highlighting UV positions and grid lines

                fixed4 texColor = tex2D(_MainTex, i.uv);

                float2 span = max(_BoundsMax - _BoundsMin, 0.0001);
                float2 normalizedPos = saturate((i.objectPos.xy - _BoundsMin) / span);

                float3 topColor = lerp(_ColorR.rgb, _ColorG.rgb, normalizedPos.x);
                float3 bottomColor = lerp(_ColorB.rgb, _ColorW.rgb, normalizedPos.x);
                float3 blendedColor = lerp(topColor, bottomColor, 1.0 - normalizedPos.y);

                float3 positionalColor = float3(normalizedPos.x, normalizedPos.y, 1.0 - normalizedPos.y);
                float3 combinedColor = lerp(blendedColor, positionalColor, 0.45);

                float gridDensity = max(_GridDensity, 1.0);
                float gridThickness = max(_GridLineThickness, 0.002);
                float2 gridPos = normalizedPos * gridDensity;
                float2 gridMod = frac(gridPos);
                float2 gridEdge = min(gridMod, 1.0 - gridMod);
                float gridDistance = min(gridEdge.x, gridEdge.y);
                float gridLineStrength = saturate((gridThickness - gridDistance) / gridThickness);
                float gridMask = pow(gridLineStrength, 2.0);

                fixed3 lineColor = lerp(combinedColor, _GridLineColor.rgb, gridMask);
                fixed3 finalBase = combinedColor * texColor.rgb;
                fixed3 finalColor = saturate(finalBase + lineColor * (gridMask * 0.8));

                return fixed4(finalColor, texColor.a);
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
