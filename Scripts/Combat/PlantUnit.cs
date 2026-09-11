using UnityEngine;

/// <summary>
/// 可玩植物单位（游戏侧）。由 PlantingSystem 实例化并注入 config / cell / system。
///
/// 职责（越小越好，战斗行为留给桥接组件）：
///  - 持有通用 Health（缺省自动补挂）并按 config.maxHealth 初始化；
///  - 死亡时通知 PlantingSystem 注销占用并清理实例；
///  - 把 config 数值同步给 PlantShooter（FEAT-004 接线点）。
///
/// 落位：Prefab 原点 = 格中心顶面（含海拔与垂直扰动），与角色/特征贴地口径一致。
/// </summary>
[DisallowMultipleComponent]
public class PlantUnit : MonoBehaviour
{
    [Tooltip("植物配置（由 PlantingSystem 注入）")]
    public PlantConfig config;
    [Tooltip("所在 HexCell（由 PlantingSystem 注入）")]
    public HexCell cell;
    [Tooltip("通用生命组件（prefab 上无则运行时自动补挂）")]
    public Health health;
    [Tooltip("中心取样高度（供 FEAT-004 索敌/命中判定取点，prefab 约 1 高的位置）")]
    public float centerHeight = 1f;

    PlantingSystem system;
    bool registered;

    public bool IsAlive { get { return health == null || health.IsAlive; } }
    public HexCell Cell { get { return cell; } }
    /// <summary>所在格中心顶面世界坐标（= 植物落位点）。</summary>
    public Vector3 CellTopWorld { get { return cell != null ? cell.transform.position : transform.position; } }
    /// <summary>植物中心世界坐标（索敌/命中取样点）。</summary>
    public Vector3 CenterWorld { get { return transform.position + Vector3.up * centerHeight; } }

    /// <summary>由 PlantingSystem 在实例化后立即调用。</summary>
    public void Initialize(PlantConfig config, HexCell cell, PlantingSystem system)
    {
        this.config = config;
        this.cell = cell;
        this.system = system;

        if (health == null) health = GetComponent<Health>();
        if (health == null) health = gameObject.AddComponent<Health>();
        if (config != null) health.SetMax(config.maxHealth, true);
        health.Died += HandleDied;

        PlantShooter shooter = GetComponent<PlantShooter>();
        if (shooter != null) shooter.ConfigureFrom(config);

        registered = true;
    }

    void HandleDied(Health h)
    {
        if (system != null) system.NotifyPlantDied(this);
    }

    void OnDestroy()
    {
        if (health != null) health.Died -= HandleDied;
        // 兜底：被外部直接 Destroy 时也要注销占用（死亡路径已注销则为空操作）
        if (registered && system != null) system.NotifyPlantRemoved(this, false);
        registered = false;
    }
}
