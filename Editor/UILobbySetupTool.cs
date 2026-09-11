using HexUI;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「联机大厅 UI」一键装配（幂等）。
///
/// 产物：
///   1. 行 prefab   Assets/Resources/UI/Panels/LobbyServerRow.prefab
///   2. 面板 prefab Assets/Resources/UI/Panels/LobbyPanel.prefab（玩家名 / 选地图 / 创建主机 / 服务器列表 / IP 直连 / 状态）
///   3. 面板注册    id = "lobby"（Normal 层 / 模态 / 阻塞输入 / 热键 F2 / Esc 可关）
///   4. 场景 NetRoot：补 NetLobbyDiscovery（局域网发现，带房主名/人数/地图）+ NetLobbyBridge（大厅桥接）
///   5. EllenNet.prefab：补 NetPlayerName（玩家名 SyncVar 同步）
///   6. 同步 UIPanelTable.csv
///
/// 场景只标脏不保存（由你 Ctrl+S）。前置：Tools/多人联机/一键装配 EllenNet + NetRoot（NetRoot 必须存在）。
/// </summary>
public static class UILobbySetupTool
{
    const string PanelsDir = "Assets/Resources/UI/Panels";
    const string RowPrefabPath = PanelsDir + "/LobbyServerRow.prefab";
    const string PanelPrefabPath = PanelsDir + "/LobbyPanel.prefab";
    const string TablePath = "Assets/Resources/Configs/Tables/UIPanelTable.asset";
    const string EllenNetPath = "Assets/Resources/Prefabs/EllenNet.prefab";
    const string NetRootName = "NetRoot";

    public const string PanelId = "lobby";

    [MenuItem("Tools/UI/一键装配 联机大厅 UI (lobby)", false, 4)]
    public static void Setup()
    {
        if (GameObject.Find(UISetupTool.RootName) == null) UISetupTool.Setup();
        if (GameObject.Find(NetRootName) == null)
        {
            Debug.LogError("[UI] 场景里没有 " + NetRootName + "：先执行 Tools/多人联机/一键装配 EllenNet + NetRoot");
            return;
        }

        GameObject row = EnsureRowPrefab();
        GameObject panel = EnsurePanelPrefab(row);
        RegisterPanel(panel);
        WireNetRoot();
        AddPlayerNameToEllenNet();

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        TableImporter.ExportPanels();
        AssetDatabase.SaveAssets();
        Debug.Log("[UI] 联机大厅装配完成（场景未保存，请 Ctrl+S）。\n" +
                  "  F2 打开大厅；两人联机：一端点\"创建主机\"，另一端\"刷新\"后在列表里点房间，或直接输入房主 IP 再点连接。");
    }

    // ================= 行 prefab =================

    static GameObject EnsureRowPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(RowPrefabPath);
        if (existing != null && existing.GetComponent<LobbyServerRow>() != null) return existing;

        UISetupTool.EnsureFolder(PanelsDir);

        GameObject root = new GameObject("LobbyServerRow", typeof(RectTransform), typeof(Image), typeof(LobbyServerRow));
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(560f, 56f);
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0.17f, 0.20f, 0.26f, 0.95f);

        Text label = UISetupTool.NewText(root.transform, "Label", "房主 · 人数 1/8 · 地图", 20,
            TextAnchor.MiddleLeft, Color.white);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(16f, 0f);
        label.rectTransform.offsetMax = new Vector2(-12f, 0f);
        label.raycastTarget = false;

        LobbyServerRow view = root.GetComponent<LobbyServerRow>();
        view.label = label;
        view.background = bg;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RowPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[UI] 已创建大厅行 prefab " + RowPrefabPath);
        return prefab;
    }

    // ================= 面板 prefab =================

    static GameObject EnsurePanelPrefab(GameObject rowPrefab)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPrefabPath);
        if (existing != null && existing.GetComponent<LobbyPanel>() != null) return existing;

        UISetupTool.EnsureFolder(PanelsDir);

        GameObject root = new GameObject("LobbyPanel", typeof(RectTransform), typeof(Image), typeof(LobbyPanel));
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1000f, 680f);
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0.10f, 0.11f, 0.14f, 0.99f);

        // ---- 标题 ----
        Text title = UISetupTool.NewText(root.transform, "Title", "局域网联机", 34, TextAnchor.MiddleLeft, Color.white);
        AnchorTopStretch(title.rectTransform, 14f, 56f, 28f, 28f);

        // ---- 左列：创建主机 ----
        RectTransform left = NewSection(root.transform, "HostSection", new Vector2(-245f, -30f), new Vector2(450f, 400f));
        UISetupTool.NewText(left, "MapLabel", "地图", 22, TextAnchor.MiddleLeft, new Color(0.8f, 0.85f, 0.92f));
        AnchorInside(left, "MapLabel", new Vector2(0f, 168f), new Vector2(430f, 32f));
        Dropdown map = NewDropdown(left, "MapDropdown", new Vector2(430f, 52f), new Vector2(0f, 122f));
        Button host = UISetupTool.NewButton(left, "HostButton", "创建主机", new Vector2(430f, 64f), new Vector2(0f, 40f), null);
        SetAnchorCenter(host.GetComponent<RectTransform>(), new Vector2(0f, 40f), new Vector2(430f, 64f));
        Text hostInfo = UISetupTool.NewText(left, "HostInfoText", "", 20, TextAnchor.UpperLeft, new Color(0.7f, 0.9f, 0.75f));
        SetAnchorCenter(hostInfo.rectTransform, new Vector2(0f, -60f), new Vector2(430f, 90f));

        // ---- 右列：加入 ----
        RectTransform right = NewSection(root.transform, "JoinSection", new Vector2(245f, -30f), new Vector2(450f, 400f));
        UISetupTool.NewText(right, "ServerListLabel", "局域网内的服务器", 22, TextAnchor.MiddleLeft, new Color(0.8f, 0.85f, 0.92f));
        AnchorInside(right, "ServerListLabel", new Vector2(-40f, 168f), new Vector2(300f, 32f));
        Button refresh = UISetupTool.NewButton(right, "RefreshButton", "刷新", new Vector2(120f, 44f), new Vector2(155f, 168f), null);
        SetAnchorCenter(refresh.GetComponent<RectTransform>(), new Vector2(155f, 168f), new Vector2(120f, 44f));
        NewServerList(right, "ServerList", new Vector2(0f, -10f), new Vector2(430f, 300f), rowPrefab);

        // ---- 底部：玩家名 / 地址 / 操作 ----
        RectTransform bottom = NewSection(root.transform, "BottomSection", new Vector2(0f, -258f), new Vector2(940f, 150f));

        UISetupTool.NewText(bottom, "NameLabel", "玩家名称", 20, TextAnchor.MiddleLeft, new Color(0.8f, 0.85f, 0.92f));
        AnchorInside(bottom, "NameLabel", new Vector2(-320f, 122f), new Vector2(280f, 26f));
        InputField nameInput = NewInput(bottom, "NameInput", "输入你的昵称", new Vector2(-320f, 84f), new Vector2(280f, 52f));

        UISetupTool.NewText(bottom, "AddressLabel", "主机地址", 20, TextAnchor.MiddleLeft, new Color(0.8f, 0.85f, 0.92f));
        AnchorInside(bottom, "AddressLabel", new Vector2(-20f, 122f), new Vector2(280f, 26f));
        InputField addressInput = NewInput(bottom, "AddressInput", "例如 192.168.1.20", new Vector2(-20f, 84f), new Vector2(280f, 52f));

        Button join = UISetupTool.NewButton(bottom, "JoinButton", "连接", new Vector2(180f, 52f), new Vector2(190f, 84f), null);
        SetAnchorCenter(join.GetComponent<RectTransform>(), new Vector2(190f, 84f), new Vector2(180f, 52f));
        Button stop = UISetupTool.NewButton(bottom, "StopButton", "断开", new Vector2(140f, 48f), new Vector2(370f, 84f), null);
        SetAnchorCenter(stop.GetComponent<RectTransform>(), new Vector2(370f, 84f), new Vector2(140f, 48f));

        Text status = UISetupTool.NewText(bottom, "StatusText", "状态：未连接", 20, TextAnchor.MiddleLeft, new Color(0.9f, 0.9f, 0.7f));
        SetAnchorCenter(status.rectTransform, new Vector2(-150f, 22f), new Vector2(620f, 44f));
        Button close = UISetupTool.NewButton(bottom, "CloseButton", "关闭", new Vector2(140f, 44f), new Vector2(370f, 22f), null);
        SetAnchorCenter(close.GetComponent<RectTransform>(), new Vector2(370f, 22f), new Vector2(140f, 44f));

        Text hint = UISetupTool.NewText(root.transform, "HintText",
            "房主：选地图 → 创建主机；其他人：刷新列表后点房间，或输入 IP 直连", 18,
            TextAnchor.MiddleCenter, new Color(0.68f, 0.72f, 0.8f));
        hint.rectTransform.anchorMin = new Vector2(0f, 0f);
        hint.rectTransform.anchorMax = new Vector2(1f, 0f);
        hint.rectTransform.pivot = new Vector2(0.5f, 0f);
        hint.rectTransform.sizeDelta = new Vector2(-56f, 26f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, 8f);

        // ---- 接线 ----
        LobbyPanel panel = root.GetComponent<LobbyPanel>();
        panel.titleText = title;
        panel.mapLabel = left.Find("MapLabel").GetComponent<Text>();
        panel.mapDropdown = map;
        panel.hostButton = host;
        panel.hostInfoText = hostInfo;
        panel.serverListLabel = right.Find("ServerListLabel").GetComponent<Text>();
        panel.refreshButton = refresh;
        panel.serverListContent = right.Find("ServerList/Viewport/Content");
        panel.nameLabel = bottom.Find("NameLabel").GetComponent<Text>();
        panel.nameInput = nameInput;
        panel.addressLabel = bottom.Find("AddressLabel").GetComponent<Text>();
        panel.addressInput = addressInput;
        panel.joinButton = join;
        panel.stopButton = stop;
        panel.statusText = status;
        panel.closeButton = close;
        panel.hintText = hint;
        panel.serverRowPrefab = rowPrefab.GetComponent<LobbyServerRow>();
        panel.serverRowResourcePath = "UI/Panels/LobbyServerRow";

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[UI] 已创建大厅面板 prefab " + PanelPrefabPath);
        return prefab;
    }

    // ================= 面板注册 / 联机接线 =================

    static void RegisterPanel(GameObject panelPrefab)
    {
        UIPanelTable table = AssetDatabase.LoadAssetAtPath<UIPanelTable>(TablePath);
        if (table == null)
        {
            Debug.LogError("[UI] 找不到面板表 " + TablePath);
            return;
        }

        UIPanelTable.Entry e = table.GetOrCreate(PanelId);
        e.id = PanelId;
        e.prefab = panelPrefab;
        e.prefabPath = "UI/Panels/LobbyPanel";
        e.layer = UIPanelLayer.Normal;
        e.modal = true;
        e.exclusiveGroup = "";
        e.escClose = true;
        e.hotkey = KeyCode.F2;
        e.blockGameInput = true;
        e.preload = false;
        e.order = 1;

        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
    }

    static void WireNetRoot()
    {
        GameObject root = GameObject.Find(NetRootName);
        if (root == null) return;

        NetLobbyDiscovery discovery = root.GetComponent<NetLobbyDiscovery>();
        if (discovery == null) discovery = root.AddComponent<NetLobbyDiscovery>();
        if (discovery.transport == null) discovery.transport = root.GetComponent<Transport>();
        if (discovery.OnServerFound == null)
        {
            discovery.OnServerFound = new Mirror.Discovery.ServerFoundUnityEvent<NetLobbyResponse>();
        }
        EditorUtility.SetDirty(discovery);

        NetLobbyBridge bridge = root.GetComponent<NetLobbyBridge>();
        if (bridge == null) bridge = root.AddComponent<NetLobbyBridge>();
        if (bridge.manager == null) bridge.manager = root.GetComponent<NetGameManager>();
        if (bridge.discovery == null) bridge.discovery = discovery;
        if (bridge.mirrorHud == null)
        {
            // 存组件引用（不是 GameObject）：HUD 与 NetRoot 同物体，只能禁用它，
            // 不能隐藏物体，否则会停掉整个联机根节点。
            bridge.mirrorHud = root.GetComponent<NetworkManagerHUD>();
            if (bridge.mirrorHud == null) bridge.mirrorHud = Object.FindObjectOfType<NetworkManagerHUD>(true);
        }
        EditorUtility.SetDirty(bridge);
        Debug.Log("[UI] NetRoot 已补装 NetLobbyDiscovery + NetLobbyBridge");
    }

    static void AddPlayerNameToEllenNet()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EllenNetPath);
        if (prefab == null)
        {
            Debug.LogWarning("[UI] 找不到 " + EllenNetPath + "，跳过玩家名组件（先跑 Tools/多人联机/一键装配）");
            return;
        }
        if (prefab.GetComponent<NetPlayerName>() != null)
        {
            Debug.Log("[UI] EllenNet 已有 NetPlayerName，跳过");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(EllenNetPath);
        contents.AddComponent<NetPlayerName>();
        PrefabUtility.SaveAsPrefabAsset(contents, EllenNetPath);
        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.SaveAssets();
        Debug.Log("[UI] 已给 EllenNet 添加 NetPlayerName（玩家名同步）");
    }

    // ================= 控件构造小工具 =================

    static RectTransform NewSection(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    static void SetAnchorCenter(RectTransform rt, Vector2 pos, Vector2 size)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    static void AnchorTopStretch(RectTransform rt, float topOffset, float height, float left, float right)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(-(left + right), height);
        rt.anchoredPosition = new Vector2((left - right) * 0.5f, -topOffset);
    }

    /// <summary>把刚创建的某个子控件（按名字）定位到容器中心坐标系。</summary>
    static void AnchorInside(Transform container, string childName, Vector2 pos, Vector2 size)
    {
        Transform child = container.Find(childName);
        if (child == null) return;
        SetAnchorCenter(child as RectTransform, pos, size);
    }

    static InputField NewInput(Transform parent, string name, string placeholder, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        SetAnchorCenter(rt, pos, size);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.16f, 0.17f, 0.21f, 1f);

        Text text = UISetupTool.NewText(go.transform, "Text", "", 22, TextAnchor.MiddleLeft, Color.white);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(12f, 4f);
        text.rectTransform.offsetMax = new Vector2(-12f, -4f);
        text.supportRichText = false;

        Text ph = UISetupTool.NewText(go.transform, "Placeholder", placeholder, 22, TextAnchor.MiddleLeft,
            new Color(0.6f, 0.63f, 0.7f, 0.9f));
        ph.rectTransform.anchorMin = Vector2.zero;
        ph.rectTransform.anchorMax = Vector2.one;
        ph.rectTransform.offsetMin = new Vector2(12f, 4f);
        ph.rectTransform.offsetMax = new Vector2(-12f, -4f);
        ph.fontStyle = FontStyle.Italic;

        InputField field = go.GetComponent<InputField>();
        field.targetGraphic = img;
        field.textComponent = text;
        field.placeholder = ph;
        field.lineType = InputField.LineType.SingleLine;
        return field;
    }

    /// <summary>服务器列表（ScrollRect + Viewport + 垂直布局 Content）。</summary>
    static ScrollRect NewServerList(Transform parent, string name, Vector2 pos, Vector2 size, GameObject rowPrefab)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        SetAnchorCenter(rt, pos, size);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.08f, 0.09f, 0.11f, 0.9f);

        GameObject vp = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        RectTransform vpRT = vp.GetComponent<RectTransform>();
        vpRT.SetParent(go.transform, false);
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = new Vector2(6f, 6f);
        vpRT.offsetMax = new Vector2(-6f, -6f);
        Image vpImg = vp.GetComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.01f);
        Mask mask = vp.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform contentRT = content.GetComponent<RectTransform>();
        contentRT.SetParent(vp.transform, false);
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.sizeDelta = new Vector2(0f, 0f);
        contentRT.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scroll = go.GetComponent<ScrollRect>();
        scroll.content = contentRT;
        scroll.viewport = vpRT;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return scroll;
    }

    /// <summary>标准 UGUI 下拉框（Label + Template(Viewport/Content/Item)）。</summary>
    static Dropdown NewDropdown(Transform parent, string name, Vector2 size, Vector2 pos)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Dropdown));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        SetAnchorCenter(rt, pos, size);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.16f, 0.17f, 0.21f, 1f);

        Text caption = UISetupTool.NewText(go.transform, "Label", "（当前地图）", 22, TextAnchor.MiddleLeft, Color.white);
        caption.rectTransform.anchorMin = Vector2.zero;
        caption.rectTransform.anchorMax = Vector2.one;
        caption.rectTransform.offsetMin = new Vector2(14f, 4f);
        caption.rectTransform.offsetMax = new Vector2(-14f, -4f);
        caption.raycastTarget = false;

        // Template（保持 inactive；Dropdown 展开时克隆它）
        GameObject tpl = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        RectTransform tplRT = tpl.GetComponent<RectTransform>();
        tplRT.SetParent(go.transform, false);
        tplRT.anchorMin = new Vector2(0f, 0f);
        tplRT.anchorMax = new Vector2(1f, 0f);
        tplRT.pivot = new Vector2(0.5f, 1f);
        tplRT.sizeDelta = new Vector2(0f, 220f);
        tplRT.anchoredPosition = new Vector2(0f, -2f);
        Image tplImg = tpl.GetComponent<Image>();
        tplImg.color = new Color(0.13f, 0.14f, 0.18f, 0.98f);

        GameObject vp = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        RectTransform vpRT = vp.GetComponent<RectTransform>();
        vpRT.SetParent(tpl.transform, false);
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = new Vector2(2f, 2f);
        vpRT.offsetMax = new Vector2(-2f, -2f);
        Image vpImg = vp.GetComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.01f);
        Mask mask = vp.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform));
        RectTransform contentRT = content.GetComponent<RectTransform>();
        contentRT.SetParent(vp.transform, false);
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.sizeDelta = new Vector2(0f, 44f);
        contentRT.anchoredPosition = Vector2.zero;

        GameObject item = new GameObject("Item", typeof(RectTransform), typeof(Image), typeof(Toggle));
        RectTransform itemRT = item.GetComponent<RectTransform>();
        itemRT.SetParent(content.transform, false);
        itemRT.anchorMin = new Vector2(0f, 0.5f);
        itemRT.anchorMax = new Vector2(1f, 0.5f);
        itemRT.pivot = new Vector2(0.5f, 0.5f);
        itemRT.sizeDelta = new Vector2(0f, 44f);
        Image itemImg = item.GetComponent<Image>();
        itemImg.color = new Color(0.17f, 0.19f, 0.24f, 1f);

        GameObject itemBg = new GameObject("Item Background", typeof(RectTransform), typeof(Image));
        RectTransform itemBgRT = itemBg.GetComponent<RectTransform>();
        itemBgRT.SetParent(item.transform, false);
        itemBgRT.anchorMin = Vector2.zero;
        itemBgRT.anchorMax = Vector2.one;
        itemBgRT.offsetMin = Vector2.zero;
        itemBgRT.offsetMax = Vector2.zero;
        Image itemBgImg = itemBg.GetComponent<Image>();
        itemBgImg.color = new Color(0.17f, 0.19f, 0.24f, 1f);

        GameObject check = new GameObject("Item Checkmark", typeof(RectTransform), typeof(Image));
        RectTransform checkRT = check.GetComponent<RectTransform>();
        checkRT.SetParent(item.transform, false);
        checkRT.anchorMin = new Vector2(0f, 0.5f);
        checkRT.anchorMax = new Vector2(0f, 0.5f);
        checkRT.pivot = new Vector2(0f, 0.5f);
        checkRT.sizeDelta = new Vector2(20f, 20f);
        checkRT.anchoredPosition = new Vector2(8f, 0f);
        Image checkImg = check.GetComponent<Image>();
        checkImg.color = new Color(0.4f, 0.9f, 0.5f, 1f);

        Text itemLabel = UISetupTool.NewText(item.transform, "Item Label", "选项", 22, TextAnchor.MiddleLeft, Color.white);
        itemLabel.rectTransform.anchorMin = Vector2.zero;
        itemLabel.rectTransform.anchorMax = Vector2.one;
        itemLabel.rectTransform.offsetMin = new Vector2(34f, 2f);
        itemLabel.rectTransform.offsetMax = new Vector2(-8f, -2f);

        Toggle toggle = item.GetComponent<Toggle>();
        toggle.targetGraphic = itemBgImg;
        toggle.graphic = checkImg;

        ScrollRect scroll = tpl.GetComponent<ScrollRect>();
        scroll.content = contentRT;
        scroll.viewport = vpRT;
        scroll.horizontal = false;

        Dropdown dd = go.GetComponent<Dropdown>();
        dd.targetGraphic = img;
        dd.captionText = caption;
        dd.template = tplRT;
        dd.itemText = itemLabel;
        dd.itemImage = itemBgImg;

        tpl.SetActive(false);
        return dd;
    }
}
