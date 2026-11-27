Shader "Magistrala/CesteStone"
{
    Properties
    {
        [MainTexture] [NoScaleOffset] _BaseMap("Stone albedo", 2D) = "white" {}
        [Normal] [NoScaleOffset] _BumpMap("OpenGL normal", 2D) = "bump" {}
        [NoScaleOffset] _RoughMap("Roughness", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Tiling("Texture tile size (m)", Float) = 2
        _VerticalScale("Vertical mesh UV to metres", Float) = 1
        _BumpScale("Normal strength", Range(0,2)) = .65
        _Slabs("Slab joints / tint", Float) = 1
        _AlbedoLift("Concrete diffuse lift (linear)", Range(0,.3)) = 0
        _Saturation("Stone saturation", Range(0,1)) = 1
        _SlabVariation("Per slab variation", Range(0,.2)) = .06
        _JointStrength("Additional procedural joints", Range(0,1)) = 1
        _CellSize("Slab width, length (m)", Vector) = (.6,.9,0,0)
        _Waterline("Waterline algae", Float) = 0
        _AlgaeAvailable("Algae texture available", Float) = 0
        _AlgaeTiling("Algae tile size (m)", Float) = 3
        [NoScaleOffset] _AlgaeMap("Algae", 2D) = "white" {}
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
            float4 _BaseColor, _CellSize;
            float _AlbedoLift, _Saturation, _SlabVariation, _JointStrength;
            float _Tiling, _VerticalScale, _BumpScale, _Slabs, _Waterline, _AlgaeAvailable, _AlgaeTiling;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_RoughMap); SAMPLER(sampler_RoughMap);
        TEXTURE2D(_AlgaeMap);

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
            output.uv = abs(n.normalWS.y) > .6 ? p.positionWS.xz : input.groundMetres * _VerticalScale; // metre UVs for vertical faces
            output.roadUS = float2(input.groundMetres.y * _VerticalScale, 0); // coping distance from sea edge
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
            return ToWorld(input, UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv / max(_Tiling, 0.001)), _BumpScale));
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
            float2 uv = input.uv / max(_Tiling,.01);
            float3 albedo = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb * _BaseColor.rgb;
            albedo = lerp(dot(albedo,float3(.2126,.7152,.0722)).xxx,albedo,_Saturation) + _AlbedoLift;
            float roughness = SAMPLE_TEXTURE2D(_RoughMap,sampler_RoughMap,uv).r;
            float3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_BumpScale);
            float2 grid = input.uv / max(_CellSize.xy,float2(.1,.1));
            grid.x += step(.5,frac(floor(grid.y)*.5))*.5;
            float2 cell = floor(grid), f = frac(grid);
            float2 dist = min(f,1-f)*_CellSize.xy;
            float d = min(dist.x,dist.y), aa = max(fwidth(d),.0008);
            float joint = 1-smoothstep(.003-aa,.007+aa,d);
            float rim = 1-smoothstep(.008,.025,d);
            albedo *= lerp(1,(1 + (Hash21(cell)*2-1)*_SlabVariation) * (1-joint*.36*_JointStrength) * (1+rim*.08*_JointStrength),_Slabs);
            float smoothness = (1-roughness)*.35 + .08;
            // Sea is y=0; irregular damp band follows the physical waterline, never the top UV.
            float wet = _Waterline * (1-smoothstep(.12,.8,input.positionWS.y + (ValueNoise(input.positionWS.xz*2)-.5)*.15));
            float coping = _Waterline * step(.6,abs(input.normalWS.y)) * (1-smoothstep(.2,.35,input.roadUS.x));
            wet = max(wet,coping);
            albedo *= 1-wet*.35;
            UNITY_BRANCH if (_AlgaeAvailable > .5 && wet > .01 && abs(input.normalWS.y) < .6)
                albedo = lerp(albedo,SAMPLE_TEXTURE2D(_AlgaeMap,sampler_BaseMap,input.uv/max(_AlgaeTiling,.1)).rgb,wet*.65);
            smoothness = lerp(smoothness,.65,wet);

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
