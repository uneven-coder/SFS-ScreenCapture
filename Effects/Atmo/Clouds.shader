Shader "Hidden/FrameEmbededState/CloudsShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PlanetRadius ("Planet Radius", Float) = 6371000.0
        _CloudStartHeight ("Cloud Start Height", Float) = 5000.0
        _CloudMaxHeight ("Cloud Max Height", Float) = 15000.0
        _AtmosphereScale ("Atmosphere Scale", Float) = 1.0
        _CloudScale ("Cloud Scale", Float) = 0.00002
        _CloudThreshold ("Cloud Threshold", Range(0.0, 1.0)) = 0.5
        _CloudCoverage ("Cloud Coverage", Range(0.0, 1.0)) = 0.5
        _CloudDensity ("Cloud Density", Float) = 0.3
        _CloudScrollSpeed ("Cloud Scroll Speed", Float) = 50.0
        _CloudDetailIntensity ("Cloud Detail Intensity", Float) = 0.4
        _CloudAlpha ("Cloud Alpha", Float) = 1.0
        _CloudLightAbsorption ("Cloud Light Absorption", Float) = 0.5
        _CloudAmbient ("Cloud Ambient", Float) = 0.5
        _CloudType ("Cloud Type", Float) = 0.0
        _CloudRaymarchSteps ("Cloud Raymarch Steps", Int) = 32
        _CloudLightSteps ("Cloud Light Steps", Int) = 4
        _CloudSoftness ("Cloud Softness", Float) = 0.3
        _CloudMovementDirection ("Cloud Movement Direction", Vector) = (1,0,0,0)
        _CloudRotationSpeed ("Cloud Rotation Speed", Float) = 0.0
        _CloudRotationAxis ("Cloud Rotation Axis", Vector) = (0,0,1,0)
        _CloudMultiScatter ("Cloud Multi-Scatter", Float) = 0.5
        _CloudBloom ("Cloud Bloom", Float) = 0.3
        _CloudDepthFade ("Cloud Depth Fade Distance", Float) = 50000.0
        _CloudDepthFadeSoftness ("Cloud Depth Fade Softness", Float) = 20000.0
        _CloudThresholdVariation ("Cloud Threshold Variation", Float) = 0.15
        _CloudThresholdNoiseScale ("Cloud Threshold Noise Scale", Float) = 0.00008
        _SunDir ("Sun Direction", Vector) = (0,1,0,0)
        _SunColor ("Sun Color", Color) = (1,0.95,0.9,1)
        _PlanetCenterWS ("Planet Center WS", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }
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

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; float3 worldPos : TEXCOORD1; float3 viewRay : TEXCOORD2; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _PlanetRadius, _CloudStartHeight, _CloudMaxHeight, _AtmosphereScale;
            float3 _PlanetCenterWS;
            float _CloudScale, _CloudThreshold, _CloudCoverage, _CloudDensity, _CloudDetailIntensity;
            float _CloudScrollSpeed, _CloudAlpha, _CloudLightAbsorption, _CloudAmbient, _CloudType, _CloudSoftness;
            float4 _CloudMovementDirection, _CloudRotationAxis;
            float _CloudRotationSpeed;
            int _CloudRaymarchSteps, _CloudLightSteps;
            float _CloudMultiScatter, _CloudBloom, _CloudDepthFade, _CloudDepthFadeSoftness;
            float _CloudThresholdVariation, _CloudThresholdNoiseScale;
            float4 _SunDir, _SunColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewRay = o.worldPos - _WorldSpaceCameraPos;
                return o;
            }

            float Hash(float n) { return frac(sin(n) * 43758.5453123); }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453123) * 2.0 - 1.0;
            }

            float GradientNoise3D(float3 p)
            {
                float3 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(dot(Hash3(i), f), dot(Hash3(i + float3(1,0,0)), f - float3(1,0,0)), u.x),
                                 lerp(dot(Hash3(i + float3(0,1,0)), f - float3(0,1,0)), dot(Hash3(i + float3(1,1,0)), f - float3(1,1,0)), u.x), u.y),
                            lerp(lerp(dot(Hash3(i + float3(0,0,1)), f - float3(0,0,1)), dot(Hash3(i + float3(1,0,1)), f - float3(1,0,1)), u.x),
                                 lerp(dot(Hash3(i + float3(0,1,1)), f - float3(0,1,1)), dot(Hash3(i + float3(1,1,1)), f - float3(1,1,1)), u.x), u.y), u.z);
            }

            float Worley3D(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float minDist = 1.0;
                [unroll] for (int x = -1; x <= 1; x++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int z = -1; z <= 1; z++)
                {
                    float3 neighbor = float3(x, y, z), diff = neighbor + Hash3(i + neighbor) * 0.5 + 0.5 - f;
                    minDist = min(minDist, dot(diff, diff));
                }
                return sqrt(minDist);
            }

            float CloudFBM(float3 p, int octaves)
            {
                float value = 0.0, amplitude = 0.5, frequency = 1.0, maxVal = 0.0;
                [loop] for (int i = 0; i < octaves; i++)
                {
                    float grad = GradientNoise3D(p * frequency) * 0.5 + 0.5, worley = 1.0 - Worley3D(p * frequency * 0.8);
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

            float3 RotateAroundAxis(float3 v, float3 axis, float angle)
            {
                float s = sin(angle), c = cos(angle);
                return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
            }

            float3 WorldToVirtual(float3 worldPos, float3 planetCenter, float scaledPlanetRadius)
            {   // Transform to virtual space normalizing by actual scaled planet radius
                float3 rel = worldPos - planetCenter;
                float worldDist = length(rel);
                float3 dir = rel / max(worldDist, 0.001);
                
                float normalizedDist = worldDist / max(scaledPlanetRadius, 1.0);
                return dir * normalizedDist * _PlanetRadius;
            }

            float SampleCloudDensity(float3 virtualPos, float virtualInnerRadius, float virtualOuterRadius, float time, bool cheap)
            {
                float distFromCenter = length(virtualPos);
                float heightNorm = saturate((distFromCenter - virtualInnerRadius) / max(virtualOuterRadius - virtualInnerRadius, 1.0));
                
                float3 axis = normalize(_CloudRotationAxis.xyz + float3(0,0,1e-3));
                float3 rel = virtualPos;
                if (_CloudRotationSpeed != 0.0) rel = RotateAroundAxis(rel, axis, time * _CloudRotationSpeed);
                
                float3 windOffset = normalize(_CloudMovementDirection.xyz + float3(0.001, 0, 0)) * time * _CloudScrollSpeed;
                float3 samplePos = rel * _CloudScale + windOffset * _CloudScale;
                
                float baseNoise = CloudFBM(samplePos, cheap ? 2 : 4);
                baseNoise *= GetCloudHeightGradient(heightNorm, _CloudType);
                
                float dynamicThreshold = _CloudThreshold;
                if (_CloudThresholdVariation > 0.001)
                {
                    float3 thresholdNoisePos = rel * _CloudThresholdNoiseScale;
                    float thresholdNoise = GradientNoise3D(thresholdNoisePos) * 0.5 + 0.5;
                    dynamicThreshold += (thresholdNoise - 0.5) * _CloudThresholdVariation * 2.0;
                    dynamicThreshold = saturate(dynamicThreshold);
                }
                
                float coverageThreshold = 1.0 - _CloudCoverage;
                baseNoise = saturate((baseNoise - coverageThreshold * dynamicThreshold) / max(1.0 - coverageThreshold * dynamicThreshold, 0.001));
                
                if (baseNoise < 0.01 || cheap) return saturate(baseNoise * _CloudDensity);
                
                float3 detailPos = samplePos * 3.5 + windOffset * _CloudScale * 0.3;
                float detail = CloudFBM(detailPos, 2), erosion = detail * _CloudDetailIntensity * (1.0 - heightNorm * 0.3);
                return saturate((baseNoise - erosion * 0.2) * _CloudDensity);
            }

            float LightMarchOptimized(float3 virtualPos, float virtualInnerRadius, float virtualOuterRadius, float time, float3 lightDir)
            {   // Light march in virtual space
                float totalDensity = 0.0, thickness = virtualOuterRadius - virtualInnerRadius, stepSize = thickness / (float)max(_CloudLightSteps, 1);
                [loop] for (int i = 0; i < _CloudLightSteps; i++)
                {
                    float3 samplePos = virtualPos + lightDir * stepSize * (float)(i + 1);
                    float dist = length(samplePos);
                    if (dist < virtualInnerRadius || dist > virtualOuterRadius) break;
                    totalDensity += SampleCloudDensity(samplePos, virtualInnerRadius, virtualOuterRadius, time, true) * stepSize * 0.0008;
                }
                return totalDensity;
            }

            bool RaySphereIntersect(float3 ro, float3 rd, float3 center, float radius, out float tNear, out float tFar)
            {
                tNear = tFar = 0.0;
                float3 oc = ro - center;
                float b = dot(oc, rd), c = dot(oc, oc) - radius * radius, disc = b * b - c;
                if (disc < 0.0) return false;
                float sqrtDisc = sqrt(disc);
                tNear = -b - sqrtDisc;
                tFar = -b + sqrtDisc;
                return tFar > 0.0;
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * 3.14159 * pow(abs(1.0 + g2 - 2.0 * g * cosTheta), 1.5));
            }

            float MultiScatterApprox(float density, float lightDensity)
            {
                float scatter = 1.0 - exp(-density * 2.0);
                return scatter * _CloudMultiScatter * exp(-lightDensity * 0.3);
            }

            struct CloudResult { float3 color; float alpha; float bloom; };

            CloudResult RaymarchClouds(float3 rayOrigin, float3 rayDir, float3 planetCenter, float3 sunDir,
                                        float innerRadius, float outerRadius, float time, float sunAlignment)
            {   // Raymarch with proper virtual space normalization using planet data not magic numbers
                CloudResult result; result.color = float3(0, 0, 0); result.alpha = 0.0; result.bloom = 0.0;
                
                float tNearInner, tFarInner, tNearOuter, tFarOuter;
                if (!RaySphereIntersect(rayOrigin, rayDir, planetCenter, outerRadius, tNearOuter, tFarOuter)) return result;
                
                bool hitInner = RaySphereIntersect(rayOrigin, rayDir, planetCenter, innerRadius, tNearInner, tFarInner);
                float tStart = max(0.0, tNearOuter), tEnd = tFarOuter;
                
                if (hitInner && tNearInner > 0.0) tEnd = min(tEnd, tNearInner);
                else if (hitInner && tFarInner > 0.0) tStart = max(tStart, tFarInner);
                
                if (tStart >= tEnd) return result;
                
                float scaleMultiplier = _AtmosphereScale / 1000000.0;
                float scaledPlanetRadius = _PlanetRadius * scaleMultiplier;
                
                float virtualPlanetRadius = _PlanetRadius;
                float virtualCloudStart = virtualPlanetRadius + _CloudStartHeight;
                float virtualCloudEnd = virtualPlanetRadius + _CloudMaxHeight;
                
                float pathLength = tEnd - tStart;
                int steps = min(_CloudRaymarchSteps, 64);
                float stepSize = pathLength / (float)steps, transmittance = 1.0;
                float cosTheta = dot(rayDir, sunDir);
                float phaseForward = HenyeyGreenstein(cosTheta, 0.6), phaseBack = HenyeyGreenstein(cosTheta, -0.3);
                float phaseVal = lerp(phaseForward, phaseBack, 0.25);
                
                float jitter = Hash(dot(rayOrigin + rayDir * _Time.y, float3(12.9898, 78.233, 45.543))) * stepSize;
                float dayLight = smoothstep(-0.2, 0.3, sunAlignment);
                float3 ambientSky = float3(0.5, 0.6, 0.8) * dayLight * 0.6;
                float3 ambientGround = float3(0.3, 0.35, 0.4) * dayLight * 0.4;
                
                [loop] for (int i = 0; i < steps; i++)
                {
                    if (transmittance < 0.02) break;
                    
                    float t = tStart + jitter + (float)i * stepSize;
                    float3 worldSamplePos = rayOrigin + rayDir * t;
                    float3 virtualSamplePos = WorldToVirtual(worldSamplePos, planetCenter, scaledPlanetRadius);
                    
                    float density = SampleCloudDensity(virtualSamplePos, virtualCloudStart, virtualCloudEnd, time, false);
                    
                    if (density > 0.002)
                    {
                        float depthFade = 1.0 - smoothstep(_CloudDepthFade, _CloudDepthFade + _CloudDepthFadeSoftness, t);
                        density *= depthFade;
                        if (density < 0.001) continue;
                        
                        float lightDensity = LightMarchOptimized(virtualSamplePos, virtualCloudStart, virtualCloudEnd, time, sunDir);
                        float lightTransmit = exp(-lightDensity * _CloudLightAbsorption);
                        
                        float distFromCenter = length(virtualSamplePos);
                        float heightNorm = saturate((distFromCenter - virtualCloudStart) / max(virtualCloudEnd - virtualCloudStart, 1.0));
                        
                        float multiScatter = MultiScatterApprox(density, lightDensity);
                        float beers = exp(-density * stepSize * _CloudAlpha * 0.8);
                        
                        float skyAmbient = _CloudAmbient * (0.6 + 0.4 * heightNorm);
                        float groundAmbient = _CloudAmbient * 0.5 * (1.0 - heightNorm * 0.5) * saturate(sunDir.y + 0.4);
                        
                        float3 sampleNormal = normalize(virtualSamplePos);
                        float bottomLit = saturate(-dot(sampleNormal, sunDir)) * (1.0 - heightNorm) * 0.35 * saturate(1.0 - lightDensity * 0.3);
                        
                        float directLight = (lightTransmit + multiScatter * 0.5) * phaseVal * 1.2;
                        float3 sunLit = _SunColor.rgb * (directLight + bottomLit);
                        
                        float3 ambientLit = ambientSky * skyAmbient + ambientGround * groundAmbient;
                        ambientLit += float3(0.15, 0.17, 0.2) * _CloudAmbient * (1.0 - heightNorm) * 0.5;
                        
                        float edge = pow(saturate(1.0 - density * 1.5), 2.0) * lightTransmit * saturate(cosTheta + 0.4) * 0.25;
                        float3 edgeLight = _SunColor.rgb * edge;
                        
                        float3 sampleColor = sunLit + ambientLit + edgeLight;
                        sampleColor = max(sampleColor, float3(0.08, 0.09, 0.12) * _CloudAmbient);
                        
                        float sampleAlpha = (1.0 - beers) * depthFade;
                        result.color += sampleColor * sampleAlpha * transmittance;
                        result.bloom += edge * sampleAlpha * transmittance * 2.0;
                        transmittance *= beers;
                    }
                }
                
                result.alpha = saturate((1.0 - transmittance) * 0.92);
                return result;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uv);
                
                float3 planetCenter = _PlanetCenterWS;
                float3 sunDir = normalize(_SunDir);
                float3 rayDir = normalize(i.viewRay);
                
                float scaleMultiplier = _AtmosphereScale / 1000000.0;
                float cloudStart = _PlanetRadius * scaleMultiplier + _CloudStartHeight * scaleMultiplier;
                float cloudEnd = _PlanetRadius * scaleMultiplier + _CloudMaxHeight * scaleMultiplier;
                
                float3 toFragment = i.worldPos - planetCenter;
                float3 normalFromCenter = normalize(toFragment);
                float sunAlignment = dot(normalFromCenter, sunDir);
                
                CloudResult cloudResult = RaymarchClouds(i.worldPos, rayDir, planetCenter, sunDir, cloudStart, cloudEnd, _Time.y, sunAlignment);
                
                if (cloudResult.alpha < 0.005) discard;
                
                float3 bloomColor = _SunColor.rgb * cloudResult.bloom * _CloudBloom;
                float3 finalColor = cloudResult.color + bloomColor;
                
                float cloudVisibility = smoothstep(-0.35, 0.15, sunAlignment) * 0.8 + 0.2;
                float finalAlpha = cloudResult.alpha * cloudVisibility * texColor.a;
                
                if (finalAlpha < 0.005) discard;
                
                return fixed4(finalColor * texColor.rgb, finalAlpha);
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
