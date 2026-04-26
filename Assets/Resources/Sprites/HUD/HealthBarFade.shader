Shader "Custom/HealthBarFade"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Fill ("Fill Amount", Range(0,1)) = 1.0
        _FadeWidth ("Fade Width", Range(0, 0.3)) = 0.05
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float _Fill;
            float _FadeWidth;
            float4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color * i.color;

                // Miêkkie zanikanie na prawej krawêdzi fill
                float edge = _Fill;
                float fade = smoothstep(edge, edge - _FadeWidth, i.uv.x);

                col.a *= fade;
                return col;
            }
            ENDCG
        }
    }
}