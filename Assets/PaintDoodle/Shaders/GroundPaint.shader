Shader "PaintDoodle/Paintable"
{
    // 通用可涂画表面：Base Texture(基础贴图)×Base Color 作底色，
    // 叠加 PaintableSurface 写入的 _PaintTex 涂鸦贴花纹理。
    // 贴花 buffer 存 premultiplied (rgb*a, a=湿润度) —— 解码回 straight 色做 albedo，
    // 湿润度驱动 BlinnPhong 高光（水迹反光）。地面/墙面/任何物体共用。
    Properties
    {
        _MainTex   ("Base Texture", 2D)  = "white" {}
        _BaseColor ("Base Color", Color) = (0.84, 0.86, 0.9, 1)
        _PaintTex  ("Paint", 2D)         = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf BlinnPhong
        #pragma target 3.0

        struct Input
        {
            float2 uv_MainTex;    // 基础贴图
            float2 uv_PaintTex;   // 涂鸦贴花
        };

        sampler2D _MainTex;
        sampler2D _PaintTex;
        fixed4 _BaseColor;

        void surf (Input IN, inout SurfaceOutput o)
        {
            // 底色 = 基础贴图 × BaseColor（无贴图时 = 纯色，同旧行为）
            fixed4 base = tex2D(_MainTex, IN.uv_MainTex) * _BaseColor;

            fixed4 p = tex2D(_PaintTex, IN.uv_PaintTex);
            fixed  wet = saturate(p.a);   // 湿润度（0=无颜料 -> 完全显示贴图）

            // premultiplied -> straight 颜色（防噪：极小时直接取 0）
            fixed3 paintCol = (wet > 0.02) ? p.rgb / wet : fixed3(0, 0, 0);

            // 湿润度低于阈值的区域 = 没喷过，原样显示基础贴图（杜绝初始发白）
            fixed blend = smoothstep(0.01, 0.06, wet);
            o.Albedo   = lerp(base.rgb, paintCol, blend);
            o.Specular = 0.25 * wet;
            o.Gloss    = 0.92 * wet;
            o.Emission = paintCol * wet * 0.08;   // 一点点自发光让颜料更艳
            o.Alpha    = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
