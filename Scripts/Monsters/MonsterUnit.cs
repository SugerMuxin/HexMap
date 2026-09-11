using System;
using UnityEngine;

/// <summary>
/// 怪物单位（游戏侧 AI 控制器）：**「找离它最近的玩家 → 沿六边形路径追击 → 被更近的玩家攻击就换锁」**。
///
/// 组成（不重复造轮子）：
///   - 移动：复用 FEAT-001 的 <see cref="HexUnit"/>（A* 寻路 + 沿曲线实时移动 + 逐格/到达事件）；
///   - 血量：复用通用层 <see cref="Health"/>（死亡自动通知刷怪器）；
///   - 目标：<see cref="PlayerTarget"/> / <see cref="PlayerTargetRegistry"/>（这一层让 AI 不认识 Hero/Ellen）。
///   本类只做决策，不做移动实现。
///
/// 锁敌规则（默认 <see cref="MonsterLockMode.FirstUntilAttacked"/>，即需求原话）：
///   1. 出生/失去目标时 → 锁定**当时最近的玩家**（detectRange 内；<=0 表示全图）；
///   2. 之后不主动改锁，除非
///        a) 有**距离它更近**的玩家攻击它（<see cref="NotifyAttacked"/>，由受伤事件自动触发），或
///        b) 当前目标死亡 / 不可索敌 / 被销毁 → 重新选最近的玩家。
///   切到 <see cref="MonsterLockMode.NearestAlways"/> 则改为周期性「谁近打谁」，受击换锁规则依旧生效。
///
/// 与现有系统的关系：只读 HexGrid / HexCell（寻路、地形通行判定）；不修改任何既有系统
/// （HeroController / EllenAttack / HexMapEditor / HexGrid / HexCell 均未改动）。
/// 玩家攻击怪物由独立组件 <see cref="PlayerMeleeAttackBridge"/> 桥接，本类只暴露 NotifyAttacked。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HexUnit))]
[RequireComponent(typeof(Health))]
public class MonsterUnit : MonoBehaviour
{
    [Header("配置（Initialize 注入）")]
    [Tooltip("怪物配置（数值 / prefab 由刷怪器注入）")]
    public MonsterConfig config;

    [Header("引用")]
    [Tooltip("地图（留空自动查找场景里的 HexGrid）")]
    public HexGrid hexGrid;
    [Tooltip("移动组件（留空自动取；不存在则自动补挂）")]
    public HexUnit hexUnit;
    [Tooltip("生命组件（留空自动取；不存在则自动补挂）")]
    public Health health;

    [Header("运行状态（只读）")]
    [Tooltip("当前锁定目标（null = 场上没有可索敌的玩家）")]
    public PlayerTarget currentTarget;

    /// <summary>目标变化：(怪物, 旧目标, 新目标)。</summary>
    public event Action<MonsterUnit, PlayerTarget, PlayerTarget> TargetChanged;
    /// <summary>死亡（Health 归零，只触发一次）。</summary>
    public event Action<MonsterUnit> Died;

    MonsterSpawner spawner;
    float nextRetargetTime;
    float nextRepathTime;
    float nextAttackTime;
    HexCell lastRequestedCell;
    bool noPathWarned;

    /// <summary>本怪物由场景配置表的第几行刷出（-1 = 非刷怪器创建；刷怪器做「等本波清完」判定用）。</summary>
    [NonSerialized] public int sourceRowIndex = -1;

    // ================= 只读状态 =================

    public bool IsAlive { get { return health == null || health.IsAlive; } }
    public MonsterConfig Config { get { return config; } }
    public MonsterSpawner Spawner { get { return spawner; } }
    /// <summary>当前所在格（可能为 null：地图未生成 / 越界）。与 PlantUnit.Cell 同口径。</summary>
    public HexCell Cell { get { return hexUnit != null ? hexUnit.CurrentCell() : null; } }
    /// <summary>是否正在移动。</summary>
    public bool IsMoving { get { return hexUnit != null && hexUnit.isTraveling; } }
    /// <summary>当前目标的世界瞄准点距离（无目标返回 -1）。</summary>
    public float DistanceToTarget
    {
        get { return currentTarget != null ? Vector3.Distance(transform.position, currentTarget.AimWorld) : -1f; }
    }

    void Awake()
    {
        EnsureRefs();
    }

    void OnEnable()
    {
        EnsureRefs();
    }

    // ================= 初始化 / 落位 =================

    /// <summary>
    /// 由刷怪器在实例化后立即调用：注入配置与出生格、初始化血量与移动参数。
    /// </summary>
    public void Initialize(MonsterConfig cfg, HexCell spawnCell, MonsterSpawner owner)
    {
        config = cfg;
        spawner = owner;
        EnsureRefs();

        if (config != null)
        {
            if (config.maxHealth > 0 && health != null) health.SetMax(config.maxHealth, true);
            if (hexUnit != null)
            {
                hexUnit.travelSpeed = Mathf.Max(0.05f, config.moveSpeed);
                hexUnit.turnSpeed = config.turnSpeed > 0f ? config.turnSpeed : hexUnit.turnSpeed;
                hexUnit.yOffset = config.yOffset;
            }
            if (config.stripColliders) StripColliders();
        }

        if (health != null)
        {
            health.Died -= HandleDied;
            health.Died += HandleDied;
            health.DamagedBy -= HandleDamagedBy;
            health.DamagedBy += HandleDamagedBy;
        }

        PlaceAt(spawnCell);
        // 出生即锁最近的玩家（需求：怪物寻找离它最近的玩家）
        AcquireNearestTarget(true);
    }

    /// <summary>把怪物放到指定格（含贴地），并同步移动组件的逻辑位置（避免 TravelTo 直接传送）。</summary>
    public void PlaceAt(HexCell cell)
    {
        if (cell == null) return;
        transform.position = cell.transform.position + Vector3.up * (config != null ? config.yOffset : 0f);
        if (hexUnit != null) hexUnit.WarpTo(cell);
        lastRequestedCell = null;
    }

    void StripColliders()
    {
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
    }

    void EnsureRefs()
    {
        if (hexUnit == null) hexUnit = GetComponent<HexUnit>();
        if (hexUnit == null) hexUnit = gameObject.AddComponent<HexUnit>();
        if (health == null) health = GetComponent<Health>();
        if (health == null) health = gameObject.AddComponent<Health>();
        if (hexGrid == null && hexUnit != null) hexGrid = hexUnit.hexGrid;
        if (hexGrid == null) hexGrid = FindObjectOfType<HexGrid>();
        if (hexUnit != null && hexUnit.hexGrid == null) hexUnit.hexGrid = hexGrid;
    }

    // ================= 主循环 =================

    void Update()
    {
        // EditMode（编辑器非运行态）不跑 AI：断言脚本用公开的 Tick(now) 手动驱动。
        if (!Application.isPlaying) return;
        if (config == null || !IsAlive) return;
        Tick(Time.time);
    }

    /// <summary>
    /// 单次 AI 决策（公开给断言脚本与外部驱动；Update 在 Play 模式每帧调用它）。
    /// 幂等、无副作用依赖：内部只做「选目标 / 寻路 / 攻击」三类动作。
    /// </summary>
    public void Tick(float now)
    {
        if (config == null || !IsAlive) return;
        EnsureRefs();

        // ---- 1. 目标有效性 ----
        if (currentTarget == null || !currentTarget.IsTargetable)
        {
            // 目标死亡 / 被销毁 / 不可索敌 → 重新选最近的玩家
            AcquireNearestTarget(false);
            if (currentTarget == null || !currentTarget.IsTargetable)
            {
                currentTarget = null;
                StopMoving();   // 场上没有可索敌的玩家：原地待机
                return;
            }
        }
        else if (config.lockMode == MonsterLockMode.NearestAlways && now >= nextRetargetTime)
        {
            nextRetargetTime = now + Mathf.Max(0.05f, config.retargetInterval);
            PlayerTarget nearest = PlayerTargetRegistry.FindNearest(transform.position, config.detectRange);
            if (nearest != null && nearest != currentTarget && nearest.IsTargetable)
            {
                SetTarget(nearest, true);
            }
        }

        if (currentTarget == null) return;

        // ---- 2. 距离判定：到攻击距离就停下 ----
        Vector3 aim = currentTarget.AimWorld;
        float distance = Vector3.Distance(transform.position, aim);
        float stopDistance = config.StopDistance;

        if (distance <= stopDistance)
        {
            StopMoving();
            FaceTowards(aim);
            if (config.attackPlayers && now >= nextAttackTime)
            {
                nextAttackTime = now + Mathf.Max(0.05f, config.attackInterval);
                Attack(currentTarget);
            }
            return;
        }

        // ---- 3. 追击：目标换格立即重算，否则按 repathInterval 节流 ----
        HexCell targetCell = ResolveTargetCell(currentTarget);
        bool cellChanged = targetCell != null && targetCell != lastRequestedCell;
        if (cellChanged || now >= nextRepathTime)
        {
            Chase(targetCell, now);
        }
        else if (!IsMoving)
        {
            // 到达了上一次的目标格却仍离得远（目标挪窝了）：立刻重算
            Chase(targetCell, now);
        }
    }

    // ================= 目标选择 / 换锁 =================

    /// <summary>锁定离自己最近的玩家。返回 true 表示目标发生了变化。</summary>
    public bool AcquireNearestTarget(bool allowSame = false)
    {
        PlayerTarget nearest = PlayerTargetRegistry.FindNearest(transform.position, config != null ? config.detectRange : 0f);
        if (nearest == null) return false;
        if (nearest == currentTarget && !allowSame && currentTarget.IsTargetable) return false;
        return SetTarget(nearest, true);
    }

    /// <summary>强制/条件设置当前目标。force=false 且与当前相同则返回 false。</summary>
    public bool SetTarget(PlayerTarget target, bool force = false)
    {
        if (target != null && !target.IsTargetable) return false;
        if (!force && target == currentTarget) return false;

        PlayerTarget prev = currentTarget;
        currentTarget = target;
        lastRequestedCell = null;
        noPathWarned = false;                                   // 换了目标 → 允许重新告警一次
        nextRepathTime = -1f;                                   // 下次 Tick 立即重算路径
        nextRetargetTime = Time.time + (config != null ? Mathf.Max(0.05f, config.retargetInterval) : 0.5f);

        if (TargetChanged != null) TargetChanged(this, prev, target);
        return true;
    }

    public void ClearTarget()
    {
        SetTarget(null, true);
    }

    /// <summary>
    /// 通知「本怪物被 attacker 攻击了」——需求核心：**有距离它更近的玩家攻击它时转换锁定目标**。
    /// 返回 true 表示发生了换锁。规则：
    ///   - attacker 不是可索敌玩家（无 PlayerTarget）→ 忽略，返回 false；
    ///   - 就是当前目标 → 不换（但会立刻重算路径，追得更紧）；
    ///   - switchOnlyWhenCloser（默认开）→ 只有 attacker 比当前目标更近才换；
    ///   - 当前无目标 → 直接锁 attacker。
    /// </summary>
    public bool NotifyAttacked(GameObject attacker)
    {
        if (attacker == null || !IsAlive) return false;
        PlayerTarget attackerTarget = PlayerTargetRegistry.FindByObject(attacker);
        if (attackerTarget == null) return false;

        if (attackerTarget == currentTarget)
        {
            nextRepathTime = -1f;    // 已在追它：立即重算一次路径
            return false;
        }

        float attackerDistance = Vector3.Distance(transform.position, attackerTarget.AimWorld);

        if (currentTarget != null)
        {
            if (config != null && config.switchOnlyWhenCloser)
            {
                float currentDistance = Vector3.Distance(transform.position, currentTarget.AimWorld);
                if (attackerDistance >= currentDistance) return false;   // 更远：不换
            }
            if (config != null && config.switchOnlyWithinDetectRange && config.detectRange > 0f &&
                attackerDistance > config.detectRange)
            {
                return false;
            }
        }

        return SetTarget(attackerTarget, true);
    }

    void HandleDamagedBy(Health h, DamageInfo info, int applied, int remaining)
    {
        // 有来源且有实际伤害 → 按「更近者优先」规则换锁（无伤害的标记攻击请直接调 NotifyAttacked）
        if (info.HasSource && applied > 0) NotifyAttacked(info.source);
    }

    // ================= 移动 / 攻击 =================

    /// <summary>解析「该往哪个格走」：目标脚下格；不可通行时退到最近可通行格。</summary>
    public HexCell ResolveTargetCell(PlayerTarget target)
    {
        if (target == null) return null;
        EnsureRefs();
        if (hexGrid == null) return null;

        HexCell cell = hexGrid.GetCellAtWorld(target.FootWorld);
        if (cell == null) return null;
        if (!hexGrid.IsWalkable(cell))
        {
            HexCell near = hexGrid.FindNearestWalkable(cell);
            if (near != null) cell = near;
        }
        return cell;
    }

    /// <summary>寻路前往指定格。targetCell 为空 / 无路时原地不动（返回 false）。</summary>
    public bool Chase(HexCell targetCell, float now)
    {
        EnsureRefs();
        nextRepathTime = now + Mathf.Max(0.05f, config != null ? config.repathInterval : 0.5f);
        if (targetCell == null || hexUnit == null) return false;

        lastRequestedCell = targetCell;
        HexCell from = hexUnit.CurrentCell();
        if (from == targetCell) return true;         // 已在目标格：交给距离判定处理
        bool ok = hexUnit.TravelTo(targetCell);
        if (!ok)
        {
            // 无路可走（目标被水/悬崖隔断、被围住、或被禁止通行）：留在原地，等下一次重算
            lastRequestedCell = null;
            WarnNoPath(targetCell);
        }
        else
        {
            noPathWarned = false;
        }
        return ok;
    }

    /// <summary>走不到目标时告警一次（同一个怪物只打一条），说清成因与处理办法。</summary>
    void WarnNoPath(HexCell targetCell)
    {
        if (noPathWarned) return;
        noPathWarned = true;

        HexCell self = hexUnit != null ? hexUnit.CurrentCell() : null;
        string extra = "";
        if (hexGrid != null && self != null && targetCell != null)
        {
            extra = "  寻路模式 pathMode=" + hexGrid.pathMode
                  + " blockUnderwater=" + hexGrid.blockUnderwater
                  + " blockCliffs=" + hexGrid.blockCliffs
                  + "；你的格=" + self.coordinates + "(海拔" + self.Elevation + ")"
                  + " 目标格=" + targetCell.coordinates + "(海拔" + targetCell.Elevation + ")";
        }
        string why = "";
        if (hexGrid != null && self != null && targetCell != null)
        {
            // 说清"到底为什么走不到"：高度不同 / 被悬崖挡 / 被水隔断（FEAT-001 的诊断 API）
            why = "  原因：" + hexGrid.DescribePathFailure(self, targetCell, hexUnit.EffectivePathMode()) + "\n";
        }
        Debug.LogWarning("[Monsters] " + name + " 找不到通往玩家（" + (targetCell != null ? targetCell.coordinates.ToString() : "?") +
                         "）的路径 → 原地待机。多半是**出生点与玩家之间被水 / 高度差隔断**（A* 无路径，不是追踪逻辑坏了）。\n" +
                         why +
                         "  处理办法：① 默认寻路是「仅同高度」——若怪物与玩家不在同一海拔层，把地图切到「允许坡道」" +
                         "（菜单 Tools/寻路系统/切换为「允许坡道」寻路，或给单位 overrideGridPathMode）；" +
                         "② 把怪物巢穴画在玩家同一层的连通陆地上；③ 填水 / 削掉 2 级以上的落差。\n" +
                         "  详情：菜单 Tools/怪物系统/诊断出生点可达性" + extra);
    }

    void StopMoving()
    {
        if (hexUnit != null && hexUnit.isTraveling) hexUnit.StopTravel(true);
    }

    /// <summary>对玩家造成一次伤害（attackPlayers 打开时由 Tick 调用；返回是否真的打到了）。</summary>
    public bool Attack(PlayerTarget target)
    {
        if (target == null) return false;
        float range = config != null ? config.attackRange : 0f;
        if (range > 0f && Vector3.Distance(transform.position, target.AimWorld) > range + 0.001f) return false;
        FaceTowards(target.AimWorld);
        if (target.health == null) return false;
        int damage = config != null ? config.attackDamage : 0;
        if (damage <= 0) return false;
        target.health.Damage(DamageInfo.From(damage, gameObject, target.AimWorld, "monster-melee"));
        return true;
    }

    /// <summary>平滑朝向世界点（只绕 Y）。</summary>
    public void FaceTowards(Vector3 worldPoint)
    {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        float turn = config != null && config.turnSpeed > 0f ? config.turnSpeed : 720f;
        Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turn * Time.deltaTime);
    }

    // ================= 死亡 =================

    void HandleDied(Health h)
    {
        StopMoving();
        currentTarget = null;
        if (Died != null) Died(this);
        if (spawner != null) spawner.NotifyMonsterDied(this);

        float life = config != null ? config.corpseLifetime : 0f;
        if (!Application.isPlaying) return;          // EditMode 不销毁（由调用方清理），避免 Unity 报 Destroy 错误
        if (life > 0f) Destroy(gameObject, life);
        else Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
            health.DamagedBy -= HandleDamagedBy;
        }
        // 兜底：被外部直接 Destroy 时也要从刷怪器的存活表里注销
        if (spawner != null) spawner.NotifyMonsterRemoved(this);
    }
}
