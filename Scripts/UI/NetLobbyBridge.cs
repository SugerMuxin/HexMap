using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

/// <summary>大厅里的一个可加入房间（由 NetLobbyBridge 从发现响应整理而来，UI 直接渲染）。</summary>
public struct LobbyServerInfo
{
    public long serverId;
    public string hostName;
    public string mapName;
    public string address;      // IP:Port（点击连接时用 address 部分）
    public int playerCount;
    public int maxPlayers;
    public bool inGame;
    public string version;
}

/// <summary>
/// 联机大厅 ↔ Mirror 的桥接器（游戏侧）。UI 只跟它打交道，不直接碰 Mirror / NetGameManager。
///
/// 职责：
///   1. 玩家名缓存（转发 NetPlayerName）；
///   2. 局域网房间发现（转发 NetLobbyDiscovery 的 OnServerFound → 去重列表 → 事件通知 UI）；
///   3. 开房（可选先载入所选地图 → StartHost → 开始广播）与加入（StartClient）；
///   4. 状态播报（未连接 / 连接中 / 已连接 / 失败 / 人数）与自动收起大厅；
///   5. 接管 Mirror 自带 NetworkManagerHUD 的显隐（自制大厅就绪后隐藏它）。
///
/// 装配：挂在场景 NetRoot（与 NetGameManager / NetLobbyDiscovery / KcpTransport 同物体），
/// 由 Tools/UI/一键装配 联机大厅 UI (lobby) 完成。
/// </summary>
[DisallowMultipleComponent]
public class NetLobbyBridge : MonoBehaviour
{
    public static NetLobbyBridge Instance { get; private set; }

    /// <summary>联机状态枚举（UI 用来决定哪些按钮可点）。</summary>
    public enum Session
    {
        Offline,      // 未联机
        Hosting,      // 我是房主
        Connecting,   // 正在连接
        Connected     // 已作为客户端连上
    }

    [Header("引用（留空自动查找）")]
    [Tooltip("联机管理器（NetworkManager 子类 NetGameManager）")]
    public NetGameManager manager;
    [Tooltip("局域网发现组件（NetLobbyDiscovery）")]
    public NetLobbyDiscovery discovery;
    [Tooltip("Mirror 自带 HUD 组件（自制大厅可用后禁用它；留空自动查找）")]
    public NetworkManagerHUD mirrorHud;

    [Header("行为")]
    [Tooltip("大厅面板 id（面板表里注册的 id）")]
    public string lobbyPanelId = "lobby";
    [Tooltip("启动时自动打开大厅面板")]
    public bool openLobbyOnStart = true;
    [Tooltip("开房 / 连接成功后自动收起大厅")]
    public bool closeLobbyOnConnect = true;
    [Tooltip("连接超时（秒），超时提示失败并断开")]
    public float connectTimeout = 12f;
    [Tooltip("默认端口（仅用于显示与默认地址）")]
    public ushort port = 7777;
    [Tooltip("隐藏 Mirror 自带 NetworkManagerHUD（保留组件，随时可关掉本项恢复）")]
    public bool hideMirrorHud = true;
    [Tooltip("发现游戏的广播间隔（秒）")]
    public float discoveryInterval = 3f;

    [Header("调试")]
    public bool logActions = true;

    /// <summary>状态 / 服务器列表 / 玩家名发生变化（UI 订阅刷新）。</summary>
    public event Action Changed;

    readonly Dictionary<long, LobbyServerInfo> servers = new Dictionary<long, LobbyServerInfo>();
    readonly List<LobbyServerInfo> serverList = new List<LobbyServerInfo>();

    Session session = Session.Offline;
    string statusText = "";
    float connectStartTime;
    bool subscribed;
    bool scanning;

    #region 只读状态

    /// <summary>当前联机会话状态。</summary>
    public Session State { get { return session; } }
    /// <summary>是否在联机（房主或客户端）。</summary>
    public bool IsOnline { get { return session != Session.Offline; } }
    /// <summary>是否是房主。</summary>
    public bool IsHosting { get { return session == Session.Hosting; } }
    /// <summary>是否正在连接。</summary>
    public bool IsConnecting { get { return session == Session.Connecting; } }
    /// <summary>是否正在扫描局域网。</summary>
    public bool IsScanning { get { return scanning; } }
    /// <summary>已连接玩家数（房主视角；客户端返回 1）。</summary>
    public int PlayerCount
    {
        get
        {
            if (NetworkServer.active) return NetworkServer.connections.Count;
            return NetworkClient.isConnected ? 1 : 0;
        }
    }
    /// <summary>人数上限。</summary>
    public int MaxPlayers { get { return manager != null ? manager.maxConnections : 0; } }
    /// <summary>状态文案（中文，直接显示）。</summary>
    public string StatusText { get { return statusText; } }
    /// <summary>本地玩家名（PlayerPrefs 缓存）。</summary>
    public string PlayerName
    {
        get { return NetPlayerName.CachedName; }
        set { NetPlayerName.SetLocalName(value); Raise(); }
    }
    /// <summary>当前地图名（空 = 未命名）。</summary>
    public string CurrentMapName { get { return MapFileUtil.CurrentMapName; } }
    /// <summary>存档目录里可用的地图名。</summary>
    public string[] MapNames { get { return MapFileUtil.ListMapNames(); } }
    /// <summary>发现的房间列表（按房主名排序）。</summary>
    public IReadOnlyList<LobbyServerInfo> Servers { get { return serverList; } }
    /// <summary>作为房主的本机地址（供玩家转告他人）。</summary>
    public string LocalAddress { get { return GetLocalIPv4() + ":" + port; } }

    #endregion

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Lobby] 场景中存在多个 NetLobbyBridge，禁用后来者：" + name);
            enabled = false;
            return;
        }
        Instance = this;
        ResolveRefs();
        if (string.IsNullOrEmpty(NetPlayerName.CachedName))
        {
            NetPlayerName.SetLocalName(SystemInfo.deviceName);   // 首次运行给个默认名，用户可改
        }
        SetStatus("未连接");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Unsubscribe();
    }

    void Start()
    {
        ResolveRefs();
        Subscribe();
        ApplyMirrorHudVisibility();
        if (openLobbyOnStart) OpenLobby();
    }

    void Update()
    {
        if (!subscribed) { ResolveRefs(); Subscribe(); }

        // 连接超时检测
        if (session == Session.Connecting && Time.time - connectStartTime > connectTimeout)
        {
            Debug.LogWarning("[Lobby] 连接超时：" + (manager != null ? manager.networkAddress : ""));
            StopNet();
            SetStatus("连接失败：超时（检查地址 / 防火墙 / 房主是否已开房）");
        }

        // 状态轮询（Mirror 的静态事件在 Shutdown 时会被清空，轮询最可靠）
        Session now = DetectSession();
        if (now != session)
        {
            session = now;
            if (session == Session.Hosting) SetStatus("你是房主 · " + LocalAddress + " · 玩家 " + PlayerCount);
            else if (session == Session.Connected) SetStatus("已连接 · " + manager.networkAddress);
            else SetStatus("未连接");
            OnSessionChanged();
        }
        else if (session != Session.Offline && session != Session.Connecting)
        {
            // 人数变化刷新（房主）
            string want = session == Session.Hosting
                ? "你是房主 · " + LocalAddress + " · 玩家 " + PlayerCount
                : "已连接 · " + manager.networkAddress;
            if (want != statusText) SetStatus(want);
        }

        if (session == Session.Connecting)
        {
            string connecting = "连接中… " + manager.networkAddress;
            if (connecting != statusText) SetStatus(connecting);
        }
    }

    #region 动作（UI 调用）

    /// <summary>扫描局域网房间。</summary>
    public void RefreshServers()
    {
        ResolveRefs();
        if (discovery == null)
        {
            SetStatus("未配置局域网发现组件（NetLobbyDiscovery）");
            return;
        }
        try
        {
            discovery.StartDiscovery();
            scanning = true;
            SetStatus("正在搜索局域网内的房间…");
            if (logActions) Debug.Log("[Lobby] 开始搜索局域网房间");
        }
        catch (Exception e)
        {
            SetStatus("搜索失败：" + e.Message);
        }
    }

    /// <summary>停止扫描。</summary>
    public void StopScan()
    {
        if (discovery == null) return;
        discovery.StopDiscovery();
        scanning = false;
        Raise();
    }

    /// <summary>清空已发现的房间列表。</summary>
    public void ClearServers()
    {
        servers.Clear();
        serverList.Clear();
        Raise();
    }

    /// <summary>
    /// 开房：保存玩家名 →（可选）载入所选地图 → StartHost → 开始广播。
    /// 返回是否成功发起。
    /// </summary>
    public bool Host(string playerName, string mapName = null)
    {
        ResolveRefs();
        if (manager == null)
        {
            SetStatus("场景里没有 NetGameManager（先跑 Tools/多人联机/一键装配）");
            return false;
        }
        if (manager.isNetworkActive)
        {
            SetStatus("已经在联机中，请先断开");
            return false;
        }

        if (!string.IsNullOrEmpty(playerName)) NetPlayerName.SetLocalName(playerName);

        if (!string.IsNullOrEmpty(mapName))
        {
            HexGrid grid = FindObjectOfType<HexGrid>();
            if (grid == null) SetStatus("场景里没有 HexGrid，跳过地图载入");
            else if (!MapFileUtil.Load(grid, mapName)) SetStatus("地图载入失败：" + mapName);
        }
        else
        {
            MapFileUtil.CurrentMapName = "";
        }

        if (discovery != null)
        {
            discovery.hostName = NetPlayerName.CachedName;
            discovery.mapName = MapFileUtil.CurrentMapName;
        }

        manager.StartHost();
        bool ok = manager.isNetworkActive;
        if (ok && discovery != null) discovery.AdvertiseServer();
        if (logActions) Debug.Log("[Lobby] 开房 " + ok + "（玩家名 " + NetPlayerName.CachedName + "，地图 " + MapFileUtil.CurrentMapName + "）");

        session = DetectSession();
        if (session == Session.Hosting) SetStatus("你是房主 · " + LocalAddress + " · 玩家 " + PlayerCount);
        else SetStatus("开房失败（看 Console 里的 Mirror 报错）");
        ApplyMirrorHudVisibility();
        OnSessionChanged();
        return ok;
    }

    /// <summary>加入房间：保存玩家名 → 设置地址 → StartClient（带超时）。</summary>
    public bool Join(string address, string playerName = null)
    {
        ResolveRefs();
        if (manager == null)
        {
            SetStatus("场景里没有 NetGameManager");
            return false;
        }
        if (manager.isNetworkActive)
        {
            SetStatus("已经在联机中，请先断开");
            return false;
        }
        if (string.IsNullOrEmpty(address))
        {
            SetStatus("请输入主机地址（留空则用 localhost）");
            address = "localhost";
        }

        if (!string.IsNullOrEmpty(playerName)) NetPlayerName.SetLocalName(playerName);
        manager.networkAddress = NormalizeAddress(address);

        manager.StartClient();
        bool ok = manager.isNetworkActive;
        if (ok)
        {
            session = Session.Connecting;
            connectStartTime = Time.time;
            SetStatus("连接中… " + manager.networkAddress);
        }
        else
        {
            SetStatus("发起连接失败（看 Console）");
        }
        if (logActions) Debug.Log("[Lobby] 加入 " + manager.networkAddress + " -> " + ok);
        ApplyMirrorHudVisibility();
        Raise();
        return ok;
    }

    /// <summary>断开当前联机会话（房主 = 关房，客户端 = 断开）。</summary>
    public void StopNet()
    {
        ResolveRefs();
        if (manager == null) return;

        if (NetworkServer.active && NetworkClient.isConnected) manager.StopHost();
        else if (NetworkServer.active) manager.StopServer();
        else if (NetworkClient.active) manager.StopClient();

        if (discovery != null) discovery.StopDiscovery();
        scanning = false;
        session = DetectSession();
        SetStatus("未连接");
        ApplyMirrorHudVisibility();
        OnSessionChanged();
    }

    /// <summary>打开大厅面板。</summary>
    public void OpenLobby()
    {
        if (HexUI.UIManager.Instance != null) HexUI.UIManager.Instance.Open(lobbyPanelId);
    }

    /// <summary>关闭大厅面板。</summary>
    public void CloseLobby()
    {
        if (HexUI.UIManager.Instance != null) HexUI.UIManager.Instance.Close(lobbyPanelId);
    }

    #endregion

    #region 内部

    void ResolveRefs()
    {
        if (manager == null) manager = NetworkManager.singleton as NetGameManager;
        if (manager == null) manager = FindObjectOfType<NetGameManager>();
        if (discovery == null) discovery = FindObjectOfType<NetLobbyDiscovery>();
        if (mirrorHud == null)
        {
            mirrorHud = FindObjectOfType<NetworkManagerHUD>(true);
        }
    }

    void Subscribe()
    {
        if (subscribed || discovery == null) return;
        if (discovery.OnServerFound == null)
        {
            discovery.OnServerFound = new ServerFoundUnityEvent<NetLobbyResponse>();
        }
        discovery.OnServerFound.AddListener(OnServerFound);
        subscribed = true;
        if (logActions) Debug.Log("[Lobby] 已订阅局域网发现事件");
    }

    void Unsubscribe()
    {
        if (!subscribed || discovery == null) return;
        discovery.OnServerFound.RemoveListener(OnServerFound);
        subscribed = false;
    }

    void OnServerFound(NetLobbyResponse response)
    {
        LobbyServerInfo info = new LobbyServerInfo
        {
            serverId = response.serverId,
            hostName = string.IsNullOrEmpty(response.hostName) ? "（未命名房主）" : response.hostName,
            mapName = response.mapName,
            address = response.EndPoint != null ? response.EndPoint.Address.ToString() : response.uri.Host,
            playerCount = response.playerCount,
            maxPlayers = response.maxPlayers,
            inGame = response.inGame,
            version = response.version
        };

        servers[info.serverId] = info;
        RebuildServerList();
        scanning = false;
        if (logActions) Debug.Log("[Lobby] 发现房间：" + info.hostName + " @ " + info.address + "（" + info.mapName + "）");
        SetStatus("发现 " + serverList.Count + " 个房间");
    }

    void RebuildServerList()
    {
        serverList.Clear();
        foreach (KeyValuePair<long, LobbyServerInfo> kv in servers) serverList.Add(kv.Value);
        serverList.Sort((a, b) => string.Compare(a.hostName, b.hostName, StringComparison.OrdinalIgnoreCase));
        Raise();
    }

    Session DetectSession()
    {
        if (NetworkServer.active && NetworkClient.isConnected) return Session.Hosting;
        if (NetworkClient.isConnected) return Session.Connected;
        if (NetworkClient.active) return Session.Connecting;
        return Session.Offline;
    }

    void OnSessionChanged()
    {
        ApplyMirrorHudVisibility();
        if (closeLobbyOnConnect && IsOnline && session != Session.Connecting) CloseLobby();
        Raise();
    }

    void ApplyMirrorHudVisibility()
    {
        if (mirrorHud == null || !hideMirrorHud) return;

        // 只禁用 HUD 组件本身，绝不隐藏它所在的物体：
        // Mirror 的 NetworkManagerHUD 通常与 NetworkManager / Transport 挂在同一个物体上
        // （本工程就是 NetRoot），一旦把那个物体 SetActive(false)，会连带停掉
        // NetworkManager / Transport / 本桥接器 —— 后者停了 Update() 就不再轮询状态，
        // 于是客户端永远停在"连接中…"、局域网发现也失效。
        mirrorHud.enabled = !IsOnline;
    }

    void SetStatus(string text)
    {
        if (statusText == text) return;
        statusText = text;
        Raise();
    }

    void Raise()
    {
        if (Changed != null) Changed();
    }

    static string NormalizeAddress(string address)
    {
        string a = address.Trim();
        int colon = a.IndexOf(':');
        if (colon > 0) a = a.Substring(0, colon);   // 端口由 transport 决定，去掉用户填的端口
        return a;
    }

    static string GetLocalIPv4()
    {
        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(Dns.GetHostName());
            for (int i = 0; i < addresses.Length; i++)
            {
                if (addresses[i].AddressFamily == AddressFamily.InterNetwork) return addresses[i].ToString();
            }
        }
        catch (Exception) { }
        return "127.0.0.1";
    }

    #endregion
}
