using NaughtyCharacter;
using UnityEngine;

/// <summary>
/// 角色脚步 / 涉水 / 落地音效触发（游戏侧组件，挂到本地活角色根物体）。
///
/// 零改动 NaughtyCharacter：每帧只读 Character 公开状态（IsGrounded /
/// HorizontalVelocity / VerticalVelocity / RemoteSuppressed）驱动音效：
///  - 脚步：地面移动距离累计到 stepDistance 触发一步；步频上限 minStepInterval；
///           脚下材质（经 HexGroundAudioBridge）决定 key：footstep/grass|stone|sand|water…；
///  - 入水/出水：脚下材质 Water ↔ 非 Water 跳变 → splash/in、splash/out；
///  - 落地：空中累计超过 airTimeToLand 后落地 → land。
///
/// 多人纪律：远端化身（Character.RemoteSuppressed / 组件被禁用）不发声——
/// 远端位置由 NetworkTransform 呈现，本组件在非本地端天然静默（读到的速度≈0 且被抑制）。
///
/// 素材缺失时 AudioService 静默跳过（一次性警告），本组件无任何报错路径。
/// </summary>
public class CharacterFootstepAudio : MonoBehaviour
{
    /// <summary>
    /// 脚步音效触发事件（key, 世界位置）。本地每次真实触发一步/水花/落地时发出。
    /// 供联机层（NetworkFootstepRelay）订阅后广播给其它客户端——本组件不依赖 Mirror。
    /// </summary>
    public event System.Action<string, Vector3> StepSfxTriggered;

    [Header("引用（可留空自动查找）")]
    [Tooltip("地面材质桥接器（HexGroundAudioBridge，挂在 HexGrid 上）")]
    public HexGroundAudioBridge groundBridge;
    [Tooltip("角色本体（默认取本物体上的 Character）")]
    public Character character;

    [Header("脚步")]
    [Tooltip("每走这么多米触发一步")]
    public float stepDistance = 0.7f;
    [Tooltip("步频上限（秒/步），防止高速时脚步过密")]
    public float minStepInterval = 0.28f;
    [Tooltip("低于该水平速度不出脚步")]
    public float speedThreshold = 0.5f;

    [Header("水花与落地")]
    [Tooltip("进出水 splash 音")]
    public bool enableSplash = true;
    [Tooltip("进出水 splash 最短间隔（秒），防同一格边界反复触发")]
    public float splashCooldown = 0.8f;
    [Tooltip("跳跃/跌落落地音（空中时长超过该值落地才触发）")]
    public bool enableLand = true;
    [Tooltip("视为'跳/跌'的最短滞空秒数")]
    public float airTimeToLand = 0.35f;

    float _accumDistance;
    float _lastStepTime;
    float _splashTimer;
    float _airTime;
    bool _wasGrounded = true;
    GroundKind _lastKind;
    bool _hasLastKind;

    void OnEnable()
    {
        if (character == null) character = GetComponent<Character>();
        if (groundBridge == null) groundBridge = FindObjectOfType<HexGroundAudioBridge>();
        _accumDistance = 0f;
        _airTime = 0f;
        _splashTimer = 0f;
        _wasGrounded = true;
        _hasLastKind = false;
    }

    void Update()
    {
        if (character == null) character = GetComponent<Character>();
        if (groundBridge == null) groundBridge = FindObjectOfType<HexGroundAudioBridge>();
        if (character == null || groundBridge == null) return;

        // 多人纪律：远端化身（被 EllenNetController 抑制 / 组件禁用）不发声
        if (character.RemoteSuppressed || !character.enabled)
        {
            return;
        }

        Vector3 pos = transform.position;
        bool grounded = character.IsGrounded;
        float speed = character.HorizontalVelocity.magnitude;
        float dt = Time.deltaTime;

        GroundKind kind = groundBridge.GetGroundKind(pos);

        // 空中计时（用于落地音判定）
        if (!grounded)
        {
            _airTime += dt;
        }

        // 入水 / 出水 splash
        _splashTimer -= dt;
        if (enableSplash && _hasLastKind && _splashTimer <= 0f)
        {
            bool wasWater = _lastKind == GroundKind.Water;
            bool isWater = kind == GroundKind.Water;
            if (wasWater != isWater)
            {
                PlayAtPoint(isWater ? "splash/in" : "splash/out", pos);
                _splashTimer = splashCooldown;
            }
        }

        // 落地（滞空足够久才认为是从高处落下）
        if (enableLand && !_wasGrounded && grounded && _airTime >= airTimeToLand)
        {
            PlayAtPoint("land", pos);
        }

        // 脚步：地面 + 有速度 + 累积里程到步长
        if (grounded && speed > speedThreshold)
        {
            _accumDistance += speed * dt;
            float interval = Mathf.Max(minStepInterval, stepDistance / Mathf.Max(speed, 0.01f));
            if (_accumDistance >= stepDistance && Time.time - _lastStepTime >= interval)
            {
                PlayAtPoint(FootstepKey(kind), pos);
                _accumDistance = 0f;
                _lastStepTime = Time.time;
            }
        }
        else
        {
            _accumDistance = 0f;
        }

        _wasGrounded = grounded;
        _lastKind = kind;
        _hasLastKind = true;
    }

    /// <summary>地面材质 → 脚步音 key。Water 用专门的涉水脚步；Default 归入 grass。</summary>
    static string FootstepKey(GroundKind kind)
    {
        switch (kind)
        {
            case GroundKind.Stone: return "footstep/stone";
            case GroundKind.Sand: return "footstep/sand";
            case GroundKind.Water: return "footstep/water";
            case GroundKind.Grass:
            case GroundKind.Default:
            default: return "footstep/grass";
        }
    }

    void PlayAtPoint(string key, Vector3 position)
    {
        AudioService svc = AudioService.Instance;
        if (svc != null && svc.bank != null)
        {
            svc.PlaySfxAtPoint(key, position);
        }
        // 联机广播钩子：本地已播，其它端经 NetworkFootstepRelay 收到后各播各的
        System.Action<string, Vector3> handler = StepSfxTriggered;
        if (handler != null)
        {
            handler(key, position);
        }
    }
}
