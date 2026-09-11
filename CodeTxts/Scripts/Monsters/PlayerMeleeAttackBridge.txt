using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家近战攻击桥接（游戏侧）：把「玩家挥了一刀」翻译成「怪物掉血 + 被激怒」。
///
/// 为什么需要它：现有 <see cref="EllenAttack"/> 只播动画并广播命中窗口事件
/// （`MeleeAttackWindowStart` / `MeleeAttackWindowEnd` / `AttackTriggered`），
/// **没有任何伤害判定**。本组件是**新增的独立组件**，只订阅那些事件，
/// 一行都不改 EllenAttack —— 符合项目「新功能用独立新组件」的约定。
///
/// 链路：EllenAttack 命中窗口 → OverlapSphere 取范围/扇形内的怪物
///       → `Health.Damage(DamageInfo{source = 玩家})`（扣血 + 广播来源）
///       → <see cref="MonsterUnit.NotifyAttacked"/>（**更近的玩家攻击它 → 换锁**，需求核心）。
///
/// 命中判定用「组件过滤」而非 Layer / Tag：怪物 prefab 自带 Collider 保留不动，
/// 断言/装配都不必改 TagManager（无 Layer / Tag 依赖）。
/// </summary>
[DisallowMultipleComponent]
public class PlayerMeleeAttackBridge : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("攻击输入组件（留空自动在自身 / 子物体 / 父物体上找 EllenAttack）")]
    public EllenAttack attack;
    [Tooltip("攻击者身份（留空自动取自身的 PlayerTarget；再退化为本物体）——怪物用它判断「谁打我」")]
    public PlayerTarget owner;

    [Header("判定")]
    [Tooltip("单次攻击伤害")]
    public int damage = 25;
    [Tooltip("命中半径（世界单位；相邻格中心距约 8.66）")]
    public float radius = 7f;
    [Tooltip("扇形限制：-1 = 360° 全向；0.5 ≈ 正前方 ±60°；1 = 只打正前方")]
    public float minDot = -1f;
    [Tooltip("命中高度中心偏移（相对自身位置，取怪物胸口高度用）")]
    public float centerHeight = 1f;
    [Tooltip("伤害类型标签（DamageInfo.kind）")]
    public string damageKind = "player-melee";
    [Tooltip("用命中窗口事件（MeleeAttackWindowStart）触发判定；关掉则按键瞬间（AttackTriggered）就判定")]
    public bool useHitWindowEvent = true;
    [Tooltip("打印命中日志")]
    public bool logHits = false;

    /// <summary>命中一只怪物：(本组件, 怪物, 实际伤害)。</summary>
    public event Action<PlayerMeleeAttackBridge, MonsterUnit, int> HitMonster;
    /// <summary>一次攻击结束：(本组件, 命中数量)。</summary>
    public event Action<PlayerMeleeAttackBridge, int> AttackPerformed;

    [Header("统计（只读）")]
    [Tooltip("已执行攻击次数")]
    public int attackCount;
    [Tooltip("已命中怪物次数（含重复命中同一只）")]
    public int hitCount;

    readonly Collider[] overlapBuffer = new Collider[64];
    readonly List<MonsterUnit> hitBuffer = new List<MonsterUnit>();

    /// <summary>攻击者 GameObject（写进 DamageInfo.source，供怪物反查是谁打的）。</summary>
    public GameObject AttackerObject
    {
        get { return owner != null ? owner.gameObject : gameObject; }
    }

    void OnEnable()
    {
        EnsureRefs();
        Subscribe(true);
    }

    void OnDisable()
    {
        Subscribe(false);
    }

    void EnsureRefs()
    {
        if (owner == null) owner = GetComponent<PlayerTarget>();
        if (owner == null) owner = GetComponentInParent<PlayerTarget>();
        if (owner == null) owner = GetComponentInChildren<PlayerTarget>(true);

        if (attack == null) attack = GetComponent<EllenAttack>();
        if (attack == null) attack = GetComponentInChildren<EllenAttack>(true);
        if (attack == null) attack = GetComponentInParent<EllenAttack>();
    }

    void Subscribe(bool on)
    {
        if (attack == null) return;
        if (on)
        {
            if (useHitWindowEvent) attack.MeleeAttackWindowStart += HandleHitWindowStart;
            else attack.AttackTriggered += HandleAttackTriggered;
        }
        else
        {
            attack.MeleeAttackWindowStart -= HandleHitWindowStart;
            attack.AttackTriggered -= HandleAttackTriggered;
        }
    }

    void HandleHitWindowStart()
    {
        PerformAttack();
    }

    void HandleAttackTriggered(string trigger)
    {
        PerformAttack();
    }

    /// <summary>
    /// 执行一次攻击判定（公开：断言脚本 / 手工触发 / 无 EllenAttack 的运行时）。
    /// 返回本次命中的**不同**怪物数量。
    /// </summary>
    public int PerformAttack()
    {
        attackCount++;
        hitBuffer.Clear();

        Vector3 center = transform.position + Vector3.up * centerHeight;
        GameObject attacker = AttackerObject;

        int count = Physics.OverlapSphereNonAlloc(center, Mathf.Max(0.1f, radius), overlapBuffer,
            ~0, QueryTriggerInteraction.Collide);
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null) continue;
            MonsterUnit monster = col.GetComponentInParent<MonsterUnit>();
            if (monster == null || !monster.IsAlive) continue;
            if (hitBuffer.Contains(monster)) continue;

            if (minDot > -1f)
            {
                Vector3 dir = monster.transform.position - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f && Vector3.Dot(dir.normalized, forward) < minDot) continue;
            }

            hitBuffer.Add(monster);
            Vector3 point = monster.transform.position + Vector3.up * centerHeight;
            int applied = 0;
            if (monster.health != null)
            {
                applied = monster.health.Damage(DamageInfo.From(damage, attacker, point, damageKind));
            }
            // 无伤害也要拉仇恨（例如伤害被免疫 / 血量组件缺失），保证「被攻击就换锁」语义完整
            monster.NotifyAttacked(attacker);

            hitCount++;
            if (logHits)
            {
                Debug.Log("[PlayerAttack] 命中 " + monster.name + "（伤害 " + applied +
                          "，剩余 " + (monster.health != null ? monster.health.CurrentHealth : 0) + "）");
            }
            if (HitMonster != null) HitMonster(this, monster, applied);
        }

        int hits = hitBuffer.Count;
        hitBuffer.Clear();
        if (AttackPerformed != null) AttackPerformed(this, hits);
        return hits;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * centerHeight, radius);
    }
#endif
}
