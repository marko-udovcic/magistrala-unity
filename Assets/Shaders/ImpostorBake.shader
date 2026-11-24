// Editor only (TreeProtos.BakeImpostor): a plant's submesh drawn unlit as its albedo, alpha-clipped like the game's leaves, into
// the impostor atlas (with the Foliage leaf grade, so the atlas holds the graded colour). Alpha out is 0 / 1 (the clip), so the atlas' mips keep the crown's coverage.
Shader "Hidden/Magistrala/ImpostorBake"
{
    Properties
    {
        _MainTex("Albedo", 2D) = "white" {}
        _Color("Colour", Color) = (1,1,1,1)
        _Cutoff("Cutoff", Float) = 0.5
        _Grade("Grade", Float) = 0
        _GradeTint("Grade tint", Vector) = (1,1,1,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_ST; half4 _Color; half _Cutoff; half _Grade; half4 _GradeTint;
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw; return o; }
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                clip(c.a - _Cutoff);
                c.rgb = lerp(c.rgb, dot(c.rgb, half3(0.2126, 0.7152, 0.0722)) * _GradeTint.rgb, _Grade); // as Foliage's leaf grade
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
    }
}
