Shader "Hidden/FrameEmbededState/BlackHoleShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Skybox ("Skybox", Cube) = "black" {}
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        
        _BlackHoleCenterWS ("Black Hole Center WS", Vector) = (0,0,0,0)
        _BlackHoleRadius ("Black Hole Radius", Float) = 10000000.0
        _SchwarzschildRadius ("Schwarzschild Radius", Float) = 0.7
        
        _AccretionInnerRadius ("Accretion Inner Radius", Float) = 1.75
        _AccretionOuterRadius ("Accretion Outer Radius", Float) = 5.0
        _AccretionHeight ("Accretion Height", Float) = 0.1
        _AccretionFade ("Accretion Fade Distance", Float) = 0.15
        _AccretionDensity ("Accretion Density", Float) = 10.0
        _AccretionSpeed ("Accretion Speed", Float) = 0.2
        _AccretionTurbulence ("Accretion Turbulence", Float) = 0.4
        
        _AccretionColorHot ("Accretion Color Hot", Color) = (3.5, 2.0, 0.5, 1.0)
        _AccretionColorCool ("Accretion Color Cool", Color) = (1.0, 0.3, 0.1, 1.0)
        _AccretionEmission ("Accretion Emission", Float) = 2.0
        
        _JetLength ("Jet Length", Float) = 15.0
        _JetWidth ("Jet Width", Float) = 0.8
        _JetDensity ("Jet Density", Float) = 0.6
        _JetSpeed ("Jet Speed", Float) = 2.0
        _JetTurbulence ("Jet Turbulence", Float) = 0.5
        _JetColor ("Jet Color", Color) = (0.3, 0.6, 1.5, 1.0)
        _JetCoreColor ("Jet Core Color", Color) = (1.2, 1.5, 2.0, 1.0)
        _JetEmission ("Jet Emission", Float) = 3.0
        
        _MagneticFieldStrength ("Magnetic Field Strength", Float) = 0.3
        _MagneticFieldColor ("Magnetic Field Color", Color) = (0.2, 0.4, 0.8, 1.0)
        _MagneticFieldDensity ("Magnetic Field Density", Float) = 8.0
        
        _EnergyRibbonIntensity ("Energy Ribbon Intensity", Float) = 0.5
        _EnergyRibbonColor ("Energy Ribbon Color", Color) = (0.8, 0.3, 1.2, 1.0)
        _EnergyRibbonSpeed ("Energy Ribbon Speed", Float) = 1.5
        
        _RotationAxis ("Rotation Axis", Vector) = (0,1,0,0)
        _RotationSpeed ("Rotation Speed", Float) = 0.05
        _ViewRotation ("View Rotation", Vector) = (0,0,0,0)
        
        _LightBendingStrength ("Light Bending Strength", Float) = 1.5
        _LensingFalloff ("Lensing Falloff", Float) = 2.0
        
        _RaymarchSteps ("Raymarch Steps", Int) = 200
        _InitialStepSize ("Initial Step Size", Float) = 0.05
        _StepGrowth ("Step Growth Factor", Float) = 1.005
        _TransmissionThreshold ("Transmission Threshold", Float) = 0.005
        
        _BoundsMin ("Bounds Min", Vector) = (-1,-1,0,0)
        _BoundsMax ("Bounds Max", Vector) = (1,1,0,0)
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 200
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
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; float3 worldPos : TEXCOORD1; float3 objectPos : TEXCOORD2; float3 viewDir : TEXCOORD3; };

            sampler2D _MainTex, _NoiseTex;
            samplerCUBE _Skybox;
            float4 _MainTex_ST;
            
            float3 _BlackHoleCenterWS;
            float _BlackHoleRadius, _SchwarzschildRadius;
            float2 _BoundsMin, _BoundsMax;
            
            float _AccretionInnerRadius, _AccretionOuterRadius, _AccretionHeight, _AccretionFade;
            float _AccretionDensity, _AccretionSpeed, _AccretionEmission, _AccretionTurbulence;
            float4 _AccretionColorHot, _AccretionColorCool;
            
            float _JetLength, _JetWidth, _JetDensity, _JetSpeed, _JetTurbulence, _JetEmission;
            float4 _JetColor, _JetCoreColor;
            
            float _MagneticFieldStrength, _MagneticFieldDensity;
            float4 _MagneticFieldColor;
            
            float _EnergyRibbonIntensity, _EnergyRibbonSpeed;
            float4 _EnergyRibbonColor;
            
            float3 _RotationAxis, _ViewRotation;
            float _RotationSpeed;
            
            float _LightBendingStrength, _LensingFalloff;
            
            int _RaymarchSteps;
            float _InitialStepSize, _StepGrowth, _TransmissionThreshold;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.objectPos = v.vertex.xyz;
                o.viewDir = normalize(o.worldPos - _WorldSpaceCameraPos);
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

            float FBM(float3 p, int octaves)
            {
                float value = 0.0, amplitude = 0.5, frequency = 1.0;
                [loop] for (int i = 0; i < octaves; i++)
                {
                    value += amplitude * (GradientNoise3D(p * frequency) * 0.5 + 0.5);
                    frequency *= 2.0;
                    amplitude *= 0.5;
                }
                return value;
            }

            float3 RotateAroundAxis(float3 p, float3 axis, float angle)
            {
                float3 a = normalize(axis);
                float c = cos(angle), s = sin(angle);
                return p * c + cross(a, p) * s + a * dot(a, p) * (1.0 - c);
            }

            float2 ObjectToLocalSpace2D(float3 objectPos)
            {
                float2 span = max(_BoundsMax - _BoundsMin, 0.0001);
                float2 normalizedPos = (objectPos.xy - _BoundsMin) / span;
                return (normalizedPos - 0.5) * 2.0;
            }

            float SamplePolarJetDensity(float3 localPos, float time)
            {   // Sample jet particle density extending from poles with turbulence and velocity falloff
                float3 axis = float3(0, 0, 1);
                float distFromAxis = length(localPos.xy);
                float heightAlongAxis = abs(localPos.z);
                
                if (heightAlongAxis < _SchwarzschildRadius * 1.2 || heightAlongAxis > _JetLength) return 0.0;
                if (distFromAxis > _JetWidth * (1.0 + heightAlongAxis * 0.15)) return 0.0;
                
                float heightNorm = saturate((heightAlongAxis - _SchwarzschildRadius * 1.2) / max(_JetLength - _SchwarzschildRadius * 1.2, 0.1));
                
                float3 flowPos = localPos + float3(0, 0, sign(localPos.z) * time * _JetSpeed);
                float3 turbulencePos = flowPos * 1.5;
                
                float turbulence = FBM(turbulencePos, 3) * _JetTurbulence;
                float worley = 1.0 - Worley3D(flowPos * 0.8 + turbulence * 0.3);
                
                float coreDist = distFromAxis / max(_JetWidth * 0.3, 0.01);
                float coreIntensity = exp(-coreDist * coreDist * 4.0);
                
                float edgeDist = distFromAxis / max(_JetWidth * (1.0 + heightAlongAxis * 0.15), 0.01);
                float edgeFalloff = 1.0 - smoothstep(0.7, 1.0, edgeDist);
                
                float baseDensity = worley * edgeFalloff * (0.3 + coreIntensity * 0.7);
                
                float velocityFalloff = exp(-heightNorm * 2.0);
                float baseAttenuation = 1.0 - smoothstep(0.0, 0.15, heightNorm);
                
                return saturate(baseDensity * velocityFalloff * (baseAttenuation * 0.3 + 0.7) * _JetDensity);
            }

            float3 SampleJetColor(float3 localPos, float density, float coreProximity)
            {   // Jet color mixing core brightness with outer cooler regions
                float coreMix = pow(coreProximity, 2.0);
                float3 color = lerp(_JetColor.rgb, _JetCoreColor.rgb, coreMix);
                
                float heightAlongAxis = abs(localPos.z);
                float heightNorm = saturate((heightAlongAxis - _SchwarzschildRadius * 1.2) / max(_JetLength, 1.0));
                float energyFalloff = 1.0 - smoothstep(0.3, 1.0, heightNorm);
                
                return color * (0.5 + energyFalloff * 1.5) * _JetEmission;
            }

            float SampleMagneticField(float3 localPos, float time)
            {   // Magnetic field lines curving from poles around black hole following dipole pattern
                float r = length(localPos);
                if (r < _SchwarzschildRadius * 1.1 || r > _AccretionOuterRadius * 1.5) return 0.0;
                
                float3 dir = normalize(localPos);
                float theta = acos(dir.z);
                float phi = atan2(dir.y, dir.x);
                
                float fieldLines = abs(sin(theta * _MagneticFieldDensity + phi * 2.0 + time * 0.1));
                fieldLines = pow(fieldLines, 8.0);
                
                float dipoleStrength = abs(cos(theta)) * sin(theta * 2.0);
                
                float radialFalloff = exp(-pow((r - _AccretionOuterRadius * 0.7) * 0.8, 2.0));
                
                return fieldLines * dipoleStrength * radialFalloff * _MagneticFieldStrength;
            }

            float SampleEnergyRibbons(float3 localPos, float time)
            {   // Spiral energy ribbons in jets showing magnetic acceleration
                float heightAlongAxis = abs(localPos.z);
                if (heightAlongAxis < _SchwarzschildRadius * 1.5 || heightAlongAxis > _JetLength * 0.8) return 0.0;
                
                float distFromAxis = length(localPos.xy);
                float angle = atan2(localPos.y, localPos.x);
                
                float spiral = angle + heightAlongAxis * 2.0 - time * _EnergyRibbonSpeed;
                float ribbonPattern = abs(sin(spiral * 3.0));
                ribbonPattern = pow(ribbonPattern, 6.0);
                
                float radiusMask = exp(-pow((distFromAxis - _JetWidth * 0.4) * 3.0, 2.0));
                float heightMask = smoothstep(_SchwarzschildRadius * 1.5, _SchwarzschildRadius * 2.0, heightAlongAxis) * 
                                   smoothstep(_JetLength * 0.8, _JetLength * 0.5, heightAlongAxis);
                
                return ribbonPattern * radiusMask * heightMask * _EnergyRibbonIntensity;
            }

            float AccretionDiskDensity(float3 localPos, float time)
            {   // Enhanced accretion disk with turbulence and vortex structures
                float l = length(localPos.xz);
                float ang = atan2(localPos.z, localPos.x);
                float y = localPos.y;
                
                if (l < _AccretionInnerRadius || l > _AccretionOuterRadius) return 0.0;
                
                float2 noiseUV = float2(0.5 * ang / 3.14159 + time * _AccretionSpeed, log(l) * 1.5);
                float n = tex2Dlod(_NoiseTex, float4(noiseUV, 0, 0)).r;
                
                float3 turbPos = localPos * 3.0 + float3(time * _AccretionSpeed * 0.5, 0, 0);
                float turbulence = FBM(turbPos, 2) * _AccretionTurbulence;
                
                float vortexDist = frac(ang / (3.14159 * 0.5) + l * 0.5 - time * 0.3);
                float vortex = exp(-pow((vortexDist - 0.5) * 6.0, 2.0)) * smoothstep(_AccretionInnerRadius * 1.2, _AccretionOuterRadius * 0.8, l);
                
                float radialFalloff = pow(max(1.0 - l / _AccretionOuterRadius, 0.0) * 
                                         clamp((l - _AccretionInnerRadius) / _AccretionFade + 1.0, 0.0, 1.0), 1.5);
                
                float heightFalloff = exp(-y * y * 400.0);
                float noiseMod = (n + max(0.0, n - 0.65) * 1.5) * 1.3;
                
                return radialFalloff * heightFalloff * _AccretionDensity * (noiseMod * (1.0 - turbulence * 0.3) + vortex * 0.5);
            }

            float BlackHoleDensity(float r) { return 10.0 * clamp((_SchwarzschildRadius - r) / 0.5 + 1.0, 0.0, 1.0); }

            float3 AccretionColor(float r, float density, float vortexInfluence)
            {   // Temperature gradient with hotspots from vortices
                float temp = 1.0 / max(r, 0.1);
                float3 hotColor = _AccretionColorHot.rgb * temp * (1.0 + vortexInfluence * 0.5);
                float3 coolColor = _AccretionColorCool.rgb * exp(r * 0.01);
                
                return lerp(coolColor, hotColor, saturate(density * 0.5 + vortexInfluence * 0.3)) * density * _AccretionEmission;
            }

            float3 ApplyGravitationalLensing(float3 rayDir, float3 rayPos, float stepSize)
            {
                float r = length(rayPos);
                float3 toCenter = -rayPos / max(r, 0.001);
                
                float viewAlignment = dot(normalize(rayPos), rayDir);
                float alignmentFactor = pow(1.0 + viewAlignment, 3.0);
                float distanceFalloff = pow(clamp(_LensingFalloff - 0.5 * r, 0.0, 1.0), 2.0);
                
                float bendStrength = _LightBendingStrength * _SchwarzschildRadius * alignmentFactor * distanceFalloff / pow(r, 4.0);
                float3 acceleration = toCenter * bendStrength;
                
                return normalize(rayDir + acceleration * stepSize);
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

            fixed4 frag(v2f i) : SV_Target
            {   // Enhanced volumetric raymarch with jets, magnetic fields and energy ribbons
                fixed4 texColor = tex2D(_MainTex, i.uv);
                
                float2 localPos2D = ObjectToLocalSpace2D(i.objectPos);
                float3 rayOrigin = float3(localPos2D * _BlackHoleRadius, 0.0);
                float3 rayDir = normalize(i.viewDir);
                
                float3 rotAxis = normalize(_RotationAxis);
                float rotAngle = _Time.y * _RotationSpeed;
                rayOrigin = RotateAroundAxis(rayOrigin, rotAxis, rotAngle);
                rayDir = RotateAroundAxis(rayDir, rotAxis, rotAngle);
                
                if (length(_ViewRotation) > 0.01)
                {
                    rayOrigin = RotateAroundAxis(rayOrigin, float3(1,0,0), _ViewRotation.x);
                    rayOrigin = RotateAroundAxis(rayOrigin, float3(0,1,0), _ViewRotation.y);
                    rayOrigin = RotateAroundAxis(rayOrigin, float3(0,0,1), _ViewRotation.z);
                    rayDir = RotateAroundAxis(rayDir, float3(1,0,0), _ViewRotation.x);
                    rayDir = RotateAroundAxis(rayDir, float3(0,1,0), _ViewRotation.y);
                    rayDir = RotateAroundAxis(rayDir, float3(0,0,1), _ViewRotation.z);
                }
                
                float3 rayPos = rayOrigin;
                float stepSize = _InitialStepSize;
                float time = _Time.y;
                
                float transmission = 1.0;
                float3 luminosity = float3(0, 0, 0);
                float bloom = 0.0;
                
                bool inBounds = false, prevInBounds = false;
                float maxDist = _BlackHoleRadius * 2.0;
                
                [loop] for (int step = 0; step < _RaymarchSteps; step++)
                {
                    if (transmission <= _TransmissionThreshold) break;
                    
                    stepSize *= _StepGrowth;
                    rayPos += rayDir * stepSize;
                    
                    float r = length(rayPos);
                    rayDir = ApplyGravitationalLensing(rayDir, rayPos, stepSize);
                    
                    float accretionDensity = AccretionDiskDensity(rayPos, time);
                    float jetDensity = SamplePolarJetDensity(rayPos, time);
                    float magneticField = SampleMagneticField(rayPos, time);
                    float energyRibbon = SampleEnergyRibbons(rayPos, time);
                    float blackHoleDensity = BlackHoleDensity(r);
                    
                    float totalDensity = accretionDensity + jetDensity + blackHoleDensity;
                    
                    if (totalDensity > 0.001 || magneticField > 0.001 || energyRibbon > 0.001)
                    {
                        float sampleTransmission = exp(-totalDensity * stepSize);
                        float3 emissionColor = float3(0, 0, 0);
                        
                        if (accretionDensity > 0.001)
                        {
                            float vortexInfluence = FBM(rayPos * 2.0, 1);
                            emissionColor += AccretionColor(length(rayPos.xz), accretionDensity, vortexInfluence);
                        }
                        
                        if (jetDensity > 0.001)
                        {
                            float distFromAxis = length(rayPos.xy);
                            float coreProximity = exp(-pow(distFromAxis / max(_JetWidth * 0.3, 0.01), 2.0) * 2.0);
                            float3 jetColor = SampleJetColor(rayPos, jetDensity, coreProximity);
                            emissionColor += jetColor * jetDensity;
                            bloom += coreProximity * jetDensity * 0.5;
                        }
                        
                        if (magneticField > 0.001)
                            emissionColor += _MagneticFieldColor.rgb * magneticField * 2.0;
                        
                        if (energyRibbon > 0.001)
                        {
                            emissionColor += _EnergyRibbonColor.rgb * energyRibbon * 3.0;
                            bloom += energyRibbon * 0.3;
                        }
                        
                        luminosity += transmission * emissionColor * stepSize;
                        transmission *= sampleTransmission;
                    }
                    
                    inBounds = r < _BlackHoleRadius;
                    if (!inBounds && prevInBounds) break;
                    if (r > maxDist) break;
                    
                    prevInBounds = inBounds;
                }
                
                float3 skyboxSample = texCUBE(_Skybox, rayDir).rgb;
                skyboxSample = pow(skyboxSample, float3(2.2, 2.2, 2.2));
                
                float3 bloomColor = (luminosity * bloom + _JetCoreColor.rgb * bloom * 0.5) * 0.8;
                float3 finalColor = luminosity + bloomColor + transmission * skyboxSample;
                
                finalColor *= texColor.rgb;
                float finalAlpha = saturate(1.0 - transmission) * texColor.a;
                
                return fixed4(finalColor, finalAlpha);
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
