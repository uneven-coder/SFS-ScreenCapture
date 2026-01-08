// using System;
// using System.Linq;
// using UnityEngine;
// using FrameEmbededState.Lib;
// using FrameEmbededState.Lib.Renders;
// using SFS.World;

// namespace FrameEmbededState.Effects.GalaxyShader
// {
//     [ShaderModule("BlackHoleShader", ShaderType.Shader, "Hidden/FrameEmbededState/BlackHoleShader", OverlayRenderMode.CustomRender)]
//     public class GalaxyShaderModule : ObjectTargetShaderModule<GalaxyShaderModule.Args, object>
//     {
//         public struct Args
//         {
//             [ShaderArg(group: "Positioning", property: "_BlackHoleCenterWS")] public Vector3 BlackHoleCenterWS;
//             [ShaderArg(group: "Positioning", property: "_BlackHoleRadius", defaultValue: 10000000f)] public float BlackHoleRadius;
//             [ShaderArg(group: "Positioning", property: "_SchwarzschildRadius", defaultValue: 0.7f)] public float SchwarzschildRadius;
//             [ShaderArg(group: "Positioning", property: "_BoundsMin")] public Vector2 BoundsMin;
//             [ShaderArg(group: "Positioning", property: "_BoundsMax")] public Vector2 BoundsMax;
            
//             [ShaderArg(group: "Accretion Disk", property: "_AccretionInnerRadius", defaultValue: 1.75f)] public float AccretionInnerRadius;
//             [ShaderArg(group: "Accretion Disk", property: "_AccretionOuterRadius", defaultValue: 5.0f)] public float AccretionOuterRadius;
//             [ShaderArg(group: "Accretion Disk", property: "_AccretionHeight", defaultValue: 0.1f)] public float AccretionHeight;
//             [ShaderArg(group: "Accretion Disk", property: "_AccretionFade", defaultValue: 0.15f)] public float AccretionFade;
//             [ShaderArg(group: "Accretion Disk", property: "_AccretionDensity", defaultValue: 10.0f)] public float AccretionDensity;
//             [ShaderArg(group: "Accretion Disk", property: "_AccretionSpeed", defaultValue: 0.2f)] public float AccretionSpeed;
//             [ShaderArg(group: "Accretion Disk", property: "_AccretionEmission", defaultValue: 2.0f)] public float AccretionEmission;
//             [ShaderArg(group: "Accretion Disk", property: "_AccretionTurbulence", defaultValue: 0.4f)] public float AccretionTurbulence;
            
//             [ShaderArg(group: "Colors", property: "_AccretionColorHot", defaultValue: "(3.5,2.0,0.5,1.0)")] public Color AccretionColorHot;
//             [ShaderArg(group: "Colors", property: "_AccretionColorCool", defaultValue: "(1.0,0.3,0.1,1.0)")] public Color AccretionColorCool;
            
//             [ShaderArg(group: "Polar Jets", property: "_JetLength", defaultValue: 15.0f)] public float JetLength;
//             [ShaderArg(group: "Polar Jets", property: "_JetWidth", defaultValue: 0.8f)] public float JetWidth;
//             [ShaderArg(group: "Polar Jets", property: "_JetDensity", defaultValue: 0.6f)] public float JetDensity;
//             [ShaderArg(group: "Polar Jets", property: "_JetSpeed", defaultValue: 2.0f)] public float JetSpeed;
//             [ShaderArg(group: "Polar Jets", property: "_JetTurbulence", defaultValue: 0.5f)] public float JetTurbulence;
//             [ShaderArg(group: "Polar Jets", property: "_JetEmission", defaultValue: 3.0f)] public float JetEmission;
//             [ShaderArg(group: "Polar Jets", property: "_JetColor", defaultValue: "(0.3,0.6,1.5,1.0)")] public Color JetColor;
//             [ShaderArg(group: "Polar Jets", property: "_JetCoreColor", defaultValue: "(1.2,1.5,2.0,1.0)")] public Color JetCoreColor;
            
//             [ShaderArg(group: "Magnetic Fields", property: "_MagneticFieldStrength", defaultValue: 0.3f)] public float MagneticFieldStrength;
//             [ShaderArg(group: "Magnetic Fields", property: "_MagneticFieldDensity", defaultValue: 8.0f)] public float MagneticFieldDensity;
//             [ShaderArg(group: "Magnetic Fields", property: "_MagneticFieldColor", defaultValue: "(0.2,0.4,0.8,1.0)")] public Color MagneticFieldColor;
            
//             [ShaderArg(group: "Energy Effects", property: "_EnergyRibbonIntensity", defaultValue: 0.5f)] public float EnergyRibbonIntensity;
//             [ShaderArg(group: "Energy Effects", property: "_EnergyRibbonSpeed", defaultValue: 1.5f)] public float EnergyRibbonSpeed;
//             [ShaderArg(group: "Energy Effects", property: "_EnergyRibbonColor", defaultValue: "(0.8,0.3,1.2,1.0)")] public Color EnergyRibbonColor;
            
//             [ShaderArg(group: "Rotation", property: "_RotationAxis", defaultValue: "(0,1,0)")] public Vector3 RotationAxis;
//             [ShaderArg(group: "Rotation", property: "_RotationSpeed", defaultValue: 0.05f)] public float RotationSpeed;
//             [ShaderArg(group: "Rotation", property: "_ViewRotation", defaultValue: "(0,0,0)")] public Vector3 ViewRotation;
            
//             [ShaderArg(group: "Gravitational Lensing", property: "_LightBendingStrength", defaultValue: 1.5f)] public float LightBendingStrength;
//             [ShaderArg(group: "Gravitational Lensing", property: "_LensingFalloff", defaultValue: 2.0f)] public float LensingFalloff;
            
//             [ShaderArg(group: "Rendering", property: "_RaymarchSteps", defaultValue: 200)] public int RaymarchSteps;
//             [ShaderArg(group: "Rendering", property: "_InitialStepSize", defaultValue: 0.05f)] public float InitialStepSize;
//             [ShaderArg(group: "Rendering", property: "_StepGrowth", defaultValue: 1.005f)] public float StepGrowth;
//             [ShaderArg(group: "Rendering", property: "_TransmissionThreshold", defaultValue: 0.005f)] public float TransmissionThreshold;
//         }

//         private static GalaxyLiveUpdater _updater;
//         private GameObject _starsObject;
//         private Renderer _starsRenderer;
//         private const string StarsObjectName = "Stars_PC";

//         private class GalaxyLiveUpdater : MonoBehaviour
//         {
//             internal GalaxyShaderModule Module;
//             internal Args BaseArgs;

//             void Update()
//             {
//                 if (Module != null)
//                     Module.Tick(BaseArgs);
//             }
//         }

//         public override object Run(in Args args)
//         {
//             InitializeUpdater();
//             _updater.Module = this;
//             _updater.BaseArgs = args;
//             FindAndApplyToStarsPc(args);
//             return null;
//         }

//         private void InitializeUpdater()
//         {
//             if (_updater != null) return;

//             var go = GameObject.Find("[FrameEmbededState] GalaxyShaderLiveUpdater") ?? 
//                      new GameObject("[FrameEmbededState] GalaxyShaderLiveUpdater") { hideFlags = HideFlags.HideAndDontSave };
//             _updater = go.GetComponent<GalaxyLiveUpdater>() ?? go.AddComponent<GalaxyLiveUpdater>();
//         }

//         private void RemoveUpdater()
//         {
//             if (_updater != null)
//             {
//                 UnityEngine.Object.Destroy(_updater.gameObject);
//                 _updater = null;
//             }
            
//             RestoreMaterials();
//         }

//         internal void Tick(in Args baseArgs)
//         {
//             if (Shader == null) { RemoveUpdater(); return; }

//             FindAndApplyToStarsPc(baseArgs);
//             UpdateGalaxyMaterial(baseArgs);
//         }

//         private void FindAndApplyToStarsPc(in Args args)
//         {   // Find Stars_PC and apply galaxy shader as additional material layer
//             _starsObject = GameObject.Find(StarsObjectName);
            
//             if (_starsObject == null)
//             {
//                 RestoreMaterials();
//                 _starsRenderer = null;
//                 return;
//             }

//             EnsureStarsHasMeshAndRenderer();

//             _starsRenderer = _starsObject.GetComponent<Renderer>();
//             if (_starsRenderer == null)
//             {
//                 RestoreMaterials();
//                 return;
//             }

//             var currentMats = _starsRenderer.sharedMaterials;
//             if (currentMats == null || currentMats.Length == 0)
//             {
//                 RestoreMaterials();
//                 return;
//             }

//             bool hasGalaxyMat = currentMats.Length > 1 && currentMats[1] != null && currentMats[1].shader == Shader;

//             if (!hasGalaxyMat)
//                 ApplyGalaxyMaterial(args);
//         }

//         private void EnsureStarsHasMeshAndRenderer()
//         {   // Create mesh and renderer components for Stars_PC if missing
//             if (_starsObject == null) return;

//             var meshFilter = _starsObject.GetComponent<MeshFilter>();
//             var meshRenderer = _starsObject.GetComponent<MeshRenderer>();

//             if (meshFilter == null)
//             {   // Add MeshFilter and create fullscreen quad mesh
//                 meshFilter = _starsObject.AddComponent<MeshFilter>();
//                 meshFilter.sharedMesh = CreateGalaxyQuadMesh();
//             }

//             if (meshRenderer == null)
//             {   // Add MeshRenderer with default transparent material
//                 meshRenderer = _starsObject.AddComponent<MeshRenderer>();
                
//                 var defaultMat = new Material(Shader.Find("Sprites/Default"));
//                 defaultMat.color = Color.white;
//                 meshRenderer.sharedMaterial = defaultMat;
//             }

//             Debug.Log($"[GalaxyShader] Ensured Stars_PC has mesh and renderer");
//         }

//         private Mesh CreateGalaxyQuadMesh()
//         {   // Create large quad mesh for galaxy rendering centered at origin
//             var mesh = new Mesh();
//             mesh.name = "GalaxyQuad";

//             float size = 50000f;
//             var vertices = new Vector3[]
//             {
//                 new Vector3(-size, -size, 0),
//                 new Vector3(size, -size, 0),
//                 new Vector3(size, size, 0),
//                 new Vector3(-size, size, 0)
//             };

//             var uvs = new Vector2[]
//             {
//                 new Vector2(0, 0),
//                 new Vector2(1, 0),
//                 new Vector2(1, 1),
//                 new Vector2(0, 1)
//             };

//             var triangles = new int[]
//             {
//                 0, 2, 1,
//                 0, 3, 2
//             };

//             mesh.vertices = vertices;
//             mesh.uv = uvs;
//             mesh.triangles = triangles;
//             mesh.RecalculateNormals();
//             mesh.RecalculateBounds();

//             return mesh;
//         }

//         private void ApplyGalaxyMaterial(in Args args)
//         {   // Add black hole shader material as second material layer
//             if (_starsRenderer == null) return;

//             var originalMats = _starsRenderer.sharedMaterials;
//             if (originalMats == null || originalMats.Length == 0) return;

//             if (!_originalMaterials.ContainsKey(_starsRenderer))
//                 _originalMaterials[_starsRenderer] = originalMats;

//             var sourceMeshFilter = _starsObject.GetComponent<MeshFilter>();
            
//             Texture mainTex = null;
//             if (originalMats[0] != null && originalMats[0].HasProperty("_MainTex"))
//                 mainTex = originalMats[0].mainTexture;

//             var galaxyMat = new Material(Shader);
//             galaxyMat.name = "BlackHoleOverlayMaterial";
//             galaxyMat.renderQueue = 3001;
            
//             if (mainTex != null)
//             {
//                 if (galaxyMat.HasProperty("_MainTex")) galaxyMat.mainTexture = mainTex;
//                 if (galaxyMat.HasProperty("_NoiseTex")) galaxyMat.SetTexture("_NoiseTex", mainTex);
//             }
            
//             var skyboxMat = RenderSettings.skybox;
//             if (skyboxMat != null && galaxyMat.HasProperty("_Skybox"))
//             {
//                 var skyboxTex = skyboxMat.GetTexture("_Tex") as Cubemap;
//                 if (skyboxTex != null) galaxyMat.SetTexture("_Skybox", skyboxTex);
//             }

//             CalculateRendererBounds(sourceMeshFilter, out var boundsMin, out var boundsMax);
//             var argsWithBounds = args;
//             argsWithBounds.BoundsMin = boundsMin;
//             argsWithBounds.BoundsMax = boundsMax;
//             argsWithBounds.BlackHoleCenterWS = _starsRenderer.bounds.center;
//             argsWithBounds.GalaxyScale = _starsRenderer.bounds.size.magnitude;

//             ApplyArgsAutomatic(galaxyMat, argsWithBounds);

//             var newMats = new Material[originalMats.Length + 1];
//             Array.Copy(originalMats, newMats, originalMats.Length);
//             newMats[originalMats.Length] = galaxyMat;

//             _customMaterials[_starsRenderer] = newMats;
//             _starsRenderer.sharedMaterials = newMats;

//             Debug.Log($"[GalaxyShader] Applied galaxy material to '{StarsObjectName}' as layer {newMats.Length} with bounds {boundsMin} to {boundsMax}");
//         }

//         private void CalculateRendererBounds(MeshFilter meshFilter, out Vector2 boundsMin, out Vector2 boundsMax)
//         {   // Calculate object-space XY bounds from mesh vertices
//             if (meshFilter != null && meshFilter.sharedMesh != null)
//             {
//                 var mesh = meshFilter.sharedMesh;
//                 var vertices = mesh.vertices;
                
//                 if (vertices.Length > 0)
//                 {
//                     float minX = vertices.Min(v => v.x);
//                     float maxX = vertices.Max(v => v.x);
//                     float minY = vertices.Min(v => v.y);
//                     float maxY = vertices.Max(v => v.y);
                    
//                     boundsMin = new Vector2(minX, minY);
//                     boundsMax = new Vector2(maxX, maxY);
//                     return;
//                 }
//             }

//             boundsMin = new Vector2(-1, -1);
//             boundsMax = new Vector2(1, 1);
//         }

//         private void UpdateGalaxyMaterial(in Args args)
//         {
//             if (_starsRenderer == null) return;

//             var currentMats = _starsRenderer.sharedMaterials;
//             if (currentMats == null || currentMats.Length < 2) return;

//             var sourceMeshFilter = _starsObject.GetComponent<MeshFilter>();
            
//             CalculateRendererBounds(sourceMeshFilter, out var boundsMin, out var boundsMax);
//             var argsWithBounds = args;
//             argsWithBounds.BoundsMin = boundsMin;
//             argsWithBounds.BoundsMax = boundsMax;
//             argsWithBounds.BlackHoleCenterWS = _starsRenderer.bounds.center;
//             argsWithBounds.GalaxyScale = _starsRenderer.bounds.size.magnitude;

//             foreach (var mat in currentMats)
//             {
//                 if (mat != null && mat.shader == Shader)
//                     ApplyArgsAutomatic(mat, argsWithBounds);
//             }
//         }

//         protected override void ApplyArgsToMaterial(Material material, Args args)
//         {   // Required override - apply arguments using automatic system
//             ApplyArgsAutomatic(material, args);
//         }

//         private void SetMaterialArgs(Material material, in Args args)
//         {   // Manual argument application including jets and magnetic fields
//             if (material == null) return;

//             material.SetVector("_BlackHoleCenterWS", args.BlackHoleCenterWS);
//             material.SetFloat("_BlackHoleRadius", args.BlackHoleRadius);
//             material.SetFloat("_SchwarzschildRadius", args.SchwarzschildRadius);
//             material.SetVector("_BoundsMin", args.BoundsMin);
//             material.SetVector("_BoundsMax", args.BoundsMax);
            
//             material.SetFloat("_AccretionInnerRadius", args.AccretionInnerRadius);
//             material.SetFloat("_AccretionOuterRadius", args.AccretionOuterRadius);
//             material.SetFloat("_AccretionHeight", args.AccretionHeight);
//             material.SetFloat("_AccretionFade", args.AccretionFade);
//             material.SetFloat("_AccretionDensity", args.AccretionDensity);
//             material.SetFloat("_AccretionSpeed", args.AccretionSpeed);
//             material.SetFloat("_AccretionEmission", args.AccretionEmission);
//             material.SetFloat("_AccretionTurbulence", args.AccretionTurbulence);
            
//             material.SetColor("_AccretionColorHot", args.AccretionColorHot);
//             material.SetColor("_AccretionColorCool", args.AccretionColorCool);
            
//             material.SetFloat("_JetLength", args.JetLength);
//             material.SetFloat("_JetWidth", args.JetWidth);
//             material.SetFloat("_JetDensity", args.JetDensity);
//             material.SetFloat("_JetSpeed", args.JetSpeed);
//             material.SetFloat("_JetTurbulence", args.JetTurbulence);
//             material.SetFloat("_JetEmission", args.JetEmission);
//             material.SetColor("_JetColor", args.JetColor);
//             material.SetColor("_JetCoreColor", args.JetCoreColor);
            
//             material.SetFloat("_MagneticFieldStrength", args.MagneticFieldStrength);
//             material.SetFloat("_MagneticFieldDensity", args.MagneticFieldDensity);
//             material.SetColor("_MagneticFieldColor", args.MagneticFieldColor);
            
//             material.SetFloat("_EnergyRibbonIntensity", args.EnergyRibbonIntensity);
//             material.SetFloat("_EnergyRibbonSpeed", args.EnergyRibbonSpeed);
//             material.SetColor("_EnergyRibbonColor", args.EnergyRibbonColor);
            
//             material.SetVector("_RotationAxis", args.RotationAxis);
//             material.SetFloat("_RotationSpeed", args.RotationSpeed);
//             material.SetVector("_ViewRotation", args.ViewRotation);
            
//             material.SetFloat("_LightBendingStrength", args.LightBendingStrength);
//             material.SetFloat("_LensingFalloff", args.LensingFalloff);
            
//             material.SetInt("_RaymarchSteps", args.RaymarchSteps);
//             material.SetFloat("_InitialStepSize", args.InitialStepSize);
//             material.SetFloat("_StepGrowth", args.StepGrowth);
//             material.SetFloat("_TransmissionThreshold", args.TransmissionThreshold);
//         }
//     }
// }
