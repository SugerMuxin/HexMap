Shader "Custom/TerrainTextured"
{
    // Part 14 地形纹理：splat 顶点色(红/绿/蓝权重) + uv2 每三角形纹理索引(3 通道)，
    // 采样 Texture2DArray（沙/草/泥/石/雪 5 层）。uv 用世界 XZ 坐标。
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Terrain Texture Array", 2DArray) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _TerrainCount ("Terrain Types", Float) = 5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.5

        UNITY_DECLARE_TEX2DARRAY(_MainTex);

        struct Input
        {
            float4 color : COLOR;
            float3 worldPos;
            float3 terrain;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;
        float _TerrainCount;

        void vert (inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            data.terrain = v.texcoord2.xyz;
        }

        float4 GetTerrainColor (Input IN, int index)
        {
            // 防御：地图里可能存在超出纹理层数的类型索引（如旧调色板残留），clamp 到末层
            float ti = min(IN.terrain[index], _TerrainCount - 1);
            float3 uvw = float3(IN.worldPos.xz * 0.03, ti);
            float4 c = UNITY_SAMPLE_TEX2DARRAY(_MainTex, uvw);
            return c * IN.color[index];
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c =
                GetTerrainColor(IN, 0) +
                GetTerrainColor(IN, 1) +
                GetTerrainColor(IN, 2);
            o.Albedo = c.rgb * _Color;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
