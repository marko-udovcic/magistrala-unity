// Colour/size proxy for the distant parked-car band. No textures, shadows or per-car CPU switches.
Shader "Magistrala/ParkedFar"
{
    Properties { _ProxyBand("Near start/end, far start/end", Vector) = (215,260,570,650) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ParkedFarForward"
            // Opaque depth state; Unity handles reversed-Z for this standard ShaderLab test.
            ZWrite On
            ZTest LEqual
            Cull Back
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _PARKEDFARDEBUG
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ProxyBand;
                float4 _ProxyEye;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(ParkProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ParkTint)
            UNITY_INSTANCING_BUFFER_END(ParkProps)
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 color : TEXCOORD0; float fog : TEXCOORD1; nointerpolation float2 fade : TEXCOORD2; };
            V Vert(A i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(p);
                float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float d = distance(origin, _ProxyEye.xyz);
            #if defined(_PARKEDFARDEBUG)
                o.color = half3(1, 0, 1);
                o.fog = 0;
                // Preserve the proxy band's hard limits, but bypass all lighting, fog and dither.
                o.fade = float2(d - _ProxyBand.x, _ProxyBand.w - distance(origin.xz, _ProxyEye.xz));
            #else
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                half3 tint = UNITY_ACCESS_INSTANCED_PROP(ParkProps, _ParkTint).rgb;
                half3 albedo = lerp(i.color.rgb, tint, i.color.a);
                Light sun = GetMainLight();
                o.color = albedo * (SampleSH(n) + sun.color * saturate(dot(n, sun.direction)));
                o.fog = ComputeFogFactor(o.positionCS.z);
                // Match the existing LOD's origin-distance dither exactly at the inner handoff.
                o.fade.x = saturate((_ProxyBand.y - d) / max(.001, _ProxyBand.y - _ProxyBand.x));
                o.fade.y = saturate((_ProxyBand.w - distance(origin.xz, _ProxyEye.xz)) / max(.001, _ProxyBand.w - _ProxyBand.z));
            #endif
                return o;
            }
            half4 Frag(V i) : SV_Target
            {
            #if defined(_PARKEDFARDEBUG)
                clip(i.fade);
                return half4(1, 0, 1, 1);
            #else
                uint2 p = (uint2)i.positionCS.xy & 3;
                const float B[16] = { 0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5 };
                float threshold = (B[p.y * 4 + p.x] + .5) / 16.0;
                clip(threshold - i.fade.x);
                clip(i.fade.y - threshold);
                return half4(MixFog(i.color, i.fog), 1);
            #endif
            }
            ENDHLSL
        }
    }
}
