using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class PixelArtGodRaysFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Shader shader;
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Header("God Rays")]
        [Range(0f, 2f)] public float intensity = 0.6f;
        [Range(4, 128)] public int raymarchSteps = 32;
        [Range(1f, 500f)] public float maxDistance = 120f;
        [Range(0f, 3f)] public float rayDensity = 0.8f;
        [Range(0f, 3f)] public float scattering = 1.0f;
        public Color godRayColor = new Color(1f, 0.95f, 0.85f, 1f);

        [Header("Clouds")]
        public float cloudHeight = 40f;
        [Range(0.001f, 0.3f)] public float cloudScale = 0.03f;
        [Range(0f, 1f)] public float cloudThreshold = 0.5f;
        [Range(0, 8)] public int cloudBands = 4;
        [Range(0f, 2f)] public float cloudSpeed = 0.25f;
        public Vector2 cloudWind = new Vector2(1f, 0.6f);
        [Range(0f, 1f)] public float cloudShadowStrength = 0.35f;

        [Header("Shadow Sampling")]
        public bool useMainLightShadow = true;
    }

    public Settings settings = new Settings();

    class Pass : ScriptableRenderPass
    {
        static readonly int _BlitTexture = Shader.PropertyToID("_BlitTexture");
        static readonly int _BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        
        static readonly int _InvViewProj = Shader.PropertyToID("_InvViewProj");
        static readonly int _CameraWS = Shader.PropertyToID("_CameraWS");
        static readonly int _SunDir = Shader.PropertyToID("_SunDir");
        static readonly int _SunColor = Shader.PropertyToID("_SunColor");
        static readonly int _Intensity = Shader.PropertyToID("_Intensity");
        static readonly int _RaymarchSteps = Shader.PropertyToID("_RaymarchSteps");
        static readonly int _MaxDistance = Shader.PropertyToID("_MaxDistance");
        static readonly int _RayDensity = Shader.PropertyToID("_RayDensity");
        static readonly int _Scattering = Shader.PropertyToID("_Scattering");
        static readonly int _GodRayColor = Shader.PropertyToID("_GodRayColor");
        static readonly int _CloudHeight = Shader.PropertyToID("_CloudHeight");
        static readonly int _CloudScale = Shader.PropertyToID("_CloudScale");
        static readonly int _CloudThreshold = Shader.PropertyToID("_CloudThreshold");
        static readonly int _CloudBands = Shader.PropertyToID("_CloudBands");
        static readonly int _CloudSpeed = Shader.PropertyToID("_CloudSpeed");
        static readonly int _CloudWind = Shader.PropertyToID("_CloudWind");
        static readonly int _CloudShadowStrength = Shader.PropertyToID("_CloudShadowStrength");
        static readonly int _UseShadow = Shader.PropertyToID("_UseShadow");

        readonly Settings settings;
        readonly Material material;

        class PassData
        {
            public TextureHandle source;
            public TextureHandle cameraDepth;
            public Material material;
            public UniversalCameraData cameraData;
            public UniversalLightData lightData;
            public Settings settings;
        }

        public Pass(Settings settings, Material material)
        {
            this.settings = settings;
            this.material = material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null) return;

            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var lightData = frameData.Get<UniversalLightData>();

            TextureHandle source = resourceData.activeColorTexture;
            if (!source.IsValid()) return;

            TextureHandle cameraDepth = resourceData.cameraDepth;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("PixelArtGodRays", out var passData))
            {
                if (cameraDepth.IsValid())
                    builder.UseTexture(cameraDepth, AccessFlags.Read);
                
                builder.SetRenderAttachment(source, 0);

                passData.source = source;
                passData.cameraDepth = cameraDepth;
                passData.material = material;
                passData.cameraData = cameraData;
                passData.lightData = lightData;
                passData.settings = settings;

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    if (data.material == null) return;
                    
                    SetupMaterialStatic(data.cameraData, data.lightData, data.settings, data.material);
                    
                    data.material.SetTexture(_BlitTexture, data.source);
                    data.material.SetVector(_BlitScaleBias, new Vector4(1, 1, 0, 0));
                    
                    Blitter.BlitTexture(ctx.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }

        static void SetupMaterialStatic(UniversalCameraData cameraData, UniversalLightData lightData, 
            Settings settings, Material material)
        {
            if (material == null) return;

            var cam = cameraData.camera;

            Matrix4x4 view = cameraData.GetViewMatrix();
            // TODO:
            Matrix4x4 proj = cameraData.GetGPUProjectionMatrix();
            Matrix4x4 invVP = (proj * view).inverse;

            material.SetMatrix(_InvViewProj, invVP);
            material.SetVector(_CameraWS, cam.transform.position);

            Vector3 sunDir = Vector3.down;
            Color sunColor = Color.white;

            int mainLightIndex = lightData.mainLightIndex;
            if (mainLightIndex >= 0 && mainLightIndex < lightData.visibleLights.Length)
            {
                var visibleLight = lightData.visibleLights[mainLightIndex];
        
                if (visibleLight.light != null && visibleLight.light.type == LightType.Directional)
                {
                    sunDir = -visibleLight.light.transform.forward.normalized;
                    sunColor = visibleLight.light.color * visibleLight.light.intensity;
                }
                else
                {
                    sunDir = -visibleLight.localToWorldMatrix.GetColumn(2).normalized;
                    sunColor = visibleLight.finalColor;
                }
            }

            if (sunDir.y > -0.1f)
            {
                sunDir = new Vector3(0, -1, 0);
            }

            material.SetVector(_SunDir, sunDir);
            material.SetColor(_SunColor, sunColor);

            material.SetFloat(_Intensity, settings.intensity);
            material.SetFloat(_RaymarchSteps, settings.raymarchSteps);
            material.SetFloat(_MaxDistance, settings.maxDistance);
            material.SetFloat(_RayDensity, settings.rayDensity);
            material.SetFloat(_Scattering, settings.scattering);
            material.SetColor(_GodRayColor, settings.godRayColor);

            material.SetFloat(_CloudHeight, settings.cloudHeight);
            material.SetFloat(_CloudScale, settings.cloudScale);
            material.SetFloat(_CloudThreshold, settings.cloudThreshold);
            material.SetFloat(_CloudBands, settings.cloudBands);
            material.SetFloat(_CloudSpeed, settings.cloudSpeed);
            material.SetVector(_CloudWind, settings.cloudWind);
            material.SetFloat(_CloudShadowStrength, settings.cloudShadowStrength);

            material.SetFloat(_UseShadow, settings.useMainLightShadow ? 1f : 0f);
        }
    }

    Pass pass;
    Material material;

    public override void Create()
    {
        if (settings.shader != null && material == null)
            material = CoreUtils.CreateEngineMaterial(settings.shader);

        if (pass == null)
        {
            pass = new Pass(settings, material)
            {
                renderPassEvent = settings.passEvent
            };
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.shader == null || material == null) return;
        renderer.EnqueuePass(pass);
    }
}