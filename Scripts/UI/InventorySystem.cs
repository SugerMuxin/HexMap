using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 背包（种子）数据源（游戏侧）。背包 UI 只跟它打交道，不直接碰 PlantingSystem / PlantConfig 的细节。
///
/// 数据链：`Assets/Resources/Configs/Tables/SeedTable.asset`（由 SeedTable.csv 导入）
///         → 本类（排序 / 解锁过滤 / 阳光 / 冷却）
///         → InventoryPanel（展示）
///
/// 种植动作转发给 PlantingSystem（选卡 + 阳光 + 冷却都在那边，本类不重复实现）。
/// 后续加"拥有数量 / 掉落 / 合成"时，只在本类扩展（如 CountOf / TryConsume），UI 不用改。
/// </summary>
[DisallowMultipleComponent]
public class InventorySystem : MonoBehaviour
{
    public static InventorySystem Instance { get; private set; }

    [Tooltip("种子表（留空则从 Resources/Configs/Tables/SeedTable 加载）")]
    public SeedTable seedTable;
    [Tooltip("种子表 Resources 路径（不带扩展名）")]
    public string seedTableResourcePath = "Configs/Tables/SeedTable";
    [Tooltip("种植系统（留空自动查找场景实例）")]
    public PlantingSystem plantingSystem;
    [Tooltip("点击种子卡片后自动关闭背包（进入种植模式）")]
    public bool closePanelAfterSelect = true;
    [Tooltip("打印调试日志")]
    public bool logActions = false;

    /// <summary>背包内容变化（表加载 / 解锁变化）。</summary>
    public event Action Changed;

    readonly List<SeedTable.Entry> entries = new List<SeedTable.Entry>();
    bool loaded;
    float lastReloadAttempt = -999f;
    [Tooltip("自动重试间隔（秒）：种子表暂时读不到（重编译 / 资产重导入窗口期）时，按此间隔重试，避免永久空背包")]
    public float reloadRetryInterval = 0.5f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[UI] 场景中存在多个 InventorySystem，保留先者，禁用后来者：" + name);
            enabled = false;
            return;
        }
        Instance = this;
        EnsureTable();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void EnsureTable()
    {
        if (seedTable == null && !string.IsNullOrEmpty(seedTableResourcePath))
        {
            seedTable = Resources.Load<SeedTable>(seedTableResourcePath);
        }

        if (seedTable == null) return;
        if (entries.Count > 0) return;                                        // 已有内容：不再重载
        if (Time.realtimeSinceStartup - lastReloadAttempt < reloadRetryInterval) return;   // 节流

        // 自愈：只要当前是空的就重试（脚本重编译 / 资产重导入的窗口期里表可能暂时读不到，
        // 旧实现把空结果用 loaded=true 锁死，会导致背包永远为空）
        lastReloadAttempt = Time.realtimeSinceStartup;
        LoadEntries(true);
    }

    /// <summary>（重新）从种子表加载背包内容。</summary>
    public void Reload()
    {
        loaded = false;
        LoadEntries(true);
    }

    void LoadEntries(bool markLoaded)
    {
        entries.Clear();
        if (seedTable != null) entries.AddRange(seedTable.OrderedSeeds());
        if (markLoaded) loaded = entries.Count > 0;
        if (logActions) Debug.Log("[UI] 背包内容加载：" + entries.Count + " 个种子（locked=" + loaded + "）");
        if (Changed != null) Changed();
    }

    /// <summary>背包里的种子（按显示顺序；含未解锁项——UI 自行灰化）。</summary>
    public IReadOnlyList<SeedTable.Entry> Entries
    {
        get { EnsureTable(); return entries; }
    }

    /// <summary>已解锁的种子数量。</summary>
    public int UnlockedCount
    {
        get
        {
            int n = 0;
            EnsureTable();
            for (int i = 0; i < entries.Count; i++) if (entries[i].unlocked) n++;
            return n;
        }
    }

    /// <summary>当前阳光（无 PlantingSystem 时返回 0，不抛异常）。</summary>
    public int Sun
    {
        get { PlantingSystem s = EnsureSystem(); return s != null ? s.Sun : 0; }
    }

    public float CooldownRemaining(PlantConfig config)
    {
        PlantingSystem s = EnsureSystem();
        return s != null ? s.GetCooldownRemaining(config) : 0f;
    }

    public bool IsCoolingDown(PlantConfig config)
    {
        return CooldownRemaining(config) > 0f;
    }

    /// <summary>是否处于种植模式（已选卡）。</summary>
    public bool IsPlanting
    {
        get { PlantingSystem s = EnsureSystem(); return s != null && s.IsPlantingMode; }
    }

    /// <summary>当前选中的植物配置（无则 null）。</summary>
    public PlantConfig Selected
    {
        get { PlantingSystem s = EnsureSystem(); return s != null ? s.SelectedPlant : null; }
    }

    /// <summary>
    /// 选中一个种子 → 进入种植模式（转发给 PlantingSystem）。
    /// 已有同种卡时再点一次 = 取消选择。
    /// </summary>
    public bool Select(SeedTable.Entry entry, bool toggleOff = true)
    {
        if (entry == null || entry.config == null) return false;
        PlantingSystem s = EnsureSystem();
        if (s == null)
        {
            Debug.LogWarning("[UI] 场景里没有 PlantingSystem，无法进入种植模式。");
            return false;
        }

        if (toggleOff && s.SelectedPlant == entry.config)
        {
            s.ClearSelection();
            if (logActions) Debug.Log("[UI] 取消选卡 " + entry.id);
            return true;
        }

        s.SelectPlant(entry.config);
        if (logActions) Debug.Log("[UI] 选卡 " + entry.id + "（进入种植模式）");
        return true;
    }

    /// <summary>取消选卡（退出种植模式）。</summary>
    public void ClearSelection()
    {
        PlantingSystem s = EnsureSystem();
        if (s != null) s.ClearSelection();
    }

    /// <summary>把种子表里已解锁的植物灌给 PlantingSystem.availablePlants（数字键临时入口用）。</summary>
    public int ApplyToPlantingSystem(bool onlyWhenEmpty = true)
    {
        PlantingSystem s = EnsureSystem();
        if (s == null || seedTable == null) return 0;
        if (onlyWhenEmpty && s.availablePlants != null && s.availablePlants.Length > 0) return 0;

        PlantConfig[] configs = seedTable.GetUnlockedConfigs();
        s.availablePlants = configs;
        return configs.Length;
    }

    PlantingSystem EnsureSystem()
    {
        if (plantingSystem != null) return plantingSystem;
        plantingSystem = PlantingSystem.Instance;
        if (plantingSystem == null) plantingSystem = FindObjectOfType<PlantingSystem>();
        return plantingSystem;
    }
}
