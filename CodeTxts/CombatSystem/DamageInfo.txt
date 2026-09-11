using System;
using UnityEngine;

/// <summary>
/// 伤害信息（通用层，零游戏类型依赖）。
///
/// 用途：给「一次伤害」附带**来源**（谁打的）与命中点，供上层做
/// AI 仇恨切换、伤害归属统计、伤害数字、部位反馈等。
///
/// 兼容：`Health.Damage(int)` 仍照旧可用（内部会包装成只带伤害量的 DamageInfo，
/// source 为 null）。想要来源信息的调用方改用 `Health.Damage(DamageInfo)`。
///
/// 本文件不引用任何游戏侧类型（无 HexGrid / HexCell / Monster 等）。
/// </summary>
[Serializable]
public struct DamageInfo
{
    [Tooltip("伤害量（>0 才生效）")]
    public int amount;

    [Tooltip("伤害来源（攻击者）。可为空 = 未知来源（环境伤害 / 调试直接扣血）")]
    public GameObject source;

    [Tooltip("命中点（世界坐标；未知时为零向量）。伤害数字 / 特效定位用")]
    public Vector3 point;

    [Tooltip("伤害类型标签（自由字符串，如 melee / projectile / poison；可为空）")]
    public string kind;

    public DamageInfo(int amount, GameObject source = null,
                      Vector3 point = default(Vector3), string kind = null)
    {
        this.amount = amount;
        this.source = source;
        this.point = point;
        this.kind = kind;
    }

    /// <summary>是否有已知来源（攻击者）。</summary>
    public bool HasSource { get { return source != null; } }

    /// <summary>只有伤害量（无来源）。</summary>
    public static DamageInfo Simple(int amount)
    {
        return new DamageInfo(amount);
    }

    /// <summary>带来源的伤害。</summary>
    public static DamageInfo From(int amount, GameObject source,
                                  Vector3 point = default(Vector3), string kind = null)
    {
        return new DamageInfo(amount, source, point, kind);
    }

    public override string ToString()
    {
        return "DamageInfo(" + amount + (source != null ? " from " + source.name : "") +
               (string.IsNullOrEmpty(kind) ? "" : " [" + kind + "]") + ")";
    }
}
