using System.Collections.Generic;
using HexUI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背包（种子）面板 —— 按 B 开关（热键配在面板表里），展示背包内所有种子。
///
/// 职责：
///   1. 从 InventorySystem（种子表数据）生成卡片（网格）；
///   2. 显示阳光 / 选中状态 / 冷却倒计时，并订阅 PlantingSystem 事件实时刷新；
///   3. 点击卡片 → InventorySystem.Select(entry) 进入种植模式，并（按配置）收起背包。
///
/// 分层：本类只认 InventorySystem / SeedCardView（游戏侧 UI），不引用 HexGrid、Mirror 等。
/// 生命周期：OnCreate 找控件 + 订阅；OnShow 重建卡片并刷新；OnHide 不销毁实例（下次开更快）。
/// </summary>
public class InventoryPanel : UIPanel
{
    [Header("控件（装配工具自动填；按名字兜底查找）")]
    public Text titleText;
    public Text sunText;
    public Text hintText;
    public Transform grid;
    public Button closeButton;
    [Tooltip("种子卡片 prefab（留空按 Resources 路径加载）")]
    public SeedCardView cardPrefab;
    public string cardPrefabResourcePath = "UI/Panels/SeedCardView";

    [Header("引用")]
    [Tooltip("背包数据源（留空自动查找场景实例）")]
    public InventorySystem inventory;

    readonly List<SeedCardView> cards = new List<SeedCardView>();
    float nextFallbackCheck;
    PlantingSystem system;
    bool subscribed;

    protected override void OnCreate()
    {
        if (titleText == null) titleText = FindChild<Text>("Title");
        if (sunText == null) sunText = FindChild<Text>("SunText");
        if (hintText == null) hintText = FindChild<Text>("Hint");
        if (closeButton == null) closeButton = FindChild<Button>("CloseButton");
        Transform found = FindChild<Transform>("Grid");
        if (found != null) grid = found;

        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (cardPrefab == null && !string.IsNullOrEmpty(cardPrefabResourcePath))
        {
            GameObject go = Resources.Load<GameObject>(cardPrefabResourcePath);
            if (go != null) cardPrefab = go.GetComponent<SeedCardView>();
        }
        if (titleText != null) titleText.text = UIText.Get("bag.title", "背包");
    }

    protected override void OnShow(object payload)
    {
        EnsureRefs();
        EnsureSubscribed();
        Rebuild();
    }

    protected override void OnHide()
    {
        // 保留卡片实例：下次打开只刷新数据
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void Update()
    {
        // 兜底：面板可见时每秒核对一次（种子表若在打开后才变得可读，这里会自动补建卡片）
        if (Time.unscaledTime < nextFallbackCheck) return;
        nextFallbackCheck = Time.unscaledTime + 1f;

        EnsureRefs();
        if (inventory == null || grid == null || cardPrefab == null) return;
        if (inventory.Entries.Count != ActiveCardCount()) Rebuild();
    }

    public override void OnRefresh()
    {
        RefreshCards();
    }

    // ================= 数据 / 订阅 =================

    void EnsureRefs()
    {
        if (inventory == null) inventory = InventorySystem.Instance;
        if (inventory == null) inventory = FindObjectOfType<InventorySystem>();

        if (system == null) system = PlantingSystem.Instance;
        if (system == null) system = FindObjectOfType<PlantingSystem>();
    }

    void EnsureSubscribed()
    {
        if (subscribed || system == null) return;
        system.SunChanged += OnSunChanged;
        system.CooldownChanged += OnCooldownChanged;
        system.SelectionChanged += OnSelectionChanged;
        system.PlantPlaced += OnPlantPlaced;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed || system == null) return;
        system.SunChanged -= OnSunChanged;
        system.CooldownChanged -= OnCooldownChanged;
        system.SelectionChanged -= OnSelectionChanged;
        system.PlantPlaced -= OnPlantPlaced;
        subscribed = false;
    }

    void OnSunChanged(int sun) { RefreshCards(); }
    void OnCooldownChanged(PlantConfig config) { RefreshCards(); }
    void OnSelectionChanged(PlantConfig config) { RefreshCards(); }
    void OnPlantPlaced(PlantUnit unit) { RefreshCards(); }

    // ================= 卡片 =================

    /// <summary>重建卡片列表（种子数量变化时）。</summary>
    public void Rebuild()
    {
        EnsureRefs();
        if (grid == null || cardPrefab == null || inventory == null) 
        {
            if (hintText != null) hintText.text = UIText.Get("bag.empty", "背包里还没有种子");
            return;
        }

        IReadOnlyList<SeedTable.Entry> entries = inventory.Entries;
        while (cards.Count < entries.Count)
        {
            SeedCardView card = Instantiate(cardPrefab, grid, false);
            card.name = "SeedCard_" + cards.Count;
            cards.Add(card);
        }

        for (int i = 0; i < cards.Count; i++)
        {
            bool used = i < entries.Count;
            cards[i].gameObject.SetActive(used);
            if (used) cards[i].Bind(entries[i], this, i);
        }

        if (hintText != null)
        {
            hintText.text = entries.Count == 0
                ? UIText.Get("bag.empty", "背包里还没有种子")
                : UIText.Get("bag.hint", "点击卡片开始种植 · B 键开关背包");
        }
        RefreshCards();
    }

    /// <summary>刷新阳光 / 冷却 / 选中状态（条目数变化时自动重建卡片）。</summary>
    public void RefreshCards()
    {
        EnsureRefs();
        if (inventory != null && grid != null && cardPrefab != null && inventory.Entries.Count != ActiveCardCount())
        {
            Rebuild();
            return;
        }

        int sun = inventory != null ? inventory.Sun : 0;
        PlantConfig selected = inventory != null ? inventory.Selected : null;

        if (sunText != null) sunText.text = UIText.Get("bag.sunPrefix", "阳光 ") + sun;

        for (int i = 0; i < cards.Count; i++)
        {
            SeedCardView card = cards[i];
            if (card == null || !card.gameObject.activeSelf || card.Entry == null) continue;

            PlantConfig config = card.Entry.config;
            card.RefreshAffordable(sun);
            float remaining = (config != null) ? inventory.CooldownRemaining(config) : 0f;
            float total = config != null ? config.cooldown : 0f;
            card.SetCooldown(remaining, total);
            card.SetSelected(selected != null && selected == config);
        }

        if (titleText != null && selected != null)
        {
            titleText.text = UIText.Get("bag.planting", "种植中：") +
                             (string.IsNullOrEmpty(selected.displayName) ? selected.name : selected.displayName);
        }
        else if (titleText != null)
        {
            titleText.text = UIText.Get("bag.title", "背包");
        }
    }

    int ActiveCardCount()
    {
        int n = 0;
        for (int i = 0; i < cards.Count; i++) if (cards[i] != null && cards[i].gameObject.activeSelf) n++;
        return n;
    }

    /// <summary>卡片点击（由 SeedCardView 上报）。</summary>
    public void OnCardClicked(SeedCardView card)
    {
        if (card == null || card.Entry == null || inventory == null) return;
        if (!card.Entry.unlocked)
        {
            Debug.Log("[UI] 该种子尚未解锁：" + card.Entry.id);
            return;
        }

        bool ok = inventory.Select(card.Entry);
        RefreshCards();
        if (ok && inventory.closePanelAfterSelect)
        {
            Close();   // 进入种植模式后收起背包，露出地图
        }
    }
}
