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
        _RayleighStrength ("Rayleigh Strength", Float) = 1.0
        _MieStrength ("Mie Strength", Float) = 0.02
        _MieG ("Mie Anisotropy", Float) = 0.76
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
            float _RayleighStrength;
            float _MieStrength;
            float _MieG;

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
                float expDensity = exp(-normalizedHeight * _DensityCurve);
                return _AtmosphereDensity * saturate(expDensity);
            }

            float3 GetRayleighCoefficients()
            {   // Wavelength-dependent Rayleigh scattering coefficients (wavelength^-4)
                // RGB wavelengths in micrometers: Red=0.65, Green=0.532, Blue=0.473
                float3 wavelengths = float3(0.650, 0.532, 0.473);
                float3 invWavelength4 = 1.0 / pow(wavelengths, 4.0);
                float nSquaredMinusOne = (_RefractiveIndex * _RefractiveIndex - 1.0);
                return invWavelength4 * nSquaredMinusOne * _RayleighStrength * 0.0025;
            }

            float RayleighPhase(float cosTheta)
            {   // Rayleigh phase function for isotropic scattering
                return 0.75 * (1.0 + cosTheta * cosTheta);
            }

            float MiePhase(float cosTheta, float g)
            {   // Henyey-Greenstein phase function for forward scattering
                float g2 = g * g;
                float denom = 1.0 + g2 - 2.0 * g * cosTheta;
                return (1.0 - g2) / (4.0 * 3.14159 * pow(denom, 1.5));
            }

            float3 CalculateOpticalDepth(float normalizedHeight, float pathLength)
            {   // Calculate optical depth for light extinction through atmosphere
                float density = GetAtmosphereDensity(normalizedHeight);
                float3 rayleigh = GetRayleighCoefficients();
                return rayleigh * density * pathLength;
            }

            float3 CalculateTransmittance(float3 opticalDepth)
            {   // Beer-Lambert law for light extinction
                return exp(-opticalDepth);
            }

            float CalculateLimbDarkening(float normalizedHeight, float3 viewDir, float3 normalFromCenter)
            {   // Limb darkening - atmosphere appears thicker at edges
                float edgeFactor = 1.0 - abs(dot(viewDir, normalFromCenter));
                float limbMultiplier = 1.0 + edgeFactor * edgeFactor * 2.0;
                return limbMultiplier;
            }

            float3 CalculateSunsetGradient(float sunAlignment, float normalizedHeight)
            {   // Physically-based sunset colors from extended light path through atmosphere
                float horizonFactor = saturate(1.0 - abs(sunAlignment));
                float pathMultiplier = 1.0 + pow(horizonFactor, 2.0) * 20.0;
                
                float3 rayleigh = GetRayleighCoefficients();
                float opticalPath = pathMultiplier * (1.0 - normalizedHeight * 0.5);
                float3 extinction = exp(-rayleigh * opticalPath * _DensityCurve);
                
                return extinction;
            }

            fixed4 frag(v2f i) : SV_Target
            {   // Render atmosphere with physically-based scattering
                // Height-based gradient with sun-angle lighting applied separately
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
                
                // Sun alignment for lighting calculations only
                float sunAlignment = dot(normalFromCenter, sunDir);
                
                // Gradient samples by HEIGHT (not sun angle) - this is the atmosphere color profile
                float gradientSample = saturate(normalizedHeight * _GradientMultiplier);
                float4 baseColor = tex2D(_GradientTex, float2(gradientSample, 0.5));
                
                // View direction for limb darkening and phase functions
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float cosTheta = dot(viewDir, sunDir);
                
                // Limb darkening effect
                float limbDarkening = CalculateLimbDarkening(normalizedHeight, viewDir, normalFromCenter);
                
                // Phase functions for scattering
                float rayleighPhase = RayleighPhase(cosTheta);
                float miePhase = MiePhase(cosTheta, _MieG);
                float combinedPhase = rayleighPhase * _RayleighStrength + miePhase * _MieStrength;
                
                // Day/night factor - smooth transition but never fully dark
                // Use wider terminator and higher minimum to keep atmosphere visible
                float terminatorSoftness = _TerminatorWidth;
                float dayFactor = smoothstep(-terminatorSoftness * 2.0, terminatorSoftness * 2.0, sunAlignment);
                float minLighting = 0.15;  // Minimum ambient so atmosphere never goes fully dark
                float lightingFactor = lerp(minLighting, 1.0, dayFactor);
                
                // Height-based falloff
                float heightFalloff = pow(1.0 - normalizedHeight, 1.5);
                
                // Rayleigh scattering coefficients for color
                float3 rayleighCoeffs = GetRayleighCoefficients();
                float3 rayleighScatter = rayleighCoeffs * density * heightFalloff;
                
                // Sunset/sunrise color calculation - only near terminator
                float3 sunsetExtinction = CalculateSunsetGradient(sunAlignment, normalizedHeight);
                float terminatorBand = smoothstep(-0.3, 0.0, sunAlignment) * smoothstep(0.4, 0.05, sunAlignment);
                
                // Primary atmosphere color with lighting applied
                float3 baseAtmosphere = baseColor.rgb * density * heightFalloff * limbDarkening;
                baseAtmosphere *= (1.0 + rayleighScatter * combinedPhase * _ScatterStrength * 0.5);
                
                // Apply day/night lighting to base atmosphere
                float3 dayColor = baseAtmosphere * lightingFactor;
                
                // Sunset/terminator enhancement - warm colors at the boundary
                float3 sunsetColor = _SunColor.rgb * sunsetExtinction;
                float3 warmTones = float3(1.0, 0.5, 0.2) * terminatorBand * (1.0 - sunsetExtinction.b);
                float3 terminatorGlow = (sunsetColor + warmTones * 0.6) * density * heightFalloff * _ScatterStrength * terminatorBand;
                
                // Night side subtle blue tint - atmospheric glow from scattered starlight
                float nightEnhance = smoothstep(0.1, -0.4, sunAlignment);
                float3 nightTint = baseColor.rgb * float3(0.2, 0.25, 0.4) * nightEnhance * density * heightFalloff * 0.3;
                
                // Combine all lighting contributions
                float3 atmosphereColor = dayColor + terminatorGlow + nightTint;
                
                // Forward scattering halo around sun (only on day side)
                float sunHalo = pow(saturate(cosTheta), 8.0) * miePhase * _MieStrength;
                atmosphereColor += _SunColor.rgb * sunHalo * density * dayFactor * 0.3;
                
                // Alpha calculation - atmosphere always has some visibility
                float baseAlpha = density * heightFalloff * baseColor.a * limbDarkening;
                float terminatorAlpha = terminatorBand * density * 0.3;
                float atmosphereAlpha = saturate(baseAlpha + terminatorAlpha) * texColor.a;
                
                float3 finalColor = atmosphereColor * texColor.rgb;
                
                return fixed4(finalColor, atmosphereAlpha);
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
