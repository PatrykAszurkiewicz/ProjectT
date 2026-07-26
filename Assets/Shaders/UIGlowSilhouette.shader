Shader "UI/BossBarGlowSilhouette"
{
    // Draws ONLY the alpha silhouette of a sprite, flat-tinted and additively
    // blended. This is what makes the boss-bar glow follow the exact shape of the
    // frame art (diamond end caps, top/bottom spikes and all) instead of a box.
    //
    // Why a shader is needed: BOSSRamka.png has a black outer shadow baked into it.
    // Tinting normal copies of the sprite multiplies that black by the glow colour,
    // which stays black — you get a dark halo, not a glow. Here the RGB is thrown
    // away and only the alpha channel is used as a mask, so the halo is always the
    // colour you ask for, and additive blending means transparent pixels contribute
    // nothing at all.
    //
    // _Sweep drives an optional moving highlight band across the sprite (a "sheen"),
    // used for the periodic light that travels along the frame.

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 1
        _AlphaPower ("Alpha Power", Float) = 1

        _Sweep ("Sweep Position", Float) = -1
        _SweepWidth ("Sweep Width", Float) = 0.1
        _SweepStrength ("Sweep Strength", Range(0,1)) = 0

        // Standard UI plumbing so this behaves like any other Canvas material.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One One            // additive
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Intensity;
            float _AlphaPower;
            float _Sweep;
            float _SweepWidth;
            float _SweepStrength;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color    = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Alpha only — the sprite's own colours are discarded.
                float a = tex2D(_MainTex, i.texcoord).a;
                a = pow(saturate(a), _AlphaPower);

                // Optional travelling highlight band.
                float band = exp(-pow((i.texcoord.x - _Sweep) / max(_SweepWidth, 0.001), 2.0));
                a *= lerp(1.0, band, saturate(_SweepStrength));

                float3 rgb = i.color.rgb * a * i.color.a * _Intensity;
                return fixed4(rgb, 0);   // additive: destination alpha untouched
            }
            ENDCG
        }
    }

    Fallback Off
}
