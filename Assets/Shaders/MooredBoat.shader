// Moored boats (Magistrala.MooredBoats, meshes made by Assets/Editor/BoatsBuilder.cs): every boat kind is one procedural mesh
// per level with plain vertex colours (no textures), so all boats of a kind, in any hull colour, are one instanced draw.
//   color     linear outside colour; a = hull paint mask (1: the per-instance hull colour replaces it)
//   uv0       x smoothness, y hull skin (1: gets the painted waterline below _Boot and the interior colour on its back faces)
//   uv1       xyz linear interior colour of the hull skin (seen from inside: Cull Off, back faces)
//   per instance (instancing buffer, MaterialPropertyBlock array _BoatTint): rgb = linear hull colour, a = bobbing phase 0..1
//   _BoatBob  per draw (one kind): x heave (m), y roll (rad), z pitch (rad), w angular speed (rad/s) — bigger boats slower
// Bobbing is all in the vertex stage (no script per boat): the mesh turns about its waterline centre (object origin) by a small
// roll / pitch and rises by the heave, from _Time, the instance's phase and its world position (a swell passing through the
// harbour: neighbours move nearly together). The shadow / depth passes bob the same way.
Shader "Magistrala/MooredBoat"
{
    Properties
    {
        _Boot("Waterline paint top (object m)", Float) = 0.06
        _BootBand("Dark band above it (object m)", Float) = 0.035
        _Antifoul1("Antifouling A", Color) = (0.42,0.08,0.06,1)
        _Antifoul2("Antifouling B", Color) = (0.06,0.10,0.20,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Boot, _BootBand;
            half4 _Antifoul1, _Antifoul2;
        CBUFFER_END
        float4 _BoatBob;

        UNITY_INSTANCING_BUFFER_START(BoatProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _BoatTint)
        UNITY_INSTANCING_BUFFER_END(BoatProps)

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float3 inner : TEXCOORD1;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float objY : TEXCOORD3;
            half4 fogAndLight : TEXCOORD4;
            half3 inner : TEXCOORD5;
            half4 color : TEXCOORD6;
            float4 tint : TEXCOORD7;   // per instance (constant over the triangle)
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // the bobbing: object-space roll (about the bow axis z), pitch (about x), heave; p / n in and out
        void Bob(inout float3 p, inout float3 n, float phase)
        {
            float4x4 m = GetObjectToWorldMatrix();
            float3 o = float3(m._m03, m._m13, m._m23);
            float s = max(length(float3(m._m00, m._m10, m._m20)), 0.01);
            float t = _Time.y * _BoatBob.w;
            float ph = phase * 6.2831853 + dot(o.xz, float2(0.071, 0.047));
            float roll = _BoatBob.y * (sin(t + ph) + 0.35 * sin(t * 1.73 + ph * 2.1));
            float pitch = _BoatBob.z * sin(t * 0.81 + ph * 1.3 + 0.7);
            float heave = _BoatBob.x * (sin(t * 0.93 + ph * 0.7 + 1.9) + 0.3 * sin(t * 1.61 + ph)) / s;
            float cr = cos(roll), sr = sin(roll), cp = cos(pitch), sp = sin(pitch);
            float3x3 R = mul(float3x3(1, 0, 0, 0, cp, -sp, 0, sp, cp), float3x3(cr, -sr, 0, sr, cr, 0, 0, 0, 1));
            p = mul(R, p); p.y += heave;
            n = mul(R, n);
        }

        Varyings LitVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float4 tint = UNITY_ACCESS_INSTANCED_PROP(BoatProps, _BoatTint);
            float3 pos = input.positionOS.xyz, nrm = input.normalOS;
            o.objY = pos.y;
            Bob(pos, nrm, tint.a);
            VertexPositionInputs p = GetVertexPositionInputs(pos);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(nrm);
            o.uv = input.uv;
            o.fogAndLight.x = ComputeFogFactor(p.positionCS.z);
            o.fogAndLight.yzw = VertexLighting(p.positionWS, o.normalWS);
            o.inner = input.inner; o.color = input.color; o.tint = tint;
            return o;
        }

        half4 LitFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            bool front = IS_FRONT_VFACE(face, true, false);
            InputData data = (InputData)0;
            data.positionWS = input.positionWS;
            data.positionCS = input.positionCS;
            float3 nw = NormalizeNormalPerPixel(input.normalWS);
            data.normalWS = front ? nw : -nw;
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

            half3 alb = lerp(input.color.rgb, input.tint.rgb, input.color.a);
            half hull = input.uv.y;
            UNITY_BRANCH if (hull > 0.5)
            {
                if (!front) alb = input.inner;
                else
                {
                    // painted waterline: antifouling (one of two per boat, from its phase) and a darker band of the hull colour above
                    half3 af = frac(input.tint.a * 7.31) < 0.55 ? _Antifoul1.rgb : _Antifoul2.rgb;
                    if (input.objY < _Boot) alb = af;
                    else if (input.objY < _Boot + _BootBand) alb *= 0.55;
                }
            }
            SurfaceData s = (SurfaceData)0;
            s.albedo = alb;
            s.metallic = 0;
            s.smoothness = input.uv.x;
            s.alpha = 1;
            s.occlusion = front ? 1 : 0.75;
            s.normalTS = half3(0, 0, 1);
            s.specular = 0;
            half4 result = UniversalFragmentPBR(data, s);
            result.rgb = MixFog(result.rgb, data.fogCoord);
            result.a = 1;
            return result;
        }

        struct ShadowVaryings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
        float3 _LightDirection;
        float3 _LightPosition;
        ShadowVaryings ShadowVertex(Attributes input)
        {
            ShadowVaryings o = (ShadowVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            float3 pos = input.positionOS.xyz, nrm = input.normalOS;
            Bob(pos, nrm, UNITY_ACCESS_INSTANCED_PROP(BoatProps, _BoatTint).a);
            float3 p = TransformObjectToWorld(pos);
            float3 n = TransformObjectToWorldNormal(nrm);
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
            float3 pos = input.positionOS.xyz, nrm = input.normalOS;
            Bob(pos, nrm, UNITY_ACCESS_INSTANCED_PROP(BoatProps, _BoatTint).a);
            o.positionCS = TransformObjectToHClip(pos);
            o.normalWS = TransformObjectToWorldNormal(nrm);
            return o;
        }
        half4 DepthFragment(ShadowVaryings input) : SV_Target { return input.positionCS.z; }
        half4 NormalsFragment(ShadowVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            float3 n = NormalizeNormalPerPixel(input.normalWS);
            if (!IS_FRONT_VFACE(face, true, false)) n = -n;
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
