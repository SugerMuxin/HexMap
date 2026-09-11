using UnityEngine;

/// <summary>
/// 植物射击器（游戏侧桥接，**占位实现**）。
///
/// 本任务（FEAT-003）只做两件事：
///  1. 从 PlantConfig 同步战斗数值（伤害 / 射程 / 攻速 / 弹速）——由 PlantUnit.Initialize 调用；
///  2. 留好接线点：muzzle 枪口、shootingEnabled 总开关、TryFire() 发射入口。
///
/// FEAT-004（战斗系统）在此接入：用通用 TargetScanner 找射程内最近敌人 →
/// 在 muzzle 处生成通用 Projectile → 命中调用目标 Health.Damage(damage)。
/// 在此之前 shootingEnabled 保持 false，Update 不做任何事（不影响现有系统与种植验收）。
/// </summary>
public class PlantShooter : MonoBehaviour
{
    [Tooltip("植物配置（PlantUnit.Initialize 注入）")]
    public PlantConfig config;

    [Header("战斗数值（ConfigureFrom 从 config 同步；config 为空时用这里的默认值）")]
    public int damage = 10;
    [Tooltip("射程（世界单位；相邻格中心距 ≈ 8.66）")]
    public float range = 12f;
    [Tooltip("攻击间隔（秒）")]
    public float interval = 1.4f;
    [Tooltip("投射物速度（世界单位/秒）")]
    public float projectileSpeed = 40f;

    [Header("接线点（FEAT-004 使用）")]
    [Tooltip("枪口（留空则用自身位置上方 1 单位）")]
    public Transform muzzle;
    [Tooltip("射击总开关：FEAT-004 接好索敌 + 投射物后置 true")]
    public bool shootingEnabled = false;

    float nextFireTime;

    /// <summary>配置是否可射击（占位阶段恒为"未就绪"，FEAT-004 打开 shootingEnabled 后生效）。</summary>
    public bool IsReady { get { return shootingEnabled && config != null && interval > 0f; } }

    /// <summary>枪口世界坐标（投射物出生点）。</summary>
    public Vector3 MuzzleWorld
    {
        get { return muzzle != null ? muzzle.position : transform.position + Vector3.up * 1f; }
    }

    /// <summary>由 PlantUnit 在实例化时同步 config 数值。</summary>
    public void ConfigureFrom(PlantConfig cfg)
    {
        if (cfg == null) return;
        config = cfg;
        damage = cfg.attackDamage;
        range = cfg.attackRange;
        interval = cfg.attackInterval;
        projectileSpeed = cfg.projectileSpeed;
    }

    /// <summary>冷却是否已到（FEAT-004 的索敌逻辑用）。</summary>
    public bool IsFireTimeReady
    {
        get { return Time.time >= nextFireTime; }
    }

    /// <summary>标记本次发射并推进冷却（FEAT-004 发射成功后调用）。</summary>
    public void NotifyFired()
    {
        nextFireTime = Time.time + Mathf.Max(0.01f, interval);
    }

    void Update()
    {
        if (!IsReady) return;
        // FEAT-004 接线点：if (IsFireTimeReady) { 索敌(TargetScanner) → 生成 Projectile → NotifyFired(); }
    }
}
