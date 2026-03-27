Shader "Custom/GrassWind"
{
    Properties
    {
        _WindStrength ("Wind Strength", Float) = 0.12
        _WindSpeed ("Wind Speed", Float) = 2.0
        _WindTurbulence ("Wind Turbulence", Float) = 2.0

        _GustStrength ("Gust Strength", Float) = 0.15
        _GustScale ("Gust Scale (lower = bigger patches)", Float) = 0.25
        _GustSpeed ("Gust Speed", Float) = 0.7

        // Shading — applied as MULTIPLIERS not subtractions to avoid black pixels
        _ShadowDarken ("Shadow Side Darken", Range(0, 0.5)) = 0.18
        _HighlightBrighten ("Highlight Side Brighten", Range(0, 0.5)) = 0.12
        _AmbientOcclusion ("Base Ambient Occlusion", Range(0, 0.5)) = 0.22
        _TipHighlight ("Tip Specular Highlight", Range(0, 0.4)) = 0.12
        _LightAngle ("Light Angle (degrees)", Float) = 135.0

        // Alpha cutoff — fragments below this alpha are discarded (removes dark fringe)
        _AlphaCutoff ("Alpha Cutoff", Range(0, 0.5)) = 0.05
    }
    SubShader
    {
        // Use premultiplied alpha: eliminates dark halos from overlapping
        // semi-transparent triangles. RGB is pre-multiplied by alpha in the 
        // fragment shader, then blended as One OneMinusSrcAlpha.
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend One OneMinusSrcAlpha
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
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1; // x = blade lean direction
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 data : TEXCOORD0; // x = heightRatio, y = bladeLean
            };

            float _WindStrength;
            float _WindSpeed;
            float _WindTurbulence;
            float _GustStrength;
            float _GustScale;
            float _GustSpeed;
            float _ShadowDarken;
            float _HighlightBrighten;
            float _AmbientOcclusion;
            float _TipHighlight;
            float _LightAngle;
            float _AlphaCutoff;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            v2f vert(appdata v)
            {
                v2f o;

                float heightRatio = v.uv.y;
                float swayAmount = pow(heightRatio, 1.5);

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // ============ GUST SYSTEM ============
                float2 gp = worldPos.xy * _GustScale;
                float gt = _Time.y * _GustSpeed;

                float gust1 = sin(gp.x * 0.7 + gp.y * 0.4 + gt) * 0.5;
                float gust2 = sin(gp.x * 0.3 - gp.y * 0.9 + gt * 1.4) * 0.3;
                float gust3 = sin(gp.x * 0.5 + gp.y * 0.7 - gt * 0.6) * 0.2;

                float gustPulse = 0.35 + 0.65 * (0.5 + 0.5 * sin(gt * 0.25 + worldPos.x * 0.08));
                float totalGust = (gust1 + gust2 + gust3) * _GustStrength * gustPulse;

                // ============ FINE TURBULENCE ============
                float t = _Time.y * _WindSpeed;

                float wind1 = sin(t + worldPos.x * _WindTurbulence + worldPos.y * 0.7)
                            * _WindStrength;
                float wind2 = sin(t * 1.4 + worldPos.x * _WindTurbulence * 0.5 - worldPos.y * 1.2)
                            * _WindStrength * 0.4;
                float wind3 = cos(t * 0.8 + worldPos.y * _WindTurbulence * 1.3 + worldPos.x * 0.6)
                            * _WindStrength * 0.25;

                float bladeHash = hash21(worldPos.xy * 10.0);
                float microJitter = (bladeHash - 0.5) * _WindStrength * 0.2
                                  * sin(t * 2.8 + bladeHash * 6.28);

                // ============ COMBINE ============
                float xDisplacement = (totalGust + wind1 + wind2 + wind3 + microJitter) * swayAmount;

                float yMicro = sin(t * 1.1 + worldPos.x * 1.7 + worldPos.y * 2.3)
                             * _WindStrength * 0.15 * swayAmount;

                v.vertex.x += xDisplacement;
                v.vertex.y += yMicro;
                v.vertex.y -= abs(xDisplacement) * 0.3 * swayAmount;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.data = float2(heightRatio, v.uv2.x);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = i.color;

                // Discard nearly invisible fragments — prevents dark fringe at edges
                if (col.a < _AlphaCutoff) discard;

                float heightRatio = i.data.x;
                float bladeLean = i.data.y;

                // ============ SHADING via MULTIPLIERS (never goes to black) ============
                // Instead of subtracting from RGB, we multiply by factors in [0.5, 1.3]
                // This darkens/brightens proportionally — dark greens stay green, not black

                // 1) Ambient occlusion at base
                // aoFactor goes from (1 - AO) at base to 1.0 by 33% height
                float aoFactor = lerp(1.0 - _AmbientOcclusion, 1.0, saturate(heightRatio * 3.0));

                // 2) Directional shading
                float lightDir = cos(_LightAngle * 0.01745329);
                float shadingDot = bladeLean * lightDir; // -1 to 1

                // Shadow: blades leaning away from light get darkened
                float shadowFactor = 1.0 - saturate(-shadingDot) * _ShadowDarken;
                // Highlight: blades leaning toward light get brightened
                float highlightFactor = 1.0 + saturate(shadingDot) * _HighlightBrighten;

                // 3) Tip specular (only on lit side)
                float tipSpec = heightRatio * heightRatio * _TipHighlight * saturate(shadingDot + 0.3);

                // Combined shading multiplier
                float shading = aoFactor * shadowFactor * highlightFactor;
                col.rgb *= shading;

                // Add tip specular as additive (small amount, won't cause issues)
                col.rgb += tipSpec;

                // 4) Subtle color temperature shift
                float shadowAmount = saturate(-shadingDot) * _ShadowDarken;
                float highlightAmount = saturate(shadingDot) * _HighlightBrighten;
                // Shadowed = cooler
                col.r *= (1.0 - shadowAmount * 0.12);
                col.b *= (1.0 + shadowAmount * 0.06);
                // Lit = warmer
                col.r *= (1.0 + highlightAmount * 0.08);

                col.rgb = saturate(col.rgb);

                // ============ PREMULTIPLIED ALPHA OUTPUT ============
                // Multiply RGB by alpha before output. This is what eliminates
                // the dark fringe: with premultiplied alpha and Blend One OneMinusSrcAlpha,
                // overlapping semi-transparent fragments blend correctly without
                // darkening where they overlap.
                col.rgb *= col.a;

                return col;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
