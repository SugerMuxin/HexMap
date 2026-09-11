using System.Net;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

/// <summary>客户端 → 房主：局域网内查询可加入的房间（空消息）。</summary>
public struct NetLobbyRequest : NetworkMessage { }

/// <summary>
/// 房主 → 客户端：房间信息（联机大厅的服务器列表用）。
/// 比 Mirror 自带 NetworkDiscovery 的响应多了房主名 / 地图 / 人数，便于列表展示。
/// </summary>
public struct NetLobbyResponse : NetworkMessage
{
    /// <summary>房主 transport 的服务地址（客户端会用它连接）。</summary>
    public System.Uri uri;
    /// <summary>房主唯一标识（同一房主多网卡时去重用）。</summary>
    public long serverId;
    /// <summary>房主（玩家）名称。</summary>
    public string hostName;
    /// <summary>当前地图名（空 = 未命名 / 新建地图）。</summary>
    public string mapName;
    /// <summary>已连接玩家数。</summary>
    public int playerCount;
    /// <summary>人数上限。</summary>
    public int maxPlayers;
    /// <summary>房主是否已经开局（大厅阶段全部为 true，留给后续"旁观/中途加入"语义）。</summary>
    public bool inGame;
    /// <summary>版本号（不一致的客户端可提示）。</summary>
    public string version;

    /// <summary>客户端收到后回填真实来源地址（不参与序列化）。</summary>
    public IPEndPoint EndPoint { get; set; }
}

/// <summary>
/// 局域网大厅发现（房主 UDP 广播 + 客户端查询）。
///
/// 与 Mirror 自带 `NetworkDiscovery` 的区别：广播内容携带**房主名 / 地图 / 人数 / 版本**，
/// 联机大厅 UI 直接拿来渲染列表；协议仍走 Mirror 的 NetworkDiscoveryBase（UDP 广播端口 47777）。
///
/// 装配：挂在场景 `NetRoot`（与 NetGameManager / KcpTransport 同物体），transport 指向 KcpTransport。
/// 装配菜单：Tools/UI/一键装配 联机大厅 UI (lobby)。
/// </summary>
[DisallowMultipleComponent]
public class NetLobbyDiscovery : NetworkDiscoveryBase<NetLobbyRequest, NetLobbyResponse>
{
    [Header("广播内容")]
    [Tooltip("房主名（留空自动用本地玩家名 / 机器名）")]
    public string hostName = "";
    [Tooltip("地图名（留空自动取当前地图名 / 网格尺寸）")]
    public string mapName = "";
    [Tooltip("版本号（客户端可据此提示不一致）")]
    public string version = "1.0";
    [Tooltip("已在游玩时把 inGame 标为 true（客户端可据此提示\"游戏进行中\"；发现响应始终会回）")]
    public bool reportInGame = true;

    /// <summary>心跳/日志开关。</summary>
    public bool logDiscovery = false;

    protected override NetLobbyRequest GetRequest()
    {
        return new NetLobbyRequest();
    }

    protected override NetLobbyResponse ProcessRequest(NetLobbyRequest request, IPEndPoint endpoint)
    {
        NetLobbyResponse response = new NetLobbyResponse
        {
            uri = transport.ServerUri(),
            serverId = ServerId,
            hostName = ResolveHostName(),
            mapName = ResolveMapName(),
            playerCount = NetworkServer.active ? NetworkServer.connections.Count : 0,
            maxPlayers = NetworkManager.singleton != null ? NetworkManager.singleton.maxConnections : 0,
            inGame = reportInGame && NetworkServer.active && NetworkServer.connections.Count > 1,
            version = version
        };
        if (logDiscovery) Debug.Log("[Lobby] 响应发现请求 " + endpoint + "（房主 " + response.hostName + "）");
        return response;
    }

    protected override void ProcessResponse(NetLobbyResponse response, IPEndPoint endpoint)
    {
        // 与 Mirror 自带实现一致：以"收到包的来源 IP"为准回填地址（避免多网卡解析不到）
        response.EndPoint = endpoint;
        System.UriBuilder realUri = new System.UriBuilder(response.uri)
        {
            Host = response.EndPoint.Address.ToString()
        };
        response.uri = realUri.Uri;

        if (logDiscovery) Debug.Log("[Lobby] 发现房间：" + response.hostName + " @ " + response.uri);
        if (OnServerFound != null) OnServerFound.Invoke(response);
    }

    /// <summary>房主名：本组件字段 → 本地玩家名（PlayerPrefs）→ 机器名。</summary>
    public string ResolveHostName()
    {
        if (!string.IsNullOrEmpty(hostName)) return hostName;
        string cached = NetPlayerName.CachedName;
        if (!string.IsNullOrEmpty(cached)) return cached;
        return SystemInfo.deviceName;
    }

    /// <summary>地图名：本组件字段 → NetLobbyBridge 记录的地图名 → 网格尺寸描述。</summary>
    public string ResolveMapName()
    {
        if (!string.IsNullOrEmpty(mapName)) return mapName;
        if (NetLobbyBridge.Instance != null && !string.IsNullOrEmpty(NetLobbyBridge.Instance.CurrentMapName))
        {
            return NetLobbyBridge.Instance.CurrentMapName;
        }
        HexGrid grid = FindObjectOfType<HexGrid>();
        if (grid != null) return grid.cellCountX + "×" + grid.cellCountZ;
        return "";
    }
}
