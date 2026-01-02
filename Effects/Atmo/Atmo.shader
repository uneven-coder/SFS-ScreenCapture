Shader "Hidden/FrameEmbededState/AtmoShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _GradientTex ("Gradient Texture", 2D) = "white" {}
        _PlanetRadius ("Planet Radius", Float) = 6371000.0
        _AtmosphereHeight ("Atmosphere Height", Float) = 100000.0
        _GradientMultiplier ("Gradient Multiplier", Float) = 1.0
        _SunDir ("Sun Direction", Vector) = (0,1,0,0)
        _SunColor ("Sun Color", Color) = (1,0.95,0.9,1)
        _AtmosphereColor ("Atmosphere Color", Color) = (0.5,0.7,1.0,1)
        _AtmosphereDensity ("Atmosphere Density", Float) = 1.0
        _DensityCurve ("Density Curve", Float) = 4.0
        _ScatterStrength ("Scatter Strength", Float) = 1.5
        _TerminatorWidth ("Terminator Width", Float) = 0.2
        _GodRayIntensity ("God Ray Intensity", Float) = 0.5
        
        _CloudStartHeight ("Cloud Start Height", Float) = 5000.0
        _CloudMaxHeight ("Cloud Max Height", Float) = 15000.0
        _CloudScale ("Cloud Scale", Float) = 0.00002
        _CloudThreshold ("Cloud Threshold", Range(0.0, 1.0)) = 0.5
        _CloudDensity ("Cloud Density", Float) = 0.3
        _CloudScrollSpeed ("Cloud Scroll Speed", Float) = 50.0
        _CloudDetailIntensity ("Cloud Detail Intensity", Float) = 0.4
        _CloudAlpha ("Cloud Alpha", Float) = 1.0
        _CloudLightAbsorption ("Cloud Light Absorption", Float) = 0.5
        _CloudAmbient ("Cloud Ambient", Float) = 0.5
        _CloudCoverage ("Cloud Coverage", Range(0.0, 1.0)) = 0.5
        _CloudType ("Cloud Type", Float) = 0.0
        _CloudRaymarchSteps ("Cloud Raymarch Steps", Int) = 32
        _CloudLightSteps ("Cloud Light Steps", Int) = 4
        _CloudSoftness ("Cloud Softness", Float) = 0.3
        _CloudMovementDirection ("Cloud Movement Direction", Vector) = (1,0,0,0)
        _CloudMultiScatter ("Cloud Multi-Scatter", Float) = 0.5
        _CloudBloom ("Cloud Bloom", Float) = 0.3
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
                float3 viewRay : TEXCOORD2;
            };

            sampler2D _MainTex;
            sampler2D _GradientTex;
            float4 _MainTex_ST;
            float _PlanetRadius;
            float _AtmosphereHeight;
            float _GradientMultiplier;
            float3 _SunDir;
            float4 _SunColor;
            float4 _AtmosphereColor;
            float _AtmosphereDensity;
            float _DensityCurve;
            float _ScatterStrength;
            float _TerminatorWidth;
            float _GodRayIntensity;
            
            float _CloudStartHeight;
            float _CloudMaxHeight;
            float _CloudScale;
            float _CloudThreshold;
            float _CloudDensity;
            float _CloudScrollSpeed;
            float _CloudDetailIntensity;
            float _CloudAlpha;
            float _CloudLightAbsorption;
            float _CloudAmbient;
            float _CloudCoverage;
            float _CloudType;
            int _CloudRaymarchSteps;
            int _CloudLightSteps;
            float _CloudSoftness;
            float3 _CloudMovementDirection;
            float _CloudMultiScatter;
            float _CloudBloom;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewRay = o.worldPos - _WorldSpaceCameraPos;
                return o;
            }

            float GetAtmosphereDensity(float normalizedHeight)
            {
                float expDensity = exp(-normalizedHeight * _DensityCurve) - exp(-_DensityCurve);
                return _AtmosphereDensity * max(expDensity, 0.0);
            }

            float Hash(float n) { return frac(sin(n) * 43758.5453123); }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453123) * 2.0 - 1.0;
            }

            float GradientNoise3D(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                
                return lerp(lerp(lerp(dot(Hash3(i), f),
                                      dot(Hash3(i + float3(1,0,0)), f - float3(1,0,0)), u.x),
                                 lerp(dot(Hash3(i + float3(0,1,0)), f - float3(0,1,0)),
                                      dot(Hash3(i + float3(1,1,0)), f - float3(1,1,0)), u.x), u.y),
                            lerp(lerp(dot(Hash3(i + float3(0,0,1)), f - float3(0,0,1)),
                                      dot(Hash3(i + float3(1,0,1)), f - float3(1,0,1)), u.x),
                                 lerp(dot(Hash3(i + float3(0,1,1)), f - float3(0,1,1)),
                                      dot(Hash3(i + float3(1,1,1)), f - float3(1,1,1)), u.x), u.y), u.z);
            }

            float Worley3D(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float minDist = 1.0;

                [unroll] for (int x = -1; x <= 1; x++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int z = -1; z <= 1; z++)
                {
                    float3 neighbor = float3(x, y, z);
                    float3 diff = neighbor + Hash3(i + neighbor) * 0.5 + 0.5 - f;
                    minDist = min(minDist, dot(diff, diff));
                }
                return sqrt(minDist);
            }

            float CloudFBM(float3 p, int octaves)
            {
                float value = 0.0, amplitude = 0.5, frequency = 1.0, maxVal = 0.0;
                
                [loop] for (int i = 0; i < octaves; i++)
                {
                    float grad = GradientNoise3D(p * frequency) * 0.5 + 0.5;
                    float worley = 1.0 - Worley3D(p * frequency * 0.8);
                    value += lerp(grad, worley, 0.3) * amplitude;
                    maxVal += amplitude;
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }
                return value / maxVal;
            }

            float GetCloudHeightGradient(float heightNorm, float cloudType)
            {
                float cumulus = saturate(smoothstep(0.0, 0.2, heightNorm) * smoothstep(1.0, 0.4, heightNorm));
                cumulus = pow(cumulus, 0.5);
                float stratus = exp(-pow((heightNorm - 0.35) * 3.5, 2.0));
                float cirrus = exp(-pow((heightNorm - 0.8) * 4.0, 2.0)) * 0.7;
                return lerp(cumulus, lerp(stratus, cirrus, saturate(cloudType - 1.0)), saturate(cloudType));
            }

            float SampleCloudDensity(float3 worldPos, float3 planetCenter, float innerRadius, float outerRadius, float time, bool cheap)
            {
                float distFromCenter = length(worldPos - planetCenter);
                float heightNorm = saturate((distFromCenter - innerRadius) / max(outerRadius - innerRadius, 1.0));
                
                float3 windOffset = normalize(_CloudMovementDirection + float3(0.001, 0, 0)) * time * _CloudScrollSpeed;
                float3 samplePos = (worldPos - planetCenter) * _CloudScale + windOffset * _CloudScale;
                
                float baseNoise = CloudFBM(samplePos, cheap ? 2 : 4);
                baseNoise *= GetCloudHeightGradient(heightNorm, _CloudType);
                
                float coverageThreshold = 1.0 - _CloudCoverage;
                baseNoise = saturate((baseNoise - coverageThreshold * _CloudThreshold) / max(1.0 - coverageThreshold * _CloudThreshold, 0.001));
                
                if (baseNoise < 0.01 || cheap)
                    return saturate(baseNoise * _CloudDensity);
                
                float3 detailPos = samplePos * 3.5 + windOffset * _CloudScale * 0.3;
                float detail = CloudFBM(detailPos, 2);
                float erosion = detail * _CloudDetailIntensity * (1.0 - heightNorm * 0.3);
                
                return saturate((baseNoise - erosion * 0.2) * _CloudDensity);
            }

            float LightMarchOptimized(float3 pos, float3 planetCenter, float innerRadius, float outerRadius, float time, float3 lightDir)
            {
                float totalDensity = 0.0;
                float thickness = outerRadius - innerRadius;
                float stepSize = thickness / (float)max(_CloudLightSteps, 1);
                
                [loop] for (int i = 0; i < _CloudLightSteps; i++)
                {
                    float3 samplePos = pos + lightDir * stepSize * (float)(i + 1);
                    float dist = length(samplePos - planetCenter);
                    if (dist < innerRadius || dist > outerRadius) break;
                    totalDensity += SampleCloudDensity(samplePos, planetCenter, innerRadius, outerRadius, time, true) * stepSize * 0.0008;
                }
                return totalDensity;
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * 3.14159 * pow(abs(1.0 + g2 - 2.0 * g * cosTheta), 1.5));
            }

            float MultiScatterApprox(float density, float lightDensity)
            {   // Approximate multiple scattering to brighten thick cloud interiors
                float scatter = 1.0 - exp(-density * 2.0);
                float multiScatter = scatter * _CloudMultiScatter * exp(-lightDensity * 0.3);
                return multiScatter;
            }

            bool RaySphereIntersect(float3 ro, float3 rd, float3 center, float radius, out float tNear, out float tFar)
            {
                tNear = tFar = 0.0;
                float3 oc = ro - center;
                float b = dot(oc, rd);
                float c = dot(oc, oc) - radius * radius;
                float disc = b * b - c;
                if (disc < 0.0) return false;
                float sqrtDisc = sqrt(disc);
                tNear = -b - sqrtDisc;
                tFar = -b + sqrtDisc;
                return tFar > 0.0;
            }

            struct CloudResult
            {
                float3 color;
                float alpha;
                float bloom;
                float depth;
            };

            CloudResult RaymarchClouds(float3 rayOrigin, float3 rayDir, float3 planetCenter, float3 sunDir,
                                        float innerRadius, float outerRadius, float time,
                                        float4 gradientColor, float3 scatterColor, float sunAlignment)
            {
                CloudResult result;
                result.color = float3(0, 0, 0);
                result.alpha = 0.0;
                result.bloom = 0.0;
                result.depth = 100000.0;
                
                float tNearInner, tFarInner, tNearOuter, tFarOuter;
                
                if (!RaySphereIntersect(rayOrigin, rayDir, planetCenter, outerRadius, tNearOuter, tFarOuter))
                    return result;
                
                bool hitInner = RaySphereIntersect(rayOrigin, rayDir, planetCenter, innerRadius, tNearInner, tFarInner);
                
                float tStart = max(0.0, tNearOuter);
                float tEnd = tFarOuter;
                
                if (hitInner && tNearInner > 0.0)
                    tEnd = min(tEnd, tNearInner);
                else if (hitInner && tFarInner > 0.0)
                    tStart = max(tStart, tFarInner);
                
                if (tStart >= tEnd) return result;
                
                float pathLength = tEnd - tStart;
                int steps = min(_CloudRaymarchSteps, 64);
                float stepSize = pathLength / (float)steps;
                
                float transmittance = 1.0;
                float cosTheta = dot(rayDir, sunDir);
                float phaseForward = HenyeyGreenstein(cosTheta, 0.6);
                float phaseBack = HenyeyGreenstein(cosTheta, -0.3);
                float phaseVal = lerp(phaseForward, phaseBack, 0.25);
                
                float jitter = Hash(dot(rayOrigin + rayDir * _Time.y, float3(12.9898, 78.233, 45.543))) * stepSize;
                
                float3 upDir = normalize(rayOrigin - planetCenter);
                float viewFromBelow = saturate(dot(rayDir, upDir) * 0.5 + 0.5);
                
                float dayLight = smoothstep(-0.2, 0.3, sunAlignment);
                float3 ambientSky = lerp(gradientColor.rgb * 0.4, float3(0.6, 0.7, 0.9), 0.5) * dayLight;
                float3 ambientGround = gradientColor.rgb * 0.3 * dayLight;
                
                bool firstHit = true;
                
                [loop] for (int i = 0; i < steps; i++)
                {
                    if (transmittance < 0.02) break;
                    
                    float t = tStart + jitter + (float)i * stepSize;
                    float3 samplePos = rayOrigin + rayDir * t;
                    
                    float density = SampleCloudDensity(samplePos, planetCenter, innerRadius, outerRadius, time, false);
                    
                    if (density > 0.002)
                    {
                        if (firstHit) { result.depth = t; firstHit = false; }
                        
                        float lightDensity = LightMarchOptimized(samplePos, planetCenter, innerRadius, outerRadius, time, sunDir);
                        float lightTransmit = exp(-lightDensity * _CloudLightAbsorption);
                        
                        float distFromCenter = length(samplePos - planetCenter);
                        float heightNorm = saturate((distFromCenter - innerRadius) / max(outerRadius - innerRadius, 1.0));
                        
                        float multiScatter = MultiScatterApprox(density, lightDensity);
                        
                        float powder = 1.0 - exp(-density * 1.2);
                        float beers = exp(-density * stepSize * _CloudAlpha * 0.8);
                        
                        float skyAmbient = _CloudAmbient * (0.6 + 0.4 * heightNorm);
                        float groundAmbient = _CloudAmbient * 0.5 * (1.0 - heightNorm * 0.5) * saturate(sunDir.y + 0.4);
                        
                        float3 sampleNormal = normalize(samplePos - planetCenter);
                        float bottomLit = saturate(-dot(sampleNormal, sunDir)) * (1.0 - heightNorm) * 0.35;
                        bottomLit *= saturate(1.0 - lightDensity * 0.3);
                        
                        float directLight = (lightTransmit + multiScatter * 0.5) * phaseVal * 1.2;
                        float3 sunLit = _SunColor.rgb * (directLight + bottomLit);
                        
                        float3 ambientLit = ambientSky * skyAmbient + ambientGround * groundAmbient;
                        ambientLit += float3(0.15, 0.17, 0.2) * _CloudAmbient * (1.0 - heightNorm) * 0.5;
                        
                        float3 scatterLit = scatterColor * powder * (directLight + multiScatter) * 0.15;
                        
                        float edge = pow(saturate(1.0 - density * 1.5), 2.0) * lightTransmit * saturate(cosTheta + 0.4) * 0.25;
                        float3 edgeLight = _SunColor.rgb * edge;
                        
                        float3 sampleColor = (sunLit + ambientLit + scatterLit + edgeLight);
                        sampleColor = max(sampleColor, float3(0.08, 0.09, 0.12) * _CloudAmbient);
                        
                        float sampleAlpha = 1.0 - beers;
                        result.color += sampleColor * sampleAlpha * transmittance;
                        result.bloom += edge * sampleAlpha * transmittance * 2.0;
                        transmittance *= beers;
                    }
                }
                
                result.alpha = saturate((1.0 - transmittance) * 0.92);
                return result;
            }

            float3 ApplyCloudPostEffects(float3 cloudColor, float bloom, float depth, float3 atmoColor, float3 sunDir, float3 viewDir)
            {   // Apply bloom glow and atmospheric perspective to cloud result
                float3 bloomColor = _SunColor.rgb * bloom * _CloudBloom;
                cloudColor += bloomColor;
                
                float atmoFade = 1.0 - exp(-depth * 0.000002);
                cloudColor = lerp(cloudColor, atmoColor * 0.5 + cloudColor * 0.5, atmoFade * 0.3);
                
                float sunGlow = pow(saturate(dot(viewDir, sunDir)), 8.0) * bloom * 0.2;
                cloudColor += _SunColor.rgb * sunGlow;
                
                return cloudColor;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uv);
                
                float3 planetCenter = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 sunDir = normalize(_SunDir);
                float3 toFragment = i.worldPos - planetCenter;
                float3 normalFromCenter = normalize(toFragment);
                float distFromCenter = length(toFragment);
                
                float radialU = ((distFromCenter / _AtmosphereHeight) - (_PlanetRadius / _AtmosphereHeight));
                radialU = saturate(radialU / max(_GradientMultiplier, 0.001));
                
                float4 gradientColor = tex2D(_GradientTex, float2(radialU, 0.5));
                
                float height = distFromCenter - _PlanetRadius;
                float normalizedHeight = saturate(height / _AtmosphereHeight);
                float density = GetAtmosphereDensity(normalizedHeight);
                float heightFalloff = 1.0 - normalizedHeight;
                
                float sunAlignment = dot(normalFromCenter, sunDir);
                float dayFactor = smoothstep(-_TerminatorWidth, _TerminatorWidth * 2.0, sunAlignment);
                
                float terminatorProximity = 1.0 - abs(sunAlignment);
                float terminatorGlow = pow(terminatorProximity, 2.0) * smoothstep(-0.4, 0.1, sunAlignment);
                
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float viewDotNormal = abs(dot(viewDir, normalFromCenter));
                float limbDarkening = pow(1.0 - viewDotNormal, 1.5);
                
                float3 dayColor = gradientColor.rgb * density * dayFactor;
                
                float scatterMix = pow(1.0 - sunAlignment * 0.5 - 0.5, 2.0);
                float3 scatterColor = lerp(_SunColor.rgb, _SunColor.rgb * _AtmosphereColor.rgb, scatterMix * density);
                
                float scatterIntensity = terminatorGlow * _ScatterStrength * density * (1.0 + limbDarkening);
                float3 terminatorScatter = scatterColor * scatterIntensity;
                
                float nightFactor = smoothstep(0.0, -_TerminatorWidth * 3.0, sunAlignment);
                float3 nightTint = gradientColor.rgb * float3(0.6, 0.7, 1.0);
                float nightIntensity = nightFactor * density * 0.15 * heightFalloff;
                float3 nightGlow = nightTint * nightIntensity;
                
                float viewSunAlignment = dot(viewDir, sunDir);
                float godRayFactor = pow(saturate(viewSunAlignment), 4.0);
                float godRayMask = smoothstep(0.3, 0.8, dayFactor) * density * heightFalloff;
                float3 godRays = scatterColor * godRayFactor * godRayMask * _GodRayIntensity;
                
                float3 atmosphereColor = dayColor + terminatorScatter + nightGlow + godRays;
                
                float baseAlpha = dayFactor * density * heightFalloff * gradientColor.a;
                float terminatorAlpha = terminatorGlow * density * 0.5;
                float nightAlpha = nightIntensity * 0.8;
                float godRayAlpha = godRayFactor * godRayMask * 0.3;
                float atmosphereAlpha = saturate(baseAlpha + terminatorAlpha + nightAlpha + godRayAlpha) * texColor.a;
                
                float3 finalColor = atmosphereColor;
                float alpha = atmosphereAlpha;
                
                // Cloud rendering with absolute heights in object space
                float cloudStart = _PlanetRadius + _CloudStartHeight;
                float cloudEnd = _PlanetRadius + _CloudMaxHeight;
                float cloudThickness = cloudEnd - cloudStart;
                
                if (cloudThickness > 0.0 && _CloudRaymarchSteps > 0 && _CloudStartHeight > 0.0)
                {   // Raymarch from atmosphere fragment inward along view ray
                    float3 rayDir = -viewDir;
                    float3 rayOrigin = i.worldPos;
                    
                    CloudResult cloudResult = RaymarchClouds(rayOrigin, rayDir, planetCenter, sunDir,
                                                              cloudStart, cloudEnd, _Time.y,
                                                              gradientColor, scatterColor, sunAlignment);
                    
                    if (cloudResult.alpha > 0.002)
                    {
                        float3 postProcessedClouds = ApplyCloudPostEffects(cloudResult.color, cloudResult.bloom, 
                                                                            cloudResult.depth, atmosphereColor, sunDir, viewDir);
                        
                        float cloudVisibility = smoothstep(-0.35, 0.15, sunAlignment) * 0.8 + 0.2;
                        float cloudAlphaFinal = cloudResult.alpha * cloudVisibility;
                        
                        finalColor = lerp(finalColor, postProcessedClouds + finalColor * 0.15, cloudAlphaFinal);
                        alpha = saturate(alpha + cloudAlphaFinal * 0.75);
                    }
                }
                
                finalColor *= texColor.rgb;
                
                return fixed4(finalColor, alpha);
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
