// Window glass with a fake room behind it (interior mapping on a one-point-perspective room atlas, BuildingTextures.Rooms).
// UV0: the window's place in its room's front wall (0..1 = the whole room front, one facade column x one storey);
// UV1: x = random per window (room, lamp, curtains), y = blind lowered 0..1, zw = 0..1 across the glass itself.
// Tangent = along UV0.x. Rooms light up when the sun is low (golden hour G: some, night: most).
Shader "Magistrala/WindowInterior"
{
    Properties
    {
        _Rooms("Room atlas (4 x 2)", 2D) = "grey" {}
        _FarFrac("Back wall share of the room image", Range(0.1, 0.9)) = 0.45
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Rooms_ST;
            half _FarFrac;
        CBUFFER_END
        TEXTURE2D(_Rooms); SAMPLER(sampler_Rooms);
        float _Wetness;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float4 data : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float4 data : TEXCOORD3;
            float3 viewTS : TEXCOORD4;
            half fog : TEXCOORD5;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes input)
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
            float3 v = p.positionWS - GetCameraPositionWS(); // camera -> glass
            o.viewTS = float3(dot(v, n.tangentWS), dot(v, n.bitangentWS), dot(v, n.normalWS));
            o.uv = input.uv;
            o.data = input.data;
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }
        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            float rnd = input.data.x;
            // interior mapping (box in [-1,1]^2 x depth), the room image drawn for a 53 deg one-point perspective
            float farFrac = _FarFrac;
            float depthScale = 1.0 / (1.0 - farFrac) - 1.0;
            float3 pos = float3(saturate(input.uv) * 2 - 1, -1);
            float3 dir = input.viewTS;
            dir.z *= -depthScale;
            float3 id = 1.0 / dir;
            float3 k = abs(id) - pos * id;
            float kMin = min(min(k.x, k.y), k.z);
            pos += kMin * dir;
            float interp = pos.z * 0.5 + 0.5;
            float realZ = saturate(interp) / depthScale + 1;
            interp = 1.0 - (1.0 / realZ);
            interp *= depthScale + 1.0;
            float2 iuv = pos.xy * lerp(1.0, farFrac, interp) * .5 + .5;
            float cell = floor(rnd * 8);
            // mirrored rooms double the variety
            if (frac(rnd * 13.7) > .5) iuv.x = 1 - iuv.x;
            float2 auv = (float2(fmod(cell, 4), floor(cell / 4)) + clamp(iuv, .002, .998)) * float2(.25, .5);
            half3 room = SAMPLE_TEXTURE2D_LOD(_Rooms, sampler_Rooms, auv, 0).rgb;

            // lighting: daylight inside is dim; lamps on when the sun is low (by window), warm or cool
            Light sun = GetMainLight();
            half day = saturate(sun.direction.y * 3);
            half dusk = saturate((.38 - sun.direction.y) * 3.2);
            half lit = step(frac(rnd * 71.3), .08 + .27 * dusk) * saturate(dusk * 1.5); // golden hour ~1/3 of the rooms
            half3 lamp = lerp(half3(1, .72, .42), half3(.95, .9, .8), step(frac(rnd * 5.1), .25));
            half3 amb = SampleSH(half3(0, 1, 0));
            half3 col = room * (amb * .35 + sun.color * .05 * day) + room * lamp * lit * 1.6;

            // curtains at the sides / net curtain, roller blind from the top (UV1.y)
            float2 g = input.data.zw;
            half curtain = frac(rnd * 31.7);
            if (curtain < .45)
            {
                float w = .12 + .2 * frac(rnd * 17.3);
                if (g.x < w || g.x > 1 - w) col = lerp(half3(.55, .5, .42), half3(.75, .72, .66), frac(rnd * 3.3)) * (amb * .5 + lamp * lit * .9);
            }
            else if (curtain < .7) col = lerp(col, half3(.8, .8, .76) * (amb * .45 + lamp * lit * 1.1), .55);
            if (g.y > 1 - input.data.y) col = half3(.78, .76, .7) * (amb * .7 + sun.color * .12 * day) * (.85 + .15 * step(frac(g.y * 22), .5));

            // glass: Fresnel reflection of the sky / probes, the sun's glint
            half3 n = normalize(input.normalWS);
            half3 vdir = GetWorldSpaceNormalizeViewDir(input.positionWS);
            half nv = saturate(dot(n, vdir));
            half fres = .06 + .7 * pow(1 - nv, 4);
            half3 refl = GlossyEnvironmentReflection(reflect(-vdir, n), input.positionWS, .08, 1);
            half3 h = normalize(sun.direction + vdir);
            half spec = pow(saturate(dot(n, h)), 300) * 6 * day;
            col = lerp(col, refl, fres) + sun.color * spec;
            col *= 1 - .25 * _Wetness * (1 - lit);
            col = MixFog(col, InitializeInputDataFog(float4(input.positionWS, 1), input.fog));
            return half4(col, 1);
        }
        Varyings DepthVert(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return o;
        }
        half4 DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
        half4 NormalsFrag(Varyings input) : SV_Target
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
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
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
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
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
            #pragma vertex DepthVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    FallBack Off
}
