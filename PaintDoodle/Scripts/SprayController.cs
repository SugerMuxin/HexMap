using UnityEngine;

namespace PaintDoodle
{
    /// <summary>
    /// 喷枪控制：从相机位置沿视线方向喷射彩色液滴。
    ///  - 按住左键：手动喷涂（颜色每 0.5s 轮换一次）
    ///  - R：恢复自动演示（环绕相机 + 定时换色喷一遍场地）
    /// </summary>
    public class SprayController : MonoBehaviour
    {
        public LiquidSim sim;
        public OrbitRig rig;

        [Header("喷射")]
        public float nozzleDist = 1.2f;   // 喷口在相机前方距离
        public float speed = 6.5f;        // 低速（黏稠蠕动）
        public float manualRate = 42f;    // 每秒滴数（大滴少而密，正好黏连）
        public float autoRate   = 30f;
        public float radiusMin = 0.2f, radiusMax = 0.34f;   // 大圆球
        [Range(0f, 1.5f)] public float spread = 0.06f;      // 聚成一股，不散开

        [Header("自动演示")]
        public float burstDuration = 1.0f;
        public float burstGap = 1.5f;

        Camera cam;
        bool auto = true;
        float burstLeft;
        float gapLeft = 1.2f;
        int colorIdx;
        float emitAcc;
        Color32 curColor;

        static readonly Color32[] Palette =
        {
            new Color32(235,  40,  55, 255),   // 红
            new Color32(250, 140,  20, 255),   // 橙
            new Color32(250, 215,  30, 255),   // 黄
            new Color32( 40, 195,  90, 255),   // 绿
            new Color32( 20, 190, 215, 255),   // 青
            new Color32( 55,  90, 240, 255),   // 蓝
            new Color32(165,  60, 215, 255),   // 紫
            new Color32(240,  70, 150, 255)    // 粉
        };

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            if (rig == null) rig = GetComponent<OrbitRig>();
            curColor = Palette[0];
        }

        void Update()
        {
            if (sim == null || cam == null) return;

            // 手动喷涂：左键按下即进手动模式。落点 = 鼠标射线与地面的交点，
            // 按住持续喷向该点，单击也立刻喷一小簇。
            bool manualDown = Input.GetMouseButtonDown(0);
            bool manualHeld = Input.GetMouseButton(0);
            if (manualDown)
            {
                if (auto)
                {
                    auto = false;
                    if (rig != null) rig.AutoRotate = false;
                }
                Vector3 tgt;
                if (GetPaintTarget(out tgt)) EmitBurst(tgt, 12);
            }
            // 轨道被手动拖动（rig.AutoRotate 变 false）时，自动喷洒也跟随停止
            if (auto && rig != null && !rig.AutoRotate)
                auto = false;

            if (Input.GetKeyDown(KeyCode.R) && !auto)
            {
                auto = true;
                if (rig != null) rig.AutoRotate = true;
                gapLeft = 0.8f;
            }

            if (auto)
            {
                // 自动演示节拍
                if (burstLeft > 0f)
                {
                    burstLeft -= Time.deltaTime;
                    SprayView(autoRate);
                }
                else
                {
                    gapLeft -= Time.deltaTime;
                    if (gapLeft <= 0f)
                    {
                        burstLeft = burstDuration;
                        gapLeft = burstGap;
                        curColor = Palette[colorIdx % Palette.Length];
                        colorIdx++;
                    }
                }
            }
            else if (manualHeld)
            {
                // 手动时颜色随时间轮换，喷向鼠标当前 raycast 命中的可涂画表面
                int ci = (int)(Time.time / 0.55f) % Palette.Length;
                curColor = Palette[ci];
                Vector3 tgt;
                if (GetPaintTarget(out tgt)) SprayAt(manualRate, tgt);
            }
        }

        /// <summary>鼠标射线命中"可涂画层"任意表面的世界点（地面/墙/箱子都行）。</summary>
        bool GetPaintTarget(out Vector3 pt)
        {
            pt = Vector3.zero;
            if (cam == null) return false;
            RaycastHit hit;
            if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition),
                                 out hit, 500f, PaintUtil.PaintableMask)) return false;
            pt = hit.point;
            return true;
        }

        /// <summary>自动/旧式：沿相机视线喷（不依赖地面落点）。</summary>
        void SprayView(float rate)
        {
            Vector3 nozzle = cam.transform.position + cam.transform.forward * nozzleDist;
            Vector3 dir = cam.transform.forward;

            emitAcc += Time.deltaTime * rate;
            int n = (int)emitAcc;
            emitAcc -= n;

            for (int i = 0; i < n && sim.CanSpawn; i++)
            {
                Vector3 jitter = Random.insideUnitSphere * spread;
                Vector3 vel = (dir + jitter).normalized * speed * Random.Range(0.92f, 1.08f);
                float r = Random.Range(radiusMin, radiusMax);
                sim.SpawnBlob(nozzle + Random.insideUnitSphere * 0.05f, vel, curColor, r);
            }
        }

        /// <summary>手动：持续喷向表面目标点。</summary>
        void SprayAt(float rate, Vector3 target)
        {
            Vector3 nozzle = cam.transform.position + cam.transform.forward * nozzleDist;

            emitAcc += Time.deltaTime * rate;
            int n = (int)emitAcc;
            emitAcc -= n;

            for (int i = 0; i < n && sim.CanSpawn; i++)
                EmitOne(nozzle, target);
        }

        /// <summary>单击立刻喷一小簇。</summary>
        void EmitBurst(Vector3 target, int count)
        {
            Vector3 nozzle = cam.transform.position + cam.transform.forward * nozzleDist;
            for (int i = 0; i < count && sim.CanSpawn; i++)
                EmitOne(nozzle, target);
        }

        /// <summary>
        /// 向 target 附近发射一粒，弹道解算：给定飞行时间 t，算初速使抛物线精确命中
        /// （抵消重力），与 LiquidSim 的积分一致 -> 颜料一定落在喷点处。
        /// </summary>
        void EmitOne(Vector3 nozzle, Vector3 target)
        {
            // 落点散布：黏稠胶流几乎不发散，微微一圈即可
            Vector3 approxN = Vector3.up;
            Vector3 t1 = Vector3.Cross(cam.transform.forward, approxN);
            if (t1.sqrMagnitude < 1e-4f) t1 = Vector3.right;
            t1.Normalize();
            Vector2 off = Random.insideUnitCircle * (spread * 1.7f + 0.10f);
            Vector3 aim = target + t1 * off.x + Vector3.Cross(t1, approxN) * off.y;

            Vector3 delta = aim - nozzle;
            float dist = delta.magnitude;
            if (dist < 0.05f) return;

            // 黏稠低速弹道：长飞行时间上限，慢吞吞地拱起再坠到目标
            float t = Mathf.Clamp(dist / speed, 0.5f, 2.2f);
            float g = sim != null ? sim.gravity : PaintUtil.PaintGravity;
            Vector3 vel = delta / t + Vector3.up * (0.5f * g * t);

            float r = Random.Range(radiusMin, radiusMax);
            sim.SpawnBlob(nozzle + Random.insideUnitSphere * 0.03f, vel, curColor, r);
        }

        void OnGUI()
        {
            if (!Application.isPlaying) return;   // OnGUI 编辑模式也会触发
            // 顶部操作提示（Unity 默认 IMGUI 字体不含中文字形，用英文）
            string mode = auto ? "AUTO" : "MANUAL";
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(8, 8, 430, 56), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(18, 14, 420, 20),
                "Metaball Paint Demo  [" + mode + "]");
            GUI.Label(new Rect(18, 34, 420, 26),
                "LMB: spray at cursor / click: puff    RMB drag: orbit    Wheel: zoom    R: auto demo");
        }
    }
}
