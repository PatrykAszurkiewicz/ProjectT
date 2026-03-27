Shader "Custom/TreeWindSway"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Wind Settings)]
        _WindSpeed ("Wind Speed", Range(0.5, 5.0)) = 1.5
        _WindStrength ("Wind Strength", Range(0.0, 0.15)) = 0.04
        _WindTurbulence ("Turbulence", Range(0.0, 3.0)) = 1.2

        [Header(Crown Mask)]
        _CrownStart ("Crown Start (V threshold)", Range(0.0, 1.0)) = 0.3
        _CrownSoftness ("Crown Softness", Range(0.01, 0.5)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "SpriteWind2D"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _WindSpeed;
                float _WindStrength;
                float _WindTurbulence;
                float _CrownStart;
                float _CrownSoftness;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);

                float crownMask = saturate((v.uv.y - _CrownStart) / _CrownSoftness);
                crownMask = crownMask * crownMask;

                float3 worldPos = TransformObjectToWorld(v.positionOS.xyz);
                float phaseOffset = worldPos.x * 1.37;
                float time = _Time.y * _WindSpeed;

                float sway = sin(time + phaseOffset) * _WindStrength;
                float turb = sin(time * 2.3 + phaseOffset * 0.7 + v.uv.y * 4.0)
                           * _WindStrength * 0.4 * _WindTurbulence;
                float flutter = sin(time * 4.7 + phaseOffset * 2.1 + v.uv.y * 8.0)
                              * _WindStrength * 0.12 * _WindTurbulence;

                v.positionOS.x += (sway + turb + flutter) * crownMask;
                v.positionOS.y -= abs(sway) * crownMask * 0.3;

                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SpriteWindForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _WindSpeed;
                float _WindStrength;
                float _WindTurbulence;
                float _CrownStart;
                float _CrownSoftness;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);

                float crownMask = saturate((v.uv.y - _CrownStart) / _CrownSoftness);
                crownMask = crownMask * crownMask;

                float3 worldPos = TransformObjectToWorld(v.positionOS.xyz);
                float phaseOffset = worldPos.x * 1.37;
                float time = _Time.y * _WindSpeed;

                float sway = sin(time + phaseOffset) * _WindStrength;
                float turb = sin(time * 2.3 + phaseOffset * 0.7 + v.uv.y * 4.0)
                           * _WindStrength * 0.4 * _WindTurbulence;
                float flutter = sin(time * 4.7 + phaseOffset * 2.1 + v.uv.y * 8.0)
                              * _WindStrength * 0.12 * _WindTurbulence;

                v.positionOS.x += (sway + turb + flutter) * crownMask;
                v.positionOS.y -= abs(sway) * crownMask * 0.3;

                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SpriteWindSRPDefault"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _WindSpeed;
                float _WindStrength;
                float _WindTurbulence;
                float _CrownStart;
                float _CrownSoftness;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);

                float crownMask = saturate((v.uv.y - _CrownStart) / _CrownSoftness);
                crownMask = crownMask * crownMask;

                float3 worldPos = TransformObjectToWorld(v.positionOS.xyz);
                float phaseOffset = worldPos.x * 1.37;
                float time = _Time.y * _WindSpeed;

                float sway = sin(time + phaseOffset) * _WindStrength;
                float turb = sin(time * 2.3 + phaseOffset * 0.7 + v.uv.y * 4.0)
                           * _WindStrength * 0.4 * _WindTurbulence;
                float flutter = sin(time * 4.7 + phaseOffset * 2.1 + v.uv.y * 8.0)
                              * _WindStrength * 0.12 * _WindTurbulence;

                v.positionOS.x += (sway + turb + flutter) * crownMask;
                v.positionOS.y -= abs(sway) * crownMask * 0.3;

                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Sprites/Default"
}
