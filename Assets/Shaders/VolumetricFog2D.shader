Shader "Custom/VolumetricFog2D"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.78, 0.82, 0.86, 1.0)
        _FogColorDeep ("Deep Fog Color", Color) = (0.60, 0.65, 0.72, 1.0)
        _Density ("Density", Range(0, 4)) = 1.0
        _NoiseScale ("Noise Scale", Float) = 0.8
        _ScrollSpeed ("Scroll Speed", Float) = 0.4
        _WindDir ("Wind Direction XY", Vector) = (1, 0.15, 0, 0)
        _WarpStrength ("Vertex Warp Strength", Float) = 0.8
        _WarpSpeed ("Vertex Warp Speed", Float) = 0.5
        _DetailScale ("Detail Noise Scale", Float) = 2.5
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.4
        _Wispiness ("Wispiness", Range(0.3, 4)) = 1.5
        _ThresholdLow ("Density Threshold Low", Range(0, 0.5)) = 0.12
        _ThresholdHigh ("Density Threshold High", Range(0.3, 1)) = 0.75
        _DensityFloor ("Density Floor", Range(0, 0.4)) = 0.08
        _PulseSpeed ("Pulse Speed", Float) = 0.3
        _PulseAmount ("Pulse Amount", Range(0, 0.5)) = 0.2
        _Phase ("Phase Offset", Float) = 0.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" }
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
                float  colorVar : TEXCOORD3; // pre-computed color variation
            };

            float4 _FogColor;
            float4 _FogColorDeep;
            float _Density;
            float _NoiseScale;
            float _ScrollSpeed;
            float4 _WindDir;
            float _WarpStrength;
            float _WarpSpeed;
            float _DetailScale;
            float _DetailStrength;
            float _Wispiness;
            float _ThresholdLow;
            float _ThresholdHigh;
            float _DensityFloor;
            float _PulseSpeed;
            float _PulseAmount;
            float _Phase;

            // ── Optimized noise ─────────────────────────────────
            // Faster hash — single multiply-add chain
            float hash21(float2 p)
            {
                float h = dot(p, float2(127.1, 311.7));
                return frac(sin(h) * 43758.5453);
            }

            float gradNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                // Quintic interpolation for smooth results
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 3-octave FBM — primary noise (was fbm5, reduced with no visible quality loss at fog scale)
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

            // 2-octave FBM — detail wisps (was fbm3)
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

                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float t = _Time.y;

                // ── VERTEX WARPING — mesh undulates like steam ──
                float warpPhase = _Phase + wp.x * 0.15 + wp.y * 0.1;
                float warpX = sin(t * _WarpSpeed + warpPhase) * _WarpStrength
                            + sin(t * _WarpSpeed * 0.6 + warpPhase * 2.3) * _WarpStrength * 0.5
                            + sin(t * _WarpSpeed * 1.4 + warpPhase * 0.7) * _WarpStrength * 0.3;
                float warpY = cos(t * _WarpSpeed * 0.8 + warpPhase * 1.4) * _WarpStrength * 0.4
                            + cos(t * _WarpSpeed * 0.4 + warpPhase * 1.8) * _WarpStrength * 0.25;

                // Edge verts warp more (center stays stable)
                float edgeFactor = length(v.uv - 0.5) * 2.0;
                edgeFactor = saturate(edgeFactor);

                v.vertex.x += warpX * edgeFactor;
                v.vertex.y += warpY * edgeFactor;

                // Gentle continuous wind push
                v.vertex.x += _WindDir.x * t * _ScrollSpeed * 0.02 * edgeFactor;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.phase = v.uv2.y + _Phase;

                wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldXY = wp.xy;

                // Pre-compute color variation in vertex shader (moved from fragment)
                o.colorVar = sin(wp.x * 0.2 + wp.y * 0.15 + t * 0.02 + _Phase) * 0.5 + 0.5;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;

                // ── Scrolling sample coordinates ──────────────────
                float2 scroll = _WindDir.xy * _ScrollSpeed * t;

                // Turbulent offset
                float2 turbOffset = float2(
                    sin(t * 0.3 + i.worldXY.y * 0.08 + i.phase) * 0.6,
                    cos(t * 0.25 + i.worldXY.x * 0.06 + i.phase) * 0.4
                );

                // Wispy stretch
                float2 samplePos = i.worldXY * _NoiseScale;
                samplePos.y /= max(0.3, _Wispiness);
                samplePos += scroll + turbOffset;

                // ── Single FBM pass for main density (was 2x fbm5 = 10 octaves, now 1x fbm3 = 3) ──
                float mainNoise = fbm3(samplePos);

                // Detail wisps (was fbm3 = 3 octaves, now fbm2 = 2)
                float2 detailPos = i.worldXY * _NoiseScale * _DetailScale;
                detailPos += scroll * 1.5 + turbOffset * 0.8;
                float detailNoise = fbm2(detailPos);

                float density = mainNoise + detailNoise * _DetailStrength;

                // ── Thresholding — cloud shapes ───────────────────
                density = smoothstep(_ThresholdLow, _ThresholdHigh, density);

                // ── Pulsing ───────────────────────────────────────
                float pulse = 1.0 + sin(t * _PulseSpeed + i.worldXY.x * 0.03) * _PulseAmount;
                density *= pulse;

                // Floor
                density = max(density, _DensityFloor);

                // Master
                density *= _Density;

                // Vertex alpha for soft mesh edges
                density *= i.color.a;

                // Early out for transparent pixels
                if (density < 0.005) discard;

                // ── Color ─────────────────────────────────────────
                float3 col = lerp(_FogColor.rgb, _FogColorDeep.rgb, saturate(density * 1.2));

                // Color variation from vertex shader (no per-pixel noise needed)
                float cv = i.colorVar;
                col.r += (cv - 0.5) * 0.04;
                col.b -= (cv - 0.5) * 0.03;

                col *= i.color.rgb;
                col = saturate(col);

                return fixed4(col, saturate(density));
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
