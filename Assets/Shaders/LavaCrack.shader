Shader "Custom/LavaCrack"
{
    Properties
    {
        _PulseSpeed ("Pulse Speed", Float) = 1.5
        _PulseAmount ("Pulse Amount", Range(0, 0.5)) = 0.25
        _CoreBrightness ("Core Brightness", Range(0.5, 3.0)) = 1.8
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.5)) = 0.15
        _FlickerSpeed ("Flicker Speed", Float) = 4.0
        _FlickerAmount ("Flicker Amount", Range(0, 0.3)) = 0.12
        _HeatDistort ("Heat Distortion", Range(0, 0.02)) = 0.005
        _HeatSpeed ("Heat Distort Speed", Float) = 3.0
        _AlphaCutoff ("Alpha Cutoff", Range(0, 0.1)) = 0.01
    }
    SubShader
    {
        Tags { "Queue"="Transparent+2" "RenderType"="Transparent" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;  // x = cross-width (0..1), y = along-length (0..1)
                float2 uv2    : TEXCOORD1;  // x = width ratio (0=edge,1=center), y = phase
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float4 color   : COLOR;
                float2 uv      : TEXCOORD0;
                float  centerT : TEXCOORD1;  // 0 at edges, 1 at center
                float  phase   : TEXCOORD2;
                float2 worldXY : TEXCOORD3;
            };

            float _PulseSpeed, _PulseAmount, _CoreBrightness;
            float _EdgeSoftness, _FlickerSpeed, _FlickerAmount;
            float _HeatDistort, _HeatSpeed, _AlphaCutoff;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            v2f vert(appdata v)
            {
                v2f o;

                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Heat distortion — subtle vertex wobble
                float heatPhase = _Time.y * _HeatSpeed + wp.x * 5.0 + wp.y * 3.7;
                float distX = sin(heatPhase) * _HeatDistort;
                float distY = cos(heatPhase * 0.8 + 1.3) * _HeatDistort * 0.7;
                v.vertex.x += distX;
                v.vertex.y += distY;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.centerT = v.uv2.x;
                o.phase = v.uv2.y;
                o.worldXY = wp.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = i.color;
                float centerT = i.centerT;

                // ── Global pulse — all lava breathes together ─────
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseAmount;

                // ── Per-crack flicker — unique per crack ──────────
                float flicker = 1.0 + sin(_Time.y * _FlickerSpeed + i.phase * 50.0) * _FlickerAmount
                                    + sin(_Time.y * _FlickerSpeed * 1.7 + i.phase * 30.0) * _FlickerAmount * 0.5;

                // ── Spatial noise flicker — flowing lava look ─────
                float flowNoise = sin(i.worldXY.x * 8.0 + i.worldXY.y * 6.0 + _Time.y * 2.0) * 0.5 + 0.5;
                float flowVar = lerp(0.85, 1.15, flowNoise);

                // ── Core brightness: center of crack glows much brighter ──
                // centerT is 1 at the middle of the crack, 0 at edges
                float brightMul = lerp(0.3, _CoreBrightness, centerT * centerT);

                // ── Edge fade ─────────────────────────────────────
                // Along the length, fade at the tips
                float lengthT = i.uv.y;
                float tipFade = smoothstep(0.0, 0.15, lengthT) * smoothstep(1.0, 0.85, lengthT);

                // Cross-width fade
                float widthFade = smoothstep(0.0, _EdgeSoftness, centerT);

                float totalBright = brightMul * pulse * flicker * flowVar * tipFade * widthFade;

                col.rgb *= totalBright;

                // Clamp to avoid blowing out too much
                col.rgb = min(col.rgb, 2.5);

                // Alpha controls the additive contribution
                col.a *= tipFade * widthFade;

                if (col.a < _AlphaCutoff) discard;

                return col * col.a;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
