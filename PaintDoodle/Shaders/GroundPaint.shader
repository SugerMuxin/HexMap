Shader "PaintDoodle/Paintable"
{
    // 通用可涂画表面 —— 世界位置投影贴花：
    // 底色 = Base Texture(可选,用模型 uv)×Base Color；颜料 = PaintableSurface 写入的
    // _PaintTex 画布，用像素"世界坐标"投到画布坐标采样 —— 不依赖模型 uv，模型没 uv 也能喷。
    // 画布参数（center/两轴/尺寸倒数）由 PaintableSurface 经 MaterialPropertyBlock 注入。
    // 颜料 buffer 存 premultiplied (rgb*a, a=湿润度)，解码回 straight 色做 albedo，
    // 湿润度驱动 BlinnPhong 高光（水迹反光）。
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
            float2 uv_MainTex;    // 基础贴图（有 uv 才有效；无 uv 时取默认白点）
            float3 worldPos;      // 像素世界坐标 —— 画布采样用它
        };

        sampler2D _MainTex;
        sampler2D _PaintTex;
        fixed4 _BaseColor;

        // 世界画布：center.xyz / uAxis.xyz+w=1/sizeU / vAxis.xyz+w=1/sizeV
        float4 _CanvasCenter;
        float4 _CanvasAxisU;
        float4 _CanvasAxisV;

        void surf (Input IN, inout SurfaceOutput o)
        {
            // 底色 = 基础贴图 × BaseColor（无贴图 = 纯色；无 uv 模型 = 白纹×色，可忽略）
            fixed4 base = tex2D(_MainTex, IN.uv_MainTex) * _BaseColor;

            // 世界位置 -> 画布坐标 (0..1)，与 PaintableSurface.PaintAtWorld 同一公式
            float3 d = IN.worldPos - _CanvasCenter.xyz;
            float2 cuv;
            cuv.x = dot(d, _CanvasAxisU.xyz) * _CanvasAxisU.w + 0.5;
            cuv.y = dot(d, _CanvasAxisV.xyz) * _CanvasAxisV.w + 0.5;

            fixed4 p = tex2D(_PaintTex, cuv);
            fixed  wet = saturate(p.a);   // 湿润度（0=无颜料 -> 完全显示底色）

            // premultiplied -> straight 颜色（防噪：极小时直接取 0）
            fixed3 paintCol = (wet > 0.02) ? p.rgb / wet : fixed3(0, 0, 0);

            // 湿润度低于阈值的区域 = 没喷过，原样显示底色（杜绝初始发白）
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
