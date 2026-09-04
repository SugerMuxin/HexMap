using UnityEngine;

namespace PaintDoodle
{
    /// <summary>
    /// 轨道相机：绕场地中心旋转俯视。右键拖动转视角，滚轮缩放。
    /// autoRotate 打开时自动缓慢环绕（配合 SprayController 的自动演示喷洒）。
    /// </summary>
    public class OrbitRig : MonoBehaviour
    {
        public Vector3 anchor = new Vector3(0f, 0.4f, 0f);

        [Header("轨道")]
        public float dist  = 10.5f;
        public float yaw   = 25f;
        public float pitch = 34f;          // 相机在目标上方的仰角（度）
        [Range(0f, 120f)] public float autoYawSpeed = 14f;

        [Header("范围")]
        public float minDist = 4f;
        public float maxDist = 18f;
        public float minPitch = 8f;
        public float maxPitch = 80f;

        bool dragging;
        Vector2 lastMouse;
        bool autoRotate = true;

        public bool AutoRotate
        {
            get { return autoRotate; }
            set { autoRotate = value; }
        }

        void Update()
        {
            // 右键拖动
            if (Input.GetMouseButtonDown(1)) { dragging = true; lastMouse = Input.mousePosition; }
            if (Input.GetMouseButtonUp(1))   dragging = false;
            if (dragging)
            {
                Vector2 cur = Input.mousePosition;
                Vector2 d = cur - lastMouse;
                lastMouse = cur;
                if (d.sqrMagnitude > 0.1f)
                {
                    yaw   += d.x * 0.28f;
                    pitch -= d.y * 0.22f;
                    autoRotate = false;      // 手动干涉 -> 关闭自动环绕
                }
            }

            // 滚轮缩放
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.0001f) dist -= wheel * 2.2f;

            if (autoRotate) yaw += autoYawSpeed * Time.deltaTime;

            dist  = Mathf.Clamp(dist, minDist, maxDist);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            float rad = pitch * Mathf.Deg2Rad;
            float cy = Mathf.Cos(rad), sy = Mathf.Sin(rad);
            Vector3 pos = anchor + new Vector3(
                Mathf.Sin(yaw * Mathf.Deg2Rad) * cy,
                sy,
                Mathf.Cos(yaw * Mathf.Deg2Rad) * cy) * dist;

            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(anchor - pos, Vector3.up);
        }
    }
}
