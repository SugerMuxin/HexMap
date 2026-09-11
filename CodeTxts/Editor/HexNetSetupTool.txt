using System.IO;
using kcp2k;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 多人联机一键装配工具（菜单：Tools/多人联机/...）。
///
/// 一键装配做的事：
///  1. 以 Assets/NaughtyCharacter/Prefabs/Ellen.prefab 为基底，创建 Prefab Variant
///     Assets/Resources/Prefabs/EllenNet.prefab：
///     根物体加 NetworkIdentity + NetworkTransformReliable(World/ClientToServer) + EllenNetController。
///  2. 在当前场景（需 SampleScene）创建 NetRoot：
///     NetGameManager（NetworkManager 子类，playerPrefab=EllenNet, autoCreatePlayer=false）
///     + kcp2k.KcpTransport + NetworkManagerHUD，然后保存场景。
///
/// 逆向清理用「清理装配」菜单。
/// </summary>
public static class HexNetSetupTool
{
    const string EllenSourcePath = "Assets/NaughtyCharacter/Prefabs/Ellen.prefab";
    const string EllenNetPath = "Assets/Resources/Prefabs/EllenNet.prefab";
    const string NetRootName = "NetRoot";

    [MenuItem("Tools/多人联机/一键装配 EllenNet + NetRoot（SampleScene）")]
    public static void Setup()
    {
        Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != "SampleScene")
        {
            Debug.LogError("[多人] 请先打开 SampleScene 再执行装配（当前场景: " + scene.name + "）");
            return;
        }

        // 1. EllenNet prefab（variant）
        string prefabPath = CreateEllenNetPrefab();
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError("[多人] EllenNet 预制体创建失败，中止场景装配");
            return;
        }

        // 2. NetRoot（先清旧的，保证可重复执行）
        GameObject old = GameObject.Find(NetRootName);
        if (old != null)
        {
            Object.DestroyImmediate(old);
        }

        GameObject root = new GameObject(NetRootName);
        NetGameManager net = root.AddComponent<NetGameManager>();
        net.autoCreatePlayer = false;
        net.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        // 必须把玩家 prefab 也注册进 spawnPrefabs：Mirror v96 NetworkClient.GetPrefab() 只查注册字典、
        // 无 Resources 回退 —— 不注册则远端客户端 spawn 角色时找不到 prefab（角色生成失败）。
        if (!net.spawnPrefabs.Contains(net.playerPrefab))
        {
            net.spawnPrefabs.Add(net.playerPrefab);
        }

        KcpTransport kcp = root.AddComponent<KcpTransport>();
        net.transport = kcp;

        root.AddComponent<NetworkManagerHUD>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[多人] 装配完成 ✅\n" +
                  "  - " + prefabPath + "（Ellen 的联机变体）\n" +
                  "  - NetRoot：NetGameManager(playerPrefab=EllenNet, autoCreatePlayer=false) + KcpTransport + NetworkManagerHUD\n" +
                  "场景已保存。Play 后点 HUD 的 Host / Client 即可联机（同机测试：一个开 Host，打包 exe 填 localhost 做 Client）。");
    }

    /// <summary>以 Ellen.prefab 为基底创建联机变体（加 NetworkIdentity / NetworkTransformReliable / EllenNetController）。</summary>
    static string CreateEllenNetPrefab()
    {
        if (File.Exists(EllenNetPath))
        {
            AssetDatabase.DeleteAsset(EllenNetPath);
        }

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(EllenSourcePath);
        if (source == null)
        {
            Debug.LogError("[多人] 找不到源预制体 " + EllenSourcePath);
            return null;
        }

        // 清理上次失败运行可能残留的场景临时实例（名为 EllenNet 且源=Ellen.prefab 的根物体）
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>(true))
        {
            if (go.name == "EllenNet" && go.transform.parent == null &&
                PrefabUtility.GetCorrespondingObjectFromSource(go) == source)
            {
                Object.DestroyImmediate(go);
                Debug.Log("[多人] 已清理上次失败残留的 EllenNet 场景实例");
            }
        }

        // 在场景实例化一份 Ellen，改造成联机版后存成新 prefab（对原 Ellen 的 instance 存档 = Prefab Variant）
        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
        inst.name = "EllenNet";

        if (inst.GetComponent<NetworkIdentity>() == null)
        {
            inst.AddComponent<NetworkIdentity>();
        }

        NetworkTransformReliable nt = inst.GetComponent<NetworkTransformReliable>();
        if (nt == null)
        {
            try
            {
                nt = inst.AddComponent<NetworkTransformReliable>();
            }
            catch (System.NullReferenceException)
            {
                // Mirror NT 的 Reset()（编辑器 AddComponent 时被 Unity 自动调用）会在 target 赋值前
                // 读取 target（GetPosition/GetRotation/GetScale）→ 抛一次 NRE；组件已创建成功，忽略并重取引用。
                nt = inst.GetComponent<NetworkTransformReliable>();
                Debug.Log("[多人] NetworkTransformReliable Reset 的一次性 NRE 已忽略（target 在下方补设）");
            }
            if (nt == null)
            {
                Debug.LogError("[多人] NetworkTransformReliable 添加失败");
                Object.DestroyImmediate(inst);
                return null;
            }
        }
        nt.target = inst.transform; // 必须在任何再次触发 Reset/ResetState 前设好 target
        nt.coordinateSpace = CoordinateSpace.World; // 注意：该枚举在 Mirror 命名空间顶层，不在 NetworkTransformBase 内
        // 必须显式设 ClientToServer！Mirror Reset() 里 ResetState()（读 target 抛 NRE）在
        // "syncDirection = ClientToServer" 默认赋值【之前】执行 → NRE 中断使默认赋值从未发生，
        // syncDirection 保持序列化默认 0 = ServerToClient → 客户端本地角色永不向服务器上报位置
        // （host 本地角色是服务器对象所以 S2C 也能广播，造成"host→client 能动、client→host 不动"的假象）。
        nt.syncDirection = SyncDirection.ClientToServer; // SyncDirection 同样在 Mirror 命名空间顶层
        // 本地玩家把位置上报房主，房主转发给其它客户端

        if (inst.GetComponent<EllenNetController>() == null)
        {
            inst.AddComponent<EllenNetController>();
        }

        // 脚步/水花/落地音效的网络转发：本地角色每步 [Command]→[ClientRpc] 广播，其它客户端听到远端脚步。
        // 依赖同物体 CharacterFootstepAudio.StepSfxTriggered 事件（AudioSystem，AudioSetupTool 会补装）；
        // 单机 / 未挂 CharacterFootstepAudio 时静默零影响。
        if (inst.GetComponent<NetworkFootstepRelay>() == null)
        {
            inst.AddComponent<NetworkFootstepRelay>();
        }

        // 动画同步：NetworkAnimator（Mirror 自带，同步 Animator 状态 + 全部非 curve 参数）。
        // 坑：syncDirection 同 NetworkTransform —— Reset() 可能因 AddComponent 时序不执行，必须显式设 ClientToServer，
        // 否则 SendMessagesAllowed(isOwned && C2S) 为 false，本地动画永不广播（远端永远看初值参数=一直播落地）。
        NetworkAnimator netAnim = inst.GetComponent<NetworkAnimator>();
        if (netAnim == null)
        {
            netAnim = inst.AddComponent<NetworkAnimator>();
        }
        netAnim.animator = inst.GetComponent<Animator>();
        netAnim.syncDirection = SyncDirection.ClientToServer;

        bool success = false;
        PrefabUtility.SaveAsPrefabAssetAndConnect(inst, EllenNetPath, InteractionMode.AutomatedAction, out success);
        Object.DestroyImmediate(inst);

        if (!success)
        {
            Debug.LogError("[多人] EllenNet 保存失败: " + EllenNetPath);
            return null;
        }

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(EllenNetPath);
        GameObject baseOf = PrefabUtility.GetCorrespondingObjectFromSource(saved);
        string kind = baseOf != null ? "Prefab Variant of " + baseOf.name : "独立 Prefab（非 variant）";
        Debug.Log("[多人] EllenNet 已创建：" + kind);

        // 自检：根物体组件应齐全
        if (saved != null)
        {
            Debug.Log("[多人] EllenNet 组件: " +
                      (saved.GetComponent<NetworkIdentity>() ? "NetworkIdentity " : "(缺 NetworkIdentity!) ") +
                      (saved.GetComponent<NetworkTransformReliable>() ? "NetworkTransformReliable " : "(缺 NT!) ") +
                      (saved.GetComponent<EllenNetController>() ? "EllenNetController " : "(缺 EllenNetController!) ") +
                      (saved.GetComponent<NaughtyCharacter.Character>() ? "Character " : "") +
                      (saved.GetComponent<CharacterController>() ? "CharacterController" : ""));
        }
        return EllenNetPath;
    }

    [MenuItem("Tools/多人联机/给 EllenNet 补装脚步网络转发")]
    public static void AddFootstepRelayToEllenNet()
    {
        if (!File.Exists(EllenNetPath))
        {
            Debug.LogError("[多人] 找不到 " + EllenNetPath + "，请先执行「一键装配 EllenNet + NetRoot」");
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(EllenNetPath);
        bool changed = false;

        if (contents.GetComponent<NetworkFootstepRelay>() == null)
        {
            contents.AddComponent<NetworkFootstepRelay>();
            changed = true;
            Debug.Log("[多人] 已给 EllenNet 添加 NetworkFootstepRelay");
        }
        else
        {
            Debug.Log("[多人] EllenNet 已有 NetworkFootstepRelay，跳过");
        }

        if (contents.GetComponent<CharacterFootstepAudio>() == null)
        {
            contents.AddComponent<CharacterFootstepAudio>();
            changed = true;
            Debug.Log("[多人] 已给 EllenNet 补装 CharacterFootstepAudio（NetworkFootstepRelay 依赖其事件）");
        }

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(contents, EllenNetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        PrefabUtility.UnloadPrefabContents(contents);

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(EllenNetPath);
        bool hasRelay = saved != null && saved.GetComponent<NetworkFootstepRelay>() != null;
        bool hasFootstep = saved != null && saved.GetComponent<CharacterFootstepAudio>() != null;
        Debug.Log("[多人] 脚步网络转发补装" + (changed ? "完成" : "（无需改动）") + "：NetworkFootstepRelay=" + hasRelay +
                  " CharacterFootstepAudio=" + hasFootstep);
    }

    [MenuItem("Tools/多人联机/清理装配（删除 EllenNet / NetRoot）")]
    public static void Cleanup()
    {
        GameObject old = GameObject.Find(NetRootName);
        if (old != null)
        {
            Object.DestroyImmediate(old);
        }

        if (File.Exists(EllenNetPath))
        {
            AssetDatabase.DeleteAsset(EllenNetPath);
        }

        Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[多人] 已清理：NetRoot 已移除、EllenNet.prefab 已删除、场景已保存");
    }
}
