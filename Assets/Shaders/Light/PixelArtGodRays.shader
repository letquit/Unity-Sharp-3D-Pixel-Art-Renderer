Shader "Hidden/PixelArt/GodRays"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "PixelArtGodRays"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            
            #define _MainTex _BlitTexture
            #define sampler_MainTex sampler_BlitTexture

            float4x4 _InvViewProj;
            float3 _CameraWS;
            float3 _SunDir;
            float4 _SunColor;
            float _Intensity;
            float _RaymarchSteps;
            float _MaxDistance;
            float _RayDensity;
            float _Scattering;
            float4 _GodRayColor;
            float _CloudHeight;
            float _CloudScale;
            float _CloudThreshold;
            float _CloudBands;
            float _CloudSpeed;
            float2 _CloudWind;
            float _CloudShadowStrength;
            float _UseShadow;
            float4 _BlitScaleBias;

            #define DEBUG_FORCE_EFFECT 0
            #define DEBUG_SKIP_DEPTH 0
            #define DEBUG_SHOW_CLOUDS 0

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                return o;
            }

            float hash21(float2 p) 
            { 
                p = frac(p * float2(123.34, 456.21)); 
                p += dot(p, p + 34.45); 
                return frac(p.x * p.y); 
            }

            float noise2D(float2 p) 
            { 
                float2 i = floor(p); 
                float2 f = frac(p); 
                float a = hash21(i); 
                float b = hash21(i + float2(1, 0)); 
                float c = hash21(i + float2(0, 1)); 
                float d = hash21(i + float2(1, 1)); 
                float2 u = f * f * (3.0 - 2.0 * f); 
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y); 
            }

            float fbm(float2 p) 
            { 
                float v = 0; 
                float a = 0.5; 
                float2 shift = float2(100, 100); 
                for (int i = 0; i < 4; i++) 
                { 
                    v += a * noise2D(p); 
                    p = p * 2.02 + shift; 
                    a *= 0.5; 
                } 
                return saturate(v);
            }

            float ApplySteppedBanding(float noise, float threshold, float stepCount) 
            { 
                if (noise < threshold) return 0.0; 
                if (stepCount <= 0.0) return 1.0; 
                float normalized = saturate((noise - threshold) / (1.0 - threshold)); 
                float bands = stepCount + 1.0; 
                return ceil(normalized * bands) / bands; 
            }

            float SampleCloudMask(float2 uv) 
            { 
                float n = fbm(uv); 
                n = saturate(n);
                return ApplySteppedBanding(n, _CloudThreshold, _CloudBands); 
            }

            float3 GetWorldPos(float2 uv)
            {
            #if DEBUG_SKIP_DEPTH
                return _CameraWS + float3(0, _CloudHeight, 10);
            #else
                float depth = SampleSceneDepth(uv);
                
                if (depth == 0.0 || depth == 1.0) 
                    return float3(999999, 999999, 999999);
                
                float2 ndc = uv * 2.0 - 1.0;
                float4 csPos = float4(ndc, depth, 1.0);
                float4 wsPos = mul(UNITY_MATRIX_I_VP, csPos);
                return wsPos.xyz / wsPos.w;
            #endif
            }

            float GetShadowAtten(float3 worldPos)
            {
                if (_UseShadow < 0.5) return 1.0;
                #if defined(_MAIN_LIGHT_SHADOWS)
                    float4 shadowCoord = TransformWorldToShadowCoord(worldPos);
                    return MainLightRealtimeShadow(shadowCoord);
                #else
                    return 1.0;
                #endif
            }

            float CloudVisibilityAtPoint(float3 worldPos)
            {
                if (any(abs(worldPos) > 99999)) 
                    return 1.0;
                
                float denom = _SunDir.y;
                if (abs(denom) < 0.0001) 
                    return 1.0;

                float t = (_CloudHeight - worldPos.y) / denom;
                
                t = abs(t);
                
                float3 hit = worldPos + _SunDir * t;
                float2 uv = hit.xz * _CloudScale + _Time.y * _CloudSpeed * _CloudWind;

                float cloud = SampleCloudMask(uv);
                return 1.0 - cloud;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

            #if DEBUG_FORCE_EFFECT
                return half4(1, 0, 0, 1);
            #endif

            #if DEBUG_SKIP_DEPTH
                float3 surfacePos = float3(_CameraWS.x, 0, _CameraWS.z + 10);
            #else
                float3 surfacePos = GetWorldPos(i.uv);
                if (any(abs(surfacePos) > 99999))
                {
                    #if DEBUG_SHOW_CLOUDS
                        return half4(1, 0, 1, 1);
                    #else
                        return col;
                    #endif
                }
            #endif

                float surfaceVisibility = CloudVisibilityAtPoint(surfacePos);
                
            #if DEBUG_SHOW_CLOUDS
                float cloudDebug = 1.0 - surfaceVisibility;
                return half4(cloudDebug, cloudDebug, cloudDebug, 1);
            #endif

                col.rgb *= lerp(1.0, surfaceVisibility, _CloudShadowStrength);

                float3 camPos = _CameraWS;
                float3 viewDir = normalize(surfacePos - camPos);
                float viewDist = distance(camPos, surfacePos);
                float maxDist = min(viewDist, _MaxDistance);
                int steps = (int)_RaymarchSteps;
                float stepSize = maxDist / max(1, steps);

                float god = 0;
                float t = 0;

                [loop]
                for (int s = 0; s < steps; s++)
                {
                    float3 p = camPos + viewDir * t;
                    float vis = CloudVisibilityAtPoint(p);
                    float shadow = GetShadowAtten(p);
                    float sample = vis * shadow;
                    float atten = exp(-t * 0.02 * _Scattering);
                    god += sample * atten;
                    t += stepSize;
                }

                god = god * (_RayDensity / max(1, steps));
                float3 rayColor = _GodRayColor.rgb * _SunColor.rgb * god * _Intensity;
                col.rgb += rayColor;

                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}