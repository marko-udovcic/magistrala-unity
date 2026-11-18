Shader "Magistrala/RainStreaks"
{
    // GPU rain (WeatherSystem): one static mesh of N quads, each a random point in a box. The vertex shader lets it fall, wraps
    // it around the camera (world-anchored, so drops stay put while you drive through them) and stretches it along the drop's
    // velocity relative to the camera over a short "shutter" time — at 80 km/h the streaks fly at you. No CPU simulation.
    Properties
    {
        _RainColor("Colour (linear, set by WeatherSystem)", Color) = (0.6, 0.62, 0.65, 1)
        _Alpha("Alpha", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Rain"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off ZTest LEqual Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _RainColor;
            float _Alpha;
            float3 _RainBox;       // box size (m)
            float3 _RainCentre;    // box centre (world)
            float3 _RainCamVel;    // camera velocity (m/s)
            float3 _RainWind;      // horizontal wind (m/s)
            float _RainSpeed;      // fall speed (m/s)
            float _RainShutter;    // streak length = relative speed × this (s)
            float _RainAmount;     // 0..1 (fade in / out, fraction of drops drawn)

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 rnd : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float3 uva : TEXCOORD0; };

            V vert(A i)
            {
                V o = (V)0;
                float3 seed = i.positionOS.xyz;
                float speed = _RainSpeed * (0.75 + 0.5 * i.rnd.x);
                float3 vel = float3(_RainWind.x, -speed, _RainWind.z);
                float3 P = seed * _RainBox + vel * _Time.y;
                float3 w = _RainCentre + (frac((P - _RainCentre) / _RainBox) - 0.5) * _RainBox;
                float3 rel = vel - _RainCamVel;
                float3 tail = w - rel * _RainShutter;
                float3 pos = lerp(w, tail, i.uv.y);
                float3 cam = _WorldSpaceCameraPos;
                float3 axis = w - tail; axis = dot(axis, axis) > 1e-8 ? normalize(axis) : float3(0, 1, 0);
                float3 toCam = normalize(cam - pos);
                float3 side = cross(axis, toCam); side = dot(side, side) > 1e-8 ? normalize(side) : float3(1, 0, 0);
                float dist = length(cam - w);
                float width = max(0.003, dist * 0.0012); // never thinner than ~1 px (no shimmering sub-pixel lines)
                pos += side * (i.uv.x * width);
                o.positionCS = TransformWorldToHClip(pos);
                float halfBox = min(_RainBox.x, _RainBox.z) * 0.5;
                float fade = saturate((dist - 0.7) / 1.5) * (1.0 - smoothstep(halfBox * 0.6, halfBox, dist));
                float keep = step(i.rnd.y, _RainAmount);
                o.uva = float3(i.uv, _Alpha * fade * keep * saturate(0.003 / width * 1.6));
                if (keep < 0.5) o.positionCS = float4(0, 0, -2, 1); // culled
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float across = 1.0 - i.uva.x * i.uva.x;
                float along = smoothstep(0.0, 0.25, i.uva.y) * smoothstep(1.0, 0.6, i.uva.y);
                return half4(_RainColor.rgb, i.uva.z * across * along);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
