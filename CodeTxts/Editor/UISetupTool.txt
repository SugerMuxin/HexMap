using System.IO;
using HexUI;
using HexUI.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 框架一键装配工具（幂等，可重复运行）。
///
/// 产物：
///   1. 面板表资产 Assets/Resources/Configs/Tables/UIPanelTable.asset
///   2. 自检面板 prefab Assets/Resources/UI/Panels/UIDemoPanel.prefab（并注册 id = demo / demo.second）
///   3. 场景对象 [UIRoot]：Canvas(ScreenSpaceOverlay 1920x1080, sortingOrder 100) + CanvasScaler +
///      GraphicRaycaster + UIManager + UICharacterInputGate
///        Panels/      常规面板容器（Background / Normal）
///        Modal Mask   模态遮罩
///        Popups/      弹窗容器（Popup / Overlay）
///
/// 场景只标记为脏，**不自动保存**（由你 Ctrl+S 决定）。
/// 现有 4 个 uGUI 菜单（Hex Map Editor / New Map Menu / SaveLoadMenu / Features Editor）不受影响。
/// </summary>
public static class UISetupTool
{
    public const string RootName = "[UIRoot]";
    public const string TablePath = "Assets/Resources/Configs/Tables/UIPanelTable.asset";
    public const string PanelPrefabDir = "Assets/Resources/UI/Panels";
    public const string DemoPanelPath = PanelPrefabDir + "/UIDemoPanel.prefab";

    [MenuItem("Tools/UI/一键装配 UI 框架 (UIRoot)", false, 1)]
    public static void Setup()
    {
        UIPanelTable table = EnsureTable();
        GameObject demoPrefab = EnsureDemoPanelPrefab();
        RegisterDemoEntries(table, demoPrefab);
        GameObject root = EnsureRoot(table);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[UI] 装配完成：场景 " + root.scene.name + " 已生成 " + RootName +
                  "（未保存，请自行 Ctrl+S）。\n面板表：" + TablePath +
                  "\n自检面板：F1 开关（demo），面板内按钮可再压一层（demo.second）。");
    }

    [MenuItem("Tools/UI/一键装配 全部 UI（框架 + 背包 + 大厅）", false, 5)]
    public static void SetupAllUI()
    {
        Setup();                       // 1. 框架（UIRoot / 面板表 / 容器 / 遮罩）
        UIBagSetupTool.Setup();        // 2. 背包（bag 表项 + 卡片/面板 prefab + [Inventory]）

        if (GameObject.Find("NetRoot") == null)
        {
            Debug.LogWarning("[UI] 场景里没有 NetRoot，已跳过联机大厅装配。\n" +
                             "  需要联机时先执行 Tools/多人联机/一键装配 EllenNet + NetRoot，再单独跑 Tools/UI/一键装配 联机大厅 UI (lobby)。");
        }
        else
        {
            UILobbySetupTool.Setup();  // 3. 联机大厅（lobby 表项 + prefab + NetRoot/EllenNet 接线）
        }

        Debug.Log("[UI] 全部 UI 装配完成（场景未保存，请 Ctrl+S）。面板：F1 自检 / B 背包 / F2 联机大厅。");
    }

    [MenuItem("Tools/UI/检查 UI 装配状态", false, 41)]
    public static void VerifySetup()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("[UI] 装配状态：");
        sb.AppendLine("  " + RootName + "：" + (GameObject.Find(RootName) != null ? "存在" : "缺失"));
        sb.AppendLine("  [Inventory]：" + (Object.FindObjectOfType<InventorySystem>() != null ? "存在" : "缺失"));
        sb.AppendLine("  NetRoot：" + (GameObject.Find("NetRoot") != null ? "存在" : "缺失"));

        UIPanelTable table = AssetDatabase.LoadAssetAtPath<UIPanelTable>("Assets/Resources/Configs/Tables/UIPanelTable.asset");
        if (table == null)
        {
            sb.AppendLine("  面板表：缺失！（先跑 一键装配 UI 框架）");
        }
        else
        {
            for (int i = 0; i < table.entries.Length; i++)
            {
                UIPanelTable.Entry e = table.entries[i];
                sb.AppendLine("  面板 " + e.id + "：prefab=" + (e.prefab != null ? e.prefab.name : "空")
                              + " / 层=" + e.layer + " / 热键=" + e.hotkey + (e.blockGameInput ? " / 阻塞输入" : ""));
            }
        }
        sb.AppendLine("  文案表：Assets/Resources/Configs/Tables/UITextTable.asset（缺 key 时运行期按 fallback 显示并警告一次）");
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/UI/移除场景 UIRoot", false, 40)]
    public static void RemoveRoot()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.Log("[UI] 场景里没有 " + RootName + "，无需移除。");
            return;
        }
        Undo.DestroyObjectImmediate(root);
        Debug.Log("[UI] 已移除场景对象 " + RootName + "（面板表与 prefab 保留）。");
    }

    // ================= 资产 =================

    static UIPanelTable EnsureTable()
    {
        UIPanelTable table = AssetDatabase.LoadAssetAtPath<UIPanelTable>(TablePath);
        if (table != null) return table;

        EnsureFolder(Path.GetDirectoryName(TablePath).Replace("\\", "/"));
        table = ScriptableObject.CreateInstance<UIPanelTable>();
        AssetDatabase.CreateAsset(table, TablePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[UI] 已创建面板表 " + TablePath);
        return table;
    }

    static GameObject EnsureDemoPanelPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPanelPath);
        if (existing != null) return existing;

        EnsureFolder(PanelPrefabDir);

        GameObject root = new GameObject("UIDemoPanel", typeof(RectTransform), typeof(Image), typeof(UIDemoPanel));
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(720f, 420f);
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0.12f, 0.13f, 0.17f, 0.98f);

        Text title = NewText(root.transform, "Title", "UI 框架自检面板", 30, TextAnchor.MiddleCenter, Color.white);
        StretchTop(title.rectTransform, 8f, 60f);

        Text body = NewText(root.transform, "Body",
            "F1 开关本面板 · Esc 关闭栈顶 · 面板内按钮可再压一层。", 22, TextAnchor.UpperCenter,
            new Color(0.85f, 0.88f, 0.95f, 1f));
        body.rectTransform.anchorMin = new Vector2(0f, 0f);
        body.rectTransform.anchorMax = new Vector2(1f, 1f);
        body.rectTransform.offsetMin = new Vector2(28f, 110f);
        body.rectTransform.offsetMax = new Vector2(-28f, -72f);

        NewButton(root.transform, "CloseButton", "关闭", new Vector2(210f, 60f), new Vector2(-118f, 26f), null);
        NewButton(root.transform, "PushButton", "压入第二层", new Vector2(210f, 60f), new Vector2(118f, 26f), null);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, DemoPanelPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[UI] 已创建自检面板 prefab " + DemoPanelPath);
        return prefab;
    }

    static void RegisterDemoEntries(UIPanelTable table, GameObject demoPrefab)
    {
        UIPanelTable.Entry demo = table.GetOrCreate("demo");
        demo.prefab = demoPrefab;
        demo.prefabPath = "UI/Panels/UIDemoPanel";
        demo.layer = UIPanelLayer.Popup;
        demo.modal = true;
        demo.exclusiveGroup = "";
        demo.escClose = true;
        demo.hotkey = KeyCode.F1;
        demo.blockGameInput = true;
        demo.order = 0;

        UIPanelTable.Entry second = table.GetOrCreate("demo.second");
        second.prefab = demoPrefab;
        second.prefabPath = "UI/Panels/UIDemoPanel";
        second.layer = UIPanelLayer.Popup;
        second.modal = true;
        second.exclusiveGroup = "";
        second.escClose = true;
        second.hotkey = KeyCode.None;
        second.blockGameInput = true;
        second.order = 1;

        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
    }

    // ================= 场景 =================

    static GameObject EnsureRoot(UIPanelTable table)
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        UIManager manager = root.GetComponent<UIManager>();
        if (manager == null) manager = root.AddComponent<UIManager>();

        if (root.GetComponent<UICharacterInputGate>() == null) root.AddComponent<UICharacterInputGate>();

        Transform panels = EnsureRectChild(root.transform, "Panels", false);
        Transform mask = EnsureRectChild(root.transform, "Modal Mask", true);
        Transform popups = EnsureRectChild(root.transform, "Popups", false);

        // 静态停放顺序：Modal Mask -> Panels -> Popups
        // 遮罩放在面板容器【之下】是"安全默认值"——它是全屏 Image + Button，
        // 一旦盖在面板之上，点击面板任意位置都会被当成"点了遮罩"，面板立刻被关掉。
        // 运行期真正的遮挡关系由 UIManager.RefreshMask() 每次打开/关闭模态面板时重算：
        // 遮罩会被挪到"最上层模态面板"正下方（同一容器内），从而既挡住下面的面板与游戏世界，
        // 又不挡模态面板自己。（所以静态位置只是停放点，不影响运行时行为。）
        mask.SetSiblingIndex(0);
        panels.SetSiblingIndex(1);
        popups.SetSiblingIndex(2);

        Image maskImage = mask.GetComponent<Image>();
        maskImage.color = new Color(0f, 0f, 0f, 0.55f);
        maskImage.raycastTarget = true;
        if (mask.gameObject.activeSelf) mask.gameObject.SetActive(false);

        manager.table = table;
        manager.panelsRoot = panels;
        manager.popupsRoot = popups;
        manager.modalMask = maskImage;
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(root);
        return root;
    }

    internal static Transform EnsureRectChild(Transform parent, string childName, bool withImage)
    {
        Transform existing = parent.Find(childName);
        if (existing != null)
        {
            if (withImage && existing.GetComponent<Image>() == null) existing.gameObject.AddComponent<Image>();
            return existing;
        }

        GameObject go = withImage
            ? new GameObject(childName, typeof(RectTransform), typeof(Image))
            : new GameObject(childName, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    // ================= 小工具 =================

    internal static void EnsureFolder(string assetFolder)
    {
        string abs = Path.GetFullPath(assetFolder);
        if (!Directory.Exists(abs))
        {
            Directory.CreateDirectory(abs);
            AssetDatabase.Refresh();
        }
    }

    /// <summary>内置默认字体（Unity 2022 为 LegacyRuntime.ttf，旧版为 Arial.ttf）。</summary>
    internal static Font DefaultFont()
    {
        Font font = null;
        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch (System.Exception) { }
        if (font == null)
        {
            try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch (System.Exception) { }
        }
        return font;
    }

    internal static Text NewText(Transform parent, string name, string content, int fontSize, TextAnchor anchor, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        Font font = DefaultFont();
        if (font != null) text.font = font;
        return text;
    }

    internal static Button NewButton(Transform parent, string name, string label, Vector2 size, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPos;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0.24f, 0.28f, 0.36f, 1f);

        Button button = go.GetComponent<Button>();
        button.targetGraphic = img;
        if (onClick != null) button.onClick.AddListener(onClick);

        Text text = NewText(go.transform, "Label", label, 24, TextAnchor.MiddleCenter, Color.white);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        return button;
    }

    internal static void StretchTop(RectTransform rt, float topOffset, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = new Vector2(0f, -topOffset);
    }
}
