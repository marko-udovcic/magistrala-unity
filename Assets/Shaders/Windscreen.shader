Shader "Magistrala/Windscreen"
{
    Properties
    {
        _WiperAngleL ("Left angle (radians)", Float) = 0
        _WiperAngleR ("Right angle (radians)", Float) = 0
        _Sweep ("Sweep (radians)", Float) = 1.48353
        _Phase ("Cycle phase", Float) = 0
        _Recovery ("Seconds since last motion", Float) = 10
        _HasWiped ("Has wiped", Float) = 0
        _WipeElapsed ("Seconds since first stroke", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Name "Windscreen"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Global weather value: deliberately absent from Properties and UnityPerMaterial.
            float _Wetness;
            CBUFFER_START(UnityPerMaterial)
                float _WiperAngleL, _WiperAngleR, _Sweep, _Phase, _Recovery, _HasWiped, _WipeElapsed;
                float4 _WiperOriginL, _WiperOriginR, _WiperRight, _WiperUp, _CabForward;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }
            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float Wiped(float3 pos, float3 origin, float angleNow)
            {
                float3 d = pos - origin;
                float2 p = float2(dot(d, _WiperRight.xyz), dot(d, _WiperUp.xyz));
                float radius2 = dot(p,p);
                // The sweep stays in the first quadrant. A small polynomial avoids per-pixel atan2.
                float2 ap = abs(p);
                float ratio = min(ap.x,ap.y) / max(max(ap.x,ap.y),.00001);
                float angle = ratio * (.97239411 - .19194795 * ratio * ratio);
                angle = ap.y > ap.x ? HALF_PI-angle : angle;
                float sector = step(0.0,p.x) * step(0.0,p.y) * step(angle,_Sweep) * smoothstep(.0225,.0324,radius2) * (1.0-smoothstep(.3136,.3481,radius2));
                // Last crossing on either stroke; analytic history needs no mask texture or render pass.
                float x = clamp(1.0 - 2.0 * angle / max(_Sweep,.01), -1.0, 1.0);
                float ax = abs(x);
                float crossing = sqrt(1.0-ax) * (1.5707288 + ax*(-.2121144 + ax*(.0742610-.0187293*ax)));
                crossing = x < 0.0 ? PI-crossing : crossing;
                float ageA = frac((_Phase - crossing) / TWO_PI + 1.0) * (60.0 / 45.0);
                float ageB = frac((_Phase + crossing) / TWO_PI + 1.0) * (60.0 / 45.0);
                float sinceCrossing = min(ageA, ageB);
                float age = sinceCrossing + _Recovery;
                float blade = 1.0 - smoothstep(.015,.045,abs(angle-angleNow));
                return sector * _HasWiped * step(sinceCrossing, _WipeElapsed) * max(exp2(-age * 2.8), blade * step(_Recovery,.02));
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = saturate(i.uv);
                float edge = 1.0 - smoothstep(0.0,.13,min(min(uv.x,1.0-uv.x),min(uv.y,1.0-uv.y)));
                float dust = edge * (.4 + .6 * Hash(floor(uv*float2(230,100))));
                float strip = smoothstep(.79,.99,uv.y);
                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                float grazing = 1.0-saturate(abs(dot(normalize(i.normalWS),view)));
                float fresnel = grazing*grazing*grazing;
                float3 reflected = reflect(-view,normalize(i.normalWS));
                half3 sky = lerp(half3(.43,.53,.57),half3(.25,.43,.56),saturate(reflected.y));
                // Forward sun near the horizon makes a faint, broad dashboard ghost.
                float lowSun = saturate(1.0-abs(_MainLightPosition.y)*3.0) * saturate(dot(normalize(_MainLightPosition.xyz),_CabForward.xyz));
                float dash = (1.0-smoothstep(.06,.30,abs(uv.y-.22))) * lowSun;
                float2 grid = uv*float2(105,65);
                float2 cell = floor(grid);
                float seed = Hash(cell);
                float2 q = frac(grid + float2(0,_Time.y*(.12+seed*.16))) - float2(.5,.5);
                q.x += (seed-.5)*.45;
                float2 ellipse = q*float2(1.0,.55);
                float drop = (1.0-smoothstep(.002025,.013225,dot(ellipse,ellipse))) * step(.50,seed);
                float clear = max(Wiped(i.positionWS,_WiperOriginL.xyz,_WiperAngleL),Wiped(i.positionWS,_WiperOriginR.xyz,_WiperAngleR));
                float rain = drop * saturate(_Wetness) * (1.0-clear);
                float alpha = .006 + strip*.065 + dust*.023 + fresnel*.055 + dash*.026 + rain*.18;
                half3 color = lerp(sky,half3(.15,.34,.31),strip*.6);
                color = lerp(color,half3(.17,.19,.18),saturate(dust*.3+dash*.65));
                color = lerp(color,half3(.72,.81,.83),rain*.7);
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
