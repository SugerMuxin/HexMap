using UnityEngine;

namespace PaintDoodle
{
    /// <summary>
    /// 可涂画表面 —— 世界位置投影贴花（不再依赖模型 uv，模型没有 uv 也能喷）。
    ///
    /// 每块表面有一个"世界画布坐标系"：
    ///   center   画布中心（默认 = renderer.bounds.center，Inspector 可覆盖）
    ///   uAxis/vAxis 画布两轴（世界单位向量，默认从 mesh local 的两个非厚度轴推出）
    ///   sizeU/V  画布覆盖的世界尺寸（米）
    /// 写入：世界点 p → cuv = (dot(p-center,uAxis)/sizeU+0.5, dot(p-center,vAxis)/sizeV+0.5)
    /// 显示：surface shader 用像素 worldPos 做同一个投影采样 _PaintTex。
    /// 两端共用世界坐标，任何朝向/缩放的平面表面都精确对齐；uv 可有可无。
    ///
    /// 使用要求：MeshCollider（raycast 命中点定位）+ Layer9 + 材质带 _PaintTex 的 shader。
    /// 平面/平板自动正确；曲面、立方体多面等非平面表面请手动调 uAxis/vAxis。
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class PaintableSurface : MonoBehaviour
    {
        [Header("贴花")]
        public int texSize = 1024;
        [Range(1f, 3f)] public float brushScale = 1.5f;   // 世界半径 -> 像素半径放大（软笔触）

        [Header("世界画布（默认自动，一般不用改）")]
        public bool overrideCanvas = false;               // 打开后可手动指定画布中心与轴
        public Vector3 canvasCenter;
        public Vector3 canvasAxisU = new Vector3(1, 0, 0);   // 画布 u 方向（自动时会被覆盖）
        public Vector3 canvasAxisV = new Vector3(0, 0, 1);   // 画布 v 方向

        Renderer rend;
        Texture2D tex;
        Color32[] buf;
        bool dirty;
        MaterialPropertyBlock mpb;

        // 画布参数（世界）
        Vector3 center;
        Vector3 uAxis, vAxis;      // 单位向量
        float sizeU, sizeV;        // 全幅米
        float perMeter;            // 像素/米（取两轴平均，圆半径按此换算）

        public Renderer SurfaceRenderer { get { return rend; } }

        void Awake()
        {
            rend = GetComponent<Renderer>();

            tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            tex.name = "Paint_" + gameObject.name;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            buf = new Color32[texSize * texSize];

            // 初始全透明黑：杜绝新建纹理未定义内容导致的初始发白
            tex.SetPixels32(buf);
            tex.Apply(false);

            if (rend.sharedMaterial == null || !rend.sharedMaterial.HasProperty("_PaintTex"))
            {
                Debug.LogWarning("[PaintDoodle] " + name + " 的材质没有 _PaintTex 属性，颜料不会显示", this);
                return;
            }

            ComputeCanvas();

            // 用 MaterialPropertyBlock 注入画布纹理与画布参数（不碰材质本体，贴图/颜色保留）
            mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetTexture("_PaintTex", tex);
            PushCanvasToBlock();
            rend.SetPropertyBlock(mpb);
        }

        /// <summary>计算世界画布参数：默认取 mesh 局部 bounds 中"最薄轴"为法线，其余两轴为画布轴。</summary>
        void ComputeCanvas()
        {
            if (overrideCanvas)
            {
                center = canvasCenter;
                uAxis = canvasAxisU.normalized;
                vAxis = canvasAxisV.normalized;
            }
            else
            {
                center = rend.bounds.center;
                var mf = GetComponent<MeshFilter>();
                Bounds mb = mf != null && mf.sharedMesh != null
                            ? mf.sharedMesh.bounds
                            : new Bounds(Vector3.zero, Vector3.one);

                // 局部尺寸
                Vector3 ls = mf != null && mf.sharedMesh != null
                             ? Vector3.Scale(mb.size, transform.lossyScale)
                             : new Vector3(
                                   Mathf.Abs(transform.lossyScale.x),
                                   Mathf.Abs(transform.lossyScale.y),
                                   Mathf.Abs(transform.lossyScale.z));

                // 挑局部最薄轴做画布法线
                float sx = ls.x, sy = ls.y, sz = ls.z;
                Vector3 nLocal;
                if (sx <= sy && sx <= sz)      nLocal = Vector3.right;   // 法线 = local x
                else if (sy <= sx && sy <= sz) nLocal = Vector3.up;      // 法线 = local y
                else                           nLocal = Vector3.forward; // 法线 = local z

                Vector3 nWorld = transform.TransformDirection(nLocal).normalized;

                // 画布两轴 = 世界法线之外的两个正交方向（沿表面对角）
                Vector3 t = Mathf.Abs(nWorld.y) < 0.99f ? Vector3.up : Vector3.right;
                uAxis = Vector3.Cross(nWorld, t).normalized;
                vAxis = Vector3.Cross(nWorld, uAxis).normalized;

                // 画布尺寸 = 表面沿两轴的世界跨度
                sizeU = SpanAlong(ls, transform, uAxis);
                sizeV = SpanAlong(ls, transform, vAxis);
            }

            if (sizeU < 0.01f) sizeU = 1f;
            if (sizeV < 0.01f) sizeV = 1f;
            perMeter = (texSize / sizeU + texSize / sizeV) * 0.5f;
        }

        /// <summary>估计表面在 worldAxis 方向上的世界跨度（把局部 AABB 各边变换后投影）。</summary>
        static float SpanAlong(Vector3 localSize, Transform t, Vector3 worldAxis)
        {
            // 局部三条边 -> 世界三条边 -> 各自在 worldAxis 上投影长度，取最大
            Vector3 ex = t.TransformDirection(localSize.x, 0f, 0f);
            Vector3 ey = t.TransformDirection(0f, localSize.y, 0f);
            Vector3 ez = t.TransformDirection(0f, 0f, localSize.z);
            float hx = Mathf.Abs(Vector3.Dot(ex, worldAxis));
            float hy = Mathf.Abs(Vector3.Dot(ey, worldAxis));
            float hz = Mathf.Abs(Vector3.Dot(ez, worldAxis));
            return Mathf.Max(hx, Mathf.Max(hy, hz));
        }

        void PushCanvasToBlock()
        {
            if (mpb == null) return;
            mpb.SetVector("_CanvasCenter", new Vector4(center.x, center.y, center.z, 1f));
            mpb.SetVector("_CanvasAxisU", new Vector4(uAxis.x, uAxis.y, uAxis.z, 1f / sizeU));
            mpb.SetVector("_CanvasAxisV", new Vector4(vAxis.x, vAxis.y, vAxis.z, 1f / sizeV));
        }

        void OnDestroy()
        {
            if (tex != null) { Destroy(tex); tex = null; }
        }

        void LateUpdate()
        {
            if (!dirty) return;
            dirty = false;
            tex.SetPixels32(buf);
            tex.Apply(false);
        }

        /// <summary>世界点落颜料（主入口）。世界坐标投影 -> 画布坐标，与 shader 显示完全一致。</summary>
        public void PaintAtWorld(Vector3 worldPoint, float worldRadius, Color32 src, float amount)
        {
            // 世界点 -> 画布坐标 0..1
            Vector3 d = worldPoint - center;
            float cu = Vector3.Dot(d, uAxis) / sizeU + 0.5f;
            float cv = Vector3.Dot(d, vAxis) / sizeV + 0.5f;
            if (cu < -0.5f || cu > 1.5f || cv < -0.5f || cv > 1.5f) return;   // 远离画布直接丢

            // 世界半径 -> 像素半径（按每米像素）
            int pr = Mathf.Max(1, Mathf.CeilToInt(worldRadius * perMeter * brushScale));
            pr = Mathf.Min(pr, texSize / 2 - 1);

            int cx = (int)(cu * texSize);
            int cy = (int)(cv * texSize);

            int x0 = Mathf.Max(0, cx - pr), x1 = Mathf.Min(texSize - 1, cx + pr);
            int y0 = Mathf.Max(0, cy - pr), y1 = Mathf.Min(texSize - 1, cy + pr);

            float invR = 1f / pr;
            float srf = src.r * (1f / 255f);
            float sgf = src.g * (1f / 255f);
            float sbf = src.b * (1f / 255f);

            for (int yy = y0; yy <= y1; yy++)
            {
                int dy = yy - cy; int dy2 = dy * dy;
                int row = yy * texSize;
                for (int xx = x0; xx <= x1; xx++)
                {
                    int dx = xx - cx;
                    int d2 = dx * dx + dy2;
                    if (d2 > pr * pr) continue;

                    float dist = Mathf.Sqrt(d2) * invR;
                    float w = 1f - dist; w *= w;             // 软边
                    float sa = amount * w;

                    int idx = row + xx;
                    Color32 c = buf[idx];
                    float da = c.a * (1f / 255f);
                    float keep = 1f - sa;
                    float oa = sa + da * keep;

                    buf[idx] = new Color32(
                        (byte)Mathf.Min(255, (srf * sa + c.r * (1f / 255f) * keep) * 255f + 0.5f),
                        (byte)Mathf.Min(255, (sgf * sa + c.g * (1f / 255f) * keep) * 255f + 0.5f),
                        (byte)Mathf.Min(255, (sbf * sa + c.b * (1f / 255f) * keep) * 255f + 0.5f),
                        (byte)Mathf.Min(255, oa * 255f + 0.5f));
                }
            }
            dirty = true;
        }
    }
}
