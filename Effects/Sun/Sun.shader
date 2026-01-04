Shader "Hidden/FrameEmbededState/SunShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SunColor ("Sun Core Color", Color) = (1.0, 0.95, 0.85, 1.0)
        _CoronaColor ("Corona Color", Color) = (1.0, 0.7, 0.4, 1.0)
        _SunIntensity ("Sun Intensity", Float) = 2.5
        _CoronaIntensity ("Corona Intensity", Float) = 1.5
        _CoronaSize ("Corona Size", Float) = 0.3
        _LimbDarkeningStrength ("Limb Darkening Strength", Float) = 0.6
        _GranulationScale ("Granulation Scale", Float) = 25.0
        _GranulationIntensity ("Granulation Intensity", Float) = 0.15
        _FlareIntensity ("Solar Flare Intensity", Float) = 0.3
        _FlareScale ("Solar Flare Scale", Float) = 8.0
        _FlareSpeed ("Solar Flare Speed", Float) = 0.05
        _RotationSpeed ("Rotation Speed", Float) = 0.005
        _AtmosphericGlow ("Atmospheric Glow", Float) = 1.2
        _BloomThreshold ("Bloom Threshold", Float) = 0.8
        _TemperatureVariation ("Temperature Variation", Float) = 0.08
        _Brightness ("Overall Brightness", Float) = 1.0
        _MinBrightness ("Min Brightness (Close)", Float) = 0.3
        _MaxBrightness ("Max Brightness (Far)", Float) = 1.5
        _BrightnessFadeStart ("Brightness Fade Start Distance", Float) = 500000.0
        _BrightnessFadeEnd ("Brightness Fade End Distance", Float) = 5000000.0
        _EdgeSoftness ("Edge Softness", Float) = 0.05
        _SunCenterWS ("Sun Center WS", Vector) = (0,0,0,0)
        _SunRadius ("Sun Radius", Float) = 696000000.0
        _SunScale ("Sun Scale", Float) = 1.0
        _CameraDistanceWS ("Camera Distance WS", Float) = 0.0
        
        _CoronaLayerStart ("Corona Layer Start", Float) = 1.02
        _CoronaLayerEnd ("Corona Layer End", Float) = 1.5
        _CoronaLayerDensity ("Corona Layer Density", Float) = 0.3
        _CoronaLayerSteps ("Corona Layer Steps", Int) = 16
        _CoronaLayerScale ("Corona Layer Scale", Float) = 4.0
        _CoronaLayerSpeed ("Corona Layer Speed", Float) = 0.03
        _CoronaLayerColor ("Corona Layer Color", Color) = (1.0, 0.8, 0.5, 1.0)
        
        _HorizonFadeStart ("Horizon Fade Start", Float) = 1000000.0
        _HorizonFadeEnd ("Horizon Fade End", Float) = 2000000.0
        _HorizonBrightness ("Horizon Brightness", Float) = 0.4
        _HorizonColorShift ("Horizon Color Shift", Color) = (1.0, 0.7, 0.4, 1.0)
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
                float3 objectPos : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _SunColor;
            float4 _CoronaColor;
            float _SunIntensity;
            float _CoronaIntensity;
            float _CoronaSize;
            float _LimbDarkeningStrength;
            float _GranulationScale;
            float _GranulationIntensity;
            float _FlareIntensity;
            float _FlareScale;
            float _FlareSpeed;
            float _RotationSpeed;
            float _AtmosphericGlow;
            float _BloomThreshold;
            float _TemperatureVariation;
            float _Brightness;
            float _MinBrightness;
            float _MaxBrightness;
            float _BrightnessFadeStart;
            float _BrightnessFadeEnd;
            float _EdgeSoftness;
            float3 _SunCenterWS;
            float _SunRadius;
            float _SunScale;
            float _CameraDistanceWS;
            float _CoronaLayerStart;
            float _CoronaLayerEnd;
            float _CoronaLayerDensity;
            int _CoronaLayerSteps;
            float _CoronaLayerScale;
            float _CoronaLayerSpeed;
            float4 _CoronaLayerColor;
            float _HorizonFadeStart;
            float _HorizonFadeEnd;
            float _HorizonBrightness;
            float4 _HorizonColorShift;

            v2f vert(appdata v)
            {   // Transform to clip, world, and preserve object space for radial calculations
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.objectPos = v.vertex.xyz;
                return o;
            }

            float Hash(float n)
            {   // Simple hash for pseudo-random values
                return frac(sin(n) * 43758.5453123);
            }

            float3 Hash3(float3 p)
            {   // 3D hash for noise generation
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), 
                          dot(p, float3(269.5, 183.3, 246.1)), 
                          dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453123) * 2.0 - 1.0;
            }

            float GradientNoise3D(float3 p)
            {   // Smooth 3D gradient noise for surface detail
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                
                return lerp(
                    lerp(lerp(dot(Hash3(i), f), 
                             dot(Hash3(i + float3(1,0,0)), f - float3(1,0,0)), u.x),
                        lerp(dot(Hash3(i + float3(0,1,0)), f - float3(0,1,0)), 
                             dot(Hash3(i + float3(1,1,0)), f - float3(1,1,0)), u.x), u.y),
                    lerp(lerp(dot(Hash3(i + float3(0,0,1)), f - float3(0,0,1)), 
                             dot(Hash3(i + float3(1,0,1)), f - float3(1,0,1)), u.x),
                        lerp(dot(Hash3(i + float3(0,1,1)), f - float3(0,1,1)), 
                             dot(Hash3(i + float3(1,1,1)), f - float3(1,1,1)), u.x), u.y), u.z);
            }

            float Turbulence(float2 p, int octaves)
            {   // Multi-octave 2D noise for surface patterns
                float value = 0.0;
                float amplitude = 1.0;
                float frequency = 1.0;
                float maxVal = 0.0;
                
                [loop] for (int i = 0; i < octaves; i++)
                {
                    float3 p3 = float3(p * frequency, _Time.y * 0.1);
                    value += abs(GradientNoise3D(p3)) * amplitude;
                    maxVal += amplitude;
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }
                return value / maxVal;
            }

            float CalculateLimbDarkening(float distFromCenter)
            {   // Radial limb darkening - sun appears darker at edges
                float cosTheta = sqrt(saturate(1.0 - distFromCenter * distFromCenter));
                float u = _LimbDarkeningStrength;
                float a = 1.0 - u;
                return a + u * cosTheta;
            }

            float SampleGranulation(float3 surfacePos, float time)
            {   // Solar granulation pattern in 3D surface space
                float3 rotAxis = normalize(float3(0, 1, 0));
                float angle = time * _RotationSpeed;
                float3 rotatedPos = surfacePos * cos(angle) + cross(rotAxis, surfacePos) * sin(angle) + rotAxis * dot(rotAxis, surfacePos) * (1.0 - cos(angle));
                float3 samplePos = rotatedPos * _GranulationScale;
                
                float granules = Turbulence(samplePos.xy, 3);
                float3 microPos = float3(samplePos.xy + float2(time * 0.1, 0), samplePos.z);
                float microStructure = GradientNoise3D(microPos * 3.5) * 0.5 + 0.5;
                
                return lerp(granules, microStructure, 0.4);
            }

            float3 SampleSolarFlares(float3 surfacePos, float time, float distFromCenter)
            {   // Animated solar flares in 3D surface space
                float3 flarePos = surfacePos * _FlareScale;
                
                float flareNoise1 = Turbulence(flarePos.xy + float2(0, time * _FlareSpeed), 4);
                float flareNoise2 = Turbulence(flarePos.xy * 1.5 - float2(time * _FlareSpeed * 0.7, 0), 3);
                
                float edgeBias = pow(saturate(distFromCenter), 2.0);
                float flareMask = pow(saturate(flareNoise1 * flareNoise2), 2.5) * edgeBias;
                float3 flareColor = lerp(_CoronaColor.rgb, float3(1.0, 0.9, 0.7), 0.6);
                
                return flareColor * flareMask * _FlareIntensity * 2.0;
            }

            float3 CalculateTemperatureVariation(float3 baseColor, float granulation)
            {   // Temperature-based color variation
                float tempVar = (granulation - 0.5) * _TemperatureVariation;
                float3 coolerColor = baseColor * float3(1.0, 0.85, 0.7);
                float3 hotterColor = baseColor * float3(1.0, 1.0, 0.95);
                
                return lerp(coolerColor, hotterColor, saturate(0.5 + tempVar));
            }

            float3 CalculateCorona(float distFromCenter, float3 surfacePos)
            {   // Solar corona in 3D surface space
                float coronaStart = 1.0 - _CoronaSize;
                float coronaMask = smoothstep(coronaStart, 1.0, distFromCenter);
                float coronaFalloff = 1.0 - saturate((distFromCenter - coronaStart) / _CoronaSize);
                coronaFalloff = pow(coronaFalloff, 2.5);
                
                float coronaTurbulence = Turbulence(surfacePos.xy * 5.0 + float2(_Time.y * 0.02, 0), 3);
                coronaFalloff *= (0.7 + coronaTurbulence * 0.3);
                
                float3 coronaGlow = _CoronaColor.rgb * coronaFalloff * _CoronaIntensity * coronaMask;
                
                return coronaGlow;
            }

            bool RaySphereIntersect(float3 ro, float3 rd, float3 center, float radius, out float tNear, out float tFar)
            {   // Ray-sphere intersection for volumetric layers
                tNear = tFar = 0.0;
                float3 oc = ro - center;
                float b = dot(oc, rd);
                float c = dot(oc, oc) - radius * radius;
                float disc = b * b - c;
                
                if (disc < 0.0)
                    return false;
                
                float sqrtDisc = sqrt(disc);
                tNear = -b - sqrtDisc;
                tFar = -b + sqrtDisc;
                return tFar > 0.0;
            }

            float SampleCoronaDensity(float3 worldPos, float3 sunCenter, float innerRadius, float outerRadius, float time)
            {   // Sample volumetric corona density with turbulent noise
                float3 toPoint = worldPos - sunCenter;
                float dist = length(toPoint);
                float heightNorm = saturate((dist - innerRadius) / max(outerRadius - innerRadius, 1.0));
                
                float3 dir = normalize(toPoint);
                float3 rotAxis = normalize(float3(0, 1, 0.1));
                float angle = time * _CoronaLayerSpeed;
                float3 rotatedDir = dir * cos(angle) + cross(rotAxis, dir) * sin(angle) + rotAxis * dot(rotAxis, dir) * (1.0 - cos(angle));
                
                float3 samplePos = rotatedDir * _CoronaLayerScale;
                float noise = Turbulence(samplePos.xy + float2(time * 0.1, 0), 3);
                
                float gradient = exp(-pow(heightNorm * 2.5, 2.0));
                return noise * gradient * _CoronaLayerDensity;
            }

            float3 RaymarchCoronaLayers(float3 rayOrigin, float3 rayDir, float3 sunCenter, float innerRadius, float outerRadius, float time)
            {   // Raymarch volumetric corona shells around sun
                float tNear, tFar;
                if (!RaySphereIntersect(rayOrigin, rayDir, sunCenter, outerRadius, tNear, tFar))
                    return float3(0, 0, 0);
                
                float tStart = max(0.0, tNear);
                float tEnd = tFar;
                
                float tNearInner, tFarInner;
                bool hitInner = RaySphereIntersect(rayOrigin, rayDir, sunCenter, innerRadius, tNearInner, tFarInner);
                if (hitInner && tNearInner > 0.0)
                    tEnd = min(tEnd, tNearInner);
                else if (hitInner && tFarInner > 0.0)
                    tStart = max(tStart, tFarInner);
                
                if (tStart >= tEnd)
                    return float3(0, 0, 0);
                
                float pathLength = tEnd - tStart;
                int steps = min(_CoronaLayerSteps, 32);
                float stepSize = pathLength / (float)steps;
                
                float3 accumulatedColor = float3(0, 0, 0);
                float transmittance = 1.0;
                
                float jitter = Hash(dot(rayOrigin + rayDir * time, float3(12.9898, 78.233, 45.543))) * stepSize;
                
                [loop] for (int i = 0; i < steps; i++)
                {
                    if (transmittance < 0.01)
                        break;
                    
                    float t = tStart + jitter + (float)i * stepSize;
                    float3 samplePos = rayOrigin + rayDir * t;
                    
                    float density = SampleCoronaDensity(samplePos, sunCenter, innerRadius, outerRadius, time);
                    
                    if (density > 0.001)
                    {
                        float absorption = exp(-density * stepSize * 0.5);
                        float sampleAlpha = (1.0 - absorption);
                        
                        float3 sampleColor = _CoronaLayerColor.rgb * density * 2.0;
                        accumulatedColor += sampleColor * sampleAlpha * transmittance;
                        transmittance *= absorption;
                    }
                }
                
                return accumulatedColor * _CoronaIntensity;
            }

            float CalculateHorizonEffect(float cameraDistance, float normalizedDist, float3 normalFromCenter, float3 viewDir)
            {   // Calculate horizon limb effect for close camera views
                float horizonBlend = 1.0 - smoothstep(_HorizonFadeStart, _HorizonFadeEnd, cameraDistance);
                
                if (horizonBlend < 0.01)
                    return 0.0;
                
                float edgeAlignment = 1.0 - abs(dot(normalFromCenter, viewDir));
                float horizonMask = pow(edgeAlignment, 3.0) * smoothstep(0.7, 1.0, normalizedDist);
                
                return horizonMask * horizonBlend;
            }

            float CalculateDistanceBrightness(float cameraDistance)
            {   // Calculate brightness multiplier based on camera distance preventing blindness when close
                float distanceFactor = saturate((cameraDistance - _BrightnessFadeStart) / 
                                               max(_BrightnessFadeEnd - _BrightnessFadeStart, 1.0));
                return lerp(_MinBrightness, _MaxBrightness, distanceFactor);
            }

            fixed4 frag(v2f i) : SV_Target
            {   // Object-space sun rendering with world-space positioning
                fixed4 texColor = tex2D(_MainTex, i.uv);
                
                float3 sunCenter = _SunCenterWS;
                float3 toFragment = i.worldPos - sunCenter;
                float3 normalFromCenter = normalize(toFragment);
                float distFromCenter = length(toFragment);
                
                float scaledRadius = _SunRadius * (_SunScale / 1000000.0);
                float normalizedDist = distFromCenter / scaledRadius;
                
                float circleMask = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, normalizedDist);
                
                if (circleMask < 0.01) discard;
                
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 finalColor = float3(0, 0, 0);
                float alpha = 0.0;
                
                float surfaceMask = 1.0 - smoothstep(0.7, 1.0, normalizedDist);
                if (surfaceMask > 0.01)
                {   // Render sun surface with granulation and flares
                    float limbDarkening = CalculateLimbDarkening(normalizedDist);
                    
                    float3 surfacePos = normalFromCenter;
                    float granulation = SampleGranulation(surfacePos, _Time.y);
                    float granulationEffect = 1.0 + (granulation - 0.5) * _GranulationIntensity;
                    
                    float3 surfaceColor = CalculateTemperatureVariation(_SunColor.rgb, granulation);
                    surfaceColor *= limbDarkening * granulationEffect * _SunIntensity;
                    
                    float3 flares = SampleSolarFlares(surfacePos, _Time.y, normalizedDist);
                    
                    finalColor = (surfaceColor + flares) * surfaceMask;
                    alpha = surfaceMask;
                    
                    float bloom = saturate((length(finalColor) - _BloomThreshold) / max(1.0 - _BloomThreshold, 0.01));
                    finalColor += finalColor * bloom * 0.5;
                }
                
                if (normalizedDist > 0.7)
                {   // Render corona in outer regions
                    float3 coronaColor = CalculateCorona(normalizedDist, normalFromCenter);
                    finalColor += coronaColor * _AtmosphericGlow * circleMask;
                    alpha = max(alpha, saturate(length(coronaColor) * 0.5) * circleMask);
                }
                
                // Raymarch volumetric corona layers
                float coronaInnerRadius = scaledRadius * _CoronaLayerStart;
                float coronaOuterRadius = scaledRadius * _CoronaLayerEnd;
                float3 coronaLayers = RaymarchCoronaLayers(i.worldPos, viewDir, sunCenter, coronaInnerRadius, coronaOuterRadius, _Time.y);
                
                if (length(coronaLayers) > 0.01)
                {
                    finalColor += coronaLayers * circleMask;
                    alpha = max(alpha, saturate(length(coronaLayers) * 0.3) * circleMask);
                }
                
                // Apply horizon effect for close views
                float horizonEffect = CalculateHorizonEffect(_CameraDistanceWS, normalizedDist, normalFromCenter, viewDir);
                if (horizonEffect > 0.01)
                {
                    float3 horizonColor = finalColor * _HorizonColorShift.rgb * _HorizonBrightness;
                    finalColor = lerp(finalColor, horizonColor, horizonEffect);
                }
                
                // Apply distance-based brightness modulation
                float distanceBrightness = CalculateDistanceBrightness(_CameraDistanceWS);
                float totalBrightness = _Brightness * distanceBrightness;
                
                finalColor *= totalBrightness * texColor.rgb;
                alpha *= texColor.a * circleMask;
                
                if (alpha < 0.01) discard;
                
                return fixed4(finalColor, alpha);
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
