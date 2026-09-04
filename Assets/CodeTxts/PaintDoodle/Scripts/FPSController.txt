using UnityEngine;

namespace PaintDoodle
{
    /// <summary>
    /// 第一人称控制器：CharacterController 移动（WASD + Shift 跑 + Space 跳），
    /// 鼠标转视角（水平转身体 yaw，垂直转相机 pitch）。点击画面锁定鼠标，ESC 释放。
    /// 相机必须作为本物体的子物体（DemoBootFPS 会挂好）。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FPSController : MonoBehaviour
    {
        [Header("移动")]
        public float moveSpeed = 6f;
        public float sprintSpeed = 10f;
        public float jumpSpeed = 6.5f;
        public float gravityScale = 1.6f;

        [Header("视角")]
        public float lookSens = 2.2f;
        public float eyeHeight = 1.62f;
        public float minPitch = -85f, maxPitch = 85f;

        CharacterController cc;
        Camera cam;
        float pitch;
        float vy;
        bool locked;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);   // 胶囊从脚底到 1.8m
        }

        void EnsureCam()
        {
            // 相机可能是之后才被挂为子物体（运行时搭建顺序），惰性查找并归位到眼睛高度
            if (cam != null) return;
            cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                cam.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
                cam.transform.localRotation = Quaternion.identity;
            }
        }

        void Update()
        {
            EnsureCam();
            if (cc == null) return;

            // 鼠标锁定管理：点击锁定，ESC 释放
            if (Input.GetKeyDown(KeyCode.Escape)) SetLock(false);
            if (!locked && Input.GetMouseButtonDown(0)) SetLock(true);

            // 视角
            if (locked && cam != null)
            {
                float mx = Input.GetAxis("Mouse X") * lookSens;
                float my = Input.GetAxis("Mouse Y") * lookSens;
                transform.Rotate(0f, mx, 0f, Space.Self);
                pitch = Mathf.Clamp(pitch - my, minPitch, maxPitch);
                cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            // 移动
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            float sp = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                       ? sprintSpeed : moveSpeed;

            Vector3 f = transform.forward; f.y = 0f; f.Normalize();
            Vector3 r = transform.right;   r.y = 0f; r.Normalize();
            Vector3 mv = (f * v + r * h) * sp;

            if (cc.isGrounded)
            {
                vy = -1.5f;
                if (Input.GetButtonDown("Jump")) vy = jumpSpeed;
            }
            else
            {
                vy += Physics.gravity.y * gravityScale * Time.deltaTime;
            }
            mv.y = vy;
            cc.Move(mv * Time.deltaTime);
        }

        void SetLock(bool on)
        {
            locked = on;
            if (on) Cursor.lockState = CursorLockMode.Locked;
            else    Cursor.lockState = CursorLockMode.None;
            Cursor.visible = !on;
        }

        void OnGUI()
        {
            if (Application.isPlaying && !locked)
            {
                GUI.color = new Color(0, 0, 0, 0.7f);
                GUI.DrawTexture(new Rect(8, 8, 430, 56), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(18, 14, 420, 20), "FPS Paint Doodle");
                GUI.Label(new Rect(18, 34, 420, 26),
                    "LMB: lock mouse + spray forward    WASD: move    Shift: run    Space: jump    ESC: unlock");
            }
        }
    }
}
