Shader "Magistrala/OvercastSky"
{
    // Rain sky (WeatherSystem): the day / sunset HDRI (same mapping as Skybox/Panoramic) blended by _Overcast into a flat grey
    // overcast dome — horizon = the fog colour (so the far hills melt into it), zenith a little brighter (CIE overcast), slow
    // low-contrast cloud mottling. In Resources so the player keeps it.
    Properties
    {
        [NoScaleOffset] _MainTex("Base HDRI (lat-long)", 2D) = "grey" {}
        _Tint("Base Tint", Color) = (.5, .5, .5, 1)
        _Exposure("Base Exposure", Float) = 1
        _Rotation("Base Rotation", Float) = 0
        _HasBase("Has Base HDRI", Float) = 1
        _Overcast("Overcast", Range(0, 1)) = 1
        _ZenithMul("Zenith Brightness", Float) = 1.3
        _CloudContrast("Cloud Contrast", Range(0, 1)) = 0.22
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
            float _Exposure, _Rotation, _HasBase, _Overcast, _ZenithMul, _CloudContrast;

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }
            float H(float2 p) { float3 q = frac(float3(p.x, p.y, p.x) * 0.1031); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }
            float N(float2 p)
            {
                float2 c = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(H(c), H(c + float2(1, 0)), f.x), lerp(H(c + float2(0, 1)), H(c + float2(1, 1)), f.x), f.y);
            }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 col = 0;
                UNITY_BRANCH if (_Overcast < 0.999 && _HasBase > 0.5)
                {
                    float a = _Rotation * PI / 180.0, s = sin(a), c = cos(a);
                    float3 r = float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z); // Skybox/Panoramic's RotateAroundYInDegrees
                    float2 uv = float2(0.5, 1.0) - float2(atan2(r.z, r.x), acos(clamp(r.y, -1, 1))) * float2(0.5 / PI, 1.0 / PI);
                    half4 t = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0);
                    half alpha = max(_MainTex_HDR.w * (t.a - 1.0) + 1.0, 0.0);
                    col = (_MainTex_HDR.x * PositivePow(alpha, _MainTex_HDR.y)) * t.rgb * _Tint.rgb * 4.59479380 * _Exposure;
                }
                float3 hor = unity_FogColor.rgb;
                float up = saturate(d.y);
                float3 oc = lerp(hor, hor * _ZenithMul, sqrt(up));
                // cloud base: a plane ~1.5 km up, slow drift; faded at the horizon (no stretched noise there)
                float2 cp = d.xz / (d.y + 0.12) * 1.6 + _Time.y * float2(0.006, 0.002);
                float n = N(cp) * 0.55 + N(cp * 2.3 + 7.1) * 0.3 + N(cp * 5.1 - 3.7) * 0.15;
                oc *= 1.0 + (n - 0.5) * 2.0 * _CloudContrast * smoothstep(0.0, 0.35, d.y);
                oc = d.y < 0 ? hor * lerp(1.0, 0.75, saturate(-d.y * 3)) : oc;
                col = lerp(col, oc, _HasBase > 0.5 ? _Overcast : 1.0);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
