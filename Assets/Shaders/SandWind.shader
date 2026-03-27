Shader "Custom/SandWind"
{
    Properties
    {
        // Shape
        _Softness ("Edge Softness", Range(0.01, 0.6)) = 0.28
        _AlphaCutoff ("Alpha Cutoff", Range(0, 0.2)) = 0.02
        _Brightness ("Brightness", Range(0.5, 2.0)) = 1.08

        // Wind stretching
        _StretchAmount ("Wind Stretch", Range(0, 3.0)) = 1.2
        _StretchSpeed ("Stretch Pulse Speed", Float) = 1.5

        // Shading
        _ShadowDarken ("Shadow Darken", Range(0, 0.5)) = 0.12
        _LightAngle ("Light Angle (deg)", Float) = 135.0

        // Warm shadow tinting (desert shadows are warm purple-brown)
        _WarmShadow ("Warm Shadow Tint", Range(0, 0.2)) = 0.06
        _ShadowWarmColor ("Shadow Warm Color", Color) = (0.55, 0.40, 0.50, 1.0)

        // Heat haze
        _HazeStrength ("Heat Haze Strength", Range(0, 0.3)) = 0.06
        _HazeSpeed ("Heat Haze Speed", Float) = 1.5
        _HazeScale ("Heat Haze Scale", Float) = 3.0

        // Multi-scale sparkle (glinting sand grains)
        _SparkleStrength ("Sand Sparkle", Range(0, 0.5)) = 0.18
        _SparkleSpeed ("Sparkle Speed", Float) = 4.0
        _SparkleWarmth ("Sparkle Warmth", Range(0, 1.0)) = 0.6

        // Dust glow (warm atmospheric scattering)
        _DustGlow ("Dust Glow", Range(0, 0.3)) = 0.08
        _DustGlowColor ("Dust Glow Color", Color) = (0.95, 0.85, 0.55, 1.0)

        // Subsurface warm scattering (sand glows at edges from sunlight)
        _SubsurfaceStrength ("Subsurface Scatter", Range(0, 0.25)) = 0.08
        _SubsurfaceColor ("Subsurface Color", Color) = (1.0, 0.85, 0.55, 1.0)

        // Mirage shimmer at distance
        _MirageStrength ("Mirage Shimmer", Range(0, 0.15)) = 0.04
        _MirageSpeed ("Mirage Speed", Float) = 2.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+1" "RenderType"="Transparent" }
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
                float2 uv2    : TEXCOORD1;  // x = depth, y = phase
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float4 color   : COLOR;
                float2 quadUV  : TEXCOORD0;
                float2 worldXY : TEXCOORD1;
                float  depth   : TEXCOORD2;
                float  phase   : TEXCOORD3;
            };

            float _Softness, _AlphaCutoff, _Brightness;
            float _StretchAmount, _StretchSpeed;
            float _ShadowDarken, _LightAngle;
            float _WarmShadow;
            float4 _ShadowWarmColor;
            float _HazeStrength, _HazeSpeed, _HazeScale;
            float _SparkleStrength, _SparkleSpeed, _SparkleWarmth;
            float _DustGlow;
            float4 _DustGlowColor;
            float _SubsurfaceStrength;
            float4 _SubsurfaceColor;
            float _MirageStrength, _MirageSpeed;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float hash31(float3 p)
            {
                p = frac(p * float3(443.897, 441.423, 437.195));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            v2f vert(appdata v)
            {
                v2f o;

                float depth = v.uv2.x;
                float phase = v.uv2.y;

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // ── Heat haze distortion — layered sine waves ─────
                float t = _Time.y;
                float hazeX = sin(t * _HazeSpeed + worldPos.x * _HazeScale + worldPos.y * _HazeScale * 0.7)
                            * _HazeStrength * (1.0 - depth) * 0.5;
                hazeX += sin(t * _HazeSpeed * 1.6 + worldPos.x * _HazeScale * 2.3 + worldPos.y * _HazeScale * 0.4)
                       * _HazeStrength * (1.0 - depth) * 0.2;

                float hazeY = cos(t * _HazeSpeed * 0.8 + worldPos.y * _HazeScale * 1.3)
                            * _HazeStrength * (1.0 - depth) * 0.3;
                hazeY += cos(t * _HazeSpeed * 1.4 + worldPos.y * _HazeScale * 0.6 + worldPos.x * _HazeScale * 1.8)
                       * _HazeStrength * (1.0 - depth) * 0.15;

                v.vertex.x += hazeX;
                v.vertex.y += hazeY;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.quadUV = v.uv * 2.0 - 1.0;
                o.depth = depth;
                o.phase = phase;
                o.worldXY = worldPos.xy;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.quadUV;

                // ── Wind-direction stretch ────────────────────────
                float stretchPulse = 1.0 + sin(_Time.y * _StretchSpeed + i.phase * 30.0) * 0.25;
                float stretch = 1.0 + _StretchAmount * stretchPulse * (1.0 - i.depth * 0.6);

                float2 distUV = float2(uv.x / max(stretch, 0.5), uv.y * lerp(1.0, 1.3, _StretchAmount * 0.3));

                float dist = length(distUV);

                // ── Organic irregular edge ────────────────────────
                float angle = atan2(distUV.y, distUV.x);
                float irregular = 1.0
                    + sin(angle * 4.0 + i.phase * 35.0) * 0.08
                    + sin(angle * 7.0 - i.phase * 18.0) * 0.05
                    + sin(angle * 2.0 + i.phase * 50.0) * 0.04
                    + sin(angle * 11.0 + i.phase * 70.0) * 0.025; // extra fine grain
                dist *= irregular;

                float circle = 1.0 - smoothstep(0.5 - _Softness, 0.7 + _Softness, dist);
                if (circle < _AlphaCutoff) discard;

                fixed4 col = i.color;

                // ── Directional sunlight shading ──────────────────
                float lightX = cos(_LightAngle * 0.01745329);
                float lightY = sin(_LightAngle * 0.01745329);
                float shadingDot = dot(uv, float2(-lightX, -lightY));
                float shade = 1.0 - saturate(shadingDot * 0.5) * _ShadowDarken;

                // ── Warm shadow tinting ───────────────────────────
                // Desert shadows aren't cool/blue — they're warm purple-brown
                // from warm ground bounce light
                float shadowAmount = saturate(shadingDot * 0.5);
                col.rgb = lerp(col.rgb, col.rgb * _ShadowWarmColor.rgb * 2.0, shadowAmount * _WarmShadow);

                // ── Multi-scale sand grain sparkle ────────────────
                float t = _Time.y;

                // Scale 1: fine grain sparkle (individual grains)
                float sparkle1 = hash21(i.worldXY * 50.0 + floor(t * _SparkleSpeed) * 0.1);
                sparkle1 = step(0.92, sparkle1);

                // Scale 2: medium cluster sparkle
                float sparkle2 = hash21(i.worldXY * 20.0 + floor(t * _SparkleSpeed * 0.6) * 0.15);
                sparkle2 = step(0.95, sparkle2) * 0.6;

                // Scale 3: broad shimmer bands
                float sparkle3 = hash21(i.worldXY * 5.0 + floor(t * _SparkleSpeed * 0.25) * 0.2);
                sparkle3 = step(0.97, sparkle3) * 0.3;

                float totalSparkle = (sparkle1 + sparkle2 + sparkle3) * _SparkleStrength * (1.0 - i.depth);

                // Warm sparkle color: mix between white and gold based on warmth setting
                float3 sparkleWhite = float3(1.0, 0.98, 0.9);
                float3 sparkleGold = float3(1.0, 0.88, 0.55);
                float3 sparkleColor = lerp(sparkleWhite, sparkleGold, _SparkleWarmth) * totalSparkle;

                // ── Subsurface warm scattering ────────────────────
                // Sand particles glow at their edges from transmitted sunlight
                float rim = smoothstep(0.25, 0.65, dist) * (1.0 - smoothstep(0.65, 1.0, dist));
                float backlit = saturate(-shadingDot);
                float sss = rim * backlit * _SubsurfaceStrength * (1.0 - i.depth * 0.5);
                float3 sssColor = _SubsurfaceColor.rgb * sss;

                // ── Atmospheric dust glow ─────────────────────────
                float dustAmount = i.depth * _DustGlow;
                float3 dustGlow = _DustGlowColor.rgb * dustAmount;

                // ── Mirage shimmer at distance ────────────────────
                float mirageWave = sin(t * _MirageSpeed + i.worldXY.x * 1.5 + i.worldXY.y * 0.8)
                                 * sin(t * _MirageSpeed * 0.7 + i.worldXY.y * 2.0)
                                 * 0.5 + 0.5;
                float mirageBoost = mirageWave * _MirageStrength * i.depth;

                // ── Combine ───────────────────────────────────────
                col.rgb *= shade * _Brightness;
                col.rgb += sparkleColor + sssColor + dustGlow;
                col.rgb += mirageBoost * float3(1.0, 0.95, 0.8);
                col.rgb = saturate(col.rgb);

                // ── Alpha: center dense, edges feathered ──────────
                float centerDense = 1.0 - smoothstep(0.0, 0.4, dist);
                col.a *= circle * lerp(0.6, 1.0, centerDense);

                // Atmospheric perspective
                col.a *= lerp(1.0, 0.5, i.depth * 0.7);

                return col;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
