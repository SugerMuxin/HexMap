using UnityEngine;

namespace PaintDoodle
{
    /// <summary>
    /// 主相机后处理：把"液体层"融合成 metaball 并合成到画面。
    /// 管线（每帧）：
    ///   1. LiquidCam（depth 小于主相机，只渲染 PaintLiquid 层的液滴网格）先画进 liquidRT
    ///   2. 主相机渲染完场景 -> OnRenderImage
    ///      液体RT --(pass0 下采样高斯)--> 1/2 -> 1/4 -> 1/8
    ///      1/8   --(pass1 iso 等值面)--> isoRT（premul 色 + mask）
    ///      isoRT --(pass0)--> 1/16 光晕
    ///      合成：iso色 premul over 场景 + 光晕提亮
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LiquidPost : MonoBehaviour
    {
        [Header("Metaball 后处理")]
        public Shader compositeShader;
        [Range(0.15f, 0.9f)]  public float isoCut    = 0.22f;   // 融合阈值：小 -> 球一靠近就融
        [Range(0.02f, 0.35f)] public float edgeSoft  = 0.10f;   // 边界利落
        [Range(0.0f, 2.5f)]   public float glow      = 0.1f;    // 去发光
        [Range(1, 4)]         public int   blurSteps = 1;       // 融合半径 1/2：iso 高分辨率，枪口细节锐利

        Camera cam;
        Camera liquidCam;
        RenderTexture liquidRT;
        Material mat;
        bool ready;

        void OnEnable()
        {
            // 注意：OnEnable 在编辑器非 Play 状态（添加组件/加载场景）也会触发，
            // 这里只在真正的 Play 模式接管渲染，避免污染保存的场景资产。
            if (!Application.isPlaying) return;
            cam = GetComponent<Camera>();
            if (compositeShader == null) compositeShader = Shader.Find("PaintDoodle/Composite");
            if (compositeShader == null)
            {
                Debug.LogError("[PaintDoodle] 找不到 PaintDoodle/Composite shader");
                enabled = false;
                return;
            }
            // 主相机不画液体层（液滴只进 liquidRT，由后处理合成，避免画两遍）
            cam.cullingMask &= ~PaintUtil.LiquidMask;

            mat = new Material(compositeShader);
            CreateLiquidCam();
            ready = true;
        }

        void OnDisable()
        {
            ready = false;
            if (liquidCam != null) { Destroy(liquidCam.gameObject); liquidCam = null; }
            ReleaseRT();
            if (mat != null) { Destroy(mat); mat = null; }
        }

        void CreateLiquidCam()
        {
            var go = new GameObject("LiquidCam");
            go.transform.SetParent(transform, false);
            liquidCam = go.AddComponent<Camera>();
            liquidCam.CopyFrom(cam);
            liquidCam.depth = cam.depth - 1f;             // 先于主相机渲染
            liquidCam.clearFlags = CameraClearFlags.SolidColor;
            liquidCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            liquidCam.cullingMask = PaintUtil.LiquidMask; // 只渲染液滴
            liquidCam.targetTexture = AcquireRT();
            liquidCam.enabled = true;
        }

        RenderTexture AcquireRT()
        {
            int w = Mathf.Max(4, Screen.width);
            int h = Mathf.Max(4, Screen.height);
            if (liquidRT == null || liquidRT.width != w || liquidRT.height != h)
            {
                ReleaseRT();
                liquidRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
                liquidRT.name = "PaintLiquidRT";
                liquidRT.filterMode = FilterMode.Bilinear;
                liquidRT.wrapMode = TextureWrapMode.Clamp;
            }
            return liquidRT;
        }

        void ReleaseRT()
        {
            if (liquidRT != null)
            {
                liquidRT.Release();
                Destroy(liquidRT);
                liquidRT = null;
            }
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (!ready || mat == null || liquidRT == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            // 分辨率变化时同步 RT（下一帧生效，无碍）
            if (liquidRT.width != src.width || liquidRT.height != src.height)
            {
                RenderTexture rt = AcquireRT();
                if (liquidCam != null) liquidCam.targetTexture = rt;
                Graphics.Blit(src, dst);
                return;
            }

            mat.SetFloat("_IsoCut", isoCut);
            mat.SetFloat("_EdgeSoft", edgeSoft);
            mat.SetFloat("_Glow", glow);

            int steps = Mathf.Clamp(blurSteps, 1, 4);
            int w = Mathf.Max(4, src.width);
            int h = Mathf.Max(4, src.height);

            var r2  = RenderTexture.GetTemporary(w >> 1, h >> 1, 0, RenderTextureFormat.ARGB32);
            var r4  = RenderTexture.GetTemporary(w >> 2, h >> 2, 0, RenderTextureFormat.ARGB32);
            var r8  = RenderTexture.GetTemporary(Mathf.Max(2, w >> 3), Mathf.Max(2, h >> 3), 0, RenderTextureFormat.ARGB32);
            var r16 = RenderTexture.GetTemporary(Mathf.Max(2, w >> 4), Mathf.Max(2, h >> 4), 0, RenderTextureFormat.ARGB32);

            // 多级下采样模糊（融合半径）
            Graphics.Blit(liquidRT, r2, mat, 0);
            if (steps >= 2) Graphics.Blit(r2, r4, mat, 0);
            if (steps >= 3) Graphics.Blit(r4, r8, mat, 0);

            RenderTexture blurOut = steps >= 3 ? r8 : (steps == 2 ? r4 : r2);

            // 等值面：分辨率跟随 blurOut（默认 1/2）—— 这是液体边缘的最终分辨率，
            // 之前写死 1/8 导致枪口细节全糊成低分辨率块。
            var iso = RenderTexture.GetTemporary(blurOut.width, blurOut.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(blurOut, iso, mat, 1);

            // 光晕（iso 再降一半模糊）
            Graphics.Blit(iso, r16, mat, 0);

            mat.SetTexture("_IsoTex", iso);
            mat.SetTexture("_GlowTex", r16);
            Graphics.Blit(src, dst, mat, 2);

            RenderTexture.ReleaseTemporary(r2);
            RenderTexture.ReleaseTemporary(r4);
            RenderTexture.ReleaseTemporary(r8);
            RenderTexture.ReleaseTemporary(iso);
            RenderTexture.ReleaseTemporary(r16);
        }
    }
}
