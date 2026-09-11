using System;
using System.Collections.Generic;
using System.IO;
using NaughtyCharacter;
using UnityEngine;
using Mirror;

/// <summary>房主 -> 客户端：完整 .map 字节流（含 header=1，格式与 SaveLoadMenu.Save 一致）。</summary>
public struct NetMapDataMessage : Mirror.NetworkMessage
{
    public byte[] data;
}

/// <summary>客户端 -> 房主：地图已加载完毕，可以给我 spawn 角色了。</summary>
public struct NetClientReadyMessage : Mirror.NetworkMessage { }

/// <summary>
/// 多人联机游戏管理器（房主制，Mirror）。
/// 挂在场景 NetRoot 物体上（作为 NetworkManager 子类使用，配 KcpTransport + NetworkManagerHUD）。
///
/// 设计要点：
///  1. 地图闸门：客户端连上房主后，房主把当前 HexGrid 序列化成 .map 字节流（与 SaveLoadMenu 同格式）下发；
///     客户端加载完地图才发 NetClientReadyMessage，房主此时才 spawn 该玩家角色
///     —— 避免玩家出生在"还没地图"的世界里掉进虚空。
///  2. 场景里"单机用"的静态角色（无 NetworkIdentity 的 Ellen）在联机会话开始后隐藏、结束后恢复，单机游玩不受影响。
///  3. 纯客户端（非房主）禁止地图编辑（HexControlMode.netClientReadOnly），并隐藏 NewMap/SaveLoad 菜单。
///
/// 装配：见 Editor/HexNetSetupTool.cs 菜单 Tools/多人联机/一键装配。
/// 联机：房主点 HUD 的 Host；客户端点 Client 并填房主 IP（默认 localhost 即本机双开）。
/// </summary>
public class NetGameManager : NetworkManager
{
    GameObject sceneCharacter;                 // 场景里单机用的静态角色（联机开始后隐藏、结束恢复）
    readonly List<GameObject> hiddenMenus = new List<GameObject>(); // 纯客户端模式下隐藏的菜单面板

    bool autoClient;   // 命令行 -autoClient：启动后自动 StartClient（自动化联调用）
    bool autoHost;     // 命令行 -autoHost：启动后自动 StartHost（自动化联调用）

    public override void Awake()
    {
        base.Awake();
        // 玩家 spawn 完全由本类控制（地图就绪后再 AddPlayerForConnection），关闭 Mirror 自动创建
        autoCreatePlayer = false;
        ParseAutoStartArgs();
    }

    /// <summary>解析命令行参数：-autoClient / -autoHost（供 exe 自动化联调，不传则维持 HUD 手动操作）。</summary>
    void ParseAutoStartArgs()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-autoClient") autoClient = true;
            else if (args[i] == "-autoHost") autoHost = true;
        }
    }

    void Start()
    {
        if (autoClient || autoHost)
        {
            StartCoroutine(AutoStartNet());
        }
    }

    System.Collections.IEnumerator AutoStartNet()
    {
        yield return null; // 等一帧，确保场景所有物体 Awake 完毕
        if (autoHost)
        {
            Debug.Log("[Net] 命令行 -autoHost，自动开房");
            StartHost();
        }
        else if (autoClient)
        {
            Debug.Log("[Net] 命令行 -autoClient，自动连接 " + networkAddress);
            StartClient();
        }
    }

    #region Mirror NetworkManager 生命周期钩子

    public override void OnStartServer()
    {
        base.OnStartServer();
        // requireAuthentication=false：本项目未配认证器，保证消息可收
        NetworkServer.RegisterHandler<NetClientReadyMessage>(OnClientReady, false);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        NetworkClient.RegisterHandler<NetMapDataMessage>(OnMapData, false);

        if (!NetworkServer.active)
        {
            // 纯客户端：进入联机会话（隐藏单机角色 + 只读）
            BeginNetSession(clientReadOnly: true);
        }
    }

    public override void OnStartHost()
    {
        base.OnStartHost();
        // 房主：地图本就在本地，直接进入会话并给自己 spawn 角色
        BeginNetSession(clientReadOnly: false);
        if (NetworkServer.localConnection != null)
        {
            SpawnPlayer(NetworkServer.localConnection);
        }
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        // 只给远端客户端发地图；房主自己的本地连接跳过
        if (conn != NetworkServer.localConnection)
        {
            SendMapTo(conn);
        }
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        base.OnServerDisconnect(conn);
        Debug.Log("[Net] 客户端断开: " + conn);
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // 已关闭 autoCreatePlayer；即使收到 AddPlayer 也什么都不做（角色由地图闸门流程 spawn）
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        EndNetSession();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        EndNetSession();
    }

    #endregion

    #region 地图闸门（server 侧）

    void SendMapTo(NetworkConnectionToClient conn)
    {
        HexGrid grid = FindObjectOfType<HexGrid>();
        if (grid == null)
        {
            Debug.LogError("[Net] 房主场景没有 HexGrid，无法下发地图");
            return;
        }

        MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);
        w.Write(1); // header，与 SaveLoadMenu.Save 一致
        grid.Save(w);
        w.Flush();
        byte[] data = ms.ToArray();
        w.Dispose();
        ms.Dispose();

        conn.Send(new NetMapDataMessage { data = data });
        Debug.Log("[Net] 已向客户端 " + conn + " 下发地图 " + data.Length + " 字节");
    }

    void OnClientReady(NetworkConnectionToClient conn, NetClientReadyMessage msg)
    {
        SpawnPlayer(conn);
    }

    void SpawnPlayer(NetworkConnectionToClient conn)
    {
        if (conn == null) return;
        if (conn.identity != null)
        {
            Debug.LogWarning("[Net] 连接 " + conn + " 已有角色，跳过重复 spawn");
            return;
        }
        if (playerPrefab == null)
        {
            Debug.LogError("[Net] playerPrefab 未设置（应指向 EllenNet）");
            return;
        }

        Vector3 pos;
        Quaternion rot = Quaternion.identity;
        HexGrid grid = FindObjectOfType<HexGrid>();
        if (grid == null || !TryGetSpawnPoint(grid, out pos))
        {
            pos = transform.position + Vector3.up * 3f;
            Debug.LogWarning("[Net] 找不到合适出生点，退回 NetRoot 上方");
        }

        GameObject go = Instantiate(playerPrefab, pos, rot);
        go.name = playerPrefab.name + " [" + conn.connectionId + "]";
        NetworkServer.AddPlayerForConnection(conn, go);
        Debug.Log("[Net] 已为 " + conn + " 生成角色 @" + pos);
    }

    /// <summary>随机挑一个非水下的格子顶面上方作为出生点（尝试 40 次）。</summary>
    bool TryGetSpawnPoint(HexGrid grid, out Vector3 pos)
    {
        for (int i = 0; i < 40; i++)
        {
            int x = UnityEngine.Random.Range(0, grid.cellCountX);
            int z = UnityEngine.Random.Range(0, grid.cellCountZ);
            HexCell cell = grid.GetCell(HexCoordinates.FromOffsetCoordinates(x, z));
            if (cell == null || cell.IsUnderwater) continue;
            pos = cell.transform.position + Vector3.up * 2f;
            return true;
        }
        pos = Vector3.zero;
        return false;
    }

    #endregion

    #region 客户端地图加载

    void OnMapData(NetMapDataMessage msg)
    {
        // 房主（host）通过本地连接也会收到这条，但地图已在本地，忽略
        if (NetworkServer.active) return;
        try
        {
            HexGrid grid = FindObjectOfType<HexGrid>();
            if (grid == null)
            {
                Debug.LogError("[Net] 客户端场景没有 HexGrid");
                return;
            }

            using (MemoryStream ms = new MemoryStream(msg.data))
            using (BinaryReader reader = new BinaryReader(ms))
            {
                int header = reader.ReadInt32();
                if (header > 1)
                {
                    Debug.LogWarning("[Net] 未知地图格式 header=" + header);
                    return;
                }
                grid.Load(reader, header);
            }
            grid.ShowUI(false);
            Debug.Log("[Net] 地图已从房主加载，请求生成角色");
            NetworkClient.Send(new NetClientReadyMessage());
        }
        catch (Exception e)
        {
            Debug.LogError("[Net] 客户端加载地图失败: " + e);
        }
    }

    #endregion

    #region 单机角色 / 编辑权限 会话切换

    void BeginNetSession(bool clientReadOnly)
    {
        HideSceneCharacter();
        if (clientReadOnly)
        {
            SetClientReadOnly(true);
        }
    }

    void EndNetSession()
    {
        RestoreSceneCharacter();
        SetClientReadOnly(false);
    }

    /// <summary>隐藏场景里"单机用"的静态角色（它没有 NetworkIdentity，不是网络角色）。</summary>
    void HideSceneCharacter()
    {
        if (sceneCharacter != null) return;
        Character[] chars = FindObjectsOfType<Character>();
        for (int i = 0; i < chars.Length; i++)
        {
            Character c = chars[i];
            if (c.GetComponentInParent<NetworkIdentity>() == null)
            {
                sceneCharacter = c.gameObject;
                break;
            }
        }
        if (sceneCharacter != null && sceneCharacter.activeSelf)
        {
            sceneCharacter.SetActive(false);
            Debug.Log("[Net] 已隐藏单机角色 " + sceneCharacter.name);
        }
    }

    void RestoreSceneCharacter()
    {
        if (sceneCharacter != null && !sceneCharacter.activeSelf)
        {
            sceneCharacter.SetActive(true);
            Debug.Log("[Net] 已恢复单机角色 " + sceneCharacter.name);
        }
        sceneCharacter = null;
    }

    /// <summary>纯客户端：禁止地图编辑并隐藏新建/存取菜单；退出联机时恢复。</summary>
    void SetClientReadOnly(bool readOnly)
    {
        HexControlMode.netClientReadOnly = readOnly;
        if (readOnly)
        {
            MonoBehaviour[] menus = { FindObjectOfType<NewMapMenu>(), FindObjectOfType<SaveLoadMenu>() };
            for (int i = 0; i < menus.Length; i++)
            {
                if (menus[i] != null && menus[i].gameObject.activeSelf)
                {
                    hiddenMenus.Add(menus[i].gameObject);
                    menus[i].gameObject.SetActive(false);
                }
            }
        }
        else
        {
            for (int i = 0; i < hiddenMenus.Count; i++)
            {
                if (hiddenMenus[i] != null)
                {
                    hiddenMenus[i].SetActive(true);
                }
            }
            hiddenMenus.Clear();
        }
    }

    #endregion
}
