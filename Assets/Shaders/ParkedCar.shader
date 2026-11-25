// Parked cars (Magistrala.Traffic.ParkedCars, baked by Assets/Editor/ParkedBake.cs): a traffic kind's parts merged into one mesh
// per level with ONE texture atlas per kind, so every parked car of a kind, in any colour, is one instanced draw.
//   _BaseMap  atlas of the kind's TripoCar albedos (a = smoothness);  _MaskMap  atlas of their masks (r paint, g glass, b metal)
//   uv0       the part's own UV (wrapped inside its cell here, as the source textures repeat); uv.x > 1.5 = plate quad
//   uv1       the cell of the part's texture in the atlas (u0, v0, du, dv)
//   uv2       x grime (0 wheels / glass), y lens (TripoCar _LampAll), z lens reflector metal, w unlit tail glow
//   color     linear tint of the albedo (far level: the far mesh's per-triangle colours on a white cell)
//   per instance (instancing buffer, MaterialPropertyBlock array _ParkTint): rgb = linear paint colour, a = plate row (0..7)
// Lamps are always off (parked). Shading follows Magistrala/TripoCar (paint shade, clear coat, wetness, grime, plates).
Shader "Magistrala/ParkedCar"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo atlas (a = smoothness)", 2D) = "white" {}
        _MaskMap("Mask atlas (r paint, g glass, b metal)", 2D) = "black" {}
        _AtlasTexel("1 / atlas size", Float) = 0.00048828125
        _PaintRef("Albedo luminance of plain paint", Float) = 0.85
        _PaintSmooth("Paint base smoothness", Range(0,1)) = 0.55
        _CoatSmooth("Clear coat smoothness", Range(0,1)) = 0.93
        _Flake("Metallic flake", Range(0,1)) = 0.15
        _PlateTex("Plate atlas (Tools/vehicles/tablice.sh)", 2D) = "white" {}
        _PlateRect("Plate cell: u0, -, du, dv (v0 from the instance's plate row)", Vector) = (0,0,1,1)
        _GrimeTex("Grime (r micro imperfections, g smear)", 2D) = "gray" {}
        _Grime("Grime strength (0 off)", Range(0,1)) = 0
        _GrimeH("Sill grime height (object m)", Float) = 0.45
        _WheelZ("Wheel arches: z front, z rear, radius, -", Vector) = (0,0,0,0)
        _GrimeColor("Grime colour", Color) = (0.30,0.27,0.22,1)
        _ParkFade("Far fade: start, end (m) — the _FARFADE copy for the far level (ParkedCars)", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        HLSLINCLUDE
        #define _CLEARCOAT 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float _AtlasTexel;
            half _PaintRef, _PaintSmooth, _CoatSmooth, _Flake;
            float4 _PlateRect;
            half _Grime, _GrimeH;
            float4 _WheelZ;
            half4 _GrimeColor;
            float4 _ParkFade;
        CBUFFER_END
        float4 _FarFadeEye; // the camera (ParkedCars / TrafficSystem set it)
        // _FARFADE (far level only): the car dissolves (4×4 Bayer dither, per car: its origin's distance) towards the draw
        // distance instead of popping out of / into view at it
        void ParkFadeClip(float4 positionCS)
        {
        #if defined(_FARFADE)
            float3 o = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
            float k = saturate((_ParkFade.y - distance(o, _FarFadeEye.xyz)) / max(_ParkFade.y - _ParkFade.x, 1e-3));
            uint2 p = (uint2)positionCS.xy & 3;
            const float B[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            clip(k - (B[p.y * 4 + p.x] + 0.5) / 16.0);
        #endif
        }
        TEXTURE2D(_PlateTex); SAMPLER(sampler_PlateTex);
        TEXTURE2D(_GrimeTex); SAMPLER(sampler_GrimeTex);
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
        float _Wetness;

        UNITY_INSTANCING_BUFFER_START(ParkProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _ParkTint)
        UNITY_INSTANCING_BUFFER_END(ParkProps)

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 cell : TEXCOORD1;
            float4 flags : TEXCOORD2;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float3 positionOS : TEXCOORD3;
            half4 fogAndLight : TEXCOORD4;
            float4 cell : TEXCOORD5;
            half4 flags : TEXCOORD6;
            half4 color : TEXCOORD7;
            float4 tint : TEXCOORD8;   // per instance (constant over the triangle)
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
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
            o.uv = input.uv;
            o.positionOS = input.positionOS.xyz;
            o.fogAndLight.x = ComputeFogFactor(p.positionCS.z);
            o.fogAndLight.yzw = VertexLighting(p.positionWS, o.normalWS);
            o.cell = input.cell; o.flags = input.flags; o.color = input.color;
            o.tint = UNITY_ACCESS_INSTANCED_PROP(ParkProps, _ParkTint);
            return o;
        }

        half4 LitFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            UNITY_SETUP_INSTANCE_ID(input);
            ParkFadeClip(input.positionCS);
            InputData data = (InputData)0;
            data.positionWS = input.positionWS;
            data.positionCS = input.positionCS;
            data.normalWS = NormalizeNormalPerPixel(input.normalWS);
            data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                data.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
            #else
                data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #endif
            data.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1), input.fogAndLight.x);
            data.vertexLighting = input.fogAndLight.yzw;
            data.bakedGI = SampleSH(data.normalWS);
            data.shadowMask = half4(1, 1, 1, 1);
            data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

            // atlas: the part's UV repeats inside its cell (gradients from the unwrapped UV: no seam at the wrap), half a texel inset
            float2 auv = input.cell.xy + clamp(frac(input.uv), _AtlasTexel * 0.5 / max(input.cell.zw, 1e-5), 1 - _AtlasTexel * 0.5 / max(input.cell.zw, 1e-5)) * input.cell.zw;
            float2 adx = ddx(input.uv) * input.cell.zw, ady = ddy(input.uv) * input.cell.zw;
            half4 alb = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, auv, adx, ady);
            half4 mask = SAMPLE_TEXTURE2D_GRAD(_MaskMap, sampler_MaskMap, auv, adx, ady);
            alb.rgb *= input.color.rgb;
            // registration plates: our quads with uv.x in 2..3 -> a row of the plate atlas (one per car), the kind's style column
            float row = input.tint.a;
            float4 pr = float4(_PlateRect.x, 1 - (row * 128 + 118) / 1024, _PlateRect.zw);
            float2 puv = pr.xy + frac(input.uv) * pr.zw;
            float2 pdx = ddx(puv), pdy = ddy(puv);
            UNITY_BRANCH if (input.uv.x > 1.5)
            {
                alb = half4(SAMPLE_TEXTURE2D_GRAD(_PlateTex, sampler_PlateTex, puv, pdx, pdy).rgb, 0.35);
                mask = 0;
            }
            half paint = mask.r, glass = mask.g, metal = mask.b, lens = input.flags.y;

            half shade = max(0.55h, saturate(dot(alb.rgb, half3(0.299, 0.587, 0.114)) / max(0.05h, _PaintRef)));
            half3 body = input.tint.rgb * lerp(1, shade, 0.5);
            SurfaceData s = (SurfaceData)0;
            s.albedo = lerp(alb.rgb, body, paint);
            s.metallic = saturate(metal * 0.95 + paint * _Flake * 0.3);
            s.smoothness = lerp(alb.a, _PaintSmooth, paint);
            s.smoothness = lerp(s.smoothness, 0.97, glass);
            s.albedo = lerp(s.albedo, s.albedo * 0.55, glass);
            s.smoothness = lerp(s.smoothness, max(s.smoothness, 0.9), lens);
            s.metallic = lerp(s.metallic, max(s.metallic, input.flags.z), lens);
            s.clearCoatMask = paint;
            s.clearCoatSmoothness = _CoatSmooth;
            s.alpha = 1;
            s.occlusion = 1;
            s.normalTS = half3(0, 0, 1);
            s.specular = 0;

            UNITY_BRANCH if (_Wetness > 0.001)
            {
                half up = saturate(data.normalWS.y * 1.5 - 0.3);
                half w = _Wetness * lerp(0.45, 1, up);
                half rough = (1 - paint) * (1 - glass);
                s.albedo *= 1 - 0.35 * w * rough;
                s.smoothness = lerp(s.smoothness, max(s.smoothness, 0.8), w * rough);
                s.clearCoatSmoothness = lerp(s.clearCoatSmoothness, 0.98, w);
                s.albedo = lerp(s.albedo, s.albedo * 0.9, w * paint);
            }
            half grime = _Grime * input.flags.x;
            UNITY_BRANCH if (grime > 0.001)
            {
                float3 po = input.positionOS;
                half4 gt = SAMPLE_TEXTURE2D(_GrimeTex, sampler_GrimeTex, float2(po.z, po.y) * 0.45 + po.x * 0.13);
                half sill = 1 - smoothstep(0.08, _GrimeH, po.y);
                float2 af = float2(po.z - _WheelZ.x, po.y - _WheelZ.z), ar = float2(po.z - _WheelZ.y, po.y - _WheelZ.z);
                half arch = _WheelZ.z > 0 ? 1 - smoothstep(_WheelZ.z * 1.05, _WheelZ.z * 1.6, min(length(af), length(ar))) : 0;
                half g = saturate(max(sill, arch * 0.8) * (0.35 + 0.9 * gt.g)) * grime * (1 - glass);
                s.albedo = lerp(s.albedo, _GrimeColor.rgb * (0.8 + 0.4 * gt.r), g * 0.75);
                s.smoothness *= 1 - 0.7 * g;
                s.clearCoatMask *= 1 - g;
                s.clearCoatSmoothness *= lerp(1, 0.9 + 0.1 * gt.r, grime);
            }
            s.emission = half3(0.22, 0.01, 0.005) * input.flags.w * (1 - paint); // (paint texels of a mixed lamp submesh stay unlit)   // unlit tail lamps keep their faint red (TripoCar's off state)

            half4 result = UniversalFragmentPBR(data, s);
            result.rgb = MixFog(result.rgb, data.fogCoord);
            result.a = 1;
            return result;
        }

        struct ShadowVaryings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        float3 _LightDirection;
        float3 _LightPosition;
        ShadowVaryings ShadowVertex(Attributes input)
        {
            ShadowVaryings o = (ShadowVaryings)0;
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
        ShadowVaryings DepthVertex(Attributes input)
        {
            ShadowVaryings o = (ShadowVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, o);
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return o;
        }
        half4 DepthFragment(ShadowVaryings input) : SV_Target { return input.positionCS.z; }
        half4 DepthFadeFragment(ShadowVaryings input) : SV_Target { UNITY_SETUP_INSTANCE_ID(input); ParkFadeClip(input.positionCS); return input.positionCS.z; }
        half4 NormalsFragment(ShadowVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            ParkFadeClip(input.positionCS);
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
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_local_fragment _ _FARFADE
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
            #pragma fragment DepthFadeFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_local_fragment _ _FARFADE
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
            #pragma multi_compile_local_fragment _ _FARFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}
