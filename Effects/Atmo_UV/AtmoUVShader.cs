using System;
using System.Linq;
using UnityEngine;
using FrameEmbededState.Lib;
using FrameEmbededState;
using FrameEmbededState.Lib.Renders;
using SFS.World;

namespace FrameEmbededState.Effects.AtmoUVShader
{
    [ShaderModule("AtmoUVShader", ShaderType.Shader, "Hidden/FrameEmbededState/AtmoUVShader", OverlayRenderMode.CustomRender)]
    public class AtmoUVShaderModule : ObjectTargetShaderModule<AtmoUVShaderModule.Args, object>
    {
        public struct Args
        {   
            public Color ColorR;
            public Color ColorG;
            public Color ColorB;
            public Color ColorW;
            public string PlayerPlanetName;
            public float GridDensity;
            public Color GridLineColor;
            public float GridLineThickness;
        }

        private Args _lastAppliedArgs;
        private bool _isApplied;

        public AtmoUVShaderModule() { }

        public override object Run(in Args args)
        {   // Execute shader application with redundancy check to prevent duplicate processing
            if (_isApplied && ArgsEqual(_lastAppliedArgs, args)) { Debug.Log($"[AtmoUVShaderModule] Skipping redundant application"); return null; }

            Debug.Log($"[AtmoUVShaderModule] === RUN CALLED ===");
            Debug.Log($"[AtmoUVShaderModule] R:{args.ColorR} G:{args.ColorG} B:{args.ColorB} W:{args.ColorW}");
            
            ApplyToTargets(args);
            
            _lastAppliedArgs = args;
            _isApplied = true;
            
            Debug.Log($"[AtmoUVShaderModule] === RUN COMPLETE ===");
            return null;
        }

        private bool ArgsEqual(Args a, Args b) =>
            a.ColorR.Equals(b.ColorR) && a.ColorG.Equals(b.ColorG) && 
            a.ColorB.Equals(b.ColorB) && a.ColorW.Equals(b.ColorW) && 
            a.PlayerPlanetName == b.PlayerPlanetName &&
            a.GridLineColor.Equals(b.GridLineColor) &&
            Mathf.Approximately(a.GridDensity, b.GridDensity) &&
            Mathf.Approximately(a.GridLineThickness, b.GridLineThickness);

        public override void ApplyToTargets(in Args args)
        {   // Find atmosphere renderers and apply corner-based color gradient shader
            if (Shader == null) { Debug.LogError($"[AtmoUVShaderModule] Shader NULL for '{Name}'"); RestoreMaterials(); return; }

            string playerPlanetName = null;
            try { playerPlanetName = PlayerController.main.player.Value.location.planet.Value.name; }
            catch (Exception ex) { Debug.Log($"[AtmoUVShaderModule] Player planet unavailable: {ex.Message}"); }

            var renderers = GameObject.FindObjectsOfType<Atmosphere>()
                .Where(a => a && (string.IsNullOrEmpty(playerPlanetName) || a.planet?.name == playerPlanetName))
                .Select(a => a.GetComponent<MeshRenderer>())
                .Where(mr => mr && mr.enabled && mr.gameObject.activeInHierarchy)
                .Cast<Renderer>()
                .ToArray();

            Debug.Log($"[AtmoUVShaderModule] Found {renderers.Length} renderers on '{playerPlanetName ?? "ALL"}'");

            if (renderers.Length == 0) { RestoreMaterials(); return; }

            RestoreMaterials();

            foreach (var renderer in renderers)
            {   // Process each atmosphere renderer with bounds calculation
                var atmo = renderer.GetComponent<Atmosphere>();
                if (atmo == null) continue;

                Debug.Log($"[AtmoUVShaderModule] Processing: {renderer.gameObject.name}");
                
                StoreAndApplyMaterials(renderer, renderer.sharedMaterials, args);

                var meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter?.sharedMesh == null) continue;

                var bounds = meshFilter.sharedMesh.bounds;
                var customMats = renderer.sharedMaterials;
                
                for (int i = 0; i < customMats.Length; i++)
                {   // Apply bounds to each material for corner-based color mapping
                    if (customMats[i] == null) continue;
                    
                    if (customMats[i].HasProperty("_BoundsMin")) customMats[i].SetVector("_BoundsMin", new Vector4(bounds.min.x, bounds.min.y, 0, 0));
                    if (customMats[i].HasProperty("_BoundsMax")) customMats[i].SetVector("_BoundsMax", new Vector4(bounds.max.x, bounds.max.y, 0, 0));
                }
            }
            
            Debug.Log($"[AtmoUVShaderModule] Applied to {renderers.Length} renderers");
        }

        protected override void ApplyArgsToMaterial(Material mat, Args args)
        {   // Apply corner colors to material properties: R=TopLeft, G=TopRight, B=BottomLeft, W=BottomRight
            if (mat == null) throw new ArgumentNullException(nameof(mat));

            if (mat.HasProperty("_ColorR")) mat.SetColor("_ColorR", args.ColorR);
            if (mat.HasProperty("_ColorG")) mat.SetColor("_ColorG", args.ColorG);
            if (mat.HasProperty("_ColorB")) mat.SetColor("_ColorB", args.ColorB);
            if (mat.HasProperty("_ColorW")) mat.SetColor("_ColorW", args.ColorW);
            if (mat.HasProperty("_GridLineColor")) mat.SetColor("_GridLineColor", args.GridLineColor);
            if (mat.HasProperty("_GridDensity")) mat.SetFloat("_GridDensity", args.GridDensity);
            if (mat.HasProperty("_GridLineThickness")) mat.SetFloat("_GridLineThickness", args.GridLineThickness);
        }

        protected override Material CreateCustomMaterial(Material original, Args args)
        {   // Create shader-replaced material preserving original textures
            if (Shader == null) { Debug.LogError($"[AtmoUVShaderModule] Shader not loaded for '{Name}'"); return original != null ? new Material(original) : null; }

            var mat = new Material(Shader) { name = $"CustomAtmo_{original?.name ?? "Material"}" };

            if (original?.HasProperty("_MainTex") == true && mat.HasProperty("_MainTex"))
                mat.mainTexture = original.mainTexture;

            ApplyArgsToMaterial(mat, args);
            return mat;
        }
    }
}
