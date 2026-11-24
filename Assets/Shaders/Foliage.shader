// Krošnje (VEGETACIJA_I_TLO.md, 23. 11.): the alpha-clipped leaves of the TreeRenderer plants and their impostors.
// URP Lit / the glTF shader lit every leaf card by its own normal: in the sun half the cards of a crown faced away from the
// light and from the sky and went almost black, and the crown read as a pile of cards. Here:
//   * normals bent outward from the crown centre (object space 0, _CrownY, 0): the crown is lit as one soft volume
//   * wrapped diffuse + light through the leaves (translucency) on the side away from the sun, ambient from the bent normal
//     with a floor, darker toward the crown's inside (cheap self-occlusion) and SSAO when it is on
//   * a per-plant shade / brightness shift from the instance position (no data per instance: same plant, same shift in
//     every build)
// Property names follow glTFast's (baseColorTexture, baseColorFactor, alphaCutoff), so the clip materials keep their values.
Shader "Magistrala/Foliage"
{
    Properties
    {
        [MainTexture] baseColorTexture("Base Map", 2D) = "white" {}
        [MainColor] baseColorFactor("Base Color", Color) = (1,1,1,1)
        alphaCutoff("Alpha Cutoff", Range(0,1)) = 0.5
        _CrownY("Crown centre height (object m)", Float) = 3
        _CrownR("Crown radius (object m)", Float) = 2.5
        _Bend("Normal bend toward the crown sphere", Range(0,1)) = 0.7
        _Wrap("Diffuse wrap", Range(0,1)) = 0.45
        _Trans("Translucency", Range(0,2)) = 0.55
        _Amb("Ambient multiplier", Range(0,3)) = 1.25
        _Inner("Inner crown darkening", Range(0,1)) = 0.35
        _Vary("Per-plant colour variation", Range(0,0.5)) = 0.12
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        _ShadowFloor("Light reaching shadowed leaves", Range(0,1)) = 0.3
        _Grade("Albedo toward its luminance x _GradeTint (0 off)", Range(0,1)) = 0
        _GradeTint("Grade tint (linear, may be > 1)", Vector) = (1,1,1,0)
        _DirShade("Direct light multiplier (impostors: no self-shadow)", Range(0,1)) = 1
        _Debug("Debug view (0 off, 1 albedo, 2 ambient, 3 direct)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 baseColorTexture_ST;
            half4 baseColorFactor;
            half alphaCutoff;
            float _CrownY, _CrownR;
            half _Bend, _Wrap, _Trans, _Amb, _Inner, _Vary;
            float _Cull, _Debug;
            half _ShadowFloor;
            half _Grade, _DirShade;
            half4 _GradeTint;
        CBUFFER_END
        TEXTURE2D(baseColorTexture);
        SAMPLER(samplerbaseColorTexture);
        float _Wetness;
        // distance fade (TreeRenderer, per draw through a property block; outside the per-material buffer): a plant's visibility
        // from its origin's horizontal distance to the camera, fading in over _FadeIn.xy and out over _FadeIn.zw (m; off when
        // y <= x / w <= z), its shadow out over _FadeSh.xy. Dithered clip, not blending: full mesh / impostor cross-fade with
        // complementary patterns (in = 1 - noise), plants appear / vanish at their draw distance without popping
        float4 _FadeIn, _FadeSh;
        float _VegFadeFrame; // TAA on: the pattern moves every frame and the history blends it into a smooth fade

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
            half4 tintFog : TEXCOORD3;   // rgb per-plant tint, a fog factor
            half inner : TEXCOORD4;      // 0 at the crown centre, 1 at its rim
            half2 fade : TEXCOORD5;      // x fade-in, y fade-out visibility (1 = fully there)
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float Hash21(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }

        half2 FadeOf(float4 band)
        {
            float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
            float d = length(origin.xz - _WorldSpaceCameraPos.xz);
            half fin = band.y > band.x ? saturate((d - band.x) / (band.y - band.x)) : 1;
            half fout = band.w > band.z ? saturate((band.w - d) / (band.w - band.z)) : 1;
            return half2(fin, fout);
        }
        // interleaved gradient noise in screen pixels; fade-in uses the complement so a fading-out full mesh and the fading-in
        // impostor fill each pixel once between them
        void FadeClip(half2 fade, float2 pix)
        {
            UNITY_BRANCH if (min(fade.x, fade.y) < 1)
            {
                pix += _VegFadeFrame * float2(47, 17);
                half n = frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
                clip(fade.x - (1 - n) - 1e-3);
                clip(fade.y - n - 1e-3);
            }
        }

        float3 BentNormalOS(float3 pos, float3 n, out half inner)
        {
            float3 d = pos - float3(0, _CrownY, 0);
            float l = length(d);
            inner = saturate(l / max(_CrownR, 0.01));
            float3 s = l > 1e-4 ? d / l : float3(0, 1, 0);
            return normalize(lerp(n, s, _Bend));
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
            half inner;
            o.normalWS = TransformObjectToWorldNormal(BentNormalOS(input.positionOS.xyz, input.normalOS, inner));
            o.inner = inner;
            o.uv = TRANSFORM_TEX(input.uv, baseColorTexture);
            float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
            float h1 = Hash21(origin.xz * 0.731 + 17.0), h2 = Hash21(origin.zx * 1.37 + 3.1);
            // brightness ± _Vary, hue toward yellow-olive or blue-green
            half b = 1 + (h1 - 0.5) * 2 * _Vary;
            half t = (h2 - 0.5) * 2 * _Vary;
            o.tintFog.rgb = b * half3(1 + 0.6 * t, 1 + 0.15 * t, 1 - 0.9 * t);
            o.tintFog.a = ComputeFogFactor(p.positionCS.z);
            o.fade = FadeOf(_FadeIn);
            return o;
        }

        half4 LitFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            FadeClip(input.fade, input.positionCS.xy);
            half4 tex = SAMPLE_TEXTURE2D(baseColorTexture, samplerbaseColorTexture, input.uv) * baseColorFactor;
            clip(tex.a - alphaCutoff);
            // olive / crnika (25. 11.): the shared broadleaf texture is a yellow green; graded toward a silvery grey-green (olive) or
            // a dark but not black evergreen (crnika) by its luminance
            UNITY_BRANCH if (_Grade > 0) tex.rgb = lerp(tex.rgb, dot(tex.rgb, half3(0.2126, 0.7152, 0.0722)) * _GradeTint.rgb, _Grade);
            half3 albedo = tex.rgb * input.tintFog.rgb;
            half3 n = NormalizeNormalPerPixel(input.normalWS);
            float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 sc = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
            #else
                float4 sc = TransformWorldToShadowCoord(input.positionWS);
            #endif
            Light L = GetMainLight(sc, input.positionWS, half4(1,1,1,1));
            // (not × distanceAttenuation: the sun's culling-mask factor, 0 for some instanced draws). Leaves inside a crown still
            // get light through the gaps: received shadow never below _ShadowFloor
            half sh = lerp(_ShadowFloor, 1, L.shadowAttenuation);
            half ndl = dot(n, L.direction);
            half diff = saturate((ndl + _Wrap) / (1 + _Wrap));
            half back = pow(saturate(dot(v, -L.direction)), 3) * _Trans * (0.35 + 0.65 * sh);
            half occ = lerp(1 - _Inner, 1, input.inner);
            half aoInd = 1, aoDir = 1;
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                aoInd = ao.indirectAmbientOcclusion; aoDir = ao.directAmbientOcclusion;
            #endif
            half3 amb = max(SampleSH(n), SampleSH(half3(0, 1, 0)) * 0.6) * _Amb * occ * aoInd;
            half3 c = albedo * (amb + _DirShade * L.color * (diff * sh * aoDir * lerp(0.8, 1, occ) + back * half3(0.85, 1, 0.55)));
            UNITY_BRANCH if (_Wetness > 0.001) c *= 1 - 0.25 * _Wetness;
            if (_Debug > 0.5) return half4(_Debug < 1.5 ? albedo : _Debug < 2.5 ? amb : _Debug < 3.5 ? L.color * diff * sh : _Debug < 4.5 ? L.shadowAttenuation.xxx : L.distanceAttenuation.xxx, 1);
            c = MixFog(c, InitializeInputDataFog(float4(input.positionWS, 1), input.tintFog.a));
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
            // two-sided leaf cards: the normal bias must push away from the light, else every card facing away from the sun
            // shadows itself (the near crowns went black in the sun)
            n = dot(n, lightDir) < 0 ? -n : n;
            o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDir)));
            o.uv = TRANSFORM_TEX(input.uv, baseColorTexture);
            o.fade = FadeOf(float4(-2, -1, _FadeSh.xy));
            return o;
        }
        Varyings DepthVertex(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            half inner;
            o.normalWS = TransformObjectToWorldNormal(BentNormalOS(input.positionOS.xyz, input.normalOS, inner));
            o.uv = TRANSFORM_TEX(input.uv, baseColorTexture);
            o.fade = FadeOf(_FadeIn);
            return o;
        }
        half4 DepthFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            FadeClip(input.fade, input.positionCS.xy);
            clip(SAMPLE_TEXTURE2D(baseColorTexture, samplerbaseColorTexture, input.uv).a * baseColorFactor.a - alphaCutoff);
            return input.positionCS.z;
        }
        half4 NormalsFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            FadeClip(input.fade, input.positionCS.xy);
            clip(SAMPLE_TEXTURE2D(baseColorTexture, samplerbaseColorTexture, input.uv).a * baseColorFactor.a - alphaCutoff);
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
