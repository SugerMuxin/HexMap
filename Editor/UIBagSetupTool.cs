using HexUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「背包（种子）UI」一键装配（幂等）。
///
/// 产物：
///   1. 卡片 prefab  Assets/Resources/UI/Panels/SeedCardView.prefab
///   2. 面板 prefab  Assets/Resources/UI/Panels/InventoryPanel.prefab
///   3. 面板表注册  id = "bag"（Normal 层 / 模态 / 阻塞游戏输入 / 热键 B / Esc 可关）
///   4. 场景对象    [Inventory]（InventorySystem：种子表 → 背包数据源）
///   5. 面板表 CSV 同步（UIPanelTable.csv，保持"改表→导入"闭环）
///
/// 场景只标脏不保存（由你 Ctrl+S）。先跑一次 `Tools/UI/一键装配 UI 框架 (UIRoot)` 更稳。
/// </summary>
public static class UIBagSetupTool
{
    const string PanelsDir = "Assets/Resources/UI/Panels";
    const string CardPrefabPath = PanelsDir + "/SeedCardView.prefab";
    const string PanelPrefabPath = PanelsDir + "/InventoryPanel.prefab";
    const string TablePath = "Assets/Resources/Configs/Tables/UIPanelTable.asset";
    const string InventoryRootName = "[Inventory]";

    public const string PanelId = "bag";

    [MenuItem("Tools/UI/一键装配 背包 UI (bag)", false, 2)]
    public static void Setup()
    {
        EnsureRoot();          // 前置：UIRoot（面板表 / 容器 / 遮罩）
        GameObject card = EnsureCardPrefab();
        GameObject panel = EnsurePanelPrefab(card);
        RegisterPanel(panel);
        GameObject inventory = EnsureInventoryRoot();

        EditorSceneManager.MarkSceneDirty(inventory.scene);
        TableImporter.ExportPanels();
        AssetDatabase.SaveAssets();
        Debug.Log("[UI] 背包 UI 装配完成（场景未保存，请 Ctrl+S）。\n" +
                  "  卡片：" + CardPrefabPath + "\n  面板：" + PanelPrefabPath + "\n" +
                  "  面板表：id=bag（B 键开关 / Esc 关闭 / 模态）\n  场景对象：" + InventoryRootName);
    }

    [MenuItem("Tools/UI/一键装配 框架 + 背包（不含联机）", false, 3)]
    public static void SetupAll()
    {
        UISetupTool.Setup();
        Setup();
        Debug.Log("[UI] 全部 UI 装配完成（场景未保存，请 Ctrl+S）。");
    }

    // ================= 资产 =================

    static void EnsureRoot()
    {
        if (GameObject.Find(UISetupTool.RootName) == null)
        {
            UISetupTool.Setup();
        }
    }

    static GameObject EnsureCardPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
        if (existing != null && existing.GetComponent<SeedCardView>() != null) return existing;

        UISetupTool.EnsureFolder(PanelsDir);

        GameObject root = new GameObject("SeedCardView", typeof(RectTransform), typeof(Image), typeof(SeedCardView));
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160f, 190f);
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0.16f, 0.18f, 0.23f, 0.95f);

        // 选中高亮框（默认关闭）
        GameObject selGO = new GameObject("Selection", typeof(RectTransform), typeof(Image));
        RectTransform selRT = selGO.GetComponent<RectTransform>();
        selRT.SetParent(root.transform, false);
        selRT.anchorMin = Vector2.zero; selRT.anchorMax = Vector2.one;
        selRT.offsetMin = new Vector2(-4f, -4f); selRT.offsetMax = new Vector2(4f, 4f);
        Image sel = selGO.GetComponent<Image>();
        sel.color = new Color(0.35f, 0.95f, 0.45f, 0.85f);
        sel.raycastTarget = false;
        selGO.SetActive(false);

        // 图标（居中偏上）
        GameObject iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        RectTransform iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.SetParent(root.transform, false);
        iconRT.anchorMin = new Vector2(0.5f, 0.5f); iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.sizeDelta = new Vector2(72f, 72f);
        iconRT.anchoredPosition = new Vector2(0f, 18f);
        Image icon = iconGO.GetComponent<Image>();
        icon.color = Color.white;
        icon.raycastTarget = false;

        // 名称（顶部）
        Text name = UISetupTool.NewText(root.transform, "NameText", "种子", 22, TextAnchor.MiddleCenter, Color.white);
        UISetupTool.StretchTop(name.rectTransform, 6f, 30f);
        name.raycastTarget = false;

        // 阳光消耗（底部）
        Text cost = UISetupTool.NewText(root.transform, "CostText", "50", 22, TextAnchor.MiddleCenter, new Color(0.95f, 0.85f, 0.3f));
        cost.rectTransform.anchorMin = new Vector2(0f, 0f); cost.rectTransform.anchorMax = new Vector2(1f, 0f);
        cost.rectTransform.pivot = new Vector2(0.5f, 0f);
        cost.rectTransform.sizeDelta = new Vector2(0f, 28f);
        cost.rectTransform.anchoredPosition = new Vector2(0f, 22f);
        cost.raycastTarget = false;

        // 可种植地形（底部小字）
        Text terrain = UISetupTool.NewText(root.transform, "TerrainText", "草|泥", 16, TextAnchor.MiddleCenter, new Color(0.7f, 0.78f, 0.7f));
        terrain.rectTransform.anchorMin = new Vector2(0f, 0f); terrain.rectTransform.anchorMax = new Vector2(1f, 0f);
        terrain.rectTransform.pivot = new Vector2(0.5f, 0f);
        terrain.rectTransform.sizeDelta = new Vector2(0f, 22f);
        terrain.rectTransform.anchoredPosition = new Vector2(0f, 2f);
        terrain.raycastTarget = false;

        // 冷却遮罩（默认关闭，垂直填充）
        GameObject cdGO = new GameObject("CooldownOverlay", typeof(RectTransform), typeof(Image));
        RectTransform cdRT = cdGO.GetComponent<RectTransform>();
        cdRT.SetParent(root.transform, false);
        cdRT.anchorMin = Vector2.zero; cdRT.anchorMax = Vector2.one;
        cdRT.offsetMin = Vector2.zero; cdRT.offsetMax = Vector2.zero;
        Image cd = cdGO.GetComponent<Image>();
        cd.color = new Color(0f, 0f, 0f, 0.6f);
        cd.raycastTarget = false;
        cdGO.SetActive(false);

        // 冷却剩余时间文本（默认关闭）
        Text cdText = UISetupTool.NewText(root.transform, "CooldownText", "3.0s", 24, TextAnchor.MiddleCenter, Color.white);
        cdText.rectTransform.anchorMin = Vector2.zero; cdText.rectTransform.anchorMax = Vector2.one;
        cdText.rectTransform.offsetMin = Vector2.zero; cdText.rectTransform.offsetMax = Vector2.zero;
        cdText.raycastTarget = false;
        cdText.gameObject.SetActive(false);

        SeedCardView view = root.GetComponent<SeedCardView>();
        view.background = bg;
        view.icon = icon;
        view.nameText = name;
        view.costText = cost;
        view.terrainText = terrain;
        view.cooldownOverlay = cd;
        view.cooldownText = cdText;
        view.selection = sel;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[UI] 已创建卡片 prefab " + CardPrefabPath);
        return prefab;
    }

    static GameObject EnsurePanelPrefab(GameObject cardPrefab)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
        if (existing != null && existing.GetComponent<InventoryPanel>() != null) return existing;

        UISetupTool.EnsureFolder(PanelsDir);

        GameObject root = new GameObject("InventoryPanel", typeof(RectTransform), typeof(Image), typeof(InventoryPanel));
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(900f, 620f);
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0.10f, 0.11f, 0.14f, 0.98f);

        Text title = UISetupTool.NewText(root.transform, "Title", "背包", 34, TextAnchor.MiddleLeft, Color.white);
        title.rectTransform.anchorMin = new Vector2(0f, 1f); title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0.5f, 1f);
        title.rectTransform.sizeDelta = new Vector2(-40f, 56f);
        title.rectTransform.anchoredPosition = new Vector2(0f, -14f);
        title.rectTransform.offsetMin = new Vector2(28f, title.rectTransform.offsetMin.y);

        Text sun = UISetupTool.NewText(root.transform, "SunText", "阳光 0", 28, TextAnchor.MiddleRight, new Color(0.95f, 0.85f, 0.3f));
        sun.rectTransform.anchorMin = new Vector2(0f, 1f); sun.rectTransform.anchorMax = new Vector2(1f, 1f);
        sun.rectTransform.pivot = new Vector2(1f, 1f);
        sun.rectTransform.sizeDelta = new Vector2(-40f, 56f);
        sun.rectTransform.anchoredPosition = new Vector2(-28f, -14f);

        // 网格容器
        GameObject gridGO = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
        RectTransform gridRT = gridGO.GetComponent<RectTransform>();
        gridRT.SetParent(root.transform, false);
        gridRT.anchorMin = new Vector2(0f, 0f); gridRT.anchorMax = new Vector2(1f, 1f);
        gridRT.offsetMin = new Vector2(28f, 84f);
        gridRT.offsetMax = new Vector2(-28f, -80f);
        GridLayoutGroup glg = gridGO.GetComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(160f, 190f);
        glg.spacing = new Vector2(16f, 16f);
        glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis = GridLayoutGroup.Axis.Horizontal;
        glg.childAlignment = TextAnchor.UpperLeft;
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 5;

        Text hint = UISetupTool.NewText(root.transform, "Hint", "点击卡片开始种植 · B 键开关背包", 20, TextAnchor.MiddleLeft, new Color(0.75f, 0.8f, 0.88f));
        hint.rectTransform.anchorMin = new Vector2(0f, 0f); hint.rectTransform.anchorMax = new Vector2(1f, 0f);
        hint.rectTransform.pivot = new Vector2(0.5f, 0f);
        hint.rectTransform.sizeDelta = new Vector2(-260f, 48f);
        hint.rectTransform.anchoredPosition = new Vector2(-100f, 18f);

        UISetupTool.NewButton(root.transform, "CloseButton", "关闭", new Vector2(160f, 48f), new Vector2(100f, 18f), null);

        InventoryPanel panel = root.GetComponent<InventoryPanel>();
        panel.titleText = title;
        panel.sunText = sun;
        panel.hintText = hint;
        panel.grid = gridRT;
        panel.cardPrefab = cardPrefab.GetComponent<SeedCardView>();
        panel.cardPrefabResourcePath = "UI/Panels/SeedCardView";
        Transform closeT = root.transform.Find("CloseButton");
        if (closeT != null) panel.closeButton = closeT.GetComponent<Button>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[UI] 已创建面板 prefab " + PanelPrefabPath);
        return prefab;
    }

    static void RegisterPanel(GameObject panelPrefab)
    {
        UIPanelTable table = AssetDatabase.LoadAssetAtPath<UIPanelTable>(TablePath);
        if (table == null)
        {
            Debug.LogError("[UI] 找不到面板表 " + TablePath + "，先跑 Tools/UI/一键装配 UI 框架 (UIRoot)");
            return;
        }

        UIPanelTable.Entry e = table.GetOrCreate(PanelId);
        e.id = PanelId;
        e.prefab = panelPrefab;
        e.prefabPath = "UI/Panels/InventoryPanel";
        e.layer = UIPanelLayer.Normal;
        e.modal = true;
        e.exclusiveGroup = "";
        e.escClose = true;
        e.hotkey = KeyCode.B;
        e.blockGameInput = true;
        e.preload = false;
        e.order = 0;

        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
    }

    static GameObject EnsureInventoryRoot()
    {
        GameObject root = GameObject.Find(InventoryRootName);
        if (root == null)
        {
            root = new GameObject(InventoryRootName);
        }

        InventorySystem inv = root.GetComponent<InventorySystem>();
        if (inv == null) inv = root.AddComponent<InventorySystem>();

        if (inv.seedTable == null)
        {
            inv.seedTable = AssetDatabase.LoadAssetAtPath<SeedTable>("Assets/Resources/Configs/Tables/SeedTable.asset");
        }
        EditorUtility.SetDirty(inv);

        // 场景里的 PlantingSystem 若还没配 availablePlants，用种子表补上（不覆盖用户已配内容）
        PlantingSystem planting = Object.FindObjectOfType<PlantingSystem>();
        if (planting != null && planting.availablePlants != null && planting.availablePlants.Length == 0)
        {
            PlantConfig[] configs = inv.seedTable != null ? inv.seedTable.GetUnlockedConfigs() : new PlantConfig[0];
            if (configs.Length > 0)
            {
                planting.availablePlants = configs;
                EditorUtility.SetDirty(planting);
                Debug.Log("[UI] 已用种子表补齐 PlantingSystem.availablePlants（" + configs.Length + " 个）。");
            }
        }
        return root;
    }
}
