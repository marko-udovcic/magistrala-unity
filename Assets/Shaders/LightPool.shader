// The light of a street lamp on the ground (StreetLights): a mesh draped on the ground under each head at build time
// (PropsBuilder.Lamps), drawn additively at night. UV0.x = falloff 0..1 (1 under the head), COLOR = the lamp's colour x strength.
// _Level (StreetLights) scales it; the fog fades it out with distance.
Shader "Magistrala/LightPool"
{
    Properties
    {
        _Strength("Strength", Range(0, 2)) = .35
        _Level("Level (runtime)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-50" "IgnoreProjector"="True" }
        Pass
        {
            Name "Pool"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Strength, _Level;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; half f : TEXCOORD0; half4 col : COLOR; half fog : TEXCOORD1; };
            V Vert(A i)
            {
                V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.f = i.uv.x; o.col = i.col; o.fog = ComputeFogFactor(o.pos.z); return o;
            }
            half4 Frag(V i) : SV_Target
            {
                half f = saturate(i.f); f = f * f * (3 - 2 * f); f *= f;
                half3 c = i.col.rgb * f * _Strength * _Level;
                // fog: towards black (additive), so far pools melt into the night haze
                c = MixFogColor(c, half3(0, 0, 0), i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
