// Buildings (HouseBuilder / BuildingsFromTiles): one material for walls, roofs, trims, shutters and the small clutter of a chunk.
// Texture array layers (BuildingTextures): 0 plaster, 1 stone ashlar, 2 roof tiles (kupa kanalica), 3 brick, 4 concrete,
// 5 shutter louvres, 6 plain (painted metal / plastic). UV0 = world metres on the face.
// UV1: x = lower layer + 8 * upper layer, y = height above the building's ground (m), z = height below the wall top (m),
//      w = dirt 0..1. COLOR: rgb = tint of the tintable layers (linear), a = height of the lower / upper layer boundary / 40 m
//      (a horizontal line: stone ground floor under plaster, plaster under an unfinished brick top floor).
Shader "Magistrala/Facade"
{
    Properties
    {
        _Albedo("Albedo array", 2DArray) = "" {}
        _Normal("Normal array (linear rgb)", 2DArray) = "" {}
        _NormalScale("Normal strength", Range(0,2)) = .8
        _Smoothness("Smoothness", Range(0,1)) = 0.12
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
            half _Smoothness;
        CBUFFER_END
        TEXTURE2D_ARRAY(_Albedo); SAMPLER(sampler_Albedo);
        TEXTURE2D_ARRAY(_Normal);
        float _Wetness; // global (WeatherSystem)

        // metres per texture repeat, per layer; tintable layers take the vertex colour, the others keep their own colour
        static const float TILE[8] = { 1.6, 2.0, 3.0, 2.0, 2.7, 0.55, 1.0, 1.0 };
        static const float TINTED[8] = { 1, 0, 1, 0, 1, 1, 1, 1 };
        static const float GLOSS[8] = { 0.1, 0.12, 0.28, 0.08, 0.1, 0.3, 0.45, 0.2 };

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

        half4 LitFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float code = round(input.data.x);
            float lower = fmod(code, 8), upper = floor(code / 8);
            float layer = input.data.y < input.color.a * 40 ? lower : upper;
            int li = (int)layer;
            float2 uv = input.uv / TILE[li];
            half3 tex = SAMPLE_TEXTURE2D_ARRAY(_Albedo, sampler_Albedo, uv, layer).rgb;
            half3 nt = SAMPLE_TEXTURE2D_ARRAY(_Normal, sampler_Albedo, uv, layer).rgb * 2 - 1;
            nt.xy *= _NormalScale;
            half3 albedo = tex * lerp(half3(1, 1, 1), input.color.rgb, TINTED[li]);
            half3 nWS = normalize(input.normalWS);
            half3 tWS = normalize(input.tangentWS.xyz);
            half3 bWS = cross(nWS, tWS) * input.tangentWS.w;
            half3 normalWS = normalize(nt.x * tWS + nt.y * bWS + max(nt.z, .2) * nWS);

            // weathering: large blotches, rain streaks from the eaves, splash dirt at the foot of the wall, faded roof patches
            float dirt = input.data.w;
            float3 wp = input.positionWS;
            half vertical = 1 - saturate(abs(nWS.y) * 2);
            float along = dot(wp.xz, normalize(float2(-nWS.z, nWS.x) + 1e-4));
            float blot = Noise(wp.xz * .23 + wp.y * .31) * .6 + Noise(float2(along, wp.y) * .9) * .4;
            float streak = Noise(float2(along * 3.1, wp.y * .12)) * Noise(float2(along * 7.3, wp.y * .25 + 3));
            float eave = saturate(1 - input.data.z / 4.5);
            float foot = saturate(1 - input.data.y / 1.1);
            half grime = vertical * dirt * saturate(blot * .5 + streak * eave * 2.0 + foot * .6);
            if (li == 2) // roof: ±12 % tone patches, darker old tiles
                albedo *= .86 + .28 * Noise(wp.xz * .35) - .12 * dirt * Noise(wp.xz * 1.7 + 7);
            albedo *= 1 - .42 * grime;
            albedo = lerp(albedo, albedo * half3(.92, .9, .84), grime * .5);

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
            surface.smoothness = GLOSS[li] * (1 - grime * .5);
            UNITY_BRANCH if (_Wetness > 0.001)
            {
                half up = saturate(nWS.y * 1.5 - 0.3) * _Wetness;
                half side = _Wetness * (0.35 + 0.3 * streak * eave);
                surface.albedo *= 1.0 - 0.35 * max(up, side);
                surface.smoothness = lerp(surface.smoothness, 0.75, max(up, side * .5));
            }
            surface.normalTS = half3(0, 0, 1);
            surface.occlusion = 1 - foot * .25 * vertical;
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
            return o;
        }
        Varyings DepthVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return o;
        }
        half4 DepthFragment(Varyings input) : SV_Target { return input.positionCS.z; }
        half4 NormalsFragment(Varyings input) : SV_Target
        {
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
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitVertex
            #pragma fragment LitFragment
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
            HLSLPROGRAM
            #pragma target 3.5
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
            #pragma target 3.5
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
            #pragma target 3.5
            #pragma vertex DepthVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}
