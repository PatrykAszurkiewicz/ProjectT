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

        // Shading
        _ShadowDarken ("Shadow Side Darken", Range(0, 0.5)) = 0.18
        _HighlightBrighten ("Highlight Side Brighten", Range(0, 0.5)) = 0.12
        _AmbientOcclusion ("Base Ambient Occlusion", Range(0, 0.5)) = 0.22
        _TipHighlight ("Tip Specular Highlight", Range(0, 0.4)) = 0.12
        _LightAngle ("Light Angle (degrees)", Float) = 135.0
        _AlphaCutoff ("Alpha Cutoff", Range(0, 0.5)) = 0.05

        // Subsurface / translucency
        _SubsurfaceStrength ("Subsurface Glow", Range(0, 0.4)) = 0.15
        _SubsurfaceColor ("Subsurface Color", Color) = (0.4, 0.7, 0.15, 1.0)

        // Wind-reactive color (shows blade undersides when blown)
        _WindColorShift ("Wind Color Shift", Range(0, 0.3)) = 0.1

        // Spatial color variation (patchy green)
        _PatchScale ("Color Patch Scale", Float) = 0.15
        _PatchStrength ("Patch Color Strength", Range(0, 0.3)) = 0.12
    }
    SubShader
    {
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
                float2 uv2 : TEXCOORD1; // x = blade lean, y = blade type (0=normal, 1=short, 2=tall)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float3 data : TEXCOORD0; // x = heightRatio, y = bladeLean, z = windDisplacement
                float2 worldXY : TEXCOORD1; // for spatial noise
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
            float _SubsurfaceStrength;
            float4 _SubsurfaceColor;
            float _WindColorShift;
            float _PatchScale;
            float _PatchStrength;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Cheap value noise for spatial color patches
            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep
                float a = hash21(i);
                float b = hash21(i + float2(1,0));
                float c = hash21(i + float2(0,1));
                float d = hash21(i + float2(1,1));
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
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

                float wind1 = sin(t + worldPos.x * _WindTurbulence + worldPos.y * 0.7) * _WindStrength;
                float wind2 = sin(t * 1.4 + worldPos.x * _WindTurbulence * 0.5 - worldPos.y * 1.2) * _WindStrength * 0.4;
                float wind3 = cos(t * 0.8 + worldPos.y * _WindTurbulence * 1.3 + worldPos.x * 0.6) * _WindStrength * 0.25;

                float bladeHash = hash21(worldPos.xy * 10.0);
                float microJitter = (bladeHash - 0.5) * _WindStrength * 0.2 * sin(t * 2.8 + bladeHash * 6.28);

                // ============ COMBINE ============
                float xDisplacement = (totalGust + wind1 + wind2 + wind3 + microJitter) * swayAmount;
                float yMicro = sin(t * 1.1 + worldPos.x * 1.7 + worldPos.y * 2.3) * _WindStrength * 0.15 * swayAmount;

                v.vertex.x += xDisplacement;
                v.vertex.y += yMicro;
                v.vertex.y -= abs(xDisplacement) * 0.3 * swayAmount;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.data = float3(heightRatio, v.uv2.x, xDisplacement);
                o.worldXY = worldPos.xy;

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = i.color;
                if (col.a < _AlphaCutoff) discard;

                float heightRatio = i.data.x;
                float bladeLean = i.data.y;
                float windDisp = i.data.z;

                // ============ SPATIAL COLOR PATCHES ============
                // Subtle variation across the field — some areas are yellower, 
                // some bluer-green, mimicking soil/moisture variation
                float patchNoise = valueNoise(i.worldXY * _PatchScale);
                float patchNoise2 = valueNoise(i.worldXY * _PatchScale * 2.3 + 50.0);

                // Shift hue slightly based on position
                col.r += (patchNoise - 0.5) * _PatchStrength * 0.8;
                col.g += (patchNoise2 - 0.5) * _PatchStrength * 0.4;
                col.b -= (patchNoise - 0.5) * _PatchStrength * 0.3;

                // ============ SHADING ============
                float aoFactor = lerp(1.0 - _AmbientOcclusion, 1.0, saturate(heightRatio * 3.0));

                float lightDirX = cos(_LightAngle * 0.01745329);
                float lightDirY = sin(_LightAngle * 0.01745329);
                float shadingDot = bladeLean * lightDirX;

                float shadowFactor = 1.0 - saturate(-shadingDot) * _ShadowDarken;
                float highlightFactor = 1.0 + saturate(shadingDot) * _HighlightBrighten;

                float tipSpec = heightRatio * heightRatio * _TipHighlight * saturate(shadingDot + 0.3);

                float shading = aoFactor * shadowFactor * highlightFactor;
                col.rgb *= shading;
                col.rgb += tipSpec;

                // ============ SUBSURFACE SCATTERING ============
                // When a blade leans AWAY from light, the light passes through it
                // making the edges glow with translucent green-yellow
                // Only affects upper half of blade (thin part)
                float backlit = saturate(-shadingDot) * heightRatio;
                float sssAmount = backlit * _SubsurfaceStrength;
                col.rgb += _SubsurfaceColor.rgb * sssAmount;

                // ============ WIND COLOR SHIFT ============
                // When wind pushes blades over, they show their lighter underside
                // The more displaced, the lighter/silvery the blade becomes
                float windIntensity = saturate(abs(windDisp) * 5.0);
                float colorShift = windIntensity * heightRatio * _WindColorShift;
                col.rgb += colorShift * float3(0.15, 0.2, 0.05); // pale green-yellow underside

                // Color temperature
                float shadowAmount = saturate(-shadingDot) * _ShadowDarken;
                float highlightAmount = saturate(shadingDot) * _HighlightBrighten;
                col.r *= (1.0 - shadowAmount * 0.1);
                col.b *= (1.0 + shadowAmount * 0.05);
                col.r *= (1.0 + highlightAmount * 0.07);

                col.rgb = saturate(col.rgb);

                // Premultiplied alpha
                col.rgb *= col.a;
                return col;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
