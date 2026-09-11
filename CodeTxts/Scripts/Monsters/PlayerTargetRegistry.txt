using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家目标注册表（静态，游戏侧）。
///
/// 定位：怪物 AI 的「玩家是谁」抽象层。怪物不关心玩家是本地 Hero、联机远端化身
/// 还是未来的 NPC 队友 —— 只认本组件。挂上 <see cref="PlayerTarget"/> 即可被索敌。
///
/// 设计：静态注册表 + [RuntimeInitializeOnLoadMethod] 复位（进 Play / 重载域都会清空，
/// 不会把上一次运行的引用带到下一次）。
///
/// 多玩家：天然支持 0..N 个目标；`FindNearest` 返回世界距离最近的一个 ——
/// 单机只有一个玩家时行为退化为「永远锁定唯一玩家」。
/// </summary>
public static class PlayerTargetRegistry
{
    static readonly List<PlayerTarget> targets = new List<PlayerTarget>();
    static readonly List<PlayerTarget> pruneBuffer = new List<PlayerTarget>();

    /// <summary>当前登记的目标数量（含已失效、未清理的条目；精确值用 CountValid）。</summary>
    public static int Count { get { return targets.Count; } }

    /// <summary>登记在册且当前**可索敌**的目标数量（已销毁 / 死亡 / targetable=false 的不计）。</summary>
    public static int CountValid
    {
        get
        {
            Prune();
            int n = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].IsTargetable) n++;
            }
            return n;
        }
    }

    /// <summary>只读访问（调用方不要修改返回的列表）。</summary>
    public static IList<PlayerTarget> All { get { return targets; } }

    public static void Register(PlayerTarget target)
    {
        if (target == null || targets.Contains(target)) return;
        targets.Add(target);
    }

    public static void Unregister(PlayerTarget target)
    {
        if (target == null) return;
        targets.Remove(target);
    }

    public static void Clear()
    {
        targets.Clear();
    }

    /// <summary>清掉被销毁（Unity 伪 null）的条目。</summary>
    public static void Prune()
    {
        if (targets.Count == 0) return;
        pruneBuffer.Clear();
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null) pruneBuffer.Add(targets[i]);
        }
        for (int i = 0; i < pruneBuffer.Count; i++)
        {
            targets.Remove(pruneBuffer[i]);
        }
        pruneBuffer.Clear();
    }

    /// <summary>
    /// 找离 from 最近的**可索敌**玩家。maxRange > 0 时超出范围返回 null；
    /// maxRange &lt;= 0 表示不限距离。
    /// </summary>
    public static PlayerTarget FindNearest(Vector3 from, float maxRange = -1f)
    {
        Prune();
        PlayerTarget best = null;
        float bestSqr = maxRange > 0f ? maxRange * maxRange : float.PositiveInfinity;

        for (int i = 0; i < targets.Count; i++)
        {
            PlayerTarget t = targets[i];
            if (t == null || !t.IsTargetable) continue;
            float sqr = (t.AimWorld - from).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = t;
            }
        }
        return best;
    }

    /// <summary>
    /// 由任意 GameObject（攻击者本体或其任意子物体）反查它属于哪个玩家目标。
    /// 找不到返回 null —— 符合「未知来源不拉仇恨」的语义。
    /// </summary>
    public static PlayerTarget FindByObject(GameObject go)
    {
        if (go == null) return null;
        PlayerTarget t = go.GetComponentInParent<PlayerTarget>();
        if (t == null) return null;
        return t.IsTargetable ? t : null;
    }

    /// <summary>把当前可索敌的目标写入 result（复用列表避免 GC），返回写入条数。</summary>
    public static int GetTargets(List<PlayerTarget> result)
    {
        if (result == null) return 0;
        result.Clear();
        Prune();
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null && targets[i].IsTargetable) result.Add(targets[i]);
        }
        return result.Count;
    }

    /// <summary>进 Play / 域重载时复位，避免静态状态跨运行残留。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        targets.Clear();
        pruneBuffer.Clear();
    }
}
