Shader "Magistrala/CesteShoulder"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Gravel grain", 2D) = "white" {}
        [Normal] [NoScaleOffset] _BumpMap("Gravel normal", 2D) = "bump" {}
        [NoScaleOffset] _RoughMap("Roughness", 2D) = "white" {}
        _GravelColor("Dusty gravel mean (sRGB)", Color) = (.335,.329,.317,1)
        _DirtColor("Outer dirt (sRGB)", Color) = (.35,.335,.30,1)
        _GrainMean("Texture linear mean", Float) = .373
        _GrainContrast("Relative grain contrast", Range(0,1)) = .25
        _Tiling("Texture size (m)", Float) = 2
        _BumpScale("Normal strength", Float) = .4
        _FeatherStart("Start of outer fade (m)", Float) = .4
        _ShoulderWidth("Full width (m)", Float) = .8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Back
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"


        // Shared by every pass, including ShadowCaster and depth passes.
        CBUFFER_START(UnityPerMaterial)
            float4 _GravelColor, _DirtColor;
            float _GrainMean, _GrainContrast, _Tiling, _BumpScale, _FeatherStart, _ShoulderWidth;
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
            if (abs(n.normalWS.y) > .6) output.tangentWS = float4(1,0,0,-sign(n.normalWS.y));
            else output.tangentWS = float4(normalize(float3(n.normalWS.z,0,-n.normalWS.x)),1);
            output.uv = abs(n.normalWS.y) > .6 ? p.positionWS.xz : input.groundMetres; // metre UVs for vertical faces
            output.roadUS = input.roadUS; // x = metres out from asphalt
            output.roadWidths = input.roadWidths;
            output.lightmapUV = input.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
            output.fogAndLight.x = ComputeFogFactor(p.positionCS.z);
            output.fogAndLight.yzw = VertexLighting(p.positionWS, n.normalWS);
            return output;
        }
        float3 ToWorld(Varyings input, float3 normalTS)
        {
            float3 n = normalize(input.normalWS);
            float3 t = normalize(input.tangentWS.xyz - n * dot(n, input.tangentWS.xyz));
            float3 b = cross(n, t) * input.tangentWS.w;
            return normalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);
        }
        float3 SurfaceNormalWS(Varyings input)
        {
            return ToWorld(input, UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv / max(_Tiling, 0.001)), input.roadWidths.x > .5 ? _BumpScale : .65));
        }
        void ShoulderClip(Varyings input)
        {
            // UV2.x marks shoulder geometry; other existing verge geometry has no edge fade.
            float coverage = input.roadWidths.x > .5 ? 1-smoothstep(_FeatherStart,_ShoulderWidth,input.roadUS.x) : 1;
            clip(coverage - max(.0001,InterleavedGradientNoise(input.positionCS.xy,0)));
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
            ShoulderClip(input);
            float2 uv = input.uv / max(_Tiling,.01);
            float3 source = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb;
            float grain = dot(source,float3(.2126,.7152,.0722));
            // Source gravel is very pale: retain desaturated grain, replace its mean albedo.
            float detail = 1 + clamp((grain/max(_GrainMean,.001)-1)*_GrainContrast,-.12,.12);
            float dirt = input.roadWidths.x > .5 ? smoothstep(.15,_ShoulderWidth,input.roadUS.x) : 0;
            float3 albedo = input.roadWidths.x > .5 ? lerp(_GravelColor.rgb,_DirtColor.rgb,dirt)*detail : source;
            float roughness = SAMPLE_TEXTURE2D(_RoughMap,sampler_RoughMap,uv).r;
            float3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),input.roadWidths.x > .5 ? _BumpScale : .65);
            float smoothness = input.roadWidths.x > .5 ? (1-roughness)*.06 : (1-roughness)*.35+.08;

            InputData data = (InputData)0;
            data.positionWS = input.positionWS;
            data.positionCS = input.positionCS;
            data.normalWS = half3(ToWorld(input, nTS));
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
            output.positionWS = p;
            output.roadUS = input.roadUS; output.roadWidths = input.roadWidths;
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
            output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.roadUS = input.roadUS; output.roadWidths = input.roadWidths;
            return output;
        }
        half4 DepthFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            ShoulderClip(input);
            return half4(input.positionCS.zzz, 1.0);
        }
        half4 NormalsFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            ShoulderClip(input);
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
