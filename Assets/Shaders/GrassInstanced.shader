Shader "Custom/GrassInstanced"
{
    Properties
    {
        _ShadowDarken ("Shadow Darken", Float) = 0.18
        _HighlightBrighten ("Highlight Brighten", Float) = 0.12
        _AmbientOcclusion ("Ambient Occlusion", Float) = 0.22
        _TipHighlight ("Tip Highlight", Float) = 0.12
        _LightAngle ("Light Angle", Float) = 135
        _SubsurfaceStrength ("Subsurface Strength", Float) = 0.15
        _SubsurfaceColor ("Subsurface Color", Color) = (0.4, 0.7, 0.15, 1)
        _WindColorShift ("Wind Color Shift", Float) = 0.1
        _PatchScale ("Patch Scale", Float) = 0.15
        _PatchStrength ("Patch Strength", Float) = 0.12
    }

    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"

            struct GrassBlade
            {
                float3 position;
                float  height;
                float  width;
                float  lean;
                float  curvature;
                float  phase;
                uint   packedType;
                float  padding;
                float4 colorBase;
                float4 colorTip;
            };

            StructuredBuffer<GrassBlade> _BladeBuffer;

            float _GameTime;
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
            float _SubsurfaceStrength;
            float4 _SubsurfaceColor;
            float _WindColorShift;
            float _PatchScale;
            float _PatchStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float2 uv    : TEXCOORD0;
            };

            float hash2D(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float noise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash2D(i);
                float b = hash2D(i + float2(1, 0));
                float c = hash2D(i + float2(0, 1));
                float d = hash2D(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                GrassBlade blade = _BladeBuffer[instanceID];
                v2f o;

                float t = v.uv.y;
                float t2 = t * t;
                float t3 = t2 * t;

                // Scale unit mesh to blade size
                float3 local = v.vertex.xyz;
                local.x *= blade.width;
                local.y *= blade.height;

                // Lean rotation
                float sinL = sin(blade.lean);
                float cosL = cos(blade.lean);
                float rx = local.x * cosL - local.y * sinL;
                float ry = local.x * sinL + local.y * cosL;

                // Curvature
                float curveX = blade.curvature * t2;

                // ── WIND ──
                float gt = _GameTime;
                float2 wp = blade.position.xy;
                float ph = blade.phase;

                float wave1 = sin(gt * _WindSpeed + ph + wp.x * 0.5) * _WindStrength;
                float wave2 = sin(gt * _WindSpeed * 0.7 + ph * 1.3 + wp.y * 0.4) * _WindStrength * 0.5;

                float turb = sin(gt * _WindTurbulence * 3.0 + ph * 2.1 + wp.x * 1.2)
                           * cos(gt * _WindTurbulence * 2.3 + ph * 1.7 + wp.y * 0.9)
                           * _WindStrength * 0.3;

                float gustBase = sin(wp.x * _GustScale + wp.y * _GustScale * 0.7 + gt * _GustSpeed);
                float gustPulse = 0.3 + 0.7 * saturate(0.5 + 0.5 * sin(gt * _GustSpeed * 0.25 + wp.x * 0.08));
                float gust = gustBase * _GustStrength * gustPulse;

                float totalWind = (wave1 + wave2 + turb + gust) * t2;
                float windY = sin(gt * _WindSpeed * 0.5 + ph * 0.9) * _WindStrength * 0.15 * t2;

                // Assemble world position
                float3 worldPos;
                worldPos.x = blade.position.x + rx + curveX + totalWind;
                worldPos.y = blade.position.y + ry + windY;
                worldPos.z = blade.position.z;

                o.pos = UnityObjectToClipPos(float4(worldPos, 1.0));
                o.uv = v.uv;

                // ── COLOR ──
                float4 col = lerp(blade.colorBase, blade.colorTip, t);

                col.rgb *= lerp(1.0 - _AmbientOcclusion, 1.0, saturate(t * 2.0));
                col.rgb += _TipHighlight * t3;

                float pn = noise2D(blade.position.xy * _PatchScale);
                col.rgb *= lerp(1.0 - _PatchStrength, 1.0 + _PatchStrength * 0.5, pn);

                col.rgb += float3(0.3, 0.5, 0.1) * totalWind * _WindColorShift;

                float lightRad = _LightAngle * 0.01745329;
                float lightDot = sin(blade.lean - lightRad);
                col.rgb += _SubsurfaceColor.rgb * saturate(lightDot) * _SubsurfaceStrength * t;
                col.rgb *= lerp(1.0, 1.0 - _ShadowDarken, saturate(-lightDot));
                col.rgb += _HighlightBrighten * saturate(lightDot) * t;

                col.rgb = saturate(col.rgb);
                o.color = col;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
