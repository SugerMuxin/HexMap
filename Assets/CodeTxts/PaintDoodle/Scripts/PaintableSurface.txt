using UnityEngine;

namespace PaintDoodle
{
    /// <summary>
    /// 可涂画表面：挂在任何想被喷上颜料的物体上（地面/墙/箱子……）。
    /// 核心原则 = 统一走"网格自己的 uv"：
    ///   写入：调用方用 raycast 命中点携带的 textureCoord（网格真实 uv0）调 PaintAtUV，
    ///         显示：材质采样同一份 _PaintTex（mesh uv 采样）。
    ///   写入与显示共用同一份 uv -> 任何物体都不可能镜像/错位，与网格形状/缩放/旋转无关。
    /// 颜料是 premultiplied alpha-over，同色叠深、异色自然混色。
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class PaintableSurface : MonoBehaviour
    {
        [Header("贴花")]
        public int texSize = 1024;
        [Range(1f, 3f)] public float brushScale = 1.5f;   // 世界半径->像素半径放大（软笔触）

        Renderer rend;
        MeshFilter mf;
        Texture2D tex;
        Color32[] buf;
        bool dirty;
        float worldPerUv = 1f;
        MaterialPropertyBlock mpb;   // 注入 _PaintTex，不碰用户材质本体

        public Renderer SurfaceRenderer { get { return rend; } }

        void Awake()
        {
            rend = GetComponent<Renderer>();
            mf = GetComponent<MeshFilter>();

            tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            tex.name = "Paint_" + gameObject.name;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            buf = new Color32[texSize * texSize];

            // 关键：立即把画布初始化为"全透明黑"。新建 Texture2D 在首次 SetPixels 前
            // 内容未定义（部分平台是白/垃圾）—— 不初始化会导致初始状态把材质顶成纯白。
            tex.SetPixels32(buf);
            tex.Apply(false);

            worldPerUv = PaintUtil.WorldPerUv(rend);

            if (rend.sharedMaterial == null || !rend.sharedMaterial.HasProperty("_PaintTex"))
            {
                Debug.LogWarning("[PaintDoodle] " + name + " 的材质没有 _PaintTex 属性，颜料不会显示", this);
                return;
            }

            // 用 MaterialPropertyBlock 注入颜料画布 —— 不动材质本体，
            // 用户材质上的 _MainTex / _BaseColor 等全部原样保留（不再实例化导致丢贴图变白）。
            mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetTexture("_PaintTex", tex);
            rend.SetPropertyBlock(mpb);
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

        /// <summary>以 mesh uv 坐标（0..1）为中心画一个软颜料点。主入口 —— 位置永远精确。</summary>
        public void PaintAtUV(Vector2 uv, float worldRadius, Color32 src, float amount)
        {
            float uvR = Mathf.Clamp(worldRadius / worldPerUv, 0.0005f, 0.5f);   // uv 半径（0..1 全幅）
            int pr = Mathf.Max(1, Mathf.CeilToInt(uvR * texSize * brushScale));
            pr = Mathf.Min(pr, texSize / 2 - 1);

            int cx = Mathf.Clamp((int)(uv.x * texSize), 0, texSize - 1);
            int cy = Mathf.Clamp((int)(uv.y * texSize), 0, texSize - 1);

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

                    float d = Mathf.Sqrt(d2) * invR;
                    float w = 1f - d; w *= w;             // 软边
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

        /// <summary>
        /// 世界点兜底近似入口（没有 raycast uv 时用，如工具类/编辑器预览）。
        /// 按水平表面约定把 local x/z 线性映射到 uv（匹配 quad 地面）。
        /// 竖直/任意朝向表面请务必走 raycast 的 PaintAtUV —— 那才是位置精确的通用路径。
        /// </summary>
        public void PaintAtWorld(Vector3 worldPoint, float worldRadius, Color32 src, float amount)
        {
            Vector3 lp = transform.InverseTransformPoint(worldPoint);
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds mb = mf.sharedMesh.bounds;   // 局部 AABB（水平面：z 向尺寸 > 0 才有效）
                float u = (lp.x - mb.min.x) / Mathf.Max(1e-5f, mb.size.x);
                float v = (lp.z - mb.min.z) / Mathf.Max(1e-5f, mb.size.z);
                PaintAtUV(new Vector2(u, v), worldRadius, src, amount);
            }
            else
            {
                // 无 MeshFilter：世界 AABB 映射（要求轴对齐平面）
                Bounds b = rend.bounds;
                float u = (worldPoint.x - b.min.x) / Mathf.Max(1e-5f, b.size.x);
                float v = (worldPoint.z - b.min.z) / Mathf.Max(1e-5f, b.size.z);
                PaintAtUV(new Vector2(u, v), worldRadius, src, amount);
            }
        }
    }
}
