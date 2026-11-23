Shader "Magistrala/HazyPanoramic"
{
    // Sunny-day sky (LightPreset.sunnyLook): the day HDRI exactly as Skybox/Panoramic draws it, with a summer haze along the
    // horizon that ends in the fog colour — the hazed hills and the far sea melt into the sky instead of standing as a
    // cut-out against cobalt (what the overcast sky does in the rain). Same properties as Skybox/Panoramic (WeatherSystem
    // copies them into the rain sky). One texture sample, sky pixels only. In Resources so the player keeps it.
    Properties
    {
        [NoScaleOffset] _MainTex("Spherical (HDR)", 2D) = "grey" {}
        _Tint("Tint Color", Color) = (.5, .5, .5, 1)
        _Exposure("Exposure", Float) = 1
        _Rotation("Rotation", Float) = 0
        _Haze("Horizon Haze", Range(0, 1)) = 0.8
        _HazePow("Haze Falloff", Float) = 6
        _HazeMul("Haze Brightness", Float) = 1.15
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_HDR;
            float4 _Tint;
            float _Exposure, _Rotation, _Haze, _HazePow, _HazeMul;

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float a = _Rotation * PI / 180.0, s = sin(a), c = cos(a);
                float3 r = float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z); // Skybox/Panoramic's RotateAroundYInDegrees
                float2 uv = float2(0.5, 1.0) - float2(atan2(r.z, r.x), acos(clamp(r.y, -1, 1))) * float2(0.5 / PI, 1.0 / PI);
                half4 t = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0);
                half alpha = max(_MainTex_HDR.w * (t.a - 1.0) + 1.0, 0.0);
                float3 col = (_MainTex_HDR.x * PositivePow(alpha, _MainTex_HDR.y)) * t.rgb * _Tint.rgb * 4.59479380 * _Exposure;
                float w = d.y <= 0 ? 1.0 : pow(1.0 - saturate(d.y), _HazePow);
                col = lerp(col, unity_FogColor.rgb * _HazeMul, saturate(_Haze * w));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
