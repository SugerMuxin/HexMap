using Mirror;
using UnityEngine;

/// <summary>
/// 联机脚步音效转发（挂 EllenNet 根，与 NetworkIdentity 同物体；EllenNetController 同层）。
///
/// 需求：客户端 A 在客户端 B 旁边走动时，B 能听到 A 的脚步声（及水花/落地）。
///
/// 工作方式（事件广播，避免每个脚步同步位置/状态）：
///  - 拥有者端（isOwned = 本地活角色）：订阅 CharacterFootstepAudio.StepSfxTriggered，
///    每触发一步（脚底材质 key + 世界坐标）经 [Command] 上报服务器（host 模式本地直通）；
///  - 服务器收到后 [ClientRpc] 广播给所有客户端；
///  - 各端收到广播：非拥有者端 → 在本地场景该角色位置播一次（3D 定位正确）；
///    拥有者端 → 跳过（本地已在 CharacterFootstepAudio.PlayAtPoint 播过，防双响）。
///
/// 发送端本地播放由 CharacterFootstepAudio 完成（不依赖本组件），本组件只负责"让其它端也听到"。
/// 单机（无网络 / 非 spawn 物体 / 无 CharacterFootstepAudio）时静默，零影响。
/// 素材缺失端由 AudioService 容错静默跳过。
/// </summary>
public class NetworkFootstepRelay : NetworkBehaviour
{
    CharacterFootstepAudio _footstep;

    void Awake()
    {
        _footstep = GetComponent<CharacterFootstepAudio>();
    }

    public override void OnStartAuthority()
    {
        base.OnStartAuthority();
        // 只在本端拥有（本地玩家）时订阅：远端化身不拥有，其脚步由拥有者端广播的 Rpc 驱动
        if (_footstep != null)
        {
            _footstep.StepSfxTriggered += OnLocalStep;
        }
    }

    public override void OnStopAuthority()
    {
        base.OnStopAuthority();
        if (_footstep != null)
        {
            _footstep.StepSfxTriggered -= OnLocalStep;
        }
    }

    void OnDestroy()
    {
        if (_footstep != null)
        {
            _footstep.StepSfxTriggered -= OnLocalStep;
        }
    }

    void OnLocalStep(string key, Vector3 position)
    {
        // 拥有者端本地已播过；这里仅把事件上报服务器、广播给其它客户端。
        // [Command] 默认 requireAuthority=true——只有拥有者端能发，符合"各自上报自己角色"语义。
        CmdBroadcastStep(key, position);
    }

    [Command]
    void CmdBroadcastStep(string key, Vector3 position)
    {
        RpcPlayStep(key, position);
    }

    [ClientRpc]
    void RpcPlayStep(string key, Vector3 position)
    {
        // 拥有者端（isOwned = 本地活角色所在端）：CharacterFootstepAudio 已本地播放，跳过防双响；
        // 非拥有者端（其它客户端 / host 视角下的远端化身）：在此播放，3D 声源在该角色位置。
        // 注：本工程 Mirror 版本无 hasAuthority 属性，用 isOwned 判断"本端是否拥有"。
        if (isOwned)
        {
            return;
        }
        AudioService svc = AudioService.Instance;
        if (svc == null || svc.bank == null)
        {
            return;
        }
        svc.PlaySfxAtPoint(key, position);
    }
}
