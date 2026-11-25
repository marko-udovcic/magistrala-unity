// Contact shadow under the player vehicles (Magistrala.VehicleLook): one quad at the wheels' contact height, multiplied over
// the ground — a soft rounded-box darkening (ambient occlusion of the body over the road) that the sun's shadow map and SSAO
// (off on brzo, half resolution on srednje) leave thin. No lighting, no textures: a few ALU per pixel under the vehicle.
Shader "Magistrala/ContactShadow"
{
    Properties
    {
        _Strength("Darkening at the centre", Range(0,1)) = 0.6
        _Soft("Edge softness (0..0.5 of the quad)", Range(0.01,0.5)) = 0.3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-50" "IgnoreProjector"="True" }
        Pass
        {
            Name "ContactShadow"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Strength, _Soft;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };
            V vert(A i)
            {
                V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; o.fog = ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 frag(V i) : SV_Target
            {
                // rounded box: distance from the inner rectangle, smooth to the quad's edge
                float2 d = abs(i.uv - 0.5) * 2;                       // 0 centre .. 1 edge
                float2 q = max(d - (1 - _Soft * 2), 0) / (_Soft * 2);
                half k = saturate(1 - length(q));
                k = k * k * (3 - 2 * k);
                half shade = 1 - _Strength * k;
                // fogged far away: back towards 1 (no dark patch through the haze)
                shade = lerp(1, shade, saturate(ComputeFogIntensity(i.fog)));
                return half4(shade.xxx, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
