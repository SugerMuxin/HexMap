using UnityEngine;

/// <summary>
/// 玩家目标标记（游戏侧）：挂上它 = 「我是一个可被怪物索敌的玩家」。
///
/// 定位：把「玩家」从具体实现里抽出来。当前场景的玩家是 Role/Ellen（NaughtyCharacter），
/// 联机时远端化身是 EllenNet —— 两者都能挂本组件，怪物 AI 完全不需要知道区别。
///
/// 注册：OnEnable 进 <see cref="PlayerTargetRegistry"/>，OnDisable/OnDestroy 退出。
/// 注意 EditMode（编辑器非运行态）下 Unity 不调用 Awake/OnEnable → 断言脚本需要显式
/// 调 `PlayerTargetRegistry.Register(...)`（本类也提供 `ManualRegister()` 便利入口）。
///
/// 瞄准点：怪物面向/攻击取的点 = `aimPoint`（留空用自身）+ 世界 Y 偏移 `aimHeight`。
/// 玩家模型比根节点矮/高时调 aimHeight 即可，不用改代码。
/// </summary>
[DisallowMultipleComponent]
public class PlayerTarget : MonoBehaviour
{
    [Header("身份")]
    [Tooltip("玩家标识（0 = 本地单机默认；联机可填连接号 / 玩家号，仅作区分用）")]
    public int playerId;

    [Tooltip("是否可被怪物索敌（关掉 = 怪物忽略这个玩家，玩家仍在场）")]
    public bool targetable = true;

    [Tooltip("是否要求对象处于 active + 组件 enabled 才可被索敌。" +
             "默认关：本项目里「隐藏 / 禁用对象」是常用的隔离手段（隐藏旧相机、旧角色），" +
             "隐藏玩家不应该让怪物直接待机。打开则恢复严格语义（隐藏 = 不可索敌）")]
    public bool requireActive = false;

    [Tooltip("禁用 / 隐藏时是否从索敌登记表注销。默认关（配合 requireActive=false：隐藏后仍能被索敌）；" +
             "打开则隐藏 = 彻底退出索敌表")]
    public bool unregisterWhenDisabled = false;

    [Header("瞄准点")]
    [Tooltip("瞄准点（留空 = 使用自身 transform）")]
    public Transform aimPoint;
    [Tooltip("瞄准点世界 Y 偏移（模型中心/胸口高度，如 1 表示身高 2 的角色取胸口）")]
    public float aimHeight = 1f;

    [Header("耐久（可选）")]
    [Tooltip("玩家生命值（留空自动在自身与子物体上找；找不到 = 玩家不可被伤害，怪物攻击无效但仇恨逻辑照常）")]
    public Health health;
    [Tooltip("玩家生命值归零 / 组件禁用时自动退出索敌列表")]
    public bool untargetableWhenDead = true;

    /// <summary>
    /// 当前是否可被索敌：开关 + 存活 +（可选）激活状态。
    /// 默认只看「targetable 开关 + 未死亡」—— 对象被隐藏 / 组件被禁用仍算在场，
    /// 与项目里「用隐藏 / 禁用隔离」的既有做法一致（要严格语义就把 requireActive 打开）。
    /// </summary>
    public bool IsTargetable
    {
        get
        {
            if (!targetable) return false;
            if (requireActive && !isActiveAndEnabled) return false;
            if (untargetableWhenDead && health != null && health.IsDead) return false;
            return true;
        }
    }

    /// <summary>是否已登记在注册表里。</summary>
    public bool IsRegistered { get { return PlayerTargetRegistry.All.Contains(this); } }

    /// <summary>瞄准点 transform（aimPoint 为空则自身）。</summary>
    public Transform AimTransform { get { return aimPoint != null ? aimPoint : transform; } }

    /// <summary>怪物索敌 / 攻击瞄准的世界坐标。</summary>
    public Vector3 AimWorld { get { return AimTransform.position + Vector3.up * aimHeight; } }

    /// <summary>玩家脚下所在位置（世界坐标，取 AimTransform 的投影，寻路目标格用）。</summary>
    public Vector3 FootWorld { get { return AimTransform.position; } }

    void Awake()
    {
        EnsureHealth();
    }

    void OnEnable()
    {
        EnsureHealth();
        PlayerTargetRegistry.Register(this);
    }

    /// <summary>
    /// 禁用 / 隐藏时：默认**保留**索敌登记（隐藏 ≠ 离场，见 requireActive 注释）；
    /// 只有 unregisterWhenDisabled = true 才注销。
    /// </summary>
    void OnDisable()
    {
        if (unregisterWhenDisabled) PlayerTargetRegistry.Unregister(this);
    }

    void OnDestroy()
    {
        PlayerTargetRegistry.Unregister(this);
    }

    void EnsureHealth()
    {
        if (health == null) health = GetComponentInChildren<Health>(true);
    }

    /// <summary>显式登记（EditMode 断言 / 手工装配用；OnEnable 会自动登记，重复调用无副作用）。</summary>
    public void ManualRegister()
    {
        PlayerTargetRegistry.Register(this);
    }

    /// <summary>显式注销。</summary>
    public void ManualUnregister()
    {
        PlayerTargetRegistry.Unregister(this);
    }
}
