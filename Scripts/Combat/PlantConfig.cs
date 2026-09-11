using UnityEngine;

/// <summary>
/// 可玩植物配置（ScriptableObject）——一张"卡"= 一个 PlantConfig。
///
/// 与视觉特征系统**完全解耦**：HexCell.PlantLevel / HexFeatureManager.plantPrefabs 仍是
/// 纯视觉装饰（概率摆放、随 chunk 重建销毁），本配置驱动的才是"可玩植物单位"（有血量、
/// 有攻击数值、随种植系统创建/销毁）。
///
/// 数值口径：
///  - 一格中心到相邻格中心 ≈ 8.66 世界单位（outerRadius=5），attackRange 按此换算。
///  - sunCost / cooldown 由 PlantingSystem 校验与计时。
///
/// 生成：Asset 菜单 Create ▸ HexMap ▸ 种植 ▸ 植物配置，或直接跑
/// 菜单 Tools/种植系统/一键装配 (PvZ 种植) 自动建占位配置。
/// </summary>
[CreateAssetMenu(fileName = "PlantConfig", menuName = "HexMap/种植/植物配置", order = 1)]
public class PlantConfig : ScriptableObject
{
    [Header("标识 / UI")]
    [Tooltip("卡片显示名（FGUI 选卡栏用）")]
    public string displayName = "植物";
    [Tooltip("卡片描述（可选）")]
    [TextArea(2, 4)] public string description;
    [Tooltip("卡片图标（可选，FGUI 用；不填不影响种植）")]
    public Sprite icon;

    [Header("种植成本")]
    [Tooltip("阳光消耗")]
    public int sunCost = 50;
    [Tooltip("该种植物的再种植冷却（秒），按 config 独立计时")]
    public float cooldown = 3f;

    [Header("单位")]
    [Tooltip("植物 prefab（pivot 建议在底部；脚本会按需剥掉自带碰撞体，避免抢走编辑/点击射线）")]
    public GameObject prefab;
    [Tooltip("种植后写入通用 Health 的血量上限")]
    public int maxHealth = 100;

    [Header("攻击数值（FEAT-004 战斗系统读取；本任务只承载数据）")]
    [Tooltip("单发伤害")]
    public int attackDamage = 10;
    [Tooltip("攻击间隔（秒）")]
    public float attackInterval = 1.4f;
    [Tooltip("射程（世界单位；相邻格中心距 ≈ 8.66）")]
    public float attackRange = 12f;
    [Tooltip("投射物速度（世界单位/秒）")]
    public float projectileSpeed = 40f;

    /// <summary>配置是否可用于种植（必须有 prefab）。</summary>
    public bool IsValid { get { return prefab != null; } }
}
