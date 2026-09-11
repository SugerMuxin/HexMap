using UnityEngine;

/// <summary>
/// 角色跟随摄像机。
/// - 第三人称：相机位于角色正后方（跟随 target.forward 水平角，角色仅在点击地面时转向，
///   因此相机只在点击后平滑绕到新方向，鼠标悬停画面不会持续转动），俯视角色，滚轮缩放；
/// - 第一人称：相机移到角色眼睛高度，朝向与角色一致；此时角色实时面向鼠标（视角随鼠标转动）；
/// - 按 toggleKey（默认 V）切换第一/第三人称。
/// </summary>
public class HeroFollowCamera : MonoBehaviour
{
    public enum ViewMode { ThirdPerson, FirstPerson }

    [Header("引用")]
    public Transform target;                // 跟随目标（角色）
    [Tooltip("第一人称时隐藏的角色自身网格（留空自动查找 target 下第一个 Renderer）")]
    public Renderer selfRenderer;

    [Header("模式")]
    public ViewMode mode = ViewMode.ThirdPerson;
    public KeyCode toggleKey = KeyCode.V;
    [Tooltip("第一人称时隐藏角色自身网格，避免遮挡视线")]
    public bool hideSelfInFirstPerson = true;

    [Header("第三人称参数")]
    [Tooltip("相机俯仰角（度，>0 从上往下看）")]
    public float pitch = 35f;
    [Tooltip("是否跟随角色朝向（相机始终在角色正后方，随角色点击转向）")]
    public bool followTargetYaw = true;
    [Tooltip("不跟随角色朝向时使用的固定水平角（度）")]
    public float yaw = 0f;
    [Tooltip("相机绕到角色侧后方的额外偏移（度），0 = 正后方")]
    public float yawOffset = 0f;
    [Tooltip("相机与角色斜距")]
    public float distance = 40f;
    [Tooltip("注视点抬高量（看角色躯干）")]
    public float lookHeight = 6f;
    [Tooltip("跟随平滑时间（秒，越大越缓）")]
    public float smoothTime = 0.25f;

    [Header("第一人称参数")]
    [Tooltip("眼睛高度（相对角色脚底）")]
    public float eyeHeight = 9f;
    [Tooltip("第一人称跟随平滑时间")]
    public float firstPersonSmoothTime = 0.1f;

    [Header("滚轮缩放（第三人称）")]
    public bool allowZoom = true;
    public float zoomSpeed = 25f;
    public float minDistance = 12f;
    public float maxDistance = 90f;

    Vector3 velocity;
    HeroController controller;

    void OnEnable()
    {
        if (target != null)
        {
            InitTarget();
        }
    }

    void Start()
    {
        if (target != null)
        {
            InitTarget();
        }
    }

    void InitTarget()
    {
        if (selfRenderer == null)
        {
            selfRenderer = target.GetComponentInChildren<Renderer>();
        }
        if (controller == null)
        {
            controller = target.GetComponent<HeroController>();
        }
        if (controller != null)
        {
            controller.firstPersonView = (mode == ViewMode.FirstPerson);
        }
        ApplySelfVisibility();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleMode();
        }
        // 滚轮缩放（仅第三人称）
        if (mode == ViewMode.ThirdPerson && allowZoom)
        {
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel != 0f)
            {
                distance = Mathf.Clamp(distance - wheel * zoomSpeed, minDistance, maxDistance);
            }
        }
    }

    void LateUpdate()
    {
        if (target == null)
        {
            return;
        }
        if (mode == ViewMode.ThirdPerson)
        {
            FollowThirdPerson();
        }
        else
        {
            FollowFirstPerson();
        }
    }

    void FollowThirdPerson()
    {
        // 相机水平角 = 角色朝向（角色只在点击地面时转向 -> 相机只在点击后平滑转到新方向）
        float camYaw = yaw;
        if (followTargetYaw)
        {
            Vector3 fwd = target.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.0001f)
            {
                camYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            }
        }
        camYaw += yawOffset;

        Vector3 desired = target.position + Quaternion.Euler(pitch, camYaw, 0f) * (Vector3.back * distance);
        transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);

        Vector3 lookAt = target.position + Vector3.up * lookHeight;
        transform.rotation = Quaternion.LookRotation(lookAt - transform.position);
    }

    void FollowFirstPerson()
    {
        // 眼睛位置，朝向与角色一致（角色第一人称下实时面向鼠标 -> 视角随鼠标转）
        Vector3 eye = target.position + Vector3.up * eyeHeight;
        transform.position = Vector3.SmoothDamp(transform.position, eye, ref velocity, firstPersonSmoothTime);
        transform.rotation = target.rotation;
    }

    public void ToggleMode()
    {
        SetMode(mode == ViewMode.ThirdPerson ? ViewMode.FirstPerson : ViewMode.ThirdPerson);
    }

    public void SetMode(ViewMode newMode)
    {
        if (mode == newMode)
        {
            return;
        }
        mode = newMode;
        velocity = Vector3.zero;
        // 同步角色视角模式：第一人称下角色实时面向鼠标（转视角），第三人称回到点击转向
        if (controller == null && target != null)
        {
            controller = target.GetComponent<HeroController>();
        }
        if (controller != null)
        {
            controller.firstPersonView = (mode == ViewMode.FirstPerson);
            // 不额外转向角色：第三人称相机位于角色正后方、看向角色前方，
            // 与第一人称视线方向天然一致，切换视野连续
        }
        ApplySelfVisibility();
        Snap();
    }

    void ApplySelfVisibility()
    {
        if (selfRenderer == null)
        {
            return;
        }
        selfRenderer.enabled = (mode == ViewMode.FirstPerson) ? !hideSelfInFirstPerson : true;
    }

    /// <summary>立即把相机放到当前模式的目标位置（无平滑）</summary>
    public void Snap()
    {
        if (target == null)
        {
            return;
        }
        if (mode == ViewMode.ThirdPerson)
        {
            float camYaw = yaw;
            if (followTargetYaw)
            {
                Vector3 fwd = target.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 0.0001f)
                {
                    camYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                }
            }
            camYaw += yawOffset;
            transform.position = target.position + Quaternion.Euler(pitch, camYaw, 0f) * (Vector3.back * distance);
            Vector3 lookAt = target.position + Vector3.up * lookHeight;
            transform.rotation = Quaternion.LookRotation(lookAt - transform.position);
        }
        else
        {
            transform.position = target.position + Vector3.up * eyeHeight;
            transform.rotation = target.rotation;
        }
        velocity = Vector3.zero;
    }
}
