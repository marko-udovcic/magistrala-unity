// Buildings (HouseBuilder / BuildingsFromTiles): one material for walls, roofs, trims, shutters and the small clutter of a chunk.
// Texture array layers (BuildingTextures): 0 plaster, 1 stone ashlar, 2 roof tiles (kupa kanalica), 3 brick, 4 concrete,
// 5 shutter louvres, 6 plain (painted metal / plastic), 7 rubble (dry-stone yard walls). UV0 = world metres on the face.
// Sources: Marko's approved Poly Haven CC0 set (25. 11. 2026): white_sandstone_blocks_02, clay_roof_tiles_02, large_red_bricks,
// concrete_wall_007, painted_metal_shutter, stone_wall; _Plaster layers 10 / 11 worn_plaster_wall and corrugated_iron_02 (hall roofs).
// UV2: x = baked occlusion 0..1 (window / door reveals, deeper = darker), y = drip mask under a window sill (1 at the sill),
//      z = darkening of the untinted stone layers (0 none .. 1 black), w = their greying (0 as photographed .. 1 grey).
//      Every mesh on this material must carry UV2 (zeros when unused): a missing stream reads garbage on Metal.
// UV1: x = lower layer + 8 * upper layer + 64 * plaster variant (0 = layer 0 / 4 as they are, 1..9 = _Plaster layer 0..8, used by
//      the plaster and concrete layers) + 1024 * old (peeled patches showing the stone, hairline cracks) + 2048 * rust overlay
//      (a coplanar quad 1 cm off the wall under an AC unit / balcony: UV2.x = across profile 0..1..0, UV2.y = 1 at the top .. 0), y = height above the building's ground (m), z = height below the wall top (m),
//      w = dirt 0..1. COLOR: rgb = tint of the tintable layers (linear), a = height of the lower / upper layer boundary / 40 m
//      (a horizontal line: stone ground floor under plaster, plaster under an unfinished brick top floor).
Shader "Magistrala/Facade"
{
    Properties
    {
        _Albedo("Albedo array", 2DArray) = "" {}
        _Normal("Normal xy + roughness b array (linear)", 2DArray) = "" {}
        _Grime("Grime masks (r streaks, g runs, b drips)", 2D) = "black" {}
        _Plaster("Plaster variants array (Poly Haven)", 2DArray) = "" {}
        _PlasterN("Plaster variants normal xy + roughness b", 2DArray) = "" {}
        _Micro("Near micro grain (r detail, gb normal xy)", 2D) = "grey" {}
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
        TEXTURE2D(_Grime); SAMPLER(sampler_Grime);
        TEXTURE2D_ARRAY(_Plaster); SAMPLER(sampler_Plaster);
        TEXTURE2D_ARRAY(_PlasterN);
        TEXTURE2D(_Micro); SAMPLER(sampler_Micro);
        float _Wetness; // global (WeatherSystem)

        // metres per texture repeat, per layer; tintable layers take the vertex colour, the others keep their own colour
        // (stone 4.2: ~13 courses = 32 cm blocks; brick 2.8: 20 cm courses, nearer the hollow clay blocks of unfinished storeys;
        // concrete 2.2 and louvres 2.2 = Poly Haven's real sizes, ~5 cm slats)
        static const float TILE[8] = { 1.6, 4.2, 3.0, 2.8, 2.2, 2.2, 1.0, 2.2 };
        static const float TINTED[8] = { 1, 0, 1, 0, 1, 1, 1, 0 };
        static const float GLOSS[8] = { 0.1, 0.12, 0.28, 0.08, 0.1, 0.3, 0.45, 0.06 };
        // the plaster variants: metres per repeat (Poly Haven's real sizes, the 1 m ones a little larger against visible repeats)
        static const float PTILE[16] = { 3.0, 2.2, 2.2, 4.0, 3.0, 2.0, 2.0, 2.5, 2.2, 1.0, 2.4, 2.7, 2.0, 2.0, 3.6, 1.8 };
        static const float PGLOSS[16] = { 0.08, 0.06, 0.07, 0.1, 0.14, 0.16, 0.12, 0.08, 0.08, 0.25, 0.07, 0.34, 0.08, 0.08, 0.1, 0.22 };
        // _Plaster layer 15: the pack's barrel tiles RoofTiles_A (kupa kanalica, Paket2 areas) — a variant of the ROOF layer
        // (variant 15 on layer 2: tinted / faded like layer 2)
        // layers 12 / 13: the UKRAINIAN CITY pack's light plasters Plaster_H / Plaster_J (tinted like the others), 14: its dressed
        // stone Brick_E — a variant of the STONE layer (variant 15 on layer 1: untinted, the house's stone tone as layer 1)
        // _Plaster layer 9 (variant 10) is not a plaster but the trimmed yard hedge (HouseBuilder Slot.Hedge): no peeling, grime,
        // rising damp or micro grain on it
        // roof layer: mean luminance 0.197 (linear) -> 0.40 (HouseBuilder.RoofTint)
        static const half ROOF_GREY = 2.03;

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float4 data : TEXCOORD1;
            float4 occ : TEXCOORD2;
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
            half4 occ : TEXCOORD6;
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
            o.occ = input.occ;
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }

        half4 LitFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float code = round(input.data.x);
            float flags = floor(code / 1024), vcode = fmod(code, 1024);
            float variant = floor(vcode / 64), base = fmod(vcode, 64);
            bool old = fmod(flags, 2) > .5, rustQuad = flags > 1.5;
            float lower = fmod(base, 8), upper = floor(base / 8);
            float layer = input.data.y < input.color.a * 40 ? lower : upper;
            int li = (int)layer;
            float3 wp0 = input.positionWS;
            float camDist = distance(wp0, _WorldSpaceCameraPos);
            // plaster / concrete with a variant: the Poly Haven plaster array instead of layer 0 / 4
            bool stoneVar = variant > 14.5 && li == 1;
            bool roofVar = variant > 14.5 && li == 2;
            bool pl = variant > .5 && variant < 14.5 && (li == 0 || li == 4) || stoneVar || roofVar;
            int pi = roofVar ? 15 : (int)variant - 1;
            bool hedge = pl && pi == 9;
            bool metal = pl && pi == 11; // corrugated hall roof: no micro grain, peeling or rising damp
            float2 uv = input.uv / (pl ? PTILE[max(pi, 0)] : TILE[li]);
            half3 tex, ns;
            UNITY_BRANCH if (pl)
            {
                tex = SAMPLE_TEXTURE2D_ARRAY(_Plaster, sampler_Plaster, uv, pi).rgb;
                ns = SAMPLE_TEXTURE2D_ARRAY(_PlasterN, sampler_Plaster, uv, pi).rgb;
            }
            else
            {
                tex = SAMPLE_TEXTURE2D_ARRAY(_Albedo, sampler_Albedo, uv, layer).rgb;
                ns = SAMPLE_TEXTURE2D_ARRAY(_Normal, sampler_Albedo, uv, layer).rgb;
            }
            // old plastered houses: patches where the render has come off (the stone layer shows, a dark broken edge) and hairline
            // cracks; only near (they alias far off and cost a sample)
            half peel = 0, crack = 0, peelRim = 0;
            UNITY_BRANCH if (old && pl && camDist < 70)
            {
                float2 pw = float2(input.uv.x, input.uv.y);
                // small patches (~0.3-1 m), more of them low on the wall (splash, damp) than up high
                float pn = Noise(pw * float2(1.3, 1.7) + 17.3) * .6 + Noise(pw * 4.1 + 4.1) * .4 + .1 * saturate(1 - input.data.y / 2.5);
                float near = saturate((70 - camDist) / 20);
                peel = saturate((pn - .8) * 22) * near;
                peelRim = saturate(1 - abs(pn - .8) * 30) * near;
                UNITY_BRANCH if (peel > 0.01)
                {
                    float2 suv = input.uv / TILE[1];
                    half3 st = SAMPLE_TEXTURE2D_ARRAY(_Albedo, sampler_Albedo, suv, 1).rgb * half3(.92, .9, .86);
                    half3 sn = SAMPLE_TEXTURE2D_ARRAY(_Normal, sampler_Albedo, suv, 1).rgb;
                    tex = lerp(tex, st, peel); ns = lerp(ns, sn, peel);
                }
                // cracks: iso-lines of a value noise, only where a second, slow noise allows (a few per wall)
                float cn = Noise(pw * float2(1.7, 1.1) + 9.7);
                float cl = abs(cn - .5);
                float allow = saturate((Noise(pw * .21 + 2.2) - .55) * 6);
                crack = saturate(1 - cl * 90) * allow * saturate((40 - camDist) / 15);
            }
            half rough = ns.b; // photoscanned roughness (BuildingTextures packs it into blue)
            // (the hall roof's corrugations are its form, not fine grain: a stronger normal; the orthophoto's light membranes /
            // galvanised sheets a little darker, so the sheets' shading reads from the road)
            half3 nt; nt.xy = (ns.xy * 2 - 1) * _NormalScale * (metal ? 1.8 : 1); nt.z = sqrt(saturate(1 - dot(nt.xy, nt.xy)));
            half3 albedo = tex * lerp(half3(1, 1, 1), input.color.rgb, pl && !stoneVar && !roofVar ? lerp(1, 0, peel) : TINTED[li]);
            albedo *= 1 - .45 * peelRim * (1 - peel) - .55 * crack;
            if (metal) albedo *= .8;
            // roof tiles with a free colour (HouseBuilder, colour measured on the orthophoto): COLOR.a lerps the tiles to their
            // own luminance x ROOF_GREY (mean 0.40 linear) before the tint, so the tint alone sets the roof's colour
            // (a = 0: the tile texture's own terracotta x tint, as the package buildings use it)
            if (li == 2) albedo = lerp(albedo, dot(tex, half3(.3, .59, .11)) * ROOF_GREY * input.color.rgb, input.color.a);
            // the house's own stone (limestone from warm cream to grey, damp or sun-bleached): zeros keep the layer as it is
            if (li == 1 || li == 7)
            {
                half lum = dot(albedo, half3(.3, .59, .11));
                albedo = lerp(albedo, lum.xxx * half3(.98, .99, 1.02), input.occ.w) * (1 - input.occ.z);
                // break the texture's repeat: block-sized tone patches (+-9 %) and a slow warm / grey drift across the wall
                float2 bw = float2(dot(input.positionWS.xz, float2(.7071, .7071)) + input.positionWS.x * .3, input.positionWS.y);
                albedo *= .91 + .18 * Noise(bw * float2(2.6, 3.4));
                albedo = lerp(albedo, albedo * half3(1.04, 1.0, .93), Noise(bw * .23 + 3.1) * .6);
            }
            // near micro grain (the walls are not plastic up close): fades in from 25 m, full within 12 m
            half micro = saturate((25 - camDist) / 13);
            UNITY_BRANCH if (micro > 0.01 && li != 2 && li != 5 && li != 6 && !hedge && !metal)
            {
                half3 mg = SAMPLE_TEXTURE2D(_Micro, sampler_Micro, input.uv / .45).rgb;
                albedo *= 1 + (mg.r - .5) * .5 * micro;
                nt.xy += (mg.gb * 2 - 1) * .55 * micro; nt.z = sqrt(saturate(1 - dot(nt.xy, nt.xy)));
            }
            half3 nWS = normalize(input.normalWS);
            half3 tWS = normalize(input.tangentWS.xyz);
            half3 bWS = cross(nWS, tWS) * input.tangentWS.w;
            half3 normalWS = normalize(nt.x * tWS + nt.y * bWS + max(nt.z, .2) * nWS);

            // weathering: large blotches, rain streaks from the eaves and under the sills, splash dirt at the foot of the wall,
            // faded roof patches; plus ambient occlusion where light cannot reach (reveals, the foot of the wall, under the eaves)
            float dirt = input.data.w;
            float3 wp = input.positionWS;
            half vertical = 1 - saturate(abs(nWS.y) * 2);
            float along = dot(wp.xz, normalize(float2(-nWS.z, nWS.x) + 1e-4));
            // one value-noise lookup on walls (GPU budget): large blotches
            float blot = Noise(float2(along * .5 + wp.y * .13, wp.y * .45));
            // rain streaks and drips: ambientCG leaking masks, 4.5 m wide x 5.5 m high repeat
            half3 leak = SAMPLE_TEXTURE2D(_Grime, sampler_Grime, float2(along * .22, wp.y * .18)).rgb;
            float streak = leak.r * leak.r;
            float eave = saturate(1 - input.data.z / 4.5);
            float foot = saturate(1 - input.data.y / 1.1);
            // drips from the window sills: the streak noise, narrower and stronger right under the sill
            half drip = input.occ.y * input.occ.y * saturate(leak.g * 1.6 + leak.b * 1.2 + .12);
            half grime = vertical * saturate(dirt * saturate(blot * .5 + streak * eave * 2.0 + foot * .6) + drip * (.35 + .5 * dirt));
            if (hedge) grime = 0;
            // rising damp: a darker, slightly green-brown band at the foot of the wall, its top edge ragged (old houses higher)
            float dampTop = (.3 + .45 * Noise(float2(along * .6, 3.7)) + .2 * Noise(float2(along * 2.9, 8.1))) * (old ? 1.5 : .8) * (.5 + dirt);
            half damp = vertical * saturate((dampTop - input.data.y) / .35) * (li == 2 || hedge ? 0 : 1) * .7;
            // rust runs under an AC unit / balcony slab (the overlay quad): the drip masks, narrowing downwards
            half rust = 0;
            if (rustQuad)
            {
                half prof = saturate(input.occ.x), top = saturate(input.occ.y);
                rust = saturate((leak.g * 1.5 + leak.b * 1.8 + .15) * prof * prof) * top * saturate(top * 3) * vertical;
            }
            if (li == 2) // roof: ±12 % tone patches, darker old tiles
                albedo *= .86 + .28 * Noise(wp.xz * .35) - .12 * dirt * Noise(wp.xz * 1.7 + 7);
            else // walls: broad ±8 % tone variation (repairs, sun-bleached patches) also on clean houses
                albedo *= .94 + .12 * blot * vertical + .06 * (1 - vertical);
            albedo *= 1 - .42 * grime;
            albedo = lerp(albedo, albedo * half3(.92, .88, .8), grime * .6);
            albedo *= 1 - .14 * damp; albedo = lerp(albedo, albedo * half3(.94, .96, .88), damp * .5);
            albedo = lerp(albedo, albedo * half3(.62, .42, .27), rust * .75);
            // occlusion: baked reveals, a soft dark band at the foot of every wall (contact with the ground), under the eaves
            half contact = saturate(1 - input.data.y / .8); contact *= contact;
            half under = input.data.z > 0 && li != 2 ? saturate(1 - input.data.z / .9) : 0;
            half ao = (1 - .6 * saturate(rustQuad ? 0 : input.occ.x)) * (1 - vertical * (.38 * contact + .3 * under * under));
            // the sun is most of the light here; URP occlusion only darkens the ambient part, so part of it goes into the albedo
            albedo *= lerp(1, ao, .55);

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
            surface.smoothness = saturate(lerp(pl ? PGLOSS[max(pi, 0)] : GLOSS[li], (1 - rough) * .6, .7)) * (1 - grime * .5) * (1 - damp * .3);
            UNITY_BRANCH if (_Wetness > 0.001)
            {
                half up = saturate(nWS.y * 1.5 - 0.3) * _Wetness;
                half side = _Wetness * (0.35 + 0.3 * streak * eave);
                surface.albedo *= 1.0 - 0.35 * max(up, side);
                surface.smoothness = lerp(surface.smoothness, 0.75, max(up, side * .5));
            }
            surface.normalTS = half3(0, 0, 1);
            surface.occlusion = ao;
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
