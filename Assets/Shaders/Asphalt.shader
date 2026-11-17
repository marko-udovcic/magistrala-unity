Shader "Magistrala/Asphalt"
{
    Properties
    {
        [MainTexture] [NoScaleOffset] _BaseMap("Asphalt Albedo", 2D) = "white" {}
        [Normal] [NoScaleOffset] _BumpMap("Asphalt Normal (OpenGL)", 2D) = "bump" {}
        [NoScaleOffset] _RoughMap("Roughness (R)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (0.62,0.62,0.64,1)
        _Tiling("Texture Tile Size (m)", Float) = 4
        _BumpScale("Normal Strength", Range(0,2)) = 1
        _SmoothMin("Minimum Smoothness", Range(0,1)) = 0.18
        _SmoothMax("Maximum Smoothness", Range(0,1)) = 0.62
        _SheenScale("Sheen Patch Size (m)", Float) = 22
        _SheenAmount("Sheen Amount", Range(0,1)) = 0.55
        _ColorVar("Macro Brightness Variation", Range(0,1)) = 0.10
        _PatchAmount("Repair Patch Density", Range(0,1)) = 0.35
        _TrackDarken("Tyre Track Darkening", Range(0,1)) = 0.12
        _TrackSmooth("Tyre Track Extra Smoothness", Range(0,1)) = 0.25
        _EdgeDust("Edge Dust", Range(0,1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"


        // Shared by every pass, including ShadowCaster and depth passes.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Tiling;
            float _BumpScale;
            float _SmoothMin;
            float _SmoothMax;
            float _SheenScale;
            float _SheenAmount;
            float _ColorVar;
            float _PatchAmount;
            float _TrackDarken;
            float _TrackSmooth;
            float _EdgeDust;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_RoughMap); SAMPLER(sampler_RoughMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 groundMetres : TEXCOORD0;
            float2 roadUS : TEXCOORD1;
            float2 roadWidths : TEXCOORD2;
            float2 lightmapUV : TEXCOORD3; // Optional: UV1/UV2 are road data.
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            float2 roadUS : TEXCOORD4;
            float2 roadWidths : TEXCOORD5;
            float2 lightmapUV : TEXCOORD6;
            half4 fogAndLight : TEXCOORD7;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings LitVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
            output.positionCS = p.positionCS;
            output.positionWS = p.positionWS;
            output.normalWS = n.normalWS;
            output.tangentWS = float4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            output.uv = input.groundMetres / max(_Tiling, 0.001);
            output.roadUS = input.roadUS;
            output.roadWidths = input.roadWidths;
            output.lightmapUV = input.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
            output.fogAndLight.x = ComputeFogFactor(p.positionCS.z);
            output.fogAndLight.yzw = VertexLighting(p.positionWS, n.normalWS);
            return output;
        }
        float3 SurfaceNormalWS(Varyings input)
        {
            float3 n = normalize(input.normalWS);
            float3 t = normalize(input.tangentWS.xyz - n * dot(n, input.tangentWS.xyz));
            float3 b = cross(n, t) * input.tangentWS.w;
            float3 normalTS = float3(UnpackNormalScale(
                SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale));
            return normalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);
        }
        float Hash21(float2 p)
        {
            float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }
        float ValueNoise(float2 p)
        {
            float2 cell = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash21(cell), Hash21(cell + float2(1,0)), f.x),
                        lerp(Hash21(cell + float2(0,1)), Hash21(cell + float2(1,1)), f.x), f.y);
        }
        float MacroFBM(float2 p)
        {
            // Three octaves only; shared by sheen and brightness variation.
            return (ValueNoise(p) * 4.0 + ValueNoise(p * 2.03 + 17.1) * 2.0
                    + ValueNoise(p * 4.11 + 41.7)) / 7.0;
        }
        float RepairMask(float2 su)
        {
            float2 cell = floor(su / float2(6.0, 3.0));
            float2 local = frac(su / float2(6.0, 3.0));
            float seed = Hash21(cell + 71.3);
            // At default density, roughly 16% of cells contain a repair.
            float enabled = step(1.0 - saturate(_PatchAmount) * 0.45, seed);
            float2 centre = float2(Hash21(cell + 9.2), Hash21(cell + 21.8)) * 0.2 + 0.4;
            float2 extent = float2(0.18, 0.20) + float2(seed, Hash21(cell + 53.6)) * 0.18;
            float2 d = abs(local - centre) - extent;
            float2 aa = max(fwidth(local), float2(0.008, 0.008));
            float2 mask = 1.0 - smoothstep(-aa, aa, d);
            return enabled * mask.x * mask.y;
        }
        float TrackMask(float2 us, float2 widths)
        {
            if (widths.x <= 0.0 || widths.y <= 0.0) return 0.0;
            float halfWidth = widths.x;
            float laneWidth = widths.y;
            float lane = floor((us.x + halfWidth) / laneWidth);
            float laneCentre = -halfWidth + (lane + 0.5) * laneWidth;
            float distanceToBand = abs(abs(us.x - laneCentre) - 0.9);
            float aa = max(fwidth(us.x), 0.015);
            // 0.55 m full width, including soft shoulders.
            float band = 1.0 - smoothstep(0.19 - aa, 0.275 + aa, distanceToBand);
            float breaks = smoothstep(0.18, 0.50, ValueNoise(float2(us.y * 0.32, lane * 7.1)));
            return band * breaks * (1.0 - smoothstep(halfWidth - 0.08, halfWidth, abs(us.x)));
        }
        float4 ShadowCoordinates(float3 positionWS)
        {
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                return ComputeScreenPos(TransformWorldToHClip(positionWS));
            #else
                return TransformWorldToShadowCoord(positionWS);
            #endif
        }
        half4 LitFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float macro = MacroFBM(input.positionWS.xz / max(_SheenScale, 0.001));
            float sheenMask = saturate(_SheenAmount * smoothstep(0.25, 0.75, macro));
            float tracks = TrackMask(input.roadUS, input.roadWidths);
            bool hasRoadFrame = input.roadWidths.x > 0.0 && input.roadWidths.y > 0.0;
            float repairs = RepairMask(hasRoadFrame ? input.roadUS.yx : input.positionWS.xz);
            float roughness = SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, input.uv).r;
            float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
            albedo *= 1.0 + (macro * 2.0 - 1.0) * _ColorVar;
            albedo *= (1.0 - 0.06 * sheenMask) * (1.0 - tracks * _TrackDarken) * (1.0 - 0.15 * repairs);
            float smoothness = saturate(lerp(_SmoothMin, _SmoothMax, sheenMask) * (1.0 - 0.6 * roughness));
            smoothness = saturate(smoothness + tracks * _TrackSmooth + repairs * 0.12);
            float dust = 0.0;
            if (hasRoadFrame)
                dust = smoothstep(input.roadWidths.x - 0.5, input.roadWidths.x, abs(input.roadUS.x)) * _EdgeDust;
            albedo = lerp(albedo, albedo * 1.55 + float3(0.06,0.05,0.035), dust);
            smoothness *= 1.0 - dust;

            InputData data = (InputData)0;
            data.positionWS = input.positionWS;
            data.positionCS = input.positionCS;
            data.normalWS = half3(SurfaceNormalWS(input));
            data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            data.shadowCoord = ShadowCoordinates(input.positionWS);
            data.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogAndLight.x);
            data.vertexLighting = input.fogAndLight.yzw;
            #if defined(LIGHTMAP_ON)
                data.bakedGI = SampleLightmap(input.lightmapUV, float2(0,0), data.normalWS);
            #else
                data.bakedGI = SampleSH(data.normalWS);
            #endif
            data.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
            data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            SurfaceData surface = (SurfaceData)0;
            surface.albedo = half3(albedo);
            surface.alpha = 1.0;
            surface.metallic = 0.0;
            surface.smoothness = half(smoothness);
            surface.normalTS = half3(0,0,1); // data.normalWS already contains the mapped normal.
            surface.occlusion = 1.0;
            half4 result = UniversalFragmentPBR(data, surface);
            result.rgb = MixFog(result.rgb, data.fogCoord);
            return result;
        }
        float3 _LightDirection;
        float3 _LightPosition;
        Varyings ShadowVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 p = TransformObjectToWorld(input.positionOS.xyz);
            float3 n = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDir = normalize(_LightPosition - p);
            #else
                float3 lightDir = _LightDirection;
            #endif
            output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDir)));
            return output;
        }

        Varyings DepthVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            return output;
        }
        half4 DepthFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return half4(input.positionCS.zzz, 1.0);
        }
        half4 NormalsFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 n = SurfaceNormalWS(input);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
            #else
                return half4(n, 0.0);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitVertex
            #pragma fragment LitFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
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
            #pragma vertex LitVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}
