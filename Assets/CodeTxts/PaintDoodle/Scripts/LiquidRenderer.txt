using UnityEngine;
using System.Collections.Generic;

namespace PaintDoodle
{
    /// <summary>
    /// 把 LiquidSim 的所有液滴合并成"一个朝向相机的 billboard quad 网格"，
    /// 单 mesh + 单 draw call 画进 liquid RenderTexture（由 PaintUtil.LiquidLayer 上的相机渲染），
    /// 顶点色 = 颜料色，alpha 由寿命做淡出。
    /// </summary>
    [RequireComponent(typeof(LiquidSim))]
    public class LiquidRenderer : MonoBehaviour
    {
        public Camera targetCamera;          // 由搭建器注入（liquid cam 每帧位置跟主相机，这里只用 right/up 做 billboard）

        [Header("渲染")]
        public Shader dotShader;

        Material mat;
        Texture2D dotTex;
        Mesh mesh;
        MeshFilter mf;

        List<Vector3> verts = new List<Vector3>(4096);
        List<Vector2> uvs   = new List<Vector2>(4096);
        List<Color32> cols  = new List<Color32>(4096);
        List<int> tris      = new List<int>(6144);

        static readonly Vector2[] QuadUV = { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };

        void Awake()
        {
            if (dotShader == null) dotShader = Shader.Find("PaintDoodle/Dot");
            mat = new Material(dotShader);
            mat.name = "LiquidDotMat";
            dotTex = BuildDotTexture();
            mat.SetTexture("_DotTex", dotTex);

            mesh = new Mesh();
            mesh.name = "LiquidDropsMesh";
            mesh.MarkDynamic();
            mesh.bounds = new Bounds(new Vector3(0, 2, 0), new Vector3(120, 60, 120)); // 固定大包围盒防剔除

            var go = new GameObject("LiquidDropsMesh");
            go.transform.SetParent(transform, false);
            PaintUtil.AssignLayer(go, PaintUtil.LiquidLayer);   // layer 8 -> 由 liquid cam 单独渲染
            mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        void OnDestroy()
        {
            if (mat != null) { Destroy(mat); mat = null; }
            if (dotTex != null) { Destroy(dotTex); dotTex = null; }
            if (mesh != null) { Destroy(mesh); mesh = null; }
        }

        static Texture2D BuildDotTexture()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.name = "LiquidDot";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[S * S];
            float half = S * 0.5f;
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);       // 0 中心 .. >1 角
                    float a = 1f - Mathf.SmoothStep(0.42f, 0.92f, d); // 中心实、边缘软
                    byte v = (byte)(Mathf.Clamp01(a) * 255f + 0.5f);
                    px[y * S + x] = new Color32(v, v, v, v);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        /// <summary>把一个 quad（中心 c、宽 rx、高 ry，右轴 ra、上轴 ua）追加到顶点缓冲。</summary>
        void PushQuad(Vector3 c, float rx, float ry, Vector3 ra, Vector3 ua, Color32 col)
        {
            verts.Add(c - ra * rx + ua * ry);
            verts.Add(c + ra * rx + ua * ry);
            verts.Add(c + ra * rx - ua * ry);
            verts.Add(c - ra * rx - ua * ry);
            uvs.Add(QuadUV[0]); uvs.Add(QuadUV[1]); uvs.Add(QuadUV[2]); uvs.Add(QuadUV[3]);
            cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
        }

        /// <summary>每帧由 LiquidSim 提交液滴数组，重建 billboard 网格。</summary>
        public void Submit(LiquidSim.Drop[] drops, int count)
        {
            if (count == 0)
            {
                if (mf != null && mf.sharedMesh != null && verts.Count > 0)
                {
                    verts.Clear();
                    mesh.Clear(false);
                }
                return;
            }

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up    = cam != null ? cam.transform.up    : Vector3.up;

            verts.Clear(); cols.Clear(); uvs.Clear();
            verts.Capacity = Mathf.Max(verts.Capacity, count * 4);

            for (int i = 0; i < count; i++)
            {
                var d = drops[i];

                // alpha 淡出：air 结束前 0.9s；roll 随速度减小淡出（快停即隐没，由渗开的
                // 水迹接棒，避免"啪"地消失）
                float fade;
                if (d.mode == 0)
                    fade = Mathf.Clamp01(d.life / 0.9f);
                else
                    fade = Mathf.Clamp01((d.vel.magnitude - 0.06f) / 0.45f);

                float a = d.col.a * (1f / 255f) * fade;
                byte ba = (byte)(a * 255f + 0.5f);
                var vc = new Color32(d.col.r, d.col.g, d.col.b, ba);

                Vector3 p = d.pos;
                Vector3 rAxis, uAxis;   // quad 的右/上（quad 平面）
                float rx = d.r, ry = d.r;

                if (d.mode == 1 && d.surf != null)
                {
                    // roll：扁 quad 贴在表面法线平面上
                    Vector3 n = d.nrm.sqrMagnitude > 0.01f ? d.nrm : Vector3.up;
                    Vector3 camDir = cam != null ? cam.transform.forward : Vector3.forward;
                    rAxis = Vector3.Cross(camDir, n);
                    if (rAxis.sqrMagnitude < 1e-4f) rAxis = Vector3.right;
                    rAxis.Normalize();
                    uAxis = Vector3.Cross(rAxis, n).normalized;   // 贴面内朝上分量
                    ry = d.r * 0.5f;
                    PushQuad(p, rx, ry, rAxis, uAxis, vc);
                }
                else
                {
                    // air：正对相机的 soft 圆球 —— 圆球本身不做拉丝，
                    // 两球靠近时由 metaball 密度融合(低阈值)自动黏连成团。
                    rAxis = right;
                    uAxis = up;
                    PushQuad(p, d.r, d.r, rAxis, uAxis, vc);
                }
            }

            int quadCount = verts.Count / 4;
            tris.Clear();
            tris.Capacity = Mathf.Max(tris.Capacity, quadCount * 6);
            for (int q = 0; q < quadCount; q++)
            {
                int b = q * 4;
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            mesh.Clear(false);
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0, false);
        }
    }
}
