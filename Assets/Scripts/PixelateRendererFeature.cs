using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class PixelateRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("Edge")]
        public Color edgeColor = Color.black;
        [Range(0f, 2f)] public float edgeStrength = 1.0f;
        [Range(0.25f, 2f)] public float edgeWidthPx = 0.75f;
        [Range(0.0001f, 0.02f)] public float depthEdgeThreshold = 0.006f;
        [Range(0.01f, 0.5f)] public float normalEdgeThreshold = 0.22f;
        
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;
        [Min(1)] public int pixelScale = 4;

        public bool enablePaletteQuantization = true;
        public bool useLabDistance = false;
        [Range(2, 256)] public int paletteColorCount = 32;
        public Color[] palette = new Color[]
        {
            new Color32(  0,   0,   0,255), new Color32( 29,  43,  83,255),
            new Color32(126,  37,  83,255), new Color32(  0, 135,  81,255),
            new Color32(171,  82,  54,255), new Color32( 95,  87,  79,255),
            new Color32(194, 195, 199,255), new Color32(255, 241, 232,255),
            new Color32(255,   0,  77,255), new Color32(255, 163,   0,255),
            new Color32(255, 236,  39,255), new Color32(  0, 228,  54,255),
            new Color32( 41, 173, 255,255), new Color32(131, 118, 156,255),
            new Color32(255, 119, 168,255), new Color32(255, 204, 170,255),
        };
    }

    public Settings settings = new Settings();
    [SerializeField] private Shader compositeShader;

    Material _mat;
    PixelatePass _pass;

    public override void Create()
    {
        if (compositeShader == null)
            compositeShader = Shader.Find("Hidden/PixelateComposite");

        if (compositeShader != null && _mat == null)
            _mat = CoreUtils.CreateEngineMaterial(compositeShader);

        _pass = new PixelatePass(settings, _mat)
        {
            renderPassEvent = settings.passEvent
        };
    }
    
    private const int MaxPalette = 256;

    private void OnValidate()
    {
        if (settings == null) return;

        settings.paletteColorCount = Mathf.Clamp(settings.paletteColorCount, 2, MaxPalette);
        SyncPaletteArrayToCount(settings, settings.paletteColorCount);
    }

    private static void SyncPaletteArrayToCount(Settings s, int targetCount)
    {
        if (targetCount < 2) targetCount = 2;

        var oldArr = s.palette ?? System.Array.Empty<Color>();
        if (oldArr.Length == targetCount) return;

        var newArr = new Color[targetCount];

        int copy = Mathf.Min(oldArr.Length, targetCount);
        for (int i = 0; i < copy; i++)
            newArr[i] = oldArr[i];

        for (int i = copy; i < targetCount; i++)
        {
            if (copy > 0)
            {
                newArr[i] = oldArr[i % copy];
            }
            else
            {
                float h = (float)i / targetCount;
                newArr[i] = Color.HSVToRGB(h, 0.7f, 1.0f);
            }
        }

        s.palette = newArr;
    }
    
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_mat == null) return;
        if (renderingData.cameraData.isPreviewCamera) return;
        if (renderingData.cameraData.cameraType == CameraType.Reflection) return;

        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_mat);
    }

    class PixelatePass : ScriptableRenderPass
    {
        readonly Settings _settings;
        readonly Material _mat;

        const int MaxPalette = 256;
        readonly Vector4[] _paletteBuffer = new Vector4[MaxPalette];

        static readonly int _LowResTexID   = Shader.PropertyToID("_LowResTex");
        static readonly int _EnableQuantID = Shader.PropertyToID("_EnableQuant");
        static readonly int _UseLabID      = Shader.PropertyToID("_UseLabDistance");
        static readonly int _PaletteCountID= Shader.PropertyToID("_PaletteCount");
        static readonly int _PaletteID     = Shader.PropertyToID("_Palette");
        
        static readonly int _EdgeColorID = Shader.PropertyToID("_EdgeColor");
        static readonly int _DepthEdgeThresholdID = Shader.PropertyToID("_DepthEdgeThreshold");
        static readonly int _NormalEdgeThresholdID = Shader.PropertyToID("_NormalEdgeThreshold");
        static readonly int _EdgeStrengthID = Shader.PropertyToID("_EdgeStrength");
        
        static readonly int _EdgeWidthPxID = Shader.PropertyToID("_EdgeWidthPx");

        public PixelatePass(Settings settings, Material mat)
        {
            _settings = settings;
            _mat = mat;
        }

        class PassData
        {
            public TextureHandle src;
            public TextureHandle low;
            public TextureHandle dst;
            public Material mat;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_mat == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle src = resourceData.activeColorTexture;
            if (!src.IsValid()) return;

            var camDesc = cameraData.cameraTargetDescriptor;
            camDesc.depthBufferBits = 0;
            camDesc.msaaSamples = 1;
            camDesc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;

            int scale = Mathf.Max(1, _settings.pixelScale);
            int lowW = Mathf.Max(1, Mathf.CeilToInt(camDesc.width / (float)scale));
            int lowH = Mathf.Max(1, Mathf.CeilToInt(camDesc.height / (float)scale));

            var lowDesc = camDesc;
            lowDesc.width = lowW;
            lowDesc.height = lowH;

            var lowTex = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, lowDesc, "_PixelateLowColor", false, FilterMode.Point);

            var fullDesc = camDesc;
            var tempTex = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, fullDesc, "_PixelateTempFull", false, FilterMode.Point);

            _mat.SetInt(_EnableQuantID, _settings.enablePaletteQuantization ? 1 : 0);
            _mat.SetInt(_UseLabID, _settings.useLabDistance ? 1 : 0);

            int count = 0;
            if (_settings.palette != null)
            {
                count = Mathf.Min(_settings.paletteColorCount, _settings.palette.Length, MaxPalette);
                for (int i = 0; i < count; i++)
                {
                    Color c = _settings.palette[i];
                    _paletteBuffer[i] = new Vector4(c.r, c.g, c.b, c.a);
                }
            }
            _mat.SetInt(_PaletteCountID, count);
            _mat.SetVectorArray(_PaletteID, _paletteBuffer);
            
            _mat.SetColor(_EdgeColorID, _settings.edgeColor);
            _mat.SetFloat(_EdgeStrengthID, _settings.edgeStrength);
            _mat.SetFloat(_EdgeWidthPxID, _settings.edgeWidthPx);
            _mat.SetFloat(_DepthEdgeThresholdID, _settings.depthEdgeThreshold);
            _mat.SetFloat(_NormalEdgeThresholdID, _settings.normalEdgeThreshold);

            // Pass1: full -> low
            {
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Pixelate Downsample", out var passData);
                passData.src = src;
                passData.dst = lowTex;
                passData.mat = null;

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.SetRenderAttachment(passData.dst, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1,1,0,0), 0, false);
                });
            }

            // Pass2: low -> temp
            {
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Pixelate Composite", out var passData);
                passData.src = lowTex;
                passData.dst = tempTex;
                passData.mat = _mat;

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.SetRenderAttachment(passData.dst, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    data.mat.SetTexture(_LowResTexID, data.src);
                    Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1,1,0,0), data.mat, 0);
                });
            }

            // Pass3: temp -> camera target
            {
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Pixelate Copy Back", out var passData);
                passData.src = tempTex;
                passData.dst = src;

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.SetRenderAttachment(passData.dst, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1,1,0,0), 0, false);
                });
            }
        }
    }
}