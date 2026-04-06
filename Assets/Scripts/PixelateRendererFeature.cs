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
        [Range(0f, 2f)] public float edgeStrength = 1.2f;
        [Range(0.25f, 2f)] public float edgeWidthPx = 1.1f;
        [Range(0.0001f, 0.02f)] public float edgeDepthBias = 0.0015f;
        [Range(0.001f, 0.5f)] public float edgeNormalBias = 0.06f;

        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;
        [Min(1)] public int pixelScale = 4;

        [Header("Palette")]
        public bool enablePaletteQuantization = true;
        public bool useLabDistance = false;
        [Range(2, 256)] public int paletteColorCount = 32;
        [Range(0f, 0.25f)] public float preQuantLift = 0.04f;
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

        [Header("External Mask")]
        public bool useExternalMaskTexture = true;
        public RenderTexture externalMaskTexture;
        [Range(0f, 1f)] public float maskThreshold = 0.05f;

        [Header("Debug")]
        [Range(0, 3)] public int debugView = 0;
        public bool forceNoMask = false;

        [Header("Camera Filter")]
        public string[] skipCameraNames = new string[] { "MaskCamera" };
    }

    public Settings settings = new Settings();
    [SerializeField] private Shader compositeShader;

    Material _mat;
    PixelatePass _pass;

    const int MaxPalette = 256;
    [SerializeField, HideInInspector] private int _lastPaletteCount = -1;

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

    void OnValidate()
    {
        if (settings == null) return;
        settings.paletteColorCount = Mathf.Clamp(settings.paletteColorCount, 2, MaxPalette);

        if (_lastPaletteCount != settings.paletteColorCount)
        {
            settings.palette = BuildDistinctPalette(settings.paletteColorCount, settings.palette);
            _lastPaletteCount = settings.paletteColorCount;
        }
        else
        {
            EnsurePaletteLength(ref settings.palette, settings.paletteColorCount);
        }
    }

    static Color[] BuildDistinctPalette(int count, Color[] old)
    {
        var result = new Color[count];
        int keep = (old != null) ? Mathf.Min(old.Length, Mathf.Min(16, count)) : 0;
        for (int i = 0; i < keep; i++) result[i] = old[i];

        const float golden = 0.61803398875f;
        for (int i = keep; i < count; i++)
        {
            float h = Mathf.Repeat((i - keep) * golden, 1f);
            float s = Mathf.Lerp(0.55f, 0.9f, Mathf.PingPong(i * 0.173f, 1f));
            float v = Mathf.Lerp(0.65f, 1.0f, Mathf.PingPong(i * 0.117f, 1f));
            result[i] = Color.HSVToRGB(h, s, v);
            result[i].a = 1f;
        }
        return result;
    }

    static void EnsurePaletteLength(ref Color[] arr, int target)
    {
        if (arr == null) arr = new Color[target];
        if (arr.Length == target) return;

        var n = new Color[target];
        int c = Mathf.Min(arr.Length, target);
        for (int i = 0; i < c; i++) n[i] = arr[i];
        for (int i = c; i < target; i++)
            n[i] = Color.HSVToRGB((float)i / Mathf.Max(1, target), 0.75f, 1f);
        arr = n;
    }

    bool ShouldSkipCamera(Camera cam)
    {
        if (cam == null) return true;
        if (settings.skipCameraNames == null) return false;

        for (int i = 0; i < settings.skipCameraNames.Length; i++)
        {
            string n = settings.skipCameraNames[i];
            if (!string.IsNullOrEmpty(n) && cam.name == n)
                return true;
        }
        return false;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_mat == null) return;
        if (renderingData.cameraData.isPreviewCamera) return;
        if (renderingData.cameraData.cameraType == CameraType.Reflection) return;
        if (ShouldSkipCamera(renderingData.cameraData.camera)) return;

        _pass.renderPassEvent = settings.passEvent;
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

        const int MaxPalettePass = 256;
        readonly Vector4[] _paletteBuffer = new Vector4[MaxPalettePass];

        static readonly int _LowResTexID = Shader.PropertyToID("_LowResTex");
        static readonly int _EnableQuantID = Shader.PropertyToID("_EnableQuant");
        static readonly int _UseLabID = Shader.PropertyToID("_UseLabDistance");
        static readonly int _PaletteCountID = Shader.PropertyToID("_PaletteCount");
        static readonly int _PaletteID = Shader.PropertyToID("_Palette");

        static readonly int _EdgeColorID = Shader.PropertyToID("_EdgeColor");
        static readonly int _EdgeStrengthID = Shader.PropertyToID("_EdgeStrength");
        static readonly int _EdgeWidthPxID = Shader.PropertyToID("_EdgeWidthPx");
        static readonly int _EdgeDepthBiasID = Shader.PropertyToID("_EdgeDepthBias");
        static readonly int _EdgeNormalBiasID = Shader.PropertyToID("_EdgeNormalBias");

        static readonly int _PixelArtMaskTexID = Shader.PropertyToID("_PixelArtMaskTex");
        static readonly int _HasMaskTexID = Shader.PropertyToID("_HasMaskTex");
        static readonly int _MaskThresholdID = Shader.PropertyToID("_MaskThreshold");

        static readonly int _DebugViewID = Shader.PropertyToID("_DebugView");
        static readonly int _PreQuantLiftID = Shader.PropertyToID("_PreQuantLift");

        public PixelatePass(Settings settings, Material mat)
        {
            _settings = settings;
            _mat = mat;
        }

        class PassData
        {
            public TextureHandle src;
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

            // 按 pixelScale 进行降采样
            int scale = Mathf.Max(1, _settings.pixelScale);
            int lowW = Mathf.Max(1, Mathf.CeilToInt(camDesc.width / (float)scale));
            int lowH = Mathf.Max(1, Mathf.CeilToInt(camDesc.height / (float)scale));
            
            // 固定低分辨率
            // int targetH = 200;
            // int lowH = Mathf.Max(1, targetH);
            // int lowW = Mathf.Max(1, Mathf.RoundToInt(camDesc.width * (lowH / (float)camDesc.height)));

            var lowDesc = camDesc;
            lowDesc.width = lowW;
            lowDesc.height = lowH;

            var lowTex = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, lowDesc, "_PixelateLowColor", false, FilterMode.Point);

            var tempTex = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph, camDesc, "_PixelateTempFull", false, FilterMode.Point);

            _mat.SetInt(_EnableQuantID, _settings.enablePaletteQuantization ? 1 : 0);
            _mat.SetInt(_UseLabID, _settings.useLabDistance ? 1 : 0);

            int count = 0;
            if (_settings.palette != null)
            {
                count = Mathf.Min(_settings.paletteColorCount, _settings.palette.Length, MaxPalettePass);
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
            _mat.SetFloat(_EdgeDepthBiasID, _settings.edgeDepthBias);
            _mat.SetFloat(_EdgeNormalBiasID, _settings.edgeNormalBias);

            _mat.SetFloat(_DebugViewID, _settings.debugView);
            _mat.SetFloat(_PreQuantLiftID, _settings.preQuantLift);
            _mat.SetFloat(_MaskThresholdID, _settings.maskThreshold);

            if (!_settings.forceNoMask && _settings.useExternalMaskTexture && _settings.externalMaskTexture != null)
            {
                _mat.SetTexture(_PixelArtMaskTexID, _settings.externalMaskTexture);
                _mat.SetFloat(_HasMaskTexID, 1f);
            }
            else
            {
                _mat.SetFloat(_HasMaskTexID, 0f);
            }

            {
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Pixelate Downsample", out var passData);
                passData.src = src;
                passData.dst = lowTex;

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.SetRenderAttachment(passData.dst, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
                });
            }

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
                    Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), data.mat, 0);
                });
            }

            {
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Pixelate Copy Back", out var passData);
                passData.src = tempTex;
                passData.dst = src;

                builder.UseTexture(passData.src, AccessFlags.Read);
                builder.SetRenderAttachment(passData.dst, 0, AccessFlags.Write);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }
    }
}