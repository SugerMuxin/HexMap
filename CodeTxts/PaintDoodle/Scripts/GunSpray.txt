using UnityEngine;

namespace PaintDoodle
{
    /// <summary>
    /// 第一人称喷枪：按住左键，从"喷口 Transform"持续喷出颜料软球（metaball 融合）。
    /// 喷口 (muzzle) 决定发射起点与方向：直接查找场景里名为 "Muzzle" 的物体使用，
    /// 不会自动创建。请在场景中自行放置一个 Muzzle（空物体/枪口模型都行），
    /// 调节它的位置与旋转即决定喷口起点与喷射方向（朝其 +Z/forward）。
    /// 找不到 Muzzle 时不喷射（只打一条警告）。
    /// </summary>
    public class GunSpray : MonoBehaviour
    {
        public LiquidSim sim;

        [Header("喷口")]
        [Tooltip("可手动指定喷口；留空则每帧查找场景中名为 Muzzle 的物体")]
        public Transform muzzle;

        [Header("喷射")]
        public float rate = 160f;        // 每秒滴数（密 -> 融合成连续胶流）
        public float speed = 15f;        // 出膛速度
        public float radiusMin = 0.09f, radiusMax = 0.16f;
        [Range(0f, 0.5f)] public float cone = 0.05f;   // 散布锥角（弧度级小量）

        Camera cam;
        float emitAcc;
        Color32 curColor;
        bool warnedNoMuzzle;

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
            if (muzzle == null) muzzle = LookupMuzzle();
        }

        /// <summary>在场景中查找名为 "Muzzle" 的物体（惰性，每帧也可再查）。</summary>
        Transform LookupMuzzle()
        {
            GameObject go = GameObject.Find("Muzzle");
            return go != null ? go.transform : null;
        }

        void Update()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (sim == null) sim = Object.FindObjectOfType<LiquidSim>();
            if (sim == null || cam == null) return;

            // muzzle 未指定：每帧尝试查找场景里的 "Muzzle"（用户可能在运行时才放好）
            if (muzzle == null) muzzle = LookupMuzzle();
            if (muzzle == null)
            {
                if (!warnedNoMuzzle && Input.GetMouseButton(0))
                {
                    warnedNoMuzzle = true;
                    Debug.LogWarning("[PaintDoodle] 找不到场景中的 Muzzle 物体，无法喷射。" +
                                     "请放一个名为 Muzzle 的物体（或拖到 GunSpray.muzzle 槽）。");
                }
                return;
            }
            warnedNoMuzzle = false;

            if (Input.GetMouseButton(0))
            {
                // 颜色随时间轮换
                int ci = (int)(Time.time / 0.5f) % Palette.Length;
                curColor = Palette[ci];

                Vector3 p0 = muzzle.position;     // 喷口世界位置
                Vector3 f  = muzzle.forward;      // 喷口朝向（可调节）

                // 锥形散布基底（垂直喷口朝向的两个正交轴）
                Vector3 s1 = Vector3.Cross(f, Vector3.up);
                if (s1.sqrMagnitude < 1e-6f) s1 = Vector3.Cross(f, Vector3.right);
                s1.Normalize();
                Vector3 s2 = Vector3.Cross(f, s1).normalized;

                emitAcc += Time.deltaTime * rate;
                int n = (int)emitAcc;
                emitAcc -= n;

                for (int i = 0; i < n && sim.CanSpawn; i++)
                {
                    float cx = (Random.value * 2f - 1f) * cone;
                    float cy = (Random.value * 2f - 1f) * cone;
                    Vector3 dir = (f + s1 * cx + s2 * cy).normalized;
                    Vector3 vel = dir * speed * Random.Range(0.92f, 1.08f);
                    float r = Random.Range(radiusMin, radiusMax);
                    sim.SpawnBlob(p0 + Random.insideUnitSphere * 0.02f, vel, curColor, r);
                }
            }
        }

        void OnDrawGizmos()
        {
            // 编辑器里显示喷口：一个小圆 + 朝向短线，方便拖拽调节时看清
            Transform m = muzzle != null ? muzzle : LookupMuzzle();
            if (m != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
                Gizmos.DrawWireSphere(m.position, 0.05f);
                Gizmos.DrawRay(m.position, m.forward * 0.5f);
            }
        }

        void OnGUI()
        {
            if (!Application.isPlaying || cam == null) return;
            if (!Cursor.visible)
            {
                GUI.color = new Color(0, 0, 0, 0.45f);
                GUI.DrawTexture(new Rect(8, 8, 340, 20), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(16, 10, 330, 18), "Hold LMB to spray paint forward");
            }
        }
    }
}
