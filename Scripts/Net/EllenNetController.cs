using NaughtyCharacter;
using Mirror;
using UnityEngine;

/// <summary>
/// EllenNet（联机角色）控制器。挂在 EllenNet prefab 根物体
/// （与 NetworkIdentity / NetworkTransformReliable 同物体）。
///
/// 关键分流：
///  - 本地玩家（isLocalPlayer）：Character + CharacterController 正常运行 ——
///    NaughtyCharacter 完整操控（WASD / 鼠标视角 / 跳跃）由 NetworkTransformReliable
///    (ClientToServer) 上报房主并转发给其它客户端。
///  - 远端玩家（!isLocalPlayer）：禁用 Character 与 CharacterController ——
///    既不会在远端重复跑移动/重力（避免与 NetworkTransform 写 transform 冲突导致抖动），
///    也不会抢场景里唯一的 PlayerInputComponent / PlayerCamera 单例。
///    位置/旋转由 NetworkTransform 插值呈现。
/// </summary>
public class EllenNetController : NetworkBehaviour
{
    EllenAttack _attack;
    Mirror.NetworkAnimator _netAnim;

    public override void OnStartClient()
    {
        base.OnStartClient();
        Character character = GetComponent<Character>();
        if (!isLocalPlayer)
        {
            // 远端化身：
            //  1. 标记 RemoteSuppressed —— 即使组件禁用逻辑未及时生效，也绝不认领共享 Controller
            //     （NaughtyCharacter 的 Controller.Character 是场景级共享单例，远端劫持后销毁会致 MRE）；
            //  2. 禁用 Character 与 CharacterController —— 阻断移动/重力/相机驱动，
            //     位置/旋转由 NetworkTransform 插值呈现。
            if (character != null)
            {
                character.RemoteSuppressed = true;
                character.enabled = false;
            }

            CharacterController characterController = GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            // 3. 禁用 EllenAttack —— 攻击输入只属于本地控制的角色；
            //    远端化身的攻击动画由 NetworkAnimator.SetTrigger 同步（见 OnStartLocalPlayer 订阅）。
            EllenAttack attack = GetComponent<EllenAttack>();
            if (attack != null)
            {
                attack.enabled = false;
            }
        }
        else
        {
            // 本地玩家：订阅攻击触发事件 → 经 NetworkAnimator.SetTrigger 广播到房主与其它客户端
            // （Mirror NetworkAnimator 只自动同步 Float/Int/Bool 与状态切换，不自动同步 Trigger，
            //   需显式 SetTrigger：本地 isOwned -> Cmd 上送 -> server Rpc 广播 -> 远端 animator.SetTrigger）。
            _attack = GetComponent<EllenAttack>();
            _netAnim = GetComponent<Mirror.NetworkAnimator>();
            if (_attack != null && _netAnim != null)
            {
                _attack.AttackTriggered += OnAttackTriggered;
            }
        }
    }

    void OnAttackTriggered(string triggerName)
    {
        if (_netAnim != null)
        {
            _netAnim.SetTrigger(triggerName);
        }
    }

    void OnDestroy()
    {
        if (_attack != null)
        {
            _attack.AttackTriggered -= OnAttackTriggered;
            _attack = null;
        }
    }
}
