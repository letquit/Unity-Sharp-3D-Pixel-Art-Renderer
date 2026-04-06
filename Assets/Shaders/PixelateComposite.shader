Shader "Hidden/PixelateComposite"
{
    Properties
    {
        _EdgeColor("Edge Color", Color) = (0,0,0,1)
        _EdgeStrength("Edge Strength", Range(0,2)) = 1
        _EdgeWidthPx("Edge Width (px)", Range(0.25,2)) = 1
        _EdgeDepthBias("Silhouette Depth Bias", Float) = 0.0015
        _EdgeNormalBias("Crease Normal Bias", Float) = 0.06
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "PixelateComposite"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            TEXTURE2D(_LowResTex);
            TEXTURE2D(_PixelArtMaskTex);
            SAMPLER(sampler_PixelArtMaskTex);

            float4 _LowResTex_TexelSize;
            int _EnableQuant;
            int _PaletteCount;
            float4 _Palette[256];

            float4 _EdgeColor;
            float _EdgeStrength;
            float _EdgeWidthPx;
            float _EdgeDepthBias;
            float _EdgeNormalBias;
            float _HasMaskTex;
            float _MaskThreshold;

            float _DebugView;    // 0 normal,1 mask,2 edge,3 pass tint
            float _PreQuantLift;

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                return o;
            }

            float3 QuantizePalette(float3 src)
            {
                if (_PaletteCount <= 0) return src;
                float minDist = 1e20;
                float3 best = src;
                [loop]
                for (int i = 0; i < _PaletteCount; i++)
                {
                    float3 p = _Palette[i].rgb;
                    float3 d = src - p;
                    float dist = dot(d, d);
                    if (dist < minDist) { minDist = dist; best = p; }
                }
                return best;
            }

            float MaskRaw(float2 uv)
            {
                if (_HasMaskTex < 0.5) return 1.0;
                float a = SAMPLE_TEXTURE2D(_PixelArtMaskTex, sampler_PixelArtMaskTex, uv).a;
                return a;
            }

            float MaskValueBinaryDilated(float2 uv, float2 stepUV)
            {
                if (_HasMaskTex < 0.5) return 1.0;

                float m = 0.0;
                m = max(m, MaskRaw(uv));
                m = max(m, MaskRaw(uv + float2( stepUV.x, 0)));
                m = max(m, MaskRaw(uv + float2(-stepUV.x, 0)));
                m = max(m, MaskRaw(uv + float2(0,  stepUV.y)));
                m = max(m, MaskRaw(uv + float2(0, -stepUV.y)));

                return step(_MaskThreshold, m);
            }

            bool NeighborCreatesSilhouette(float centerDepth, float2 baseUV, float2 offset)
            {
                float nd = SampleSceneDepth(baseUV + offset);
                return (centerDepth - nd) > _EdgeDepthBias;
            }

            bool NeighborCreatesCrease(float centerMask, float centerDepth, float3 centerNormal, float2 baseUV, float2 offset, float2 stepUV)
            {
                float2 nuv = baseUV + offset;
                float nd = SampleSceneDepth(nuv);

                if (abs(nd - centerDepth) > _EdgeDepthBias * 0.75) return false;

                float nMask = MaskValueBinaryDilated(nuv, stepUV);
                if (abs(centerMask - nMask) > 0.1) return false;

                float3 nn = normalize(SampleSceneNormals(nuv));
                float diff = 1.0 - saturate(dot(centerNormal, nn));
                return diff > _EdgeNormalBias;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float2 pixelCoord = floor(uv / _LowResTex_TexelSize.xy);
                float2 suv = (pixelCoord + 0.5) * _LowResTex_TexelSize.xy;

                float2 stepUV = lerp(1.0 / _ScreenParams.xy, _LowResTex_TexelSize.xy, 0.25) * _EdgeWidthPx;

                float m = MaskValueBinaryDilated(suv, stepUV);

                if (_DebugView > 0.5 && _DebugView < 1.5)
                    return half4(m, m, m, 1);

                half4 c = SAMPLE_TEXTURE2D(_LowResTex, sampler_PointClamp, suv);
                c.rgb = max(c.rgb, _PreQuantLift.xxx);

                if (_EnableQuant == 1)
                    c.rgb = QuantizePalette(c.rgb);

                float d0 = SampleSceneDepth(suv);
                float3 n0 = normalize(SampleSceneNormals(suv));

                float2 o0 = float2( stepUV.x, 0);
                float2 o1 = float2(-stepUV.x, 0);
                float2 o2 = float2(0,  stepUV.y);
                float2 o3 = float2(0, -stepUV.y);
                float2 o4 = float2( stepUV.x,  stepUV.y);
                float2 o5 = float2(-stepUV.x,  stepUV.y);
                float2 o6 = float2( stepUV.x, -stepUV.y);
                float2 o7 = float2(-stepUV.x, -stepUV.y);

                float e = 0.0;
                e += (NeighborCreatesSilhouette(d0, suv, o0) || NeighborCreatesCrease(m, d0, n0, suv, o0, stepUV)) ? 1 : 0;
                e += (NeighborCreatesSilhouette(d0, suv, o1) || NeighborCreatesCrease(m, d0, n0, suv, o1, stepUV)) ? 1 : 0;
                e += (NeighborCreatesSilhouette(d0, suv, o2) || NeighborCreatesCrease(m, d0, n0, suv, o2, stepUV)) ? 1 : 0;
                e += (NeighborCreatesSilhouette(d0, suv, o3) || NeighborCreatesCrease(m, d0, n0, suv, o3, stepUV)) ? 1 : 0;
                e += (NeighborCreatesSilhouette(d0, suv, o4) || NeighborCreatesCrease(m, d0, n0, suv, o4, stepUV)) ? 1 : 0;
                e += (NeighborCreatesSilhouette(d0, suv, o5) || NeighborCreatesCrease(m, d0, n0, suv, o5, stepUV)) ? 1 : 0;
                e += (NeighborCreatesSilhouette(d0, suv, o6) || NeighborCreatesCrease(m, d0, n0, suv, o6, stepUV)) ? 1 : 0;
                e += (NeighborCreatesSilhouette(d0, suv, o7) || NeighborCreatesCrease(m, d0, n0, suv, o7, stepUV)) ? 1 : 0;

                e = saturate((e / 8.0) * _EdgeStrength) * m;

                if (_DebugView > 1.5 && _DebugView < 2.5)
                    return half4(e, e, e, 1);

                if (_DebugView > 2.5)
                    c.rgb = lerp(c.rgb, float3(1, 0, 1), 0.25);

                c.rgb = lerp(c.rgb, _EdgeColor.rgb, e);
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}