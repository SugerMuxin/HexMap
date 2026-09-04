using UnityEngine;

namespace PaintDoodle
{
    /// <summary>
    /// 液滴 CPU 模拟（无刚体，纯积分，2019.4 内建管线友好）。
    /// 与表面的交互全部走 Physics.Raycast 命中"可涂画层"(PaintableLayer)：
    ///   命中点携带网格真实 uv -> PaintableSurface.PaintAtUV 落颜料，
    ///   显示与写入共用同一 uv，任何朝向/形状的表面都不会错位或镜像。
    /// 生命周期：
    ///   air  : 抛物线飞行；沿运动方向 raycast，撞到可涂画表面 -> 溅开/滚/吸附
    ///   roll : 贴表面滚动（表面法线平面化速度），每帧 raycast 贴面拖湿迹，停/超时 -> 渗开水迹
    /// </summary>
    public class LiquidSim : MonoBehaviour
    {
        public struct Drop
        {
            public Vector3 pos, vel;
            public Color32 col;
            public float r;              // 半径
            public float life;
            public float maxLife;
            public byte mode;            // 0=air 1=roll
            public Vector3 nrm;          // roll：所在表面法线
            public PaintableSurface surf;// roll：所在表面
        }

        [Header("容量/物理")]
        public int maxDrops = 700;
        public float gravity = PaintUtil.PaintGravity;

        [Header("落地行为")]
        [Range(0f, 2f)] public float splashExtra = 0.3f;   // 溅射派生比例（黏稠：少溅碎）

        Drop[] drops;
        int count;
        LiquidRenderer rend;

        public bool CanSpawn { get { return count < maxDrops - 2; } }

        void Awake()
        {
            drops = new Drop[maxDrops];
        }

        void Start()
        {
            rend = GetComponent<LiquidRenderer>();
        }

        /// <summary>喷射入口。</summary>
        public void SpawnBlob(Vector3 pos, Vector3 vel, Color32 col, float radius)
        {
            if (count >= maxDrops - 2) return;
            drops[count++] = new Drop
            {
                pos = pos, vel = vel, col = col, r = radius,
                life = 9f, maxLife = 9f, mode = 0
            };
        }

        void AddDrop(Drop d)
        {
            if (count >= maxDrops) return;
            drops[count++] = d;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt > 0.05f) dt = 0.05f;

            float g = gravity * dt;
            int i = 0;
            bool splashEnabled = splashExtra > 0.01f;

            while (i < count)
            {
                bool remove = false;
                Drop d = drops[i];

                if (d.mode == 0)
                {
                    // ---------- air ----------
                    d.vel.y -= g;
                    d.pos += d.vel * dt;
                    d.life -= dt;

                    Vector3 step = d.vel * dt;
                    float stepLen = step.magnitude;

                    RaycastHit hit = default(RaycastHit);
                    bool hitSurf = false;
                    if (stepLen > 1e-4f)
                        hitSurf = Physics.Raycast(d.pos, step.normalized, out hit,
                                                  stepLen + d.r * 1.5f, PaintUtil.PaintableMask);

                    if (hitSurf)
                    {
                        LandOnSurface(ref d, hit, splashEnabled, ref remove);
                    }
                    else if (d.life <= 0f)
                    {
                        remove = true;
                    }
                }
                else
                {
                    // ---------- roll ----------
                    d.life -= dt;

                    // 速度平面化 + 摩擦
                    d.vel = Vector3.ProjectOnPlane(d.vel, d.nrm);
                    d.vel *= Mathf.Exp(-2.2f * dt);
                    d.pos += d.vel * dt;

                    PaintableSurface ps = d.surf;
                    RaycastHit h = default(RaycastHit);
                    bool onSurf = false;
                    if (ps != null &&
                        Physics.Raycast(d.pos, -d.nrm, out h, d.r * 2f + 0.12f, PaintUtil.PaintableMask) &&
                        h.collider.GetComponent<PaintableSurface>() == ps)
                    {
                        onSurf = true;
                        d.pos = h.point + d.nrm * d.r;

                        // 拖尾：越慢越浓
                        float sp = d.vel.magnitude;
                        float wetAmt = 0.08f + 0.2f * Mathf.Clamp01(1.4f - sp);
                        ps.PaintAtUV(h.textureCoord, d.r * 2.3f, d.col, wetAmt);
                    }

                    bool stopped = d.life <= 0f || d.vel.sqrMagnitude < 0.012f;
                    if (!onSurf || stopped)
                    {
                        // 停下 / 滚出表面：在最后位置渗开一小片水迹
                        if (onSurf && ps != null)
                            ps.PaintAtUV(h.textureCoord, d.r * 5.5f, d.col, 0.6f);
                        remove = true;
                    }
                }

                if (remove)
                {
                    drops[i] = drops[count - 1];
                    count--;
                }
                else
                {
                    drops[i] = d;
                    i++;
                }
            }

            if (rend != null) rend.Submit(drops, count);
        }

        /// <summary>air 滴撞上可涂画表面：溅开 / 摊开 / 转滚动 / 吸附收尾。</summary>
        void LandOnSurface(ref Drop d, RaycastHit hit, bool splashEnabled, ref bool remove)
        {
            PaintableSurface ps = hit.collider.GetComponent<PaintableSurface>();
            if (ps == null) { remove = true; return; }   // 撞到不可涂物体 -> 消失

            Vector3 n = hit.normal;
            d.pos = hit.point + n * (d.r + 0.002f);      // 贴到表面外侧

            float impact = -Vector3.Dot(d.vel, n);       // 法向接近速度
            if (impact < 0f) impact = 0f;

            bool big = d.r >= 0.03f;

            if (impact > 2.2f && big)
            {
                // 高速撞击：主溅 + 派生小飞沫
                ps.PaintAtUV(hit.textureCoord, d.r * 3.6f, d.col, 0.75f);
                if (splashEnabled && Random.value < 0.85f && count < maxDrops)
                {
                    Vector3 refl = Vector3.Reflect(d.vel, n) * 0.35f;
                    int n2 = Random.value < 0.4f ? 2 : 1;
                    Vector3 t = Vector3.Cross(n, Vector3.up);
                    if (t.sqrMagnitude < 1e-4f) t = Vector3.right;
                    t.Normalize();
                    Vector3 bt = Vector3.Cross(n, t);

                    for (int k = 0; k < n2 && count < maxDrops; k++)
                    {
                        float ang = Random.Range(0f, Mathf.PI * 2f);
                        float rad = Random.Range(0.2f, 1.0f);
                        Vector3 tangent = (t * Mathf.Cos(ang) + bt * Mathf.Sin(ang)) * rad;
                        Vector3 v = refl + tangent * 0.7f + n * Random.Range(0.6f, 1.4f);
                        AddDrop(new Drop
                        {
                            pos = d.pos,
                            vel = v,
                            col = d.col,
                            r = d.r * Random.Range(0.30f, 0.45f),
                            life = 1.4f, maxLife = 1.4f, mode = 0
                        });
                    }
                }
            }
            else if (impact > 0.4f)
            {
                // 中速：啪地摊成一大滩（黏稠颜料不碎）
                ps.PaintAtUV(hit.textureCoord, d.r * 3.0f, d.col, 0.55f);
            }

            // 接近水平的表面且有动量 -> 转滚动；否则吸附结束
            Vector3 horiz = Vector3.ProjectOnPlane(d.vel, n);
            if (n.y > 0.6f && horiz.sqrMagnitude > 0.02f)
            {
                d.mode = 1;
                d.surf = ps;
                d.nrm = n;
                d.vel = horiz * 0.55f;
                d.maxLife = 2.6f;
                d.life = Random.Range(1.4f, d.maxLife);
            }
            else
            {
                // 墙面/陡面或已无动量：原地渗成一大片后收尾
                if (impact > 0.4f || !big)
                    ps.PaintAtUV(hit.textureCoord, d.r * 6.0f, d.col, 0.55f);
                remove = true;
            }
        }
    }
}
