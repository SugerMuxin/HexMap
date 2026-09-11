using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Ellen 攻击输入组件（挂 Ellen / EllenNet 根物体，与 Animator 同物体）。
///
/// 鼠标左键 -> Animator Trigger（默认 AttackL，播 Combo1）
/// 鼠标右键 -> Animator Trigger（默认 AttackR，播 Combo2）
///
/// 依赖：AnimatorController 已由装配工具（Tools/角色控制/给 Ellen 装配左右键攻击）
/// 加入 Attack_L / Attack_R 两个状态与 AnyState 触发转换。直接 SetTrigger 即可。
///
/// 设计要点：
///  - 输入仲裁：本组件激活时置 HexControlMode.heroActive=true，
///    让 HexMapEditor 让出鼠标（与 HeroController 相同的项目约定；组件禁用/销毁后让出）。
///  - busy 保护：攻击动画播放中忽略再次输入，避免 Trigger 堆积导致连发/取消。
///  - UI 保护：鼠标在 uGUI 上时不触发（与 HeroController 一致；IMGUI 如 NetworkManagerHUD 无法拦截）。
///  - 联机：远端化身由 EllenNetController 直接禁用本组件（本组件不自行判断网络身份，
///    网络分流统一收口在联机层，单机 prefab 不带 EllenNetController 时天然只服务本地）。
///  - 空中可触发（默认）：动画播完按 IsGrounded 由 Animator 决定回 Idle_Running 或 Airborne。
///    如需仅地面攻击，打开 requireGrounded。
/// </summary>
public class EllenAttack : MonoBehaviour
{
    /// <summary>当前启用中的 EllenAttack 数量（用于输入占用仲裁：>0 时 heroActive=true）。</summary>
    static int _activeCount;

    [Header("触发配置")]
    [Tooltip("左键触发参数（Animator Trigger 名，与 AnimatorController 装配一致）")]
    public string leftTrigger = "AttackL";
    [Tooltip("右键触发参数（Animator Trigger 名）")]
    public string rightTrigger = "AttackR";
    [Tooltip("左键攻击状态名（busy 检测用，须与 AnimatorController 中状态名一致）")]
    public string leftStateName = "Attack_L";
    [Tooltip("右键攻击状态名（busy 检测用）")]
    public string rightStateName = "Attack_R";

    [Header("规则")]
    [Tooltip("攻击动画播放中忽略再次输入（防 Trigger 堆积连发）")]
    public bool blockWhileBusy = true;
    [Tooltip("仅在地面（IsGrounded）时可触发；关闭则跳跃/下落中也可攻击")]
    public bool requireGrounded = false;
    [Tooltip("鼠标在 uGUI 上时不触发攻击")]
    public bool ignoreUI = true;

    Animator _animator;
    NaughtyCharacter.Character _character;
    int _leftHash;
    int _rightHash;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _character = GetComponent<NaughtyCharacter.Character>();
        _leftHash = Animator.StringToHash(leftStateName);
        _rightHash = Animator.StringToHash(rightStateName);
    }

    void OnEnable()
    {
        _activeCount++;
        HexControlMode.heroActive = true;
    }

    void Update()
    {
        if (_animator == null || !_animator.isActiveAndEnabled)
        {
            return;
        }

        if (blockWhileBusy && IsInAttackState())
        {
            return;
        }

        if (requireGrounded && _character != null && !_character.IsGrounded)
        {
            return;
        }

        if (ignoreUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        bool left = Input.GetMouseButtonDown(0);
        bool right = Input.GetMouseButtonDown(1);
        if (left)
        {
            FireAttack(leftTrigger);
        }
        else if (right)
        {
            FireAttack(rightTrigger);
        }
    }

    /// <summary>当前 Animator 是否正播放攻击状态（busy 检测）。</summary>
    public bool IsInAttackState()
    {
        if (_animator == null)
        {
            return false;
        }
        AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
        return info.shortNameHash == _leftHash || info.shortNameHash == _rightHash;
    }

    // ------------------------------------------------------------------
    // AnimationEvent 接收（Combo clip 内嵌 MeleeAttackStart/MeleeAttackEnd 事件）
    //
    // 没有这两个方法时 Unity 每次播放攻击都会报：
    //   "AnimationEvent 'MeleeAttackStart' on animation 'EllenCombo1' has no receiver!"
    // 方法名必须与 clip 事件名一致（public、无参、挂在 Animator 同物体）。
    // 命中窗口语义：Start = 攻击判定开始，End = 判定结束。
    // 伤害/音效/网络广播请订阅下方事件，不要改方法签名。
    // ------------------------------------------------------------------

    /// <summary>命中窗口开始（Combo clip 事件触发）。</summary>
    public event System.Action MeleeAttackWindowStart;

    /// <summary>命中窗口结束（Combo clip 事件触发）。</summary>
    public event System.Action MeleeAttackWindowEnd;

    /// <summary>
    /// 攻击触发事件（本地输入真实触发攻击时通知，参数 = Animator Trigger 名）。
    /// 用途：联机层桥接（EllenNetController 订阅后转发 Mirror NetworkAnimator.SetTrigger 做全端动画广播）。
    /// 保持本组件零网络依赖 —— 单机运行时无人订阅，无任何开销。
    /// </summary>
    public event System.Action<string> AttackTriggered;

    void FireAttack(string trigger)
    {
        _animator.SetTrigger(trigger);
        AttackTriggered?.Invoke(trigger);
    }

    /// <summary>AnimationEvent 接收器：命中窗口开始。</summary>
    public void MeleeAttackStart()
    {
        MeleeAttackWindowStart?.Invoke();
    }

    /// <summary>AnimationEvent 接收器：命中窗口结束。</summary>
    public void MeleeAttackEnd()
    {
        MeleeAttackWindowEnd?.Invoke();
    }

    void OnDisable()
    {
        _activeCount = Mathf.Max(0, _activeCount - 1);
        // 本组件禁用/销毁：若场上已无任何启用中的攻击组件，则交还地图编辑鼠标
        if (_activeCount == 0)
        {
            HexControlMode.heroActive = false;
        }
    }

    void OnDestroy()
    {
        // 静态计数在 OnDisable 已减（禁用与销毁都会先走 OnDisable）。
        // 此处仅兜底：若物体被 Destroy 时组件本就 disabled（计数未减），补一次。
        // （正常路径不会重复减——OnDisable 已把计数归零时这里不再减。）
    }
}
