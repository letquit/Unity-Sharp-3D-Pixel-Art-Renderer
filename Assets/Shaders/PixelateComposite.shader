Shader "Hidden/PixelateComposite"
{
    Properties
    {
        _EdgeColor("Edge Color", Color) = (0,0,0,1)
        _DepthEdgeThreshold("Depth Edge Threshold", Float) = 0.01
        _NormalEdgeThreshold("Normal Edge Threshold", Float) = 0.20
        _EdgeStrength("Edge Strength", Range(0,1)) = 1
        _EdgeWidthPx("Edge Width (px)", Range(0.25,2)) = 0.75
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "PixelateComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            TEXTURE2D(_LowResTex);

            float4 _LowResTex_TexelSize;
            int _EnableQuant;
            int _UseLabDistance;
            int _PaletteCount;
            float4 _Palette[64];

            float4 _EdgeColor;
            float _DepthEdgeThreshold;
            float _NormalEdgeThreshold;
            float _EdgeStrength;
            float _EdgeWidthPx;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                return o;
            }

            float3 rgb2xyz(float3 rgb)
            {
                rgb = pow(max(rgb, 0.0), 2.2);
                float3x3 m = float3x3(
                    0.4124564, 0.3575761, 0.1804375,
                    0.2126729, 0.7151522, 0.0721750,
                    0.0193339, 0.1191920, 0.9503041
                );
                return mul(m, rgb);
            }

            float3 xyz2lab(float3 xyz)
            {
                xyz /= float3(0.95047, 1.0, 1.08883);
                float3 f = (xyz > 0.008856) ? pow(xyz, 1.0 / 3.0) : (7.787 * xyz + 16.0 / 116.0);
                return float3(116.0 * f.y - 16.0, 500.0 * (f.x - f.y), 200.0 * (f.y - f.z));
            }

            float DistRGB(float3 a, float3 b)
            {
                float3 d = a - b;
                return dot(d, d);
            }

            float DistLab(float3 a, float3 b)
            {
                float3 la = xyz2lab(rgb2xyz(a));
                float3 lb = xyz2lab(rgb2xyz(b));
                float3 d = la - lb;
                return dot(d, d);
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
                    float dist = (_UseLabDistance == 1) ? DistLab(src, p) : DistRGB(src, p);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        best = p;
                    }
                }
                return best;
            }

            float DepthEdge(float2 uv, float2 stepUV)
            {
                float dC = SampleSceneDepth(uv);
                float dL = SampleSceneDepth(uv + float2(-stepUV.x, 0));
                float dR = SampleSceneDepth(uv + float2( stepUV.x, 0));
                float dU = SampleSceneDepth(uv + float2(0,  stepUV.y));
                float dD = SampleSceneDepth(uv + float2(0, -stepUV.y));

                float diff = max(max(abs(dC - dL), abs(dC - dR)), max(abs(dC - dU), abs(dC - dD)));
                return smoothstep(_NormalEdgeThreshold * 0.5, _NormalEdgeThreshold, diff);
            }

            float NormalEdge(float2 uv, float2 stepUV)
            {
                float3 nC = normalize(SampleSceneNormals(uv));
                float3 nL = normalize(SampleSceneNormals(uv + float2(-stepUV.x, 0)));
                float3 nR = normalize(SampleSceneNormals(uv + float2( stepUV.x, 0)));
                float3 nU = normalize(SampleSceneNormals(uv + float2(0,  stepUV.y)));
                float3 nD = normalize(SampleSceneNormals(uv + float2(0, -stepUV.y)));

                float dl = 1.0 - saturate(dot(nC, nL));
                float dr = 1.0 - saturate(dot(nC, nR));
                float du = 1.0 - saturate(dot(nC, nU));
                float dd = 1.0 - saturate(dot(nC, nD));

                float diff = max(max(dl, dr), max(du, dd));
                return smoothstep(_NormalEdgeThreshold * 0.5, _NormalEdgeThreshold, diff);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // 像素对齐采样
                float2 uv = i.uv;
                float2 pixelCoord = floor(uv / _LowResTex_TexelSize.xy);
                float2 snappedUV = (pixelCoord + 0.5) * _LowResTex_TexelSize.xy;

                float4 c = SAMPLE_TEXTURE2D(_LowResTex, sampler_PointClamp, snappedUV);

                if (_EnableQuant == 1)
                    c.rgb = QuantizePalette(c.rgb);

                float2 stepUV = lerp(1.0 / _ScreenParams.xy, _LowResTex_TexelSize.xy, 0.1) * _EdgeWidthPx;

                float eDepth = DepthEdge(snappedUV, stepUV);
                float eNormal = NormalEdge(snappedUV, stepUV);

                float edge = saturate(max(eDepth, eNormal) * _EdgeStrength);

                c.rgb = lerp(c.rgb, _EdgeColor.rgb, edge);
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}