using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 攻击装配修复工具（菜单：Tools/角色控制/修复攻击装配）。
///
/// 背景：HexMap 与 HexMap2 共享同一 Assets 目录。若两个编辑器实例同时打开同一个
/// AnimatorController 并各自保存，会产生竞态污染（负 fileID、悬空 fileID:0 转换、
/// 多余 SM_Attack 子状态机等）。本工具在【脚本域重载后】从磁盘干净加载 controller，
/// 删除全部攻击相关残留，再调用 HexAttackSetupTool.Setup() 重建一份干净装配。
///
/// 用法：保存本文件 -> unity-cli editor refresh --compile（触发域重载，清空内存缓存）
///       -> exec HexAttackRepairTool.RepairAndSetup()
/// </summary>
public static class HexAttackRepairTool
{
    const string ControllerPath = "Assets/NaughtyCharacter/Characters/Ellen/EllenAnimatorController.controller";

    [MenuItem("Tools/角色控制/修复攻击装配")]
    public static void RepairAndSetup()
    {
        var log = new List<string>();

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("[攻击修复] 找不到 controller: " + ControllerPath);
            return;
        }

        // --- dump 加载到的结构（确认是磁盘版而非内存缓存） ---
        log.Add("加载参数: " + string.Join(",", System.Array.ConvertAll(controller.parameters, p => p.name)));
        AnimatorStateMachine root = controller.layers[0].stateMachine;
        log.Add("加载 root.states: " + root.states.Length + " sms: " + root.stateMachines.Length);

        // 1) 删攻击参数
        for (int i = controller.parameters.Length - 1; i >= 0; i--)
        {
            if (controller.parameters[i].name == "AttackL" || controller.parameters[i].name == "AttackR")
            {
                controller.RemoveParameter(i);
            }
        }

        // 2) 删 root 上指向攻击状态的 AnyState 转换（注意：数组可能含 null 悬空项，需判空）
        var keptAny = new List<AnimatorStateTransition>();
        foreach (AnimatorStateTransition t in root.anyStateTransitions)
        {
            if (t == null)
            {
                log.Add("清除 null 悬空 AnyState 转换");
                continue;
            }
            string dstName = t.destinationState != null ? t.destinationState.name : null;
            bool isAttack = dstName == "Attack_L" || dstName == "Attack_R";
            if (isAttack)
            {
                Object.DestroyImmediate(t, true);
                log.Add("删除 AnyState->" + dstName);
            }
            else
            {
                keptAny.Add(t);
            }
        }
        root.anyStateTransitions = keptAny.ToArray();

        // 3) 删 root 直接子状态中的攻击状态
        var keptStates = new List<ChildAnimatorState>();
        foreach (ChildAnimatorState cs in root.states)
        {
            if (cs.state != null && (cs.state.name == "Attack_L" || cs.state.name == "Attack_R"))
            {
                log.Add("删除状态 " + cs.state.name);
                DestroyStateAndTransitions(cs.state);
            }
            else
            {
                keptStates.Add(cs);
            }
        }
        root.states = keptStates.ToArray();

        // 4) 删 SM_Attack 子状态机（含内部状态与转换）
        var keptSM = new List<ChildAnimatorStateMachine>();
        foreach (ChildAnimatorStateMachine csm in root.stateMachines)
        {
            if (csm.stateMachine != null && csm.stateMachine.name == "SM_Attack")
            {
                log.Add("删除子状态机 SM_Attack");
                DestroyStateMachineAndChildren(csm.stateMachine);
            }
            else
            {
                keptSM.Add(csm);
            }
        }
        root.stateMachines = keptSM.ToArray();

        // 5) 清理任何名字带 Attack 的游离状态/子状态机（防御性，防负 ID 悬空对象）
        CleanupDanglingAttackObjects(root, log);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // 6) 结构自检
        AnimatorController reloaded = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimatorStateMachine r2 = reloaded.layers[0].stateMachine;
        log.Add("清理后参数: " + string.Join(",", System.Array.ConvertAll(reloaded.parameters, p => p.name)));
        log.Add("清理后 root.states: " + r2.states.Length + " sms: " + r2.stateMachines.Length);

        Debug.Log("[攻击修复] 清理完成，准备重新装配：\n" + string.Join("\n", log));
    }

    static void DestroyStateAndTransitions(AnimatorState state)
    {
        var transitions = new List<AnimatorStateTransition>(state.transitions);
        foreach (AnimatorStateTransition t in transitions)
        {
            Object.DestroyImmediate(t, true);
        }
        Object.DestroyImmediate(state, true);
    }

    static void DestroyStateMachineAndChildren(AnimatorStateMachine sm)
    {
        foreach (ChildAnimatorState cs in sm.states)
        {
            if (cs.state != null)
            {
                DestroyStateAndTransitions(cs.state);
            }
        }
        foreach (ChildAnimatorStateMachine csm in sm.stateMachines)
        {
            if (csm.stateMachine != null)
            {
                DestroyStateMachineAndChildren(csm.stateMachine);
            }
        }
        Object.DestroyImmediate(sm, true);
    }

    /// <summary>防御性清理：遍历 root 下所有可达子状态机，删除名字含 Attack 的游离对象。</summary>
    static void CleanupDanglingAttackObjects(AnimatorStateMachine root, List<string> log)
    {
        var stack = new Stack<AnimatorStateMachine>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            AnimatorStateMachine sm = stack.Pop();
            if (sm == null)
            {
                continue;
            }
            var states = new List<ChildAnimatorState>();
            foreach (ChildAnimatorState cs in sm.states)
            {
                if (cs.state != null && cs.state.name.Contains("Attack"))
                {
                    log.Add("清理游离状态 " + cs.state.name + " @ " + sm.name);
                    DestroyStateAndTransitions(cs.state);
                }
                else
                {
                    states.Add(cs);
                }
            }
            sm.states = states.ToArray();
            foreach (ChildAnimatorStateMachine csm in sm.stateMachines)
            {
                if (csm.stateMachine != null && csm.stateMachine.name.Contains("Attack"))
                {
                    log.Add("清理游离子状态机 " + csm.stateMachine.name + " @ " + sm.name);
                    Object.DestroyImmediate(csm.stateMachine, true);
                }
                else if (csm.stateMachine != null)
                {
                    stack.Push(csm.stateMachine);
                }
            }
        }
    }
}
