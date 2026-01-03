Shader "Hidden/FrameEmbededState/AtmoShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _GradientTex ("Gradient Texture", 2D) = "white" {}
        _PlanetRadius ("Planet Radius", Float) = 6371000.0
        _AtmosphereHeight ("Atmosphere Height", Float) = 100000.0
        _GradientMultiplier ("Gradient Multiplier", Float) = 1.0
        _AtmosphereScale ("Atmosphere Scale", Float) = 1.0
        _PlanetCenterWS ("Planet Center WS", Vector) = (0,0,0,0)
        _SunDir ("Sun Direction", Vector) = (0,1,0,0)
        _SunColor ("Sun Color", Color) = (1,0.95,0.9,1)
        _AtmosphereDensity ("Atmosphere Density", Float) = 1.0
        _DensityCurve ("Density Curve", Float) = 4.0
        _ScatterStrength ("Scatter Strength", Float) = 1.5
        _TerminatorWidth ("Terminator Width", Float) = 0.2
        _RefractiveIndex ("Refractive Index", Float) = 1.0003
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
            #pragma target 4.0
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
                float3 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            sampler2D _GradientTex;
            float4 _MainTex_ST;
            float _PlanetRadius;
            float _AtmosphereHeight;
            float _GradientMultiplier;
            float _AtmosphereScale;
            float3 _PlanetCenterWS;
            float3 _SunDir;
            float4 _SunColor;
            float _AtmosphereDensity;
            float _DensityCurve;
            float _ScatterStrength;
            float _TerminatorWidth;
            float _RefractiveIndex;

            v2f vert(appdata v)
            {   // Transform vertex to clip space and world space for radial calculations
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float GetAtmosphereDensity(float normalizedHeight)
            {   // Exponential density falloff with configurable curve
                float expDensity = exp(-normalizedHeight * _DensityCurve) - exp(-_DensityCurve);
                return _AtmosphereDensity * max(expDensity, 0.0);
            }

            float3 CalculateRayleighExtinction(float opticalDepth)
            {   // Wavelength-dependent extinction using Rayleigh scattering (wavelength^-4)
                float3 wavelengths = float3(0.650, 0.532, 0.473);
                float3 invWavelength4 = 1.0 / (wavelengths * wavelengths * wavelengths * wavelengths);
                float nMinusOne = _RefractiveIndex - 1.0;
                return invWavelength4 * nMinusOne * nMinusOne * _DensityCurve * 0.5;
            }

            float3 CalculateSunsetColor(float3 atmoColor, float3 sunColor, float sunAlignment, float density, float heightNorm)
            {   // Physically-based sunset color by filtering sunlight through atmosphere path
                float horizonAngle = saturate(1.0 - abs(sunAlignment));
                float pathMultiplier = 1.0 + horizonAngle * horizonAngle * 15.0;
                pathMultiplier *= (1.0 - heightNorm * 0.7);
                
                float baseDensity = GetAtmosphereDensity(heightNorm);
                float opticalDepth = baseDensity * pathMultiplier * _DensityCurve;
                float3 extinction = CalculateRayleighExtinction(opticalDepth);
                float3 transmittance = exp(-extinction * opticalDepth);
                float3 filteredSun = sunColor.rgb * transmittance;
                
                float phase = 0.75 * (1.0 + sunAlignment * sunAlignment);
                float3 scatteredLight = atmoColor.rgb * (1.0 - transmittance) * phase * 0.5;
                float terminatorBand = smoothstep(-0.15, 0.1, sunAlignment) * smoothstep(0.4, 0.05, sunAlignment);
                float3 sunsetResult = lerp(filteredSun, filteredSun + scatteredLight, saturate(density));
                float3 warmBoost = float3(1.0, 0.6, 0.3) * terminatorBand * (1.0 - transmittance.b) * sunColor.rgb;
                
                return sunsetResult + warmBoost * 0.8;
            }

            fixed4 frag(v2f i) : SV_Target
            {   // Render flat 2D atmosphere with radial height-based coloring using bounds scale
                fixed4 texColor = tex2D(_MainTex, i.uv);
                
                float3 planetCenter = _PlanetCenterWS;
                float3 sunDir = normalize(_SunDir);
                float3 toFragment = i.worldPos - planetCenter;
                float3 normalFromCenter = normalize(toFragment);
                float distFromCenter = length(toFragment);
                
                float scaledRadius = _PlanetRadius * (_AtmosphereScale / 1000000.0);
                float scaledAtmoHeight = _AtmosphereHeight * (_AtmosphereScale / 1000000.0);
                
                float height = distFromCenter - scaledRadius;
                float normalizedHeight = saturate(height / scaledAtmoHeight);
                float density = GetAtmosphereDensity(normalizedHeight);
                float heightFalloff = 1.0 - normalizedHeight;
                
                float sunAlignment = dot(normalFromCenter, sunDir);
                float dayFactor = smoothstep(-_TerminatorWidth, _TerminatorWidth * 2.0, sunAlignment);
                
                float terminatorProximity = 1.0 - abs(sunAlignment);
                float terminatorGlow = pow(terminatorProximity, 2.0) * smoothstep(-0.4, 0.1, sunAlignment);
                
                float gradientSample = saturate(normalizedHeight * _GradientMultiplier);
                float4 heightColor = tex2D(_GradientTex, float2(gradientSample, 0.5));
                
                float3 dayColor = heightColor.rgb * density * dayFactor * heightFalloff;
                
                float3 sunsetColor = CalculateSunsetColor(heightColor.rgb, _SunColor.rgb, sunAlignment, density, normalizedHeight);
                float3 terminatorScatter = sunsetColor * terminatorGlow * _ScatterStrength;
                
                float nightFactor = smoothstep(0.0, -_TerminatorWidth * 3.0, sunAlignment);
                float3 nightTint = heightColor.rgb * float3(0.3, 0.4, 0.6);
                float nightIntensity = nightFactor * density * 0.08 * heightFalloff;
                float3 nightGlow = nightTint * nightIntensity;
                
                float3 atmosphereColor = dayColor + terminatorScatter + nightGlow;
                
                float baseAlpha = dayFactor * density * heightFalloff * heightColor.a;
                float terminatorAlpha = terminatorGlow * density * 0.4;
                float nightAlpha = nightIntensity * 0.6;
                float atmosphereAlpha = saturate(baseAlpha + terminatorAlpha + nightAlpha) * texColor.a;
                
                float3 finalColor = atmosphereColor * texColor.rgb;
                
                return fixed4(finalColor, atmosphereAlpha);
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
