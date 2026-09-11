using System;
using UnityEngine;

/// <summary>
/// 通用生命值组件（通用层，零游戏类型依赖）。
///
/// 能力：当前/上限血量、伤害/治疗、归零死亡事件，以及血量变化事件供 UI 订阅。
/// 任意"可被伤害 / 可死亡"的对象挂载即可（植物、僵尸、建筑、玩家……）。
///
/// 约定：
///  - Damage 不会把血量打到负数；返回**实际扣除量**（便于伤害数字 / 吸血 / 护盾等扩展）。
///  - Died 事件只触发一次（首次归零）；死亡后再受伤不再扣血、不再重复触发事件。
///  - 事件为普通 C# 事件（非 UnityEvent），订阅方请在 OnDestroy/OnDisable 里自行退订。
///
/// 装配：与任何游戏逻辑无关，可跨项目复用；本文件不引用任何游戏侧类型。
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour
{
    [Header("血量")]
    [Tooltip("最大生命值；运行时可 SetMax 修改")]
    [SerializeField] int maxHealth = 100;

    [Tooltip("当前生命值（只读；用 Damage / Heal / ResetHealth 修改）")]
    [SerializeField] int currentHealth = 100;

    [Tooltip("初始即为死亡状态（占位/未激活单位用）；一般保持 false")]
    [SerializeField] bool startDead = false;

    /// <summary>血量变化（伤害 / 治疗 / 重置 / 上限调整后触发）</summary>
    public event Action<Health> Changed;
    /// <summary>受伤：（自身, 实际伤害, 剩余血量）</summary>
    public event Action<Health, int, int> Damaged;
    /// <summary>
    /// 受伤（带来源信息）：(自身, 伤害信息, 实际扣除, 剩余血量)。
    /// 与 Damaged 同时触发；想拿「谁打的」做 AI 仇恨 / 归属统计的订阅这个。
    /// </summary>
    public event Action<Health, DamageInfo, int, int> DamagedBy;
    /// <summary>归零（只触发一次）</summary>
    public event Action<Health> Died;
    /// <summary>从死亡恢复（ResetHealth / SetMax(refill) / Heal 复活时触发）</summary>
    public event Action<Health> Revived;

    public int MaxHealth { get { return maxHealth; } }
    public int CurrentHealth { get { return currentHealth; } }
    public bool IsDead { get { return dead; } }
    public bool IsAlive { get { return !dead; } }

    /// <summary>血量比例 0~1（上限为 0 时返回 0）</summary>
    public float Normalized
    {
        get { return maxHealth > 0 ? Mathf.Clamp01((float)currentHealth / maxHealth) : 0f; }
    }

    bool dead;

    void Awake()
    {
        if (maxHealth < 0) maxHealth = 0;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
        if (currentHealth < 0) currentHealth = 0;
        dead = startDead || currentHealth <= 0;
    }

    /// <summary>
    /// 设置血量上限。refill=true（默认）= 直接满血并复活（初始化单位用）；
    /// refill=false = 只夹紧当前血量。
    /// </summary>
    public void SetMax(int max, bool refill = true)
    {
        maxHealth = Mathf.Max(0, max);
        if (refill)
        {
            currentHealth = maxHealth;
            SetDead(maxHealth <= 0);
        }
        else if (currentHealth > maxHealth)
        {
            currentHealth = maxHealth;
            if (currentHealth <= 0) SetDead(true);
        }
        RaiseChanged();
    }

    /// <summary>受到伤害。返回实际扣除量（0 = 未生效：伤害非正 / 已死亡）。</summary>
    public int Damage(int amount)
    {
        return ApplyDamage(amount, new DamageInfo(amount));
    }

    /// <summary>
    /// 受到伤害（带来源）。沿用 info.amount 作为伤害量，返回实际扣除量。
    /// 即使 source 为空也会照常触发 DamagedBy（订阅方可用 HasSource 判断）。
    /// </summary>
    public int Damage(DamageInfo info)
    {
        return ApplyDamage(info.amount, info);
    }

    int ApplyDamage(int amount, DamageInfo info)
    {
        if (amount <= 0 || dead || currentHealth <= 0)
        {
            return 0;
        }
        int applied = Mathf.Min(amount, currentHealth);
        currentHealth -= applied;
        RaiseChanged();
        if (Damaged != null) Damaged(this, applied, currentHealth);
        if (DamagedBy != null) DamagedBy(this, info, applied, currentHealth);
        if (currentHealth <= 0) SetDead(true);
        return applied;
    }

    /// <summary>治疗。返回实际恢复量（已死亡且 revive=false 时不生效）。</summary>
    public int Heal(int amount, bool revive = false)
    {
        if (amount <= 0) return 0;
        if (dead && !revive) return 0;
        int before = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        RaiseChanged();
        if (dead && currentHealth > 0) SetDead(false);
        return currentHealth - before;
    }

    /// <summary>直接击杀（调试 / 场景脚本用）。</summary>
    public void Kill()
    {
        Damage(currentHealth > 0 ? currentHealth : 1);
        if (!dead) SetDead(true);
    }

    /// <summary>满血复活。</summary>
    public void ResetHealth()
    {
        currentHealth = maxHealth;
        SetDead(maxHealth <= 0);
        RaiseChanged();
    }

    void SetDead(bool value)
    {
        if (dead == value) return;
        dead = value;
        if (dead)
        {
            if (Died != null) Died(this);
        }
        else
        {
            if (Revived != null) Revived(this);
        }
    }

    void RaiseChanged()
    {
        if (Changed != null) Changed(this);
    }
}
