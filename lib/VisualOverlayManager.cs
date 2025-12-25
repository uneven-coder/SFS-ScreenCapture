// Add inside VisualOverlayManager class:

public struct ModelTextureData
{   // Contains model texture references for direct pixel manipulation
    public MeshRenderer Renderer;
    public Texture2D ColorTexture;
    public Texture2D NormalTexture;
    public bool UseNormals;
    public float Smoothness;
}

public struct FrameData
{   // ...existing fields...
    public NativeArray<Color32> Source;
    public NativeArray<Color32> Result;
    public int Width;
    public int Height;
    public RendererMaterialGroup[] RendererMaterials;
    public ModelTextureData[] ModelTextures;  // Add this field
}
