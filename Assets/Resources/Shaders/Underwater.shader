Shader "Custom/Underwater"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _WaterColor ("Water Color", Color) = (0.03, 0.28, 0.38, 1)
        _DeepColor ("Deep Color", Color) = (0.0, 0.05, 0.12, 1)
        _FogDensity ("Fog Density", Range(0.005, 1.0)) = 0.1
        _FogStart ("Fog Start Distance", Range(0.0, 20.0)) = 3.0
        _Distortion ("Refraction Distortion", Range(0.0, 0.02)) = 0.004
        _DistortSpeed ("Distortion Speed", Range(0.0, 4.0)) = 1.2
        _Brightness ("Brightness", Range(0.2, 1.6)) = 1.0
        _Saturation ("Saturation", Range(0.0, 1.5)) = 1.0
        _Vignette ("Vignette", Range(0.0, 1.0)) = 0.35
        _Caustics ("Caustics", Range(0.0, 1.5)) = 0.45
        _CausticsScale ("Caustics Scale", Range(0.002, 0.08)) = 0.015
        _CausticsSpeed ("Caustics Speed", Range(0.0, 4.0)) = 1.6
        _NoiseTex ("Caustics Noise", 2D) = "white" {}
        _SurfaceGlow ("Surface Glow", Range(0.0, 2.0)) = 1.0
        _EffectAmount ("Effect Amount", Range(0.0, 1.0)) = 1.0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
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
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 ray : TEXCOORD1; // 世界位移 / 单位深度
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _CameraDepthTexture;
            sampler2D _NoiseTex;
            float4x4 _FrustumCornersRay;

            float4 _WaterColor;
            float4 _DeepColor;
            float _FogDensity;
            float _FogStart;
            float _Distortion;
            float _DistortSpeed;
            float _Brightness;
            float _Saturation;
            float _Vignette;
            float _Caustics;
            float _CausticsScale;
            float _CausticsSpeed;
            float _SurfaceGlow;
            float _EffectAmount;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float3 r0 = _FrustumCornersRay[0].xyz;
                float3 r1 = _FrustumCornersRay[1].xyz;
                float3 r2 = _FrustumCornersRay[2].xyz;
                float3 r3 = _FrustumCornersRay[3].xyz;
                float3 bottom = lerp(r0, r1, v.uv.x);
                float3 top = lerp(r2, r3, v.uv.x);
                o.ray = lerp(bottom, top, v.uv.y);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y;
                // 入水程度 0..1：统一缩放所有子效果。全片只保留一条 uv 采样链，
                // 所有混合都发生在同一像素的颜色空间内 -> 绝不叠加第二张画面(无残影/无双影)
                float amt = _EffectAmount;

                float3 rayV = i.ray;
                float3 rayN = normalize(rayV);
                float up = rayN.y;
                float upAmt = smoothstep(0.04, 0.5, up);

                // ---------- 深度 ----------
                float raw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                float d = clamp(LinearEyeDepth(raw), 0.0, 500.0);

                // ---------- 折射：零均值小幅 uv 扰动(随入水度)，单次采样 ----------
                // 注意：warp 必须零均值(无直流偏置)且幅度小(几px)，否则整屏被平移、
                // 角色被"撕扯错位"成双影。频率取中高：角色边缘轻微波动，画面整体不晃。
                float2 warp = float2(
                    sin(uv.y * 24.0 + t * _DistortSpeed) * 0.5 +
                    sin(uv.x * 17.0 - t * 0.8 * _DistortSpeed) * 0.25 +
                    sin((uv.x + uv.y) * 31.0 + t * 0.6 * _DistortSpeed) * 0.25,
                    cos(uv.x * 21.0 + t * 0.9 * _DistortSpeed) * 0.5 +
                    cos(uv.y * 13.0 - t * _DistortSpeed) * 0.25 +
                    cos((uv.y - uv.x) * 29.0 + t * 0.7 * _DistortSpeed) * 0.25
                );
                float3 baseCol = tex2D(_MainTex, uv + warp * (_Distortion * amt)).rgb;

                // ---------- 雾：近景(角色)无雾区，超出 _FogStart 平滑爬升；随入水度淡入 ----------
                float fog = 1.0 - exp(-_FogDensity * amt * max(d - _FogStart, 0.0));
                // 雾色：近处水色 -> 远处深水色；仰视(看水面)方向整体提亮，避免暗到发灰/发黑
                float3 fogCol = lerp(_WaterColor.rgb, _DeepColor.rgb, saturate(d / 80.0));
                fogCol = lerp(fogCol, _WaterColor.rgb * 2.2, upAmt * 0.5);
                float3 col = lerp(baseCol, fogCol, fog);

                // ---------- 焦散：远处地面光斑；随入水度淡入 ----------
                float2 wuv = _WorldSpaceCameraPos.xz + rayV.xz * d;
                float ct = t * _CausticsSpeed;
                float n1 = tex2D(_NoiseTex, wuv * _CausticsScale + float2( ct * 0.02, -ct * 0.013)).r;
                float n2 = tex2D(_NoiseTex, wuv * _CausticsScale * 1.37 + float2(-ct * 0.015,  ct * 0.019)).r;
                float n3 = tex2D(_NoiseTex, wuv * _CausticsScale * 2.11 + float2(-ct * 0.011, -ct * 0.009)).r;
                float caustic = saturate((n1 - n2) * 2.2) * (0.4 + n3 * 0.6);
                float causticFar = smoothstep(_FogStart + 1.0, _FogStart + 8.0, d);
                float causticVis = causticFar * (1.0 - fog) * (1.0 - upAmt * 0.9) * amt;
                col *= 1.0 + (caustic - 0.5) * 2.0 * _Caustics * causticVis;

                // ---------- 天窗(仰视透过水面)：只对当前像素提亮，不二次偏移采样 -> 无错位双影 ----------
                float3 skyView = baseCol * (0.9 + _SurfaceGlow * 0.18);
                skyView = lerp(skyView, skyView * _WaterColor.rgb * 1.4 + _WaterColor.rgb * 0.4, 0.3);
                col = lerp(col, skyView, upAmt * amt);

                // ---------- 暗角 / 亮度 / 饱和度：权重随入水度归零，出水=纯原图 ----------
                float2 vc = uv - 0.5;
                col *= 1.0 - smoothstep(0.55, 1.15, saturate(length(vc) * 1.8)) * (_Vignette * amt);
                float sat = lerp(1.0, _Saturation, amt);
                float bri = lerp(1.0, _Brightness, amt);
                float luma = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(luma.xxx, col, sat) * bri;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
