// Unlit, alpha-blended, vertex-coloured shader for the wave direction arc.
//
// WHY THIS EXISTS: Sprites/Default passes UVs straight through its vertex program, so
// material.mainTextureScale and material.mainTextureOffset have no effect on it. The arc
// band needs both — tiling to repeat the streak pattern along the arc, and a per-frame
// offset to scroll it. This shader applies TRANSFORM_TEX and keeps the LineRenderer's
// vertex colours (which carry the tip fade), which is everything the band needs.
//
// INSTALL: drop this anywhere under Assets. Either
//   (a) put it in a folder named "Resources" (WaveSpawner does Resources.Load<Shader>), or
//   (b) add it to Project Settings > Graphics > Always Included Shaders,
// otherwise Shader.Find will return null in a build and WaveSpawner falls back to
// Sprites/Default — the arc still renders, it just stops flowing.
//
// This is a built-in-pipeline CG shader. Unlit shaders like this render correctly under
// URP; they are simply not SRP-batched, which is irrelevant for a handful of arcs.

Shader "Game/WaveArc"
{
    Properties
    {
        _MainTex ("Band texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }

    Fallback Off
}
