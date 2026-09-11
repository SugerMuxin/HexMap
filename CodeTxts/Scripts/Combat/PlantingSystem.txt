using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>种植校验结果（供 UI 提示与单元测试断言；Ok 之外的枚举即失败原因）。</summary>
public enum PlantResult
{
    Ok = 0,
    NoCell,          // 空格 / 坐标越界
    NoConfig,        // 未选卡 / 配置缺少 prefab
    WrongTerrain,    // 地形不可种植（沙 / 石 / 雪 …）
    Underwater,      // 水下格
    LairCell,        // 怪物巢穴格（MonsterLairLevel > 0）
    Occupied,        // 已被植物占用
    NotEnoughSun,    // 阳光不足
    Cooling          // 冷却中
}

/// <summary>
/// 种植系统（游戏侧，PvZ 3D FEAT-003）。
///
/// 职责：阳光账本、选卡（种植模式）、冷却计时、种植合法性校验、植物实例化与占用表。
/// 与现有系统解耦：只读 HexCell 的既有字段（TerrainTypeIndex / IsUnderwater / MonsterLairLevel）
/// 与 HexGrid.GetCell，不修改 HexGrid/HexCell/HexMapEditor 的任何逻辑；视觉特征
/// （HexCell.PlantLevel / HexFeatureManager.plantPrefabs）保持纯装饰语义不受影响。
///
/// 校验分三层（UI 高亮 / 种植 / 提示各取所需）：
///   CheckCell(cell)            只看格规则：可种植地形 + 非水下 + 非巢穴 + 未占用
///   Validate(cell, config)     再叠加 config / 阳光 / 冷却
///   CanPlant(cell, config)     Validate == Ok
///
/// 落位：cell.transform.position（格中心顶面，含海拔与垂直扰动）——与 HeroController 同口径。
/// 碰撞体：默认剥掉植物自带 Collider，避免抢走 HexMapEditor / HeroController 的
/// Physics.Raycast（同 HexFeatureManager.keepColliders 的既有坑）。
///
/// 装配：挂在场景根对象（如 "[Planting]"），Inspector 拖入 HexGrid 与植物卡列表即可；
/// 或跑菜单 Tools/种植系统/一键装配 (PvZ 种植)。
/// </summary>
[DisallowMultipleComponent]
public class PlantingSystem : MonoBehaviour
{
    public static PlantingSystem Instance { get; private set; }

    [Header("引用")]
    [Tooltip("地图（留空自动查找场景里的 HexGrid）")]
    public HexGrid hexGrid;
    [Tooltip("植物实例的父物体（留空自动创建子物体 \"Plants Container\"）")]
    public Transform plantContainer;
    [Tooltip("可选植物卡列表（临时按键选卡与 FGUI 选卡栏共用这一份数据源）")]
    public PlantConfig[] availablePlants;

    [Header("阳光")]
    [Tooltip("初始阳光")]
    public int startSun = 50;
    [Tooltip("阳光上限（<=0 表示不限）")]
    public int maxSun = 9999;
    [Tooltip("自然阳光产出数量（0 = 关闭）")]
    public int sunIncomeAmount = 25;
    [Tooltip("自然阳光产出间隔（秒）")]
    public float sunIncomeInterval = 5f;

    [Header("种植规则")]
    [Tooltip("可种植地形（下标 = terrainTypeIndex：0沙 1草 2泥 3石 4雪）")]
    public bool[] plantableTerrain = { false, true, true, false, false };
    [Tooltip("是否允许种在怪物巢穴格上（默认否——巢穴是出怪点）")]
    public bool allowPlantOnLair = false;
    [Tooltip("种植后保留选卡（PvZ 风格：可连续种植，直到阳光不足或取消）")]
    public bool keepSelectionAfterPlant = true;

    [Header("表现 / 调试")]
    [Tooltip("剥掉植物 prefab 自带碰撞体（默认剥：避免抢走编辑/点击的 Physics.Raycast）")]
    public bool stripPlantColliders = true;
    [Tooltip("落位 y 偏移（贴地微调，一般 0）")]
    public float plantYOffset = 0f;
    [Tooltip("植物朝向（度，绕 Y；FEAT-004 接管瞄准后可不用）")]
    public float plantYaw = 0f;
    [Tooltip("打印阳光变化日志（FEAT-005 接入 FGUI 后可关）")]
    public bool logSunChanges = true;
    [Tooltip("每帧清理失效登记（植物实例被外部 Destroy / 读档重建地图导致格对象失效时的自愈）")]
    public bool pruneStaleEachFrame = true;
    [Tooltip("清理失效登记时，同时销毁\"所在格已消失\"的孤立植物实例（读档重建地图场景）")]
    public bool destroyOrphansOnPrune = true;

    /// <summary>阳光变化（含产出与消耗）</summary>
    public event Action<int> SunChanged;
    /// <summary>选卡变化（null = 退出种植模式）</summary>
    public event Action<PlantConfig> SelectionChanged;
    /// <summary>某植物冷却开始/结束（UI 置灰刷新用）</summary>
    public event Action<PlantConfig> CooldownChanged;
    /// <summary>植物种下</summary>
    public event Action<PlantUnit> PlantPlaced;
    /// <summary>植物被移除（死亡 / 手动清理）</summary>
    public event Action<PlantUnit> PlantRemoved;

    readonly Dictionary<HexCell, PlantUnit> occupancy = new Dictionary<HexCell, PlantUnit>();
    readonly Dictionary<PlantConfig, float> cooldownUntil = new Dictionary<PlantConfig, float>();
    readonly List<PlantConfig> cooldownExpired = new List<PlantConfig>();
    readonly List<PlantUnit> removalBuffer = new List<PlantUnit>();
    readonly List<KeyValuePair<HexCell, PlantUnit>> staleBuffer =
        new List<KeyValuePair<HexCell, PlantUnit>>();
    readonly List<PlantUnit> orphanBuffer = new List<PlantUnit>();

    int sun;
    PlantConfig selected;
    float nextSunIncomeTime;

    public int Sun { get { return sun; } }
    public PlantConfig SelectedPlant { get { return selected; } }
    /// <summary>是否处于种植模式（选卡非空）。输入仲裁由 PlantingInput 落到 HexControlMode.plantingActive。</summary>
    public bool IsPlantingMode { get { return selected != null; } }
    public int PlantCount { get { return occupancy.Count; } }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Planting] 场景中存在多个 PlantingSystem，保留先者，禁用后来者：" + name);
            enabled = false;
            return;
        }
        Instance = this;
        sun = Mathf.Max(0, startSun);
        nextSunIncomeTime = Time.time + Mathf.Max(0.1f, sunIncomeInterval);
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        EnsureRefs();
        if (SunChanged != null) SunChanged(sun);
    }

    void Update()
    {
        if (hexGrid == null || plantContainer == null) EnsureRefs();

        // 自然阳光产出
        if (sunIncomeAmount > 0 && sunIncomeInterval > 0f && Time.time >= nextSunIncomeTime)
        {
            nextSunIncomeTime = Time.time + sunIncomeInterval;
            AddSun(sunIncomeAmount);
        }

        if (pruneStaleEachFrame) PruneStaleEntries();

        // 冷却到期：清表并广播（Time.time 单调递增，到期即冷却完成）
        if (cooldownUntil.Count > 0)
        {
            cooldownExpired.Clear();
            foreach (KeyValuePair<PlantConfig, float> kv in cooldownUntil)
            {
                if (Time.time >= kv.Value) cooldownExpired.Add(kv.Key);
            }
            for (int i = 0; i < cooldownExpired.Count; i++)
            {
                cooldownUntil.Remove(cooldownExpired[i]);
                if (CooldownChanged != null) CooldownChanged(cooldownExpired[i]);
            }
        }
    }

    void EnsureRefs()
    {
        if (hexGrid == null)
        {
            hexGrid = FindObjectOfType<HexGrid>();
        }
        if (plantContainer == null)
        {
            Transform existing = transform.Find("Plants Container");
            if (existing != null)
            {
                plantContainer = existing;
            }
            else
            {
                GameObject go = new GameObject("Plants Container");
                go.transform.SetParent(transform, false);
                plantContainer = go.transform;
            }
        }
    }

    /// <summary>世界坐标 → 所在格（越界 / 网格未生成返回 null，绝不抛异常）。</summary>
    public HexCell GetCellAtWorld(Vector3 worldPosition)
    {
        if (hexGrid == null) return null;
        try
        {
            Vector3 local = hexGrid.transform.InverseTransformPoint(worldPosition);
            return hexGrid.GetCell(HexCoordinates.FromPosition(local));
        }
        catch (Exception)
        {
            // HexGrid.GetCell 只按 cellCountX/Z 查界、不校验 cells 数组：网格未生成时越界。
            return null;
        }
    }

    // ================= 阳光 =================

    public void AddSun(int amount)
    {
        if (amount == 0) return;
        int before = sun;
        sun = Mathf.Max(0, sun + amount);
        if (maxSun > 0) sun = Mathf.Min(sun, maxSun);
        if (sun == before) return;
        if (logSunChanges) Debug.Log("[Planting] 阳光 " + before + " → " + sun + (amount > 0 ? " (+" + amount + ")" : " (" + amount + ")"));
        if (SunChanged != null) SunChanged(sun);
    }

    public bool TrySpendSun(int amount)
    {
        if (amount <= 0) return true;
        if (sun < amount) return false;
        AddSun(-amount);
        return true;
    }

    public void ResetSun(int amount)
    {
        int before = sun;
        sun = Mathf.Max(0, amount);
        if (maxSun > 0) sun = Mathf.Min(sun, maxSun);
        if (sun != before && SunChanged != null) SunChanged(sun);
    }

    // ================= 选卡（种植模式） =================

    public void SelectPlant(PlantConfig config)
    {
        if (selected == config) return;
        selected = config;
        if (SelectionChanged != null) SelectionChanged(selected);
    }

    public void ClearSelection()
    {
        if (selected == null) return;
        selected = null;
        if (SelectionChanged != null) SelectionChanged(null);
    }

    // ================= 校验 =================

    public bool IsTerrainPlantable(int terrainTypeIndex)
    {
        if (plantableTerrain == null) return false;
        if (terrainTypeIndex < 0 || terrainTypeIndex >= plantableTerrain.Length) return false;
        return plantableTerrain[terrainTypeIndex];
    }

    /// <summary>仅"格规则"校验（不含阳光 / 冷却），供 UI 高亮可种植格。</summary>
    public PlantResult CheckCell(HexCell cell)
    {
        if (cell == null) return PlantResult.NoCell;
        if (!IsTerrainPlantable(cell.TerrainTypeIndex)) return PlantResult.WrongTerrain;
        if (cell.IsUnderwater) return PlantResult.Underwater;
        if (!allowPlantOnLair && cell.MonsterLairLevel > 0) return PlantResult.LairCell;
        if (occupancy.ContainsKey(cell)) return PlantResult.Occupied;
        return PlantResult.Ok;
    }

    /// <summary>完整校验：格规则 + config + 阳光 + 冷却。</summary>
    public PlantResult Validate(HexCell cell, PlantConfig config)
    {
        PlantResult r = CheckCell(cell);
        if (r != PlantResult.Ok) return r;
        if (config == null || config.prefab == null) return PlantResult.NoConfig;
        // 冷却先于阳光判定：冷却/阳光都是"卡"级状态，冷却中优先提示"冷却中"（UI 置灰语义更准确）
        if (GetCooldownRemaining(config) > 0f) return PlantResult.Cooling;
        if (sun < config.sunCost) return PlantResult.NotEnoughSun;
        return PlantResult.Ok;
    }

    public bool CanPlant(HexCell cell, PlantConfig config)
    {
        return Validate(cell, config) == PlantResult.Ok;
    }

    public bool IsOccupied(HexCell cell)
    {
        return cell != null && occupancy.ContainsKey(cell);
    }

    public PlantUnit GetPlant(HexCell cell)
    {
        if (cell == null) return null;
        PlantUnit unit;
        return occupancy.TryGetValue(cell, out unit) ? unit : null;
    }

    public float GetCooldownRemaining(PlantConfig config)
    {
        if (config == null) return 0f;
        float until;
        if (!cooldownUntil.TryGetValue(config, out until)) return 0f;
        return Mathf.Max(0f, until - Time.time);
    }

    public bool IsCoolingDown(PlantConfig config)
    {
        return GetCooldownRemaining(config) > 0f;
    }

    // ================= 种植 / 移除 =================

    /// <summary>
    /// 尝试种植。失败时 result 给出原因、unit 为 null；成功时已扣阳光 + 进入冷却 + 登记占用。
    /// 供 UI / 输入层调用（UI 用 result 提示"阳光不足 / 冷却中 / 地形不可种植"等）。
    /// </summary>
    public bool TryPlant(HexCell cell, PlantConfig config, out PlantResult result, out PlantUnit unit)
    {
        unit = null;
        result = Validate(cell, config);
        if (result != PlantResult.Ok) return false;

        if (!TrySpendSun(config.sunCost))
        {
            result = PlantResult.NotEnoughSun;
            return false;
        }

        unit = Spawn(cell, config);
        if (unit == null)
        {
            AddSun(config.sunCost);  // 回滚阳光（理论上 Validate 已挡住，兜底）
            result = PlantResult.NoConfig;
            return false;
        }

        occupancy[cell] = unit;
        if (config.cooldown > 0f)
        {
            cooldownUntil[config] = Time.time + config.cooldown;
            if (CooldownChanged != null) CooldownChanged(config);
        }
        if (!keepSelectionAfterPlant) ClearSelection();

        if (PlantPlaced != null) PlantPlaced(unit);
        result = PlantResult.Ok;
        return true;
    }

    /// <summary>便捷种植（失败返回 null；原因用 Validate/TryPlant 查询）。</summary>
    public PlantUnit Plant(HexCell cell, PlantConfig config)
    {
        PlantResult r;
        PlantUnit unit;
        TryPlant(cell, config, out r, out unit);
        return unit;
    }

    /// <summary>用当前选卡在指定格种植（临时输入入口 / FGUI 点击用）。</summary>
    public bool TryPlantSelected(HexCell cell, out PlantResult result)
    {
        PlantUnit unit;
        return TryPlant(cell, selected, out result, out unit);
    }

    PlantUnit Spawn(HexCell cell, PlantConfig config)
    {
        if (config == null || config.prefab == null) return null;
        EnsureRefs();

        GameObject go = Instantiate(config.prefab, plantContainer, false);
        go.name = (string.IsNullOrEmpty(config.displayName) ? config.name : config.displayName) + " (Plant)";

        // 落位：格中心顶面（含海拔与垂直扰动）——与 HeroController.MoveTo 同口径
        Vector3 position = cell.transform.position;
        position.y += plantYOffset;
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, plantYaw, 0f));

        if (stripPlantColliders)
        {
            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                cols[i].enabled = false;
            }
        }

        PlantUnit unit = go.GetComponent<PlantUnit>();
        if (unit == null) unit = go.AddComponent<PlantUnit>();
        unit.Initialize(config, cell, this);
        return unit;
    }

    /// <summary>植物死亡（PlantUnit 回调）：注销占用并销毁实例。</summary>
    public void NotifyPlantDied(PlantUnit unit)
    {
        RemovePlant(unit, true);
    }

    /// <summary>植物实例被销毁时的兜底注销（已注销则空操作）。</summary>
    public void NotifyPlantRemoved(PlantUnit unit, bool destroy)
    {
        RemovePlant(unit, destroy);
    }

    /// <summary>移除植物（plant 为空 / 已注销 → 返回 false）。destroy=true 同时销毁实例。</summary>
    public bool RemovePlant(PlantUnit unit, bool destroy = true)
    {
        if (unit == null) return false;

        HexCell key = null;
        if (unit.cell != null && occupancy.TryGetValue(unit.cell, out PlantUnit cur) && cur == unit)
        {
            key = unit.cell;
        }
        else
        {
            // 兜底：格引用已被销毁（地图重建）时按值扫描
            foreach (KeyValuePair<HexCell, PlantUnit> kv in occupancy)
            {
                if (kv.Value == unit)
                {
                    key = kv.Key;
                    break;
                }
            }
        }

        bool removed = false;
        if (key != null && occupancy.Remove(key)) removed = true;

        if (removed && PlantRemoved != null) PlantRemoved(unit);
        if (destroy && unit != null && unit.gameObject != null) DestroyObject(unit.gameObject);
        return removed;
    }

    /// <summary>移除指定格上的植物。</summary>
    public bool RemovePlantAt(HexCell cell, bool destroy = true)
    {
        return RemovePlant(GetPlant(cell), destroy);
    }

    /// <summary>
    /// 清理失效登记：植物实例被外部 Destroy（不走 PlantUnit.OnDestroy）或地图重建后
    /// HexCell 对象失效时，占用表会留下 Unity 伪 null 引用 → 每帧自愈，返回清理条数。
    /// </summary>
    public int PruneStaleEntries()
    {
        if (occupancy.Count == 0) return 0;
        staleBuffer.Clear();
        orphanBuffer.Clear();
        foreach (KeyValuePair<HexCell, PlantUnit> kv in occupancy)
        {
            // 注意：UnityEngine.Object 的 == null 对已销毁对象为 true（伪 null）
            if (kv.Value == null)
            {
                staleBuffer.Add(kv);              // 实例已被外部销毁：只注销登记
            }
            else if (kv.Key == null)
            {
                staleBuffer.Add(kv);              // 所在格已消失（读档重建地图）：注销 + 销毁孤立实例
                orphanBuffer.Add(kv.Value);
            }
        }
        for (int i = 0; i < staleBuffer.Count; i++)
        {
            occupancy.Remove(staleBuffer[i].Key);
            if (PlantRemoved != null) PlantRemoved(staleBuffer[i].Value);
        }
        if (destroyOrphansOnPrune)
        {
            for (int i = 0; i < orphanBuffer.Count; i++)
            {
                PlantUnit orphan = orphanBuffer[i];
                if (orphan != null && orphan.gameObject != null) DestroyObject(orphan.gameObject);
            }
        }
        int count = staleBuffer.Count;
        staleBuffer.Clear();
        orphanBuffer.Clear();
        return count;
    }

    /// <summary>
    /// 清空所有植物（新地图 / 读档后调用——地图重建会销毁旧 HexCell，
    /// 届时占用表里的格引用失效、植物会浮在旧位置）。
    /// </summary>
    public void ClearPlants(bool destroy = true)
    {
        removalBuffer.Clear();
        removalBuffer.AddRange(occupancy.Values);
        occupancy.Clear();
        for (int i = 0; i < removalBuffer.Count; i++)
        {
            PlantUnit unit = removalBuffer[i];
            if (unit == null) continue;
            if (PlantRemoved != null) PlantRemoved(unit);
            if (destroy && unit.gameObject != null) DestroyObject(unit.gameObject);
        }
        removalBuffer.Clear();

        // 兜底：清掉任何未被登记的残留实例（孤立 / 手动拖进容器的）
        if (destroy && plantContainer != null)
        {
            PlantUnit[] leftovers = plantContainer.GetComponentsInChildren<PlantUnit>(true);
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && leftovers[i].gameObject != null)
                {
                    DestroyObject(leftovers[i].gameObject);
                }
            }
        }
    }

    static void DestroyObject(GameObject go)
    {
        // 编辑器（非 Play）下 Destroy 会报 "Destroy may not be called from edit mode"
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }

    /// <summary>校验结果的中文说明（UI 提示 / 日志用）。</summary>
    public static string Describe(PlantResult result)
    {
        switch (result)
        {
            case PlantResult.Ok: return "可种植";
            case PlantResult.NoCell: return "无效格子";
            case PlantResult.NoConfig: return "未选卡或配置缺少 prefab";
            case PlantResult.WrongTerrain: return "该地形不可种植（仅草/泥）";
            case PlantResult.Underwater: return "水下不可种植";
            case PlantResult.LairCell: return "怪物巢穴格不可种植";
            case PlantResult.Occupied: return "该格已被植物占用";
            case PlantResult.NotEnoughSun: return "阳光不足";
            case PlantResult.Cooling: return "冷却中";
            default: return result.ToString();
        }
    }
}
