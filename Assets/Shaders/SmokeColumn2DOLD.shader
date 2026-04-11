Shader "Custom/SmokeColumn2D"
{
    Properties
    {
        _SmokeColor ("Smoke Color", Color) = (0.72, 0.75, 0.80, 1.0)
        _SmokeDark ("Smoke Dark Core", Color) = (0.50, 0.54, 0.60, 1.0)
        _Density ("Density", Range(0, 4)) = 0.8
        _NoiseScale ("Noise Scale", Float) = 1.2
        _RiseSpeed ("Rise Speed", Float) = 0.25
        _BillowSpeed ("Billow Speed", Float) = 0.5
        _BillowAmount ("Billow Amount", Range(0, 4)) = 1.5
        _Dissipation ("Top Dissipation", Range(0.1, 3)) = 1.0
        _WindBend ("Wind Bend Amount", Range(0, 2)) = 0.5
        _WindDir ("Wind Direction XY", Vector) = (1, 0.1, 0, 0)
        _DetailScale ("Detail Scale", Float) = 3.0
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.4
        _InternalLight ("Internal Light", Range(0, 0.5)) = 0.15
        _LightDir ("Light Direction XY", Vector) = (-0.7, 0.7, 0, 0)
        _Phase ("Phase", Float) = 0
        _ThresholdLow ("Threshold Low", Range(0, 0.5)) = 0.08
        _ThresholdHigh ("Threshold High", Range(0.3, 1)) = 0.72
    }
    SubShader
    {
        Tags { "Queue"="Transparent+11" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
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
                float2 uv     : TEXCOORD0;
                float2 uv2    : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float4 color   : COLOR;
                float2 uv      : TEXCOORD0;
                float2 worldXY : TEXCOORD1;
                float  phase   : TEXCOORD2;
                float  shapeMask : TEXCOORD3; // pre-computed column shape mask
            };

            float4 _SmokeColor, _SmokeDark;
            float _Density, _NoiseScale, _RiseSpeed;
            float _BillowSpeed, _BillowAmount, _Dissipation;
            float _WindBend;
            float4 _WindDir;
            float _DetailScale, _DetailStrength;
            float _InternalLight;
            float4 _LightDir;
            float _Phase;
            float _ThresholdLow, _ThresholdHigh;

            // ── Optimized noise ─────────────────────────────────
            float hash21(float2 p)
            {
                float h = dot(p, float2(127.1, 311.7));
                return frac(sin(h) * 43758.5453);
            }

            float gradNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 3-octave FBM — primary smoke noise (was fbm5)
            float fbm3(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                float2x2 rot = float2x2(0.8, 0.6, -0.6, 0.8);
                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    v += a * gradNoise(p);
                    p = mul(rot, p) * 2.02;
                    a *= 0.49;
                }
                return v;
            }

            // 2-octave FBM — detail (was fbm3)
            float fbm2(float2 p)
            {
                float v = 0.5 * gradNoise(p);
                p = mul(float2x2(0.8, 0.6, -0.6, 0.8), p) * 2.04;
                v += 0.25 * gradNoise(p);
                return v;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float t = _Time.y;
                float heightT = v.uv.y;
                float phase = _Phase;

                // ── Billowing vertex displacement ──────────
                float billowPhase = t * _BillowSpeed + heightT * 4.0 + phase;
                float billow1 = sin(billowPhase * 3.0) * _BillowAmount;
                float billow2 = sin(billowPhase * 1.7 + 1.5) * _BillowAmount * 0.6;
                float billow3 = sin(billowPhase * 4.5 + 3.0) * _BillowAmount * 0.3;
                float totalBillow = billow1 + billow2 + billow3;

                float horzSign = sign(v.uv.x - 0.5);
                float horzDist = abs(v.uv.x - 0.5) * 2.0;
                v.vertex.x += totalBillow * horzSign * horzDist * (0.4 + heightT * 0.6);

                // Wind bending
                float bendAmount = heightT * heightT * _WindBend;
                v.vertex.x += bendAmount * _WindDir.x * 3.0;
                v.vertex.y += bendAmount * _WindDir.y;

                // Vertical rise oscillation
                v.vertex.y += sin(t * _BillowSpeed * 0.5 + phase) * heightT * 0.3;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.phase = phase;

                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldXY = wp.xy;

                // Pre-compute column shape mask in vertex shader (moved from fragment)
                float hMask = 1.0 - smoothstep(0.6, 1.0, horzDist);
                float topFade = 1.0 - smoothstep(0.6, 1.0, heightT * _Dissipation);
                float bottomFade = smoothstep(0.0, 0.08, heightT);
                o.shapeMask = hMask * topFade * bottomFade;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Early out — if shape mask is near zero, skip all noise
                float mask = i.shapeMask * i.color.a;
                if (mask < 0.005) discard;

                float t = _Time.y;

                // ── Rising noise (single pass — was 2x fbm5 = 10 octaves, now 1x fbm3 = 3) ──
                float2 sp = i.worldXY * _NoiseScale + i.phase * 7.0;
                sp.y -= t * _RiseSpeed;
                sp.x += t * _WindDir.x * _RiseSpeed * 0.3;

                float mainNoise = fbm3(sp);

                // Detail (was fbm3 = 3 octaves, now fbm2 = 2)
                float2 dp = i.worldXY * _NoiseScale * _DetailScale + i.phase * 5.0;
                dp.y -= t * _RiseSpeed * 1.6;
                float detail = fbm2(dp);

                float density = mainNoise + detail * _DetailStrength;
                density = smoothstep(_ThresholdLow, _ThresholdHigh, density);

                // Apply pre-computed shape mask
                density *= mask * _Density;

                if (density < 0.003) discard;

                // ── Cheap internal light approximation ────────────
                // Use main noise offset instead of a separate fbm3 call
                float lightDiff = saturate(mainNoise * 0.3 - 0.1) * _InternalLight;

                // ── Color ─────────────────────────────────────────
                float3 col = lerp(_SmokeColor.rgb, _SmokeDark.rgb, saturate(density * 1.5));
                col += lightDiff;
                col = lerp(col, _SmokeColor.rgb * 1.15, i.uv.y * 0.25);
                col *= i.color.rgb;
                col = saturate(col);

                return fixed4(col, saturate(density));
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
