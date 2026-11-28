// The pack's buildings and facade details (UKRAINIAN CITY, PaketZgrade / PaketDetalji): one texture array per material set,
// lit exactly like Magistrala/Facade (UniversalFragmentPBR, metallic 0, part of the occlusion folded into the albedo), so a
// pack hall in the shade reads like our plastered houses next to it, not like the pack's own (much darker) Lit graphs.
// _Albedo array: rgb albedo (the pack's base map x its mask AO). _Normal array: rg normal xy, b cutout alpha (JPG flipbooks). UV0 = texture coordinates (metres x the material's world scale for the pack's triplanar materials,
// else the mesh UV x tiling). UV1: x = layer, y = smoothness (the layer's mean), z = height above the building's ground (m, contact
// darkening and splash dirt), w = dirt 0..1. COLOR rgb = the material's tint (linear).
Shader "Magistrala/Paket"
{
    Properties
    {
        _Albedo("Albedo array (a cutout)", 2DArray) = "" {}
        _Normal("Normal array (r smooth, g y, a x)", 2DArray) = "" {}
        _Grime("Grime masks (r streaks, g runs, b drips)", 2D) = "black" {}
        _NormalScale("Normal strength", Range(0,2)) = .9
        _Gain("Albedo gain", Range(0.5,2)) = 1
        _Tint("Tint (variant)", Color) = (1,1,1,1)
        [Toggle(_ALPHATEST_ON)] _AlphaTest("Cutout", Float) = 0
        _Cutoff("Cutoff", Range(0,1)) = .5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half _NormalScale;
            half _Gain;
            half4 _Tint;
            half _Cutoff;
        CBUFFER_END
        TEXTURE2D_ARRAY(_Albedo); SAMPLER(sampler_Albedo);
        TEXTURE2D_ARRAY(_Normal); SAMPLER(sampler_Normal);
        TEXTURE2D(_Grime); SAMPLER(sampler_Grime);
        float _Wetness; // global (WeatherSystem)

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float4 data : TEXCOORD1;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            float4 data : TEXCOORD4;
            half4 color : COLOR;
            half fog : TEXCOORD5;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
        }

        Varyings LitVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = n.normalWS;
            o.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            o.uv = input.uv;
            o.data = input.data;
            o.color = input.color;
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }

        half4 LitFragment(Varyings input, bool front : SV_IsFrontFace) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float layer = round(input.data.x);
            half4 ns = SAMPLE_TEXTURE2D_ARRAY(_Normal, sampler_Normal, input.uv, layer);
            #if defined(_ALPHATEST_ON)
                clip(ns.b - _Cutoff);
            #endif
            half4 tex = SAMPLE_TEXTURE2D_ARRAY(_Albedo, sampler_Albedo, input.uv, layer);
            half3 nt; nt.xy = (ns.rg * 2 - 1) * _NormalScale; nt.z = sqrt(saturate(1 - dot(nt.xy, nt.xy)));
            half3 albedo = tex.rgb * input.color.rgb * _Tint.rgb * _Gain;
            half3 nWS = normalize(input.normalWS);
            #if defined(_ALPHATEST_ON)
                nWS = front ? nWS : -nWS;
            #endif
            half3 tWS = normalize(input.tangentWS.xyz);
            half3 bWS = cross(nWS, tWS) * input.tangentWS.w;
            half3 normalWS = normalize(nt.x * tWS + nt.y * bWS + max(nt.z, .2) * nWS);

            // light weathering as on our facades: broad blotches, rain streaks, splash dirt and contact shadow at the foot
            float3 wp = input.positionWS;
            float dirt = input.data.w;
            half vertical = 1 - saturate(abs(nWS.y) * 2);
            float along = dot(wp.xz, normalize(float2(-nWS.z, nWS.x) + 1e-4));
            float blot = Noise(float2(along * .5 + wp.y * .13, wp.y * .45));
            half3 leak = SAMPLE_TEXTURE2D(_Grime, sampler_Grime, float2(along * .22, wp.y * .18)).rgb;
            float foot = saturate(1 - input.data.z / 1.1);
            half grime = vertical * saturate(dirt * saturate(blot * .5 + leak.r * leak.r * .8 + foot * .6));
            albedo *= .95 + .1 * blot * vertical;
            albedo *= 1 - .35 * grime;
            albedo = lerp(albedo, albedo * half3(.92, .88, .8), grime * .6);
            half contact = saturate(1 - input.data.z / .8); contact *= contact;
            half ao = 1 - vertical * .38 * contact;
            albedo *= lerp(1, ao, .55);

            InputData data = (InputData)0;
            data.positionWS = wp;
            data.positionCS = input.positionCS;
            data.normalWS = normalWS;
            data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(wp);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                data.shadowCoord = ComputeScreenPos(TransformWorldToHClip(wp));
            #else
                data.shadowCoord = TransformWorldToShadowCoord(wp);
            #endif
            data.fogCoord = InitializeInputDataFog(float4(wp, 1), input.fog);
            data.bakedGI = SampleSH(normalWS);
            data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            data.shadowMask = half4(1, 1, 1, 1);
            SurfaceData surface = (SurfaceData)0;
            surface.albedo = albedo;
            surface.alpha = 1;
            surface.metallic = 0;
            surface.smoothness = saturate(input.data.y) * (1 - grime * .5);
            UNITY_BRANCH if (_Wetness > 0.001)
            {
                half up = saturate(nWS.y * 1.5 - 0.3) * _Wetness;
                half side = _Wetness * .4;
                surface.albedo *= 1.0 - 0.35 * max(up, side);
                surface.smoothness = lerp(surface.smoothness, 0.75, max(up, side * .5));
            }
            surface.normalTS = half3(0, 0, 1);
            surface.occlusion = ao;
            half4 result = UniversalFragmentPBR(data, surface);
            result.rgb = MixFog(result.rgb, data.fogCoord);
            return result;
        }

        float3 _LightDirection;
        float3 _LightPosition;
        Varyings ShadowVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            float3 p = TransformObjectToWorld(input.positionOS.xyz);
            float3 n = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - p);
            #else
                float3 lightDir = _LightDirection;
            #endif
            o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDir)));
            o.uv = input.uv; o.data = input.data;
            return o;
        }
        Varyings DepthVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.uv = input.uv; o.data = input.data;
            return o;
        }
        void Clip(Varyings input)
        {
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D_ARRAY(_Normal, sampler_Normal, input.uv, round(input.data.x)).b - _Cutoff);
            #endif
        }
        half4 DepthFragment(Varyings input) : SV_Target { Clip(input); return input.positionCS.z; }
        half4 NormalsFragment(Varyings input) : SV_Target
        {
            Clip(input);
            float3 n = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
            #else
                return half4(n, 0);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitVertex
            #pragma fragment LitFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVertex
            #pragma fragment DepthFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVertex
            #pragma fragment NormalsFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}
