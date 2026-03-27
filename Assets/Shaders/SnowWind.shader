Shader "Custom/SnowWind"
{
    Properties
    {
        _Softness ("Edge Softness", Range(0.01, 0.6)) = 0.3
        _AlphaCutoff ("Alpha Cutoff", Range(0, 0.2)) = 0.02
        _Brightness ("Brightness", Range(0.5, 2.0)) = 1.05

        _ShadowDarken ("Shadow Darken", Range(0, 0.5)) = 0.08
        _LightAngle ("Light Angle (deg)", Float) = 135.0

        // Spatial shimmer
        _ShimmerSpeed ("Shimmer Speed", Float) = 2.0
        _ShimmerStrength ("Shimmer Strength", Range(0, 0.5)) = 0.08

        // Crystal glint system
        _CrystalGlint ("Crystal Glint", Range(0, 1.0)) = 0.0
        _GlintSpeed ("Glint Speed", Float) = 3.5
        _GlintThreshold ("Glint Threshold", Range(0.5, 0.99)) = 0.85

        // Blue shadow tinting
        _ShadowBlue ("Shadow Blue Tint", Range(0, 0.15)) = 0.06
        _ShadowCool ("Shadow Cool Shift", Range(0, 0.1)) = 0.03

        // Subsurface frost glow
        _FrostGlow ("Frost Edge Glow", Range(0, 0.3)) = 0.10
        _FrostColor ("Frost Glow Color", Color) = (0.80, 0.88, 1.0, 1.0)

        // Depth fog (far particles subtly haze)
        _DepthFog ("Depth Fog Amount", Range(0, 0.4)) = 0.15
        _FogColor ("Fog Color", Color) = (0.75, 0.80, 0.90, 1.0)
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
                float2 uv2    : TEXCOORD1;  // x = depth (0=near,1=far), y = phase
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
            float _ShimmerSpeed, _ShimmerStrength;
            float _CrystalGlint, _GlintSpeed, _GlintThreshold;
            float _ShadowBlue, _ShadowCool;
            float _FrostGlow;
            float4 _FrostColor;
            float _DepthFog;
            float4 _FogColor;

            // ── Hash functions for sparkle ─────────────────────
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
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.quadUV = v.uv * 2.0 - 1.0; // -1..1
                o.depth = v.uv2.x;
                o.phase = v.uv2.y;

                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldXY = wp.xy;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ── Soft circle with organic irregularity ─────────
                float dist = length(i.quadUV);

                // Multi-frequency irregularity for natural snowflake feel
                float angle = atan2(i.quadUV.y, i.quadUV.x);
                float irregular = 1.0
                    + sin(angle * 6.0 + i.phase * 40.0) * 0.06
                    + sin(angle * 3.0 - i.phase * 20.0) * 0.04
                    + sin(angle * 8.0 + i.phase * 60.0) * 0.025;  // extra fine detail
                dist *= irregular;

                float circle = 1.0 - smoothstep(0.7 - _Softness, 0.7 + _Softness, dist);
                if (circle < _AlphaCutoff) discard;

                fixed4 col = i.color;

                // ── Directional shading with blue shadow ──────────
                float lightX = cos(_LightAngle * 0.01745329);
                float lightY = sin(_LightAngle * 0.01745329);
                float shadingDot = dot(i.quadUV, float2(-lightX, -lightY));
                float shade = 1.0 - saturate(shadingDot * 0.4) * _ShadowDarken;

                // Blue shadow tinting — snow in shadow picks up sky color
                float shadowAmount = saturate(shadingDot * 0.5);
                col.b += shadowAmount * _ShadowBlue;
                col.r -= shadowAmount * _ShadowCool;
                col.g -= shadowAmount * _ShadowCool * 0.5;

                // ── Base shimmer / sparkle ─────────────────────────
                float shimmer1 = sin(_Time.y * _ShimmerSpeed + i.phase * 50.0 + i.worldXY.x * 3.0) * 0.5 + 0.5;
                float shimmer2 = sin(_Time.y * _ShimmerSpeed * 1.7 + i.phase * 30.0 + i.worldXY.y * 2.5) * 0.5 + 0.5;
                float shimmer = shimmer1 * shimmer2;
                float shimmerBoost = shimmer * _ShimmerStrength * (1.0 - i.depth);

                // ── Crystal glint system ──────────────────────────
                // Sharp sparkle points that flash like ice catching sunlight
                float glintBoost = 0.0;
                if (_CrystalGlint > 0.01)
                {
                    // Animated spatial hash for glint positions
                    float t = _Time.y * _GlintSpeed;
                    float2 glintCoord = i.worldXY * 8.0;

                    // Multiple glint layers at different scales
                    float g1 = hash31(float3(floor(glintCoord), floor(t * 0.7)));
                    float g2 = hash31(float3(floor(glintCoord * 1.7 + 50.0), floor(t * 1.1 + 10.0)));
                    float g3 = hash31(float3(floor(glintCoord * 0.5 + 100.0), floor(t * 0.4 + 20.0)));

                    // Smooth transition for each glint
                    float ft = frac(t * 0.7);
                    float pulse1 = smoothstep(0.0, 0.15, ft) * smoothstep(0.4, 0.15, ft);
                    float ft2 = frac(t * 1.1 + 0.3);
                    float pulse2 = smoothstep(0.0, 0.12, ft2) * smoothstep(0.35, 0.12, ft2);
                    float ft3 = frac(t * 0.4 + 0.6);
                    float pulse3 = smoothstep(0.0, 0.2, ft3) * smoothstep(0.5, 0.2, ft3);

                    float glint = 0.0;
                    if (g1 > _GlintThreshold) glint += (g1 - _GlintThreshold) / (1.0 - _GlintThreshold) * pulse1;
                    if (g2 > _GlintThreshold) glint += (g2 - _GlintThreshold) / (1.0 - _GlintThreshold) * pulse2 * 0.7;
                    if (g3 > _GlintThreshold) glint += (g3 - _GlintThreshold) / (1.0 - _GlintThreshold) * pulse3 * 0.5;

                    // Center of the patch glints more
                    float centerMask = 1.0 - smoothstep(0.0, 0.6, dist);
                    glintBoost = glint * _CrystalGlint * centerMask * (1.0 - i.depth * 0.7);
                }

                // ── Frost edge glow ───────────────────────────────
                // Rim lighting effect — edges of snow patches glow faintly
                float rim = smoothstep(0.3, 0.7, dist) * (1.0 - smoothstep(0.7, 1.0, dist));
                float frostGlow = rim * _FrostGlow * (0.7 + 0.3 * shimmer1);
                float3 frostContrib = _FrostColor.rgb * frostGlow;

                // ── Apply shading ─────────────────────────────────
                col.rgb *= shade * _Brightness;
                col.rgb += shimmerBoost;
                col.rgb += glintBoost;
                col.rgb += frostContrib;

                // ── Depth fog — far particles haze toward sky color ─
                float fogAmount = i.depth * _DepthFog;
                col.rgb = lerp(col.rgb, _FogColor.rgb, fogAmount);

                col.rgb = saturate(col.rgb);

                // ── Alpha: center more opaque, soft fade ──────────
                float centerGlow = 1.0 - smoothstep(0.0, 0.5, dist);
                col.a *= circle * lerp(0.7, 1.0, centerGlow);

                // Glinting patches flash brighter alpha too
                col.a = saturate(col.a + glintBoost * 0.3);

                return col;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
