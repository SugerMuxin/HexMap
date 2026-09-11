using UnityEngine;

/// <summary>
/// 怪物锁敌策略。
/// </summary>
public enum MonsterLockMode
{
    /// <summary>
    /// 首选最近目标 + 被更近的玩家攻击时换锁（默认，符合需求原话）：
    /// 出生后锁定「当时最近的玩家」，此后不再主动改锁；
    /// 只有①有距离它更近的玩家攻击它，或②当前目标死亡/消失，才重新选目标。
    /// </summary>
    FirstUntilAttacked = 0,

    /// <summary>
    /// 永远跟最近：每隔 retargetInterval 秒重算一次最近玩家并改锁（谁近打谁）。
    /// 受击换锁规则依然生效（多数情况下已是最近，等同于无变化）。
    /// </summary>
    NearestAlways = 1
}

/// <summary>
/// 怪物配置（ScriptableObject，游戏侧，一行 = 一种怪物）。
///
/// 数据来源：`Assets/Resources/Configs/Tables/MonsterTable.csv` 经 Editor/TableImporter 导入
/// （按 id 稳定 upsert 到 `Assets/Resources/Configs/Monsters/MonsterConfig_{id}.asset`，
/// 已存在资产保留 GUID / 文件名 / 用户额外改动，只覆盖表管字段）。
///
/// 与既有系统的关系：怪物**只读** HexGrid / HexCell（寻路与地形判定），不修改它们；
/// 视觉特征（MonsterLairLevel / HexFeatureManager.monsterLairPrefabs）保持纯装饰语义。
/// </summary>
[CreateAssetMenu(fileName = "MonsterConfig", menuName = "HexMap/怪物/怪物配置", order = 20)]
public class MonsterConfig : ScriptableObject
{
    [Header("身份")]
    [Tooltip("显示名（Unity 中没有则为资产名）")]
    public string displayName = "怪物";
    [TextArea(1, 3)]
    public string description;
    [Tooltip("图标（UI 用，可空）")]
    public Sprite icon;

    [Tooltip("怪物 prefab（缺失 = 配置无效，无法刷怪）")]
    public GameObject prefab;

    [Header("生存")]
    [Tooltip("最大生命值（由通用 CombatSystem.Health 承载）")]
    public int maxHealth = 100;

    [Header("移动")]
    [Tooltip("移动速度：每秒走过多少格（写进 HexUnit.travelSpeed；1 = 每秒一格）")]
    public float moveSpeed = 4f;
    [Tooltip("转向速度（度/秒）")]
    public float turnSpeed = 720f;
    [Tooltip("落位 y 偏移（贴地微调，一般 0）")]
    public float yOffset = 0f;

    [Header("索敌")]
    [Tooltip("锁敌策略：FirstUntilAttacked = 锁最近 + 被更近的玩家攻击才换锁（默认）；NearestAlways = 永远跟最近")]
    public MonsterLockMode lockMode = MonsterLockMode.FirstUntilAttacked;
    [Tooltip("索敌范围（世界单位；<=0 = 不限距离，全图找最近的玩家）")]
    public float detectRange = 0f;
    [Tooltip("NearestAlways 模式下重算最近目标的间隔（秒）")]
    public float retargetInterval = 0.5f;
    [Tooltip("被攻击时只在「攻击者比当前目标更近」才换锁（关掉 = 谁打我我就打谁，最后攻击者优先）")]
    public bool switchOnlyWhenCloser = true;
    [Tooltip("被攻击时要求攻击者在索敌范围内才换锁（detectRange<=0 时本项无效）")]
    public bool switchOnlyWithinDetectRange = false;

    [Header("追击")]
    [Tooltip("重算路径的间隔（秒）。目标换格 / 卡住时立即重算，不必等这个间隔")]
    public float repathInterval = 0.5f;
    [Tooltip("到达判定距离（世界单位；用于「原地不动」的判定）")]
    public float arriveDistance = 1f;
    [Tooltip("相邻格中心距约 8.66 —— 攻击距离要跨越一格时至少给到 9")]
    public float attackRange = 6f;

    [Header("攻击")]
    [Tooltip("是否攻击玩家（默认关：怪物只追不说；打开后按 attackDamage 扣玩家 Health）")]
    public bool attackPlayers = false;
    [Tooltip("对玩家的伤害（attackPlayers 打开时生效）")]
    public int attackDamage = 10;
    [Tooltip("攻击间隔（秒）")]
    public float attackInterval = 1.5f;

    [Header("外观 / 清理")]
    [Tooltip("剥掉 prefab 自带碰撞体（默认关：保留碰撞体，玩家的攻击判定要靠它命中）")]
    public bool stripColliders = false;
    [Tooltip("死亡后尸体保留秒数（0 = 立即销毁）")]
    public float corpseLifetime = 1.5f;

    /// <summary>配置是否可用于刷怪（prefab 已接线）。</summary>
    public bool IsValid { get { return prefab != null; } }

    /// <summary>显示名（空则退化为资产名）。</summary>
    public string DisplayName
    {
        get { return string.IsNullOrEmpty(displayName) ? name : displayName; }
    }

    /// <summary>实际使用的停止距离（攻击距离优先，兜底到达距离）。</summary>
    public float StopDistance
    {
        get { return attackRange > 0f ? attackRange : Mathf.Max(0.5f, arriveDistance); }
    }
}
