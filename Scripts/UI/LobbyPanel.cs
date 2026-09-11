using System.Collections.Generic;
using HexUI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 联机大厅面板（热键 F2；启动时由 NetLobbyBridge 自动打开）。
///
/// 功能：
///   玩家名称（本地缓存，回填 / 失焦保存）
///   主机：选地图 → 创建主机（地图列表 = persistentDataPath 下的 .map，与存档菜单同源）
///   加入：局域网服务器列表（刷新 + 点击连接）+ 手动输入 IP 直连
///   状态区：连接中 / 已连接 / 失败原因 / 断开
///
/// 分层：本类只认 NetLobbyBridge（桥接器），不直接引用 Mirror / NetGameManager。
/// </summary>
public class LobbyPanel : UIPanel
{
    [Header("控件（装配工具自动填；缺失时按名字兜底查找）")]
    public Text titleText;
    public Text nameLabel;
    public InputField nameInput;
    public Text mapLabel;
    public Dropdown mapDropdown;
    public Button hostButton;
    public Text hostInfoText;
    public Button refreshButton;
    public Text serverListLabel;
    public Transform serverListContent;
    public Text addressLabel;
    public InputField addressInput;
    public Button joinButton;
    public Button stopButton;
    public Text statusText;
    public Button closeButton;
    public Text hintText;

    [Header("引用 / 资源")]
    public NetLobbyBridge bridge;
    public LobbyServerRow serverRowPrefab;
    public string serverRowResourcePath = "UI/Panels/LobbyServerRow";

    readonly List<LobbyServerRow> rows = new List<LobbyServerRow>();
    bool subscribed;
    string lastRowSignature = "";
    bool refreshing;

    protected override void OnCreate()
    {
        if (titleText == null) titleText = FindChild<Text>("Title");
        if (nameLabel == null) nameLabel = FindChild<Text>("NameLabel");
        if (nameInput == null) nameInput = FindChild<InputField>("NameInput");
        if (mapLabel == null) mapLabel = FindChild<Text>("MapLabel");
        if (mapDropdown == null) mapDropdown = FindChild<Dropdown>("MapDropdown");
        if (hostButton == null) hostButton = FindChild<Button>("HostButton");
        if (hostInfoText == null) hostInfoText = FindChild<Text>("HostInfoText");
        if (refreshButton == null) refreshButton = FindChild<Button>("RefreshButton");
        if (serverListLabel == null) serverListLabel = FindChild<Text>("ServerListLabel");
        if (addressLabel == null) addressLabel = FindChild<Text>("AddressLabel");
        if (addressInput == null) addressInput = FindChild<InputField>("AddressInput");
        if (joinButton == null) joinButton = FindChild<Button>("JoinButton");
        if (stopButton == null) stopButton = FindChild<Button>("StopButton");
        if (statusText == null) statusText = FindChild<Text>("StatusText");
        if (closeButton == null) closeButton = FindChild<Button>("CloseButton");
        if (hintText == null) hintText = FindChild<Text>("HintText");

        if (serverListContent == null)
        {
            Transform found = FindChild<Transform>("ServerList");
            if (found != null)
            {
                Transform viewport = found.Find("Viewport");
                Transform content = viewport != null ? viewport.Find("Content") : null;
                if (content != null) serverListContent = content;
            }
        }
        if (serverRowPrefab == null && !string.IsNullOrEmpty(serverRowResourcePath))
        {
            GameObject go = Resources.Load<GameObject>(serverRowResourcePath);
            if (go != null) serverRowPrefab = go.GetComponent<LobbyServerRow>();
        }

        // 静态文案（走文案表，改表即可换字）
        if (titleText != null) titleText.text = UIText.Get("lobby.title", "局域网联机");
        if (nameLabel != null) nameLabel.text = UIText.Get("lobby.playerName", "玩家名称");
        if (mapLabel != null) mapLabel.text = UIText.Get("lobby.map", "地图");
        if (serverListLabel != null) serverListLabel.text = UIText.Get("lobby.serverList", "局域网内的服务器");
        if (addressLabel != null) addressLabel.text = UIText.Get("lobby.address", "主机地址");
        if (hintText != null) hintText.text = UIText.Get("lobby.hint", "");
        SetButtonLabel(hostButton, UIText.Get("lobby.host", "创建主机"));
        SetButtonLabel(refreshButton, UIText.Get("ui.common.refresh", "刷新"));
        SetButtonLabel(joinButton, UIText.Get("lobby.connect", "连接"));
        SetButtonLabel(stopButton, UIText.Get("lobby.disconnect", "断开"));
        SetButtonLabel(closeButton, UIText.Get("ui.common.close", "关闭"));
        if (nameInput != null && nameInput.placeholder != null)
        {
            Text ph = nameInput.placeholder.GetComponent<Text>();
            if (ph != null) ph.text = UIText.Get("lobby.namePlaceholder", "输入你的昵称");
        }

        // 按钮绑定（全部代码绑定，prefab 无需配 UnityEvent）
        if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
        if (refreshButton != null) refreshButton.onClick.AddListener(OnRefreshClicked);
        if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
        if (stopButton != null) stopButton.onClick.AddListener(OnStopClicked);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (nameInput != null) nameInput.onEndEdit.AddListener(OnNameEdited);
        if (mapDropdown != null) mapDropdown.onValueChanged.AddListener(OnMapChanged);
        if (addressInput != null) addressInput.onEndEdit.AddListener(OnAddressEdited);
    }

    protected override void OnShow(object payload)
    {
        EnsureRefs();
        Subscribe();
        RefreshAll(force: true);
    }

    protected override void OnHide()
    {
        // 保留实例：下次打开更快
    }

    void OnDestroy()
    {
        if (subscribed && bridge != null) bridge.Changed -= OnBridgeChanged;
        subscribed = false;
    }

    public override void OnRefresh()
    {
        RefreshAll(force: true);
    }

    /// <summary>供外部（测试 / 代码）强制刷新一次。</summary>
    public void RefreshFromBridge()
    {
        RefreshAll(force: true);
    }

    void EnsureRefs()
    {
        if (bridge == null) bridge = NetLobbyBridge.Instance;
        if (bridge == null) bridge = FindObjectOfType<NetLobbyBridge>();
    }

    void Subscribe()
    {
        if (subscribed || bridge == null) return;
        bridge.Changed += OnBridgeChanged;
        subscribed = true;
    }

    void OnBridgeChanged()
    {
        RefreshAll();
    }

    static void SetButtonLabel(Button button, string text)
    {
        if (button == null) return;
        Text label = button.GetComponentInChildren<Text>(true);
        if (label != null) label.text = text;
    }

    // ================= 刷新 =================

    void RefreshAll(bool force = false)
    {
        EnsureRefs();
        if (bridge == null)
        {
            if (statusText != null) statusText.text = "未找到 NetLobbyBridge（先跑装配菜单）";
            return;
        }
        if (refreshing && !force) return;
        refreshing = true;

        bool online = bridge.IsOnline;
        bool connecting = bridge.IsConnecting;

        // 玩家名
        if (nameInput != null && !nameInput.isFocused)
        {
            string mine = bridge.PlayerName;
            if (nameInput.text != mine) nameInput.text = mine;
        }

        // 地图下拉
        if (mapDropdown != null)
        {
            string[] maps = bridge.MapNames;
            int wanted = maps.Length + 1;
            if (mapDropdown.options.Count != wanted)
            {
                mapDropdown.ClearOptions();
                List<Dropdown.OptionData> options = new List<Dropdown.OptionData>();
                options.Add(new Dropdown.OptionData(UIText.Get("lobby.mapCurrent", "（当前地图）")));
                for (int i = 0; i < maps.Length; i++) options.Add(new Dropdown.OptionData(maps[i]));
                mapDropdown.AddOptions(options);
                mapDropdown.value = 0;
                mapDropdown.RefreshShownValue();
            }
            if (mapDropdown.captionText != null && mapDropdown.value < mapDropdown.options.Count)
            {
                mapDropdown.captionText.text = mapDropdown.options[mapDropdown.value].text;
            }
        }

        // 主机信息
        if (hostInfoText != null)
        {
            if (bridge.IsHosting)
            {
                hostInfoText.text = "本机地址 " + bridge.LocalAddress + "（玩家用这个 IP 直连）";
            }
            else if (online)
            {
                hostInfoText.text = "已加入 " + (bridge.StatusText ?? "");
            }
            else
            {
                hostInfoText.text = "";
            }
        }

        // 服务器列表
        RefreshServerRows();

        // 状态 / 按钮可用性
        if (statusText != null) statusText.text = UIText.Get("lobby.statusPrefix", "状态：") + bridge.StatusText;
        if (hostButton != null) hostButton.interactable = !online;
        if (joinButton != null) joinButton.interactable = !online && !connecting;
        if (refreshButton != null) refreshButton.interactable = !online && !connecting;
        if (stopButton != null) stopButton.interactable = online || connecting;
        if (mapDropdown != null) mapDropdown.interactable = !online;
        if (addressInput != null) addressInput.interactable = !online && !connecting;

        refreshing = false;
    }

    void RefreshServerRows()
    {
        if (serverListContent == null || serverRowPrefab == null || bridge == null) return;

        IReadOnlyList<LobbyServerInfo> list = bridge.Servers;
        System.Text.StringBuilder sig = new System.Text.StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            sig.Append(list[i].serverId).Append('|').Append(list[i].playerCount).Append(';');
        }
        string signature = sig.ToString();
        if (signature == lastRowSignature) return;
        lastRowSignature = signature;

        while (rows.Count < list.Count)
        {
            LobbyServerRow row = Instantiate(serverRowPrefab, serverListContent, false);
            row.name = "ServerRow_" + rows.Count;
            rows.Add(row);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            bool used = i < list.Count;
            rows[i].gameObject.SetActive(used);
            if (used) rows[i].Bind(list[i], this);
        }
    }

    // ================= 交互 =================

    void OnNameEdited(string value)
    {
        if (bridge != null) bridge.PlayerName = value;
        RefreshAll();
    }

    void OnAddressEdited(string value)
    {
        // 失焦时不做动作（点"连接"才连）
    }

    void OnMapChanged(int index)
    {
        if (mapDropdown != null && mapDropdown.captionText != null && index < mapDropdown.options.Count)
        {
            mapDropdown.captionText.text = mapDropdown.options[index].text;
        }
    }

    /// <summary>当前选择的地图名（"" = 用当前地图不换图）。</summary>
    string SelectedMap()
    {
        if (mapDropdown == null || bridge == null) return "";
        int index = mapDropdown.value;
        if (index <= 0) return "";
        string[] maps = bridge.MapNames;
        int mapIndex = index - 1;
        return (mapIndex >= 0 && mapIndex < maps.Length) ? maps[mapIndex] : "";
    }

    void OnHostClicked()
    {
        if (bridge == null) return;
        string name = nameInput != null ? nameInput.text : null;
        string map = SelectedMap();
        bridge.Host(name, map);
        RefreshAll(force: true);
    }

    void OnJoinClicked()
    {
        if (bridge == null) return;
        string address = addressInput != null ? addressInput.text : "";
        if (string.IsNullOrEmpty(address)) address = "localhost";
        string name = nameInput != null ? nameInput.text : null;
        bridge.Join(address, name);
        RefreshAll(force: true);
    }

    void OnRefreshClicked()
    {
        if (bridge == null) return;
        bridge.ClearServers();
        bridge.RefreshServers();
        RefreshAll(force: true);
    }

    void OnStopClicked()
    {
        if (bridge == null) return;
        bridge.StopNet();
        RefreshAll(force: true);
    }

    /// <summary>点击某一行服务器 → 直接连接该地址。</summary>
    public void OnServerRowClicked(LobbyServerRow row)
    {
        if (row == null || bridge == null) return;
        if (addressInput != null) addressInput.text = row.Address;
        bridge.Join(row.Address, nameInput != null ? nameInput.text : null);
        RefreshAll(force: true);
    }
}
