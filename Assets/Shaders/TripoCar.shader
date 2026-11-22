// Traffic cars from Tripo (Assets/Editor/TripoCarImporter.cs): one baked albedo + a class mask in the model's own UV atlas,
// the livery projected in object space (independent of the broken / mirrored atlas).
//   _BaseMap  rgb = albedo (livery texels already painted over with paint white), a = smoothness of the non-paint classes
//   _MaskMap  r = paint (tinted by _PaintColor, clear coat), g = glass, b = metal (rims / chrome), a = lamp emission strength
//   lamps: the head / tail lamp submeshes get a material copy with _LampColor set (TrafficPart.matOn)
//   livery: _LiveryTex (Tools/vehicles/livery.py), bottom half = side band (u front -> rear), top-left quarter = wordmark
//   wetness: global _Wetness (WeatherSystem) - darker diffuse, glossier, beads on the clear coat read as a stronger coat
// GPU instancing (TrafficSystem draws with RenderMeshInstanced), SRP Batcher compatible (one UnityPerMaterial layout).
Shader "Magistrala/TripoCar"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo (a = smoothness)", 2D) = "white" {}
        _MaskMap("Mask (r paint, g glass, b metal, a lamp)", 2D) = "black" {}
        _LiveryTex("Livery sheet", 2D) = "black" {}
        [MainColor] _PaintColor("Paint colour", Color) = (1,1,1,1)
        _PaintRef("Albedo luminance of plain paint", Float) = 0.85
        _PaintSmooth("Paint base smoothness", Range(0,1)) = 0.55
        _CoatSmooth("Clear coat smoothness", Range(0,1)) = 0.93
        _Flake("Metallic flake", Range(0,1)) = 0.15
        [HDR] _LampColor("Lamp emission (lit variant)", Color) = (0,0,0,0)
        _Livery("Livery on", Float) = 0
        _LivBand("Band: z front, z rear, y0, y1 (object m)", Vector) = (0,0,0,0)
        _LivSide("Side text: z centre, y centre, width, -", Vector) = (0,0,0,0)
        _LivTop("Bonnet text: z centre, width, mirrored, -", Vector) = (0,0,0,0)
        _LivRear("Rear text: y centre, width, -, -", Vector) = (0,0,0,0)
        _LivColor("Livery tint (multiplies the sheet)", Color) = (1,1,1,1)
        // Sketchfab candidates (Assets/Editor/KandidatImporter.cs): whole-submesh lamps, plates, grime
        _LampAll("Lamp emission on every texel (lamp submeshes)", Float) = 0
        _LampMetal("Lamp submeshes: reflector metal (head lamps)", Range(0,1)) = 0
        _PlateTex("Plate atlas (Tools/vehicles/tablice.sh)", 2D) = "white" {}
        _PlateRect("Plate cell in the atlas (u0, v0, du, dv); quads with uv.x > 1.5", Vector) = (0,0,1,1)
        _GrimeTex("Grime (r micro imperfections, g smear)", 2D) = "gray" {}
        _Grime("Grime strength (0 off)", Range(0,1)) = 0
        _GrimeH("Sill grime height (object m)", Float) = 0.45
        _WheelZ("Wheel arches: z front, z rear, radius, -", Vector) = (0,0,0,0)
        _GrimeColor("Grime colour", Color) = (0.30,0.27,0.22,1)
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
            half4 _PaintColor;
            half _PaintRef, _PaintSmooth, _CoatSmooth, _Flake;
            half4 _LampColor;
            float _Livery;
            float4 _LivBand, _LivSide, _LivTop, _LivRear;
            half4 _LivColor;
            half _LampAll, _LampMetal;
            float4 _PlateRect;
            half _Grime, _GrimeH;
            float4 _WheelZ;
            half4 _GrimeColor;
        CBUFFER_END
        TEXTURE2D(_PlateTex); SAMPLER(sampler_PlateTex);
        TEXTURE2D(_GrimeTex); SAMPLER(sampler_GrimeTex);
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
        TEXTURE2D(_LiveryTex); SAMPLER(sampler_LiveryTex);
        float _Wetness;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float3 positionOS : TEXCOORD3;
            half3 normalOS : TEXCOORD4;
            half4 fogAndLight : TEXCOORD5;
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
            o.normalOS = input.normalOS;
            o.fogAndLight.x = ComputeFogFactor(p.positionCS.z);
            o.fogAndLight.yzw = VertexLighting(p.positionWS, o.normalWS);
            return o;
        }

        // one decal: box (u, v) in 0..1 -> sheet rect (u0, v0, du, dv); outside the box: nothing
        half4 Sheet(float2 uv, float4 rect)
        {
            if (any(uv < 0) || any(uv > 1)) return 0;
            return SAMPLE_TEXTURE2D(_LiveryTex, sampler_LiveryTex, rect.xy + clamp(uv, 0.01, 0.99) * rect.zw); // inset: no bleed from the next rect
        }
        half4 Over(half4 a, half4 b) { return half4(lerp(a.rgb, b.rgb, b.a), a.a + b.a * (1 - a.a)); }

        // object-space livery: sides by |n.x|, bonnet by n.y (front part only), rear by -n.z. Text reads left -> right on both sides.
        half4 Livery(float3 p, half3 n)
        {
            half4 c = 0;
            half side = smoothstep(0.35, 0.6, abs(n.x));
            if (side > 0)
            {
                // band: u = 0 at its front end on both sides (the car is mirror-symmetric)
                float2 ub = float2((_LivBand.x - p.z) / max(1e-3, _LivBand.x - _LivBand.y), (p.y - _LivBand.z) / max(1e-3, _LivBand.w - _LivBand.z));
                c = Over(c, Sheet(ub, float4(0, 0, 1, 0.5)));
                // wordmark: viewed from the left (x < 0) the front is on the reader's left
                float h = _LivSide.z * 0.25;
                float u = 0.5 + (p.x < 0 ? (_LivSide.x - p.z) : (p.z - _LivSide.x)) / _LivSide.z;
                float2 ut = float2(u, (p.y - _LivSide.y) / h + 0.5);
                c = Over(c, Sheet(ut, float4(0, 0.75, 0.5, 0.25)));
                c.a *= side;
            }
            half top = smoothstep(0.45, 0.7, n.y);
            if (top > 0 && _LivTop.y > 0)
            {
                // read from in front of the car: right = -x, letter tops towards the windscreen
                float u = 0.5 - p.x / _LivTop.y; if (_LivTop.z > 0.5) u = 1 - u;
                float2 ut = float2(u, 0.5 + (_LivTop.x - p.z) / (_LivTop.y * 0.25));
                half4 t = Sheet(ut, float4(0, 0.75, 0.5, 0.25)); t.a *= top;
                c = Over(c, t);
            }
            half rear = smoothstep(0.45, 0.7, -n.z);
            if (rear > 0 && _LivRear.y > 0)
            {
                float2 ut = float2(0.5 + p.x / _LivRear.y, 0.5 + (p.y - _LivRear.x) / (_LivRear.y * 0.25));
                half4 t = Sheet(ut, float4(0, 0.75, 0.5, 0.25)); t.a *= rear;
                c = Over(c, t);
            }
            c.rgb *= _LivColor.rgb;
            return c;
        }

        half4 LitFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
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

            half4 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv);
            // registration plates: our own quads in the paint submesh with uv.x in 2..3 -> this material's cell of the plate atlas
            // (gradients taken outside the branch: the quads are whole, the branch is coherent)
            float2 puv = _PlateRect.xy + frac(input.uv) * _PlateRect.zw;
            float2 pdx = ddx(puv), pdy = ddy(puv);
            UNITY_BRANCH if (input.uv.x > 1.5)
            {
                alb = half4(SAMPLE_TEXTURE2D_GRAD(_PlateTex, sampler_PlateTex, puv, pdx, pdy).rgb, 0.35);
                mask = 0;
            }
            half paint = mask.r, glass = mask.g, metal = mask.b, lamp = max(mask.a, _LampAll);

            // paint: the baked shading of the white body (its luminance against plain paint) times the body colour
            half shade = max(0.55h, saturate(dot(alb.rgb, half3(0.299, 0.587, 0.114)) / max(0.05h, _PaintRef)));
            half3 body = _PaintColor.rgb * lerp(1, shade, 0.5);
            if (_Livery > 0.5)
            {
                half4 liv = Livery(input.positionOS, normalize(input.normalOS));
                body = lerp(body, liv.rgb * lerp(1, shade, 0.5), liv.a);
            }
            SurfaceData s = (SurfaceData)0;
            s.albedo = lerp(alb.rgb, body, paint);
            s.metallic = saturate(metal * 0.95 + paint * _Flake * 0.3);
            s.smoothness = lerp(alb.a, _PaintSmooth, paint);
            s.smoothness = lerp(s.smoothness, 0.97, glass);
            s.albedo = lerp(s.albedo, s.albedo * 0.55, glass); // tinted glass: let the reflection carry it
            // lamp submeshes (candidates): glass lens over a reflector
            s.smoothness = lerp(s.smoothness, max(s.smoothness, 0.9), _LampAll);
            s.metallic = lerp(s.metallic, max(s.metallic, _LampMetal), _LampAll);
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
                s.albedo *= 1 - 0.35 * w * rough;                          // tyres, trim, plastics soak dark
                s.smoothness = lerp(s.smoothness, max(s.smoothness, 0.8), w * rough);
                s.clearCoatSmoothness = lerp(s.clearCoatSmoothness, 0.98, w);
                s.albedo = lerp(s.albedo, s.albedo * 0.9, w * paint);       // wet paint reads a touch deeper
            }
            // grime (Sketchfab candidates): dust / road film towards the sills and round the wheel arches, broken up by a CC0 smear
            // mask (ambientCG Smear007) projected on the sides; micro imperfections (SurfaceImperfections014) dull the clear coat a little
            UNITY_BRANCH if (_Grime > 0.001)
            {
                float3 po = input.positionOS;
                half4 gt = SAMPLE_TEXTURE2D(_GrimeTex, sampler_GrimeTex, float2(po.z, po.y) * 0.45 + po.x * 0.13);
                half sill = 1 - smoothstep(0.08, _GrimeH, po.y);
                float2 af = float2(po.z - _WheelZ.x, po.y - _WheelZ.z), ar = float2(po.z - _WheelZ.y, po.y - _WheelZ.z);
                half arch = _WheelZ.z > 0 ? 1 - smoothstep(_WheelZ.z * 1.05, _WheelZ.z * 1.6, min(length(af), length(ar))) : 0;
                half g = saturate(max(sill, arch * 0.8) * (0.35 + 0.9 * gt.g)) * _Grime * (1 - glass);
                s.albedo = lerp(s.albedo, _GrimeColor.rgb * (0.8 + 0.4 * gt.r), g * 0.75);
                s.smoothness *= 1 - 0.7 * g;
                s.clearCoatMask *= 1 - g;
                s.clearCoatSmoothness *= lerp(1, 0.9 + 0.1 * gt.r, _Grime);
            }
            s.emission = _LampColor.rgb * lamp;

            half4 result = UniversalFragmentPBR(data, s);
            result.rgb = MixFog(result.rgb, data.fogCoord);
            result.a = 1;
            return result;
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
            o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDir)));
            return o;
        }
        Varyings DepthVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return o;
        }
        half4 DepthFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            return input.positionCS.z;
        }
        half4 NormalsFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
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
