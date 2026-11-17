Shader "Magistrala/Sea"
{
    Properties
    {
        [Normal] [NoScaleOffset] _NormalMap("Wave Normal (OpenGL)", 2D) = "bump" {}
        _NormalScale1("First Normal Tile Size (m)", Float) = 38
        _NormalScale2("Second Normal Tile Size (m)", Float) = 11
        _Speed1("First Scroll Speed (tiles/s, XY)", Vector) = (0.020,0.012,0,0)
        _Speed2("Second Scroll Speed (tiles/s, XY)", Vector) = (-0.015,0.025,0,0)
        _NormalStrength("Normal Strength", Range(0,2)) = 0.55
        _Smoothness("Smoothness", Range(0,1)) = 0.94
        _FresnelPower("Fresnel Power", Float) = 5
        _ReflectionStrength("Environment Reflection Strength", Range(0,2)) = 0.85
        _SunSpec("Sun Glint Strength", Float) = 6
        _SunSpecPower("Sun Glint Exponent", Float) = 900
        [MainColor] _Tint("Body Colour Multiplier", Color) = (1,1,1,1)
        _FoamShore("Shore Foam (reserved)", Range(0,1)) = 0
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


        // One identical material layout for ForwardLit, DepthOnly and DepthNormals.
        CBUFFER_START(UnityPerMaterial)
            float4 _Speed1;
            float4 _Speed2;
            float4 _Tint;
            float _NormalScale1;
            float _NormalScale2;
            float _NormalStrength;
            float _Smoothness;
            float _FresnelPower;
            float _ReflectionStrength;
            float _SunSpec;
            float _SunSpecPower;
            float _FoamShore;
        CBUFFER_END
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR; // Body colour is already LINEAR.
            float2 lightmapUV : TEXCOORD1; // Optional; TEXCOORD0 is unused.
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            half4 color : COLOR;
            half4 fogAndLight : TEXCOORD2;
            float2 lightmapUV : TEXCOORD3;
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
            output.positionCS = p.positionCS;
            output.positionWS = p.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.color = input.color;
            output.fogAndLight.x = ComputeFogFactor(p.positionCS.z);
            output.fogAndLight.yzw = VertexLighting(p.positionWS, output.normalWS);
            output.lightmapUV = input.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
            return output;
        }
        float3 SurfaceNormalWS(Varyings input)
        {
            // Velocities are normalized tile scroll rates: worldXZ/scale + time*speed.
            float2 uv1 = input.positionWS.xz / max(_NormalScale1, 0.001) + _Time.y * _Speed1.xy;
            float2 uv2 = input.positionWS.xz / max(_NormalScale2, 0.001) + _Time.y * _Speed2.xy;
            float3 n1 = float3(UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1), _NormalStrength));
            float3 n2 = float3(UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv2), _NormalStrength));
            float3 normalTS = normalize(float3(n1.xy + n2.xy, max(n1.z * n2.z, 0.001))); // Whiteout blend.
            float3 n = normalize(input.normalWS);
            // World X/Z mapping: +normal-map X points +world X, +Y points +world Z.
            float3 axis = abs(n.x) < 0.99 ? float3(1,0,0) : float3(0,0,1);
            float3 t = normalize(axis - n * dot(axis, n));
            float3 b = normalize(cross(t, n));
            return normalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);
        }
        half3 WaterLambert(Light light, half3 normalWS)
        {
            return light.color * half(light.distanceAttenuation * light.shadowAttenuation)
                * saturate(dot(normalWS, light.direction));
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
            // Float precision for reflection direction and exponent 900 on Metal.
            float3 n = SurfaceNormalWS(input);
            float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
            float3 r = reflect(-v, n);
            float fresnel = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, v)), max(_FresnelPower, 0.001));
            InputData inputData = (InputData)0; // Also used by the URP cluster light-loop macro.
            inputData.positionWS = input.positionWS;
            inputData.positionCS = input.positionCS;
            inputData.normalWS = half3(n);
            inputData.viewDirectionWS = half3(v);
            inputData.shadowCoord = ShadowCoordinates(input.positionWS);
            inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
            #if defined(LIGHTMAP_ON)
                inputData.bakedGI = SampleLightmap(input.lightmapUV, float2(0,0), inputData.normalWS);
            #else
                inputData.bakedGI = SampleSH(inputData.normalWS);
            #endif
            AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
            half4 shadowMask = CalculateShadowMask(inputData);
            Light mainLight = GetMainLight(inputData, shadowMask, ao);
            MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
            half3 diffuse = WaterLambert(mainLight, inputData.normalWS);
            #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
                    {
                        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
                        diffuse += WaterLambert(light, inputData.normalWS);
                    }
                #endif
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
                    diffuse += WaterLambert(light, inputData.normalWS);
                LIGHT_LOOP_END
            #endif
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                diffuse += input.fogAndLight.yzw * ao.directAmbientOcclusion;
            #endif
            float3 body = float3(input.color.rgb) * _Tint.rgb;
            // Preserve the authored turquoise/blue, with a low Lambert contribution.
            float3 bodyLighting = (0.55 + 0.20 * float3(inputData.bakedGI)) * float(ao.indirectAmbientOcclusion)
                                + 0.20 * float3(diffuse);
            half perceptualRoughness = half(1.0 - saturate(_Smoothness));
            half3 environment = GlossyEnvironmentReflection(half3(r), perceptualRoughness, half(1.0));
            float glint = pow(saturate(dot(r, float3(mainLight.direction))), max(_SunSpecPower, 1.0))
                        * max(_SunSpec, 0.0) * saturate(dot(n, float3(mainLight.direction)))
                        * mainLight.distanceAttenuation * float(mainLight.shadowAttenuation);
            float3 color = body * bodyLighting * (1.0 - fresnel)
                         + float3(environment) * fresnel * _ReflectionStrength * float(ao.indirectAmbientOcclusion)
                         + glint * float3(mainLight.color);
            half fog = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogAndLight.x);
            return half4(MixFog(half3(color), fog), 1.0);
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
