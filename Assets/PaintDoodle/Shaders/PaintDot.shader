Shader "PaintDoodle/Dot"
{
    // Metaball 液滴源：每个滴 = 一个 soft-dot quad，画进独立 RT。
    // 关键：Blend One One（加法混合）—— 重叠的滴把"密度"线性累加，
    // 后处理对密度场做模糊 + 阈值化，两滴靠近时中间密度越过阈值 -> 融合成连体。
    Properties
    {
        _DotTex ("Soft Dot", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One            // additive：密度场叠加（metaball 的核心）
        ZWrite Off
        Cull Off

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
                float4 color  : COLOR;
            };
            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            sampler2D _DotTex;
            float4 _DotTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _DotTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed a = tex2D(_DotTex, i.uv).a;
                fixed d = i.color.a * a;          // 本滴在该像素的密度贡献
                return fixed4(i.color.rgb * d, d); // premul 色 + 密度
            }
            ENDCG
        }
    }
}
