Shader "Magistrala/Spray"
{
    // Wheel spray / mist on a wet road (WetSpray): soft round billboard, vertex colour, faded where it meets geometry
    // (camera depth texture) and near the camera so a puff passing the lens never fills the screen.
    Properties
    {
        _Color("Colour (linear)", Color) = (0.6, 0.62, 0.65, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Spray"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off ZTest LEqual Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 _Color;
            struct A { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float4 screen : TEXCOORD1; float eye : TEXCOORD2; };
            V vert(A i)
            {
                V o;
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(w);
                o.color = i.color; o.uv = i.uv;
                o.screen = ComputeScreenPos(o.positionCS);
                o.eye = -TransformWorldToView(w).z;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 d = i.uv * 2 - 1;
                float r = dot(d, d);
                float a = exp(-r * 3.5) * saturate(1 - r); // gaussian-ish: soft mist, not discs
                float2 suv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                a *= saturate((scene - i.eye) / 0.6) * saturate((i.eye - 2.0) / 4.0);
                return half4(_Color.rgb * i.color.rgb, a * i.color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
