Shader "PaintDoodle/Composite"
{
    // 屏幕空间 metaball 后处理：
    //   pass0  下采样高斯模糊（液体RT 每级减半，premultiplied rgba 一起模糊）
    //   pass1  等值面提取：alpha 经过 smoothstep 得到 mask，得到"融成一团"的液体轮廓
    //   pass2  合成：iso色 + 场景*(1-mask)，再加一次外扩光晕（湿润高亮）
    // 前置：粒子已画进一张独立 RenderTexture（黑底，alpha-over 即 premultiplied over black）
    Properties
    {
        _MainTex ("Scene", 2D)    = "white" {}
        _IsoTex  ("Iso", 2D)      = "black" {}
        _GlowTex ("Glow", 2D)     = "black" {}
        _IsoCut  ("Iso Cut", Range(0.15, 0.9))  = 0.22
        _EdgeSoft("Edge Soft", Range(0.02, 0.35))= 0.10
        _Glow    ("Glow", Range(0.0, 2.5))      = 0.1
    }
    SubShader
    {
        ZWrite Off ZTest Always Cull Off

        // ---- pass 0 : 1/2 下采样 + 3x3 高斯模糊 ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 t = _MainTex_TexelSize.xy;
                half4 c00 = tex2D(_MainTex, i.uv + t * float2(-1,-1));
                half4 c01 = tex2D(_MainTex, i.uv + t * float2( 0,-1));
                half4 c02 = tex2D(_MainTex, i.uv + t * float2( 1,-1));
                half4 c10 = tex2D(_MainTex, i.uv + t * float2(-1, 0));
                half4 c11 = tex2D(_MainTex, i.uv);
                half4 c12 = tex2D(_MainTex, i.uv + t * float2( 1, 0));
                half4 c20 = tex2D(_MainTex, i.uv + t * float2(-1, 1));
                half4 c21 = tex2D(_MainTex, i.uv + t * float2( 0, 1));
                half4 c22 = tex2D(_MainTex, i.uv + t * float2( 1, 1));

                half4 acc = (c00 + c02 + c20 + c22)
                          + 2.0 * (c01 + c10 + c12 + c21)
                          + 4.0 * c11;
                return acc * (1.0 / 16.0);
            }
            ENDCG
        }

        // ---- pass 1 : 等值面 mask（iso） ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _IsoCut;
            float _EdgeSoft;

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 b = tex2D(_MainTex, i.uv);
                fixed m = smoothstep(_IsoCut - _EdgeSoft, _IsoCut + _EdgeSoft, b.a);
                return fixed4(b.rgb, m);
            }
            ENDCG
        }

        // ---- pass 2 : 合成到场景 ----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; // scene
            sampler2D _IsoTex;  // 1/8   premul color + mask
            sampler2D _GlowTex; // 1/16  mask 再模糊 = 光晕
            float _Glow;

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 scene = tex2D(_MainTex, i.uv);
                fixed4 iso   = tex2D(_IsoTex,  i.uv);
                fixed4 glow  = tex2D(_GlowTex, i.uv);

                // iso.rgb 是 premultiplied（黑底 alpha-over），直接 premul over 场景
                fixed3 col = iso.rgb + scene.rgb * (1.0 - iso.a);

                // 湿润润泽（非发光）：内部极轻提亮、边缘自然过渡；乘数 0.7 保持墨色浓郁不发白
                col += iso.rgb * glow.a * _Glow * 0.7;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
