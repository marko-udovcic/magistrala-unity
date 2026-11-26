// Živice (zivica, 25. 11., Marko: "živica i zidići fale na staroj cesti"): the leaf modules of the clipped garden hedges
// (Assets/Editor/HedgeShell.cs), drawn instanced by TreeRenderer. The mesh carries the look —
//   * normal: the hedge face's outward normal bent a little toward the card's own (the hedge lit as one clipped volume)
//   * COLOR.rgb: per-card tint (new growth lighter, old leaves darker / bluer), COLOR.a: depth occlusion (1 on the skin,
//     ~0.45 deep in the hedge and near its foot)
// plus a per-instance shade from the instance's position (±_Vary: pittosporum / laurel / privet are one module set). Lighting
// as Foliage (wrapped diffuse, translucency, ambient floor, received-shadow floor, SSAO). Alpha-clipped with alpha-to-coverage,
// two-sided. TreeRenderer ends the modules at ~115 m; before that they dither out over _Far (m from the camera, per pixel) so
// the box hedge underneath (Magistrala/Facade's hedge layer) takes over without a pop.
Shader "Magistrala/Hedge"
{
    Properties
    {
        [MainTexture] _BaseMap("Leaf atlas", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.45
        _Wrap("Diffuse wrap", Range(0,1)) = 0.45
        _Trans("Translucency", Range(0,2)) = 0.35
        _Amb("Ambient multiplier", Range(0,3)) = 1.15
        _ShadowFloor("Light reaching shadowed leaves", Range(0,1)) = 0.3
        _Far("Dither out from x to y m", Vector) = (85, 112, 0, 0)
        _Vary("Per-instance colour variation", Range(0,0.5)) = 0.14
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff, _Wrap, _Trans, _Amb, _ShadowFloor, _Vary;
            float4 _Far;
        CBUFFER_END
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        float _Wetness;
        float _VegFadeFrame;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            half4 color : TEXCOORD3;
            half fog : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float Hash21(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
        // far dither (complementary to nothing: the box underneath is always drawn)
        void FarClip(float3 posWS, float2 pix)
        {
            float d = length(posWS.xz - _WorldSpaceCameraPos.xz);
            half v = _Far.y > _Far.x ? saturate((_Far.y - d) / (_Far.y - _Far.x)) : 1;
            UNITY_BRANCH if (v < 1)
            {
                pix += _VegFadeFrame * float2(47, 17);
                half n = frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
                clip(v - n - 1e-3);
            }
        }

        Varyings LitVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            // per instance: brightness ± _Vary, hue toward yellow-green (privet) or a dark blue-green (laurel)
            float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
            float h1 = Hash21(floor(origin.xz * 0.25) * 0.731 + 17.0), h2 = Hash21(floor(origin.zx * 0.25) * 1.37 + 3.1);
            half b = 1 + (h1 - 0.5) * 2 * _Vary, t = (h2 - 0.5) * 2 * _Vary;
            o.color = half4(input.color.rgb * b * half3(1 + 0.5 * t, 1 + 0.15 * t, 1 - 0.8 * t), input.color.a);
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }

        half4 LitFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            FarClip(input.positionWS, input.positionCS.xy);
            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            clip(tex.a - _Cutoff);
            half occ = input.color.a;
            half3 albedo = tex.rgb * input.color.rgb;
            half3 n = NormalizeNormalPerPixel(input.normalWS);
            float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 sc = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
            #else
                float4 sc = TransformWorldToShadowCoord(input.positionWS);
            #endif
            Light L = GetMainLight(sc, input.positionWS, half4(1,1,1,1));
            half sh = lerp(_ShadowFloor, 1, L.shadowAttenuation);
            half diff = saturate((dot(n, L.direction) + _Wrap) / (1 + _Wrap));
            half back = pow(saturate(dot(v, -L.direction)), 3) * _Trans * (0.35 + 0.65 * sh);
            half aoInd = 1, aoDir = 1;
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                aoInd = ao.indirectAmbientOcclusion; aoDir = ao.directAmbientOcclusion;
            #endif
            half3 amb = max(SampleSH(n), SampleSH(half3(0, 1, 0)) * 0.6) * _Amb * occ * aoInd;
            half3 c = albedo * (amb + L.color * (diff * sh * aoDir * lerp(0.55, 1, occ) + back * half3(0.85, 1, 0.55)));
            UNITY_BRANCH if (_Wetness > 0.001) c *= 1 - 0.25 * _Wetness;
            c = MixFog(c, input.fog);
            return half4(c, 1);
        }

        float3 _LightDirection;
        float3 _LightPosition;
        Varyings ShadowVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, o);
            float3 p = TransformObjectToWorld(input.positionOS.xyz);
            float3 n = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - p);
            #else
                float3 lightDir = _LightDirection;
            #endif
            n = dot(n, lightDir) < 0 ? -n : n;
            o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDir)));
            o.positionWS = p;
            o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return o;
        }
        Varyings DepthVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return o;
        }
        half4 DepthFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            FarClip(input.positionWS, input.positionCS.xy);
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            return input.positionCS.z;
        }
        half4 NormalsFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            FarClip(input.positionWS, input.positionCS.xy);
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
            float3 n = NormalizeNormalPerPixel(input.normalWS);
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
            AlphaToMask On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitVertex
            #pragma fragment LitFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
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
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment DepthFragment
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
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}
