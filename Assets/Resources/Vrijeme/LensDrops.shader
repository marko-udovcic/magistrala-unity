Shader "Magistrala/LensDrops"
{
    // Rain on the camera lens (WeatherSystem): a full-screen quad written straight in clip space, drawn last in the transparent
    // queue (before post). Procedural drops refract the opaque colour texture (inverted, blurred image inside each drop, dark
    // rim): small beads that appear and dry, and a few bigger drops that slide down leaving a thin trail. Subtle on purpose.
    Properties
    {
        _Amount("Amount", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+200" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "LensDrops"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            float _Amount;
            float _LensWind;   // 0..1: camera speed — drops blown sideways / fewer beads at speed

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float4 screen : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.positionCS = float4(i.uv * 2 - 1, UNITY_NEAR_CLIP_VALUE, 1);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }
            float H1(float2 p) { float3 q = frac(float3(p.x, p.y, p.x) * 0.1031); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }
            float3 H3(float2 p) { return float3(H1(p), H1(p + 17.3), H1(p + 41.9)); }

            // static beads: one possible bead per cell, appears, sits, shrinks away
            void Beads(float2 q, float t, float density, inout float2 n, inout float m)
            {
                float2 cell = floor(q); float2 f = frac(q) - 0.5;
                float3 r = H3(cell);
                float life = frac(t * (0.05 + 0.05 * r.z) + r.x * 5.7);
                float on = step(1.0 - density, H1(cell + 3.7));
                float rad = (0.12 + 0.16 * r.z) * (1.0 - smoothstep(0.55, 1.0, life)) * smoothstep(0.0, 0.04, life);
                float2 d = f - (r.xy - 0.5) * 0.55;
                float l = length(d) / max(rad, 1e-3);
                float k = on * (1.0 - smoothstep(0.8, 1.0, l));
                n += d / max(rad, 1e-3) * k; m = max(m, k);
            }
            // sliding drops: per cell a drop that runs down (with a little sideways wiggle) and a trail of tiny beads above it
            void Slide(float2 p, float2 scale, float t, float wind, inout float2 n, inout float m)
            {
                float2 q = p * scale;
                float2 cell = floor(q); float2 f = frac(q) - float2(0.5, 0.0);
                float3 r = H3(float2(cell.x, cell.y * 7.1));
                if (r.z < 0.4) return;
                float ph = frac(t * (0.10 + 0.16 * r.x) + r.y);
                float y = 1.05 - ph * ph * 1.15;                 // accelerates down the cell
                float x = (r.x - 0.5) * 0.5 + sin(ph * 9.0 + r.y * 6.3) * 0.04 + (1.0 - y) * wind * 0.35;
                float2 d = (f - float2(x, y)) / scale;            // in screen heights
                float rad = 0.008 + 0.008 * r.z;
                d.y *= 0.8;                                       // a bit taller than wide
                float l = length(d) / rad;
                float k = 1.0 - smoothstep(0.75, 1.0, l);
                n += d / rad * k; m = max(m, k);
                // thin wet trail with tiny beads above the drop
                float above = (f.y - y) / scale.y;
                if (above > 0.0 && above < 0.12)
                {
                    float tx = (f.x - x) / scale.x + above * wind * 0.3;
                    float fadeT = 1.0 - above / 0.12;
                    float tr = (1.0 - smoothstep(0.0008, 0.0022, abs(tx))) * fadeT;
                    float bi = floor(above * 160.0 + r.x * 3.0);
                    float2 bd = float2(tx, frac(above * 160.0 + r.x * 3.0) / 160.0 - 0.5 / 160.0) / 0.0022;
                    float bk = (1.0 - smoothstep(0.6, 1.0, length(bd))) * fadeT * step(0.55, H1(float2(cell.x, bi)));
                    n += bd * bk * 0.6 + float2(sign(tx) * tr * 0.4, 0); m = max(m, max(tr * 0.3, bk * 0.8));
                }
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 p = float2(uv.x * aspect, uv.y);
                float t = _Time.y;
                float2 n = 0; float m = 0;
                float bead = 1.0 - _LensWind * 0.6;
                Beads(p * 30.0, t, 0.2 * bead, n, m);
                Beads(p * 17.0 + 3.1, t * 0.8, 0.08 * bead, n, m);
                Slide(p, float2(7.0, 1.6), t, _LensWind, n, m);
                Slide(p + float2(0.37, 0.51), float2(5.0, 1.1), t * 0.85, _LensWind, n, m);
                m *= _Amount;
                clip(m - 0.003);
                // a drop is a tiny fisheye: shows the scene around it, upside down and soft
                float2 off = -n * 0.022 * float2(1.0 / aspect, 1.0);
                float2 su = saturate(uv + off);
                float2 b = float2(0.0025 / aspect, 0.0025);
                half3 c = (SampleSceneColor(su + b) + SampleSceneColor(su - b) + SampleSceneColor(su + float2(b.x, -b.y)) + SampleSceneColor(su - float2(b.x, -b.y))) * 0.25;
                float rim = saturate(length(n));
                c *= 1.0 - 0.45 * smoothstep(0.5, 1.0, rim);
                c += 0.12 * smoothstep(0.6, 0.95, saturate(dot(n, float2(-0.5, 0.7)))) * dot(c + 0.3, half3(0.2126, 0.7152, 0.0722)); // sky glint on the upper rim
                return half4(c, m * 0.92);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
