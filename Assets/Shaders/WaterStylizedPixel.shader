Shader "Custom/WaterStylizedPixel"
{
    Properties
    {
        [Header(Base)]
        _BaseColor("Base Color", Color) = (0.52,0.75,1,0.82)
        _ColorShallow("Shallow Color", Color) = (0.42,0.82,0.88,1)
        _ColorDeep("Deep Color", Color) = (0.06,0.22,0.36,1)
        _WaterClearness("Water Clearness", Range(0,1)) = 0.55
        _FadeDistance("Fade Distance", Float) = 0.0
        _WaterDepth("Water Depth", Float) = 3.0

        [Header(Refraction)]
        _RefractionAmplitude("Refraction Amplitude", Range(0,0.08)) = 0.015
        _RefractionFrequency("Refraction Frequency", Float) = 6.0
        _RefractionSpeed("Refraction Speed", Float) = 0.35

        [Header(Vertex Waves)]
        _WaveAmp("Wave Amp", Float) = 0.08
        _WaveFreq("Wave Freq", Float) = 1.0
        _WaveSpeed("Wave Speed", Float) = 1.0

        [Header(Foam)]
        _FoamTex("Foam Tex", 2D) = "white" {}
        _FoamTiling("Foam Tiling", Float) = 4.0
        _FoamSpeed("Foam Speed", Float) = 0.2
        _FoamThreshold("Foam Threshold", Range(0,1)) = 0.55
        _ShoreFoamWidth("Shore Foam Width", Float) = 0.35
        _FoamColor("Foam Color", Color) = (1,1,1,1)

        [Header(Reflection)]
        _WaterReflectionTex("Reflection RT", 2D) = "black" {}
        _ReflectionStrength("Reflection Strength", Range(0,1)) = 0.45
        _DistortionMap("Distortion Map (RG)", 2D) = "gray" {}
        _DistortionStrength("Distortion Strength", Range(0,0.08)) = 0.02

        [Header(Surface Wave Highlights)]
        _WavePatternTex("Wave Pattern Tex", 2D) = "white" {}
        _WavePatternTiling("Wave Pattern Tiling", Float) = 3.0
        _WavePatternSpeed("Wave Pattern Speed", Vector) = (0.12,0.04,0,0)
        _WavePatternThreshold("Wave Pattern Threshold", Range(0,1)) = 0.35
        _WavePatternSoftness("Wave Pattern Softness", Range(0.001,0.5)) = 0.18
        _WavePatternStrength("Wave Pattern Strength", Range(0,2)) = 1.0
        _WaveHighlightColor("Wave Highlight Color", Color) = (1,1,1,1)

        [Header(Pixel)]
        _PixelStep("Pixel Step", Range(1,8)) = 4

        [Header(Debug)]
        _DebugWaveMask("Debug Wave Mask (0/1)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Pass
        {
            Name "Water"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_FoamTex);            SAMPLER(sampler_FoamTex);
            TEXTURE2D(_DistortionMap);      SAMPLER(sampler_DistortionMap);
            TEXTURE2D(_WaterReflectionTex); SAMPLER(sampler_WaterReflectionTex);
            TEXTURE2D(_WavePatternTex);     SAMPLER(sampler_WavePatternTex);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _ColorShallow, _ColorDeep, _FoamColor, _WaveHighlightColor;
            float _WaterClearness, _FadeDistance, _WaterDepth;
            float _RefractionAmplitude, _RefractionFrequency, _RefractionSpeed;
            float _WaveAmp, _WaveFreq, _WaveSpeed;
            float _FoamTiling, _FoamSpeed, _FoamThreshold, _ShoreFoamWidth;
            float _ReflectionStrength, _DistortionStrength;
            float _WavePatternTiling, _WavePatternThreshold, _WavePatternSoftness, _WavePatternStrength;
            float4 _WavePatternSpeed;
            float _PixelStep, _DebugWaveMask;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 p = v.positionOS.xyz;

                float t = _Time.y * _WaveSpeed;
                float w1 = sin((p.x + t) * _WaveFreq);
                float w2 = sin((p.z - t * 0.9) * (_WaveFreq * 1.13));
                p.y += (w1 * w2) * _WaveAmp;

                VertexPositionInputs vp = GetVertexPositionInputs(p);
                o.positionCS = vp.positionCS;
                o.positionWS = vp.positionWS;
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(vp.positionCS);
                return o;
            }

            float DepthFade(float2 uv, float surfaceEyeDepth)
            {
                float sceneRaw = SampleSceneDepth(uv);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                return saturate((sceneEye - surfaceEyeDepth - _FadeDistance) / max(_WaterDepth, 1e-4));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / max(i.screenPos.w, 1e-6);

                float2 pixelSize = _PixelStep / _ScreenParams.xy;
                float2 pixUV = (floor(screenUV / pixelSize) + 0.5) * pixelSize;

                float2 duv1 = i.uv * _RefractionFrequency + _Time.yy * _RefractionSpeed;
                float2 duv2 = i.uv * (_RefractionFrequency * 0.73) - _Time.yy * (_RefractionSpeed * 0.61);
                float2 d1 = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, duv1).rg * 2.0 - 1.0;
                float2 d2 = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, duv2).rg * 2.0 - 1.0;
                float2 distortion = (d1 + d2) * 0.5 * _DistortionStrength;

                float surfaceEyeDepth = LinearEyeDepth(i.positionCS.z / i.positionCS.w, _ZBufferParams);
                float depthFade = DepthFade(pixUV, surfaceEyeDepth);

                float2 refrUV = saturate(pixUV + distortion * (_RefractionAmplitude * (0.2 + depthFade)));

                half3 refr = SampleSceneColor(refrUV).rgb;
                half3 depthTint = lerp(_ColorShallow.rgb, _ColorDeep.rgb, depthFade);
                half3 waterUnder = lerp(depthTint, refr, _WaterClearness);

                float2 reflUV = float2(1.0 - refrUV.x, refrUV.y);
                half3 reflection = SAMPLE_TEXTURE2D(_WaterReflectionTex, sampler_WaterReflectionTex, reflUV).rgb;

                float3 N = float3(0,1,0);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.positionWS);
                float fres = pow(1.0 - saturate(dot(N, V)), 3.0);
                float reflectLerp = saturate(_ReflectionStrength * (0.35 + fres));

                half3 col = lerp(waterUnder, reflection, reflectLerp);

                float shore = 1.0 - saturate(depthFade / max(_ShoreFoamWidth, 1e-4));
                float2 foamUV = i.uv * _FoamTiling + _Time.yy * _FoamSpeed;
                float foamNoise = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV).r;
                float foamSurf = step(_FoamThreshold, foamNoise);
                float foamMix = saturate(max(shore, foamSurf * (0.4 + 0.6 * depthFade)));
                col = lerp(col, _FoamColor.rgb, foamMix * _FoamColor.a);

                col *= _BaseColor.rgb;

                float2 wuv1 = i.uv * _WavePatternTiling + _WavePatternSpeed.xy * _Time.y;
                float2 wuv2 = i.uv * (_WavePatternTiling * 0.73) - _WavePatternSpeed.xy * 0.67 * _Time.y;
                float wp1 = SAMPLE_TEXTURE2D(_WavePatternTex, sampler_WavePatternTex, wuv1).r;
                float wp2 = SAMPLE_TEXTURE2D(_WavePatternTex, sampler_WavePatternTex, wuv2).r;
                float wv = wp1 * 0.6 + wp2 * 0.4;
                float waveMask = smoothstep(_WavePatternThreshold - _WavePatternSoftness, _WavePatternThreshold + _WavePatternSoftness, wv);

                if (_DebugWaveMask > 0.5)
                    return half4(waveMask, waveMask, waveMask, 1);

                col = saturate(col + _WaveHighlightColor.rgb * waveMask * _WavePatternStrength * 0.6);

                return half4(col, _BaseColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}