Shader "Custom/HealthBarFade"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Fill ("Fill Amount", Range(0,1)) = 1.0
        _CornerRadius ("Corner Radius (UV)", Range(0, 0.5)) = 0.5
        _AspectRatio ("Bar Width / Height", Float) = 8.55
        _AAStrength ("AA Strength", Range(0.5, 5.0)) = 2.0
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
            float _CornerRadius;
            float _AspectRatio;
            float _AAStrength;
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

                if (_Fill >= 0.999) return col;

                float x = i.uv.x * _AspectRatio;
                float y = i.uv.y;
                float fillX = _Fill * _AspectRatio;
                float r = _CornerRadius;

                // AA band — controlled by user-tunable strength.
                // Use both fwidth axes and take the larger to handle stretched UVs.
                float aa = max(fwidth(i.uv.x) * _AspectRatio, fwidth(i.uv.y)) * _AAStrength;
                aa = max(aa, 0.005);

                float distRight = fillX - x;

                if (x > fillX - r && y > 1.0 - r)
                {
                    float2 cornerCenter = float2(fillX - r, 1.0 - r);
                    distRight = r - distance(float2(x, y), cornerCenter);
                }
                else if (x > fillX - r && y < r)
                {
                    float2 cornerCenter = float2(fillX - r, r);
                    distRight = r - distance(float2(x, y), cornerCenter);
                }

                float alpha = smoothstep(-aa, aa, distRight);
                col.a *= alpha;
                return col;
            }
            ENDCG
        }
    }
}
