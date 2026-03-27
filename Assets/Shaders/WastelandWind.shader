Shader "Custom/WastelandWind"
{
    Properties
    {
        _Softness ("Edge Softness", Range(0.01, 0.6)) = 0.22
        _AlphaCutoff ("Alpha Cutoff", Range(0, 0.2)) = 0.02
        _Brightness ("Brightness", Range(0.3, 1.5)) = 0.85

        _ShadowDarken ("Shadow Darken", Range(0, 0.5)) = 0.15
        _LightAngle ("Light Angle (deg)", Float) = 135.0

        // Grit — jagged irregular edges
        _GritAmount ("Grit Irregularity", Range(0, 0.4)) = 0.18
        _GritFrequency ("Grit Frequency", Range(2, 20)) = 9.0

        // Toxic tint — sickly greenish pulse
        _ToxicStrength ("Toxic Tint", Range(0, 0.3)) = 0.06
        _ToxicSpeed ("Toxic Pulse Speed", Float) = 0.6
        _ToxicColor ("Toxic Color", Color) = (0.35, 0.42, 0.20, 1.0)

        // Desaturation — washes out color
        _Desaturation ("Desaturation", Range(0, 1)) = 0.4

        // Ember glow — additive hot particle glow
        _EmberGlow ("Ember Glow", Range(0, 1.0)) = 0.0
        _EmberGlowColor ("Ember Glow Color", Color) = (1.0, 0.6, 0.2, 1.0)

        // Corrosion edge — brownish-red fringe on edges
        _CorrosionEdge ("Corrosion Edge", Range(0, 0.3)) = 0.0
        _CorrosionColor ("Corrosion Color", Color) = (0.45, 0.20, 0.08, 1.0)

        // Acid haze — green-yellow atmospheric fog at distance
        _AcidHaze ("Acid Haze", Range(0, 0.2)) = 0.0
        _AcidHazeColor ("Acid Haze Color", Color) = (0.40, 0.45, 0.20, 1.0)

        // Flickering darkness — subtle unstable light
        _FlickerStrength ("Flicker Strength", Range(0, 0.15)) = 0.07
        _FlickerSpeed ("Flicker Speed", Float) = 3.0
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
                float2 uv2    : TEXCOORD1;
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
            float _ShadowDarken, _LightAngle;
            float _GritAmount, _GritFrequency;
            float _ToxicStrength, _ToxicSpeed;
            float4 _ToxicColor;
            float _Desaturation;
            float _EmberGlow;
            float4 _EmberGlowColor;
            float _CorrosionEdge;
            float4 _CorrosionColor;
            float _AcidHaze;
            float4 _AcidHazeColor;
            float _FlickerStrength, _FlickerSpeed;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.quadUV = v.uv * 2.0 - 1.0;
                o.depth = v.uv2.x;
                o.phase = v.uv2.y;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldXY = wp.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.quadUV;
                float dist = length(uv);

                // ── Gritty irregular edge ─────────────────────────
                float angle = atan2(uv.y, uv.x);
                float grit = 1.0
                    + sin(angle * _GritFrequency + i.phase * 30.0) * _GritAmount
                    + sin(angle * _GritFrequency * 1.7 - i.phase * 45.0) * _GritAmount * 0.6
                    + sin(angle * _GritFrequency * 0.5 + i.phase * 15.0) * _GritAmount * 0.4;
                dist *= grit;

                float circle = 1.0 - smoothstep(0.6 - _Softness, 0.7 + _Softness, dist);
                if (circle < _AlphaCutoff) discard;

                fixed4 col = i.color;

                // ── Desaturation ──────────────────────────────────
                float lum = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(col.rgb, float3(lum, lum, lum), _Desaturation);

                // ── Directional shading ───────────────────────────
                float lightX = cos(_LightAngle * 0.01745329);
                float lightY = sin(_LightAngle * 0.01745329);
                float shade = 1.0 - saturate(dot(uv, float2(-lightX, -lightY)) * 0.5) * _ShadowDarken;

                // ── Toxic tint pulse ──────────────────────────────
                float toxicPulse = 0.5 + 0.5 * sin(_Time.y * _ToxicSpeed + i.phase * 20.0 + i.worldXY.x * 0.5);
                float toxicAmount = _ToxicStrength * toxicPulse * (1.0 - i.depth * 0.7);
                col.rgb = lerp(col.rgb, _ToxicColor.rgb, toxicAmount);

                // ── Flickering darkness ───────────────────────────
                // Unstable light source — subtle brightness wobble
                float flicker1 = sin(_Time.y * _FlickerSpeed + i.worldXY.x * 1.5 + i.worldXY.y * 0.8) * 0.5 + 0.5;
                float flicker2 = sin(_Time.y * _FlickerSpeed * 1.7 + i.phase * 40.0) * 0.5 + 0.5;
                float flickerDarken = 1.0 - _FlickerStrength * flicker1 * flicker2;

                col.rgb *= shade * _Brightness * flickerDarken;

                // ── Corrosion edge ────────────────────────────────
                // Brownish-red fringe at particle edges
                float edgeBand = smoothstep(0.3, 0.6, dist) * (1.0 - smoothstep(0.6, 0.9, dist));
                col.rgb = lerp(col.rgb, _CorrosionColor.rgb * 0.5, edgeBand * _CorrosionEdge);

                // ── Ember glow ────────────────────────────────────
                // Additive inner glow for hot particles
                if (_EmberGlow > 0.01)
                {
                    float centerGlow = 1.0 - smoothstep(0.0, 0.5, dist);
                    float pulse = 0.6 + 0.4 * sin(_Time.y * 5.0 + i.phase * 30.0);
                    col.rgb += _EmberGlowColor.rgb * _EmberGlow * centerGlow * pulse;
                }

                // ── Acid haze at distance ─────────────────────────
                float hazeAmount = i.depth * _AcidHaze;
                col.rgb = lerp(col.rgb, _AcidHazeColor.rgb, hazeAmount);

                col.rgb = saturate(col.rgb);

                // ── Alpha ─────────────────────────────────────────
                float centerDense = 1.0 - smoothstep(0.0, 0.4, dist);
                col.a *= circle * lerp(0.6, 1.0, centerDense);

                return col;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
