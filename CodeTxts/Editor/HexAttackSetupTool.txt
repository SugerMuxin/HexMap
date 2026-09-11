using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Ellen 左右键攻击一键装配工具（菜单：Tools/角色控制/给 Ellen 装配左右键攻击）。
///
/// 装配内容（全部幂等，可重复执行）：
///  1. EllenAnimatorController.controller：
///     - 加 Trigger 参数 AttackL / AttackR；
///     - Base Layer root 加普通状态 Attack_L（@EllenCombo1.fbx）/ Attack_R（@EllenCombo2.fbx）；
///     - root 加 AnyState 转换：任意状态（跑/跳/落地）满足 AttackL/AttackR trigger 即打断进入攻击；
///     - 攻击状态播完（exitTime）后按 IsGrounded 回 SM_Idle_Running（地面）或 SM_Airborne（空中）。
///  2. NaughtyCharacter/Prefabs/Ellen.prefab 根物体加 EllenAttack 组件
///     （场景中的 Ellen 实例与联机 EllenNet variant 都会自动继承）。
///
/// 不保存场景：只改 .controller 与 .prefab 资产。
/// </summary>
public static class HexAttackSetupTool
{
    const string ControllerPath = "Assets/NaughtyCharacter/Characters/Ellen/EllenAnimatorController.controller";
    const string Combo1Fbx = "Assets/NaughtyCharacter/Characters/Ellen/AnimationClips/@EllenCombo1.fbx";
    const string Combo2Fbx = "Assets/NaughtyCharacter/Characters/Ellen/AnimationClips/@EllenCombo2.fbx";
    const string EllenPrefabPath = "Assets/NaughtyCharacter/Prefabs/Ellen.prefab";

    const string TriggerL = "AttackL";
    const string TriggerR = "AttackR";
    const string StateL = "Attack_L";
    const string StateR = "Attack_R";

    [MenuItem("Tools/角色控制/给 Ellen 装配左右键攻击")]
    public static void Setup()
    {
        bool ok = true;
        ok &= SetupAnimatorController();
        ok &= SetupPrefab();
        Debug.Log("[攻击装配] " + (ok ? "完成 ✅（AnimatorController + Ellen.prefab + EllenAttack）" : "部分失败，见上方错误日志"));
    }

    static bool SetupAnimatorController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("[攻击装配] 找不到 AnimatorController：" + ControllerPath);
            return false;
        }

        // 1. Trigger 参数
        if (FindParameter(controller, TriggerL) == null)
        {
            controller.AddParameter(TriggerL, AnimatorControllerParameterType.Trigger);
        }
        if (FindParameter(controller, TriggerR) == null)
        {
            controller.AddParameter(TriggerR, AnimatorControllerParameterType.Trigger);
        }

        // 2. 状态与 clip
        AnimatorStateMachine root = controller.layers[0].stateMachine;
        AnimationClip clipL = LoadFirstClip(Combo1Fbx, "Combo1");
        AnimationClip clipR = LoadFirstClip(Combo2Fbx, "Combo2");
        if (clipL == null || clipR == null)
        {
            Debug.LogError("[攻击装配] 加载攻击 clip 失败：" + Combo1Fbx + " / " + Combo2Fbx);
            return false;
        }

        AnimatorState stateL = FindState(root, StateL);
        if (stateL == null)
        {
            stateL = root.AddState(StateL);
            stateL.motion = clipL;
        }
        AnimatorState stateR = FindState(root, StateR);
        if (stateR == null)
        {
            stateR = root.AddState(StateR);
            stateR.motion = clipR;
        }

        // 3. AnyState -> 攻击（Trigger 打断任意移动状态）
        AddAnyStateTransition(root, stateL, TriggerL);
        AddAnyStateTransition(root, stateR, TriggerR);

        // 4. 攻击播完回移动：地面 -> SM_Idle_Running，空中 -> SM_Airborne
        AnimatorStateMachine idleSM = FindChildStateMachine(root, "SM_Idle_Running");
        AnimatorStateMachine airSM = FindChildStateMachine(root, "SM_Airborne");
        if (idleSM == null || airSM == null)
        {
            Debug.LogError("[攻击装配] 找不到 SM_Idle_Running / SM_Airborne 子状态机，装配中止");
            return false;
        }
        AddExitTransition(root, stateL, idleSM, airSM);
        AddExitTransition(root, stateR, idleSM, airSM);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[攻击装配] AnimatorController 就绪：AttackL/AttackR trigger + " + StateL + "(" + clipL.name + ") + " + StateR + "(" + clipR.name + ")");
        return true;
    }

    static AnimatorControllerParameter FindParameter(AnimatorController controller, string name)
    {
        foreach (AnimatorControllerParameter p in controller.parameters)
        {
            if (p.name == name)
            {
                return p;
            }
        }
        return null;
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState child in sm.states)
        {
            if (child.state.name == name)
            {
                return child.state;
            }
        }
        return null;
    }

    static AnimatorStateMachine FindChildStateMachine(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorStateMachine child in sm.stateMachines)
        {
            if (child.stateMachine.name == name)
            {
                return child.stateMachine;
            }
        }
        return null;
    }

    /// <summary>AnyState -> target，条件为 trigger 参数。</summary>
    static void AddAnyStateTransition(AnimatorStateMachine sm, AnimatorState target, string triggerName)
    {
        // 幂等：已有指向该状态的 AnyState 转换则跳过
        foreach (AnimatorStateTransition t in sm.anyStateTransitions)
        {
            if (t.destinationState == target)
            {
                return;
            }
        }
        AnimatorStateTransition tr = sm.AddAnyStateTransition(target);
        tr.hasExitTime = false;
        tr.duration = 0.1f;
        tr.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
    }

    /// <summary>攻击状态 exitTime 播完：IsGrounded=true -> idleSM，false -> airSM。</summary>
    static void AddExitTransition(AnimatorStateMachine sm, AnimatorState attackState,
        AnimatorStateMachine idleSM, AnimatorStateMachine airSM)
    {
        // 幂等：该攻击状态已有一条指向 idle 子状态机的退出转换则跳过
        foreach (AnimatorStateTransition t in attackState.transitions)
        {
            if (t != null && t.destinationStateMachine == idleSM)
            {
                return;
            }
        }

        // 注意：必须用 AddTransition(stateMachine) 工厂重载创建转换——
        // 手工 new AnimatorStateTransition 再 AddTransition(t) 的对象不会被归入
        // controller 资产序列化，SaveAssets 后磁盘上只剩 m_Transitions: {fileID: 0} 悬空引用。
        AnimatorStateTransition toIdle = attackState.AddTransition(idleSM);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 0.95f;
        toIdle.duration = 0.1f;
        toIdle.AddCondition(AnimatorConditionMode.If, 0f, "IsGrounded");

        AnimatorStateTransition toAir = attackState.AddTransition(airSM);
        toAir.hasExitTime = true;
        toAir.exitTime = 0.95f;
        toAir.duration = 0.1f;
        toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsGrounded");
    }

    /// <summary>从 fbx 中取名字包含 namePart 的第一个 AnimationClip。</summary>
    static AnimationClip LoadFirstClip(string fbxPath, string namePart)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (Object o in assets)
        {
            AnimationClip clip = o as AnimationClip;
            if (clip != null && clip.name.IndexOf(namePart, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return clip;
            }
        }
        // 退路：取第一个动画 clip
        foreach (Object o in assets)
        {
            AnimationClip clip = o as AnimationClip;
            if (clip != null)
            {
                return clip;
            }
        }
        return null;
    }

    static bool SetupPrefab()
    {
        if (!File.Exists(EllenPrefabPath))
        {
            Debug.LogError("[攻击装配] 找不到 Ellen.prefab：" + EllenPrefabPath);
            return false;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(EllenPrefabPath);
        bool changed = false;
        if (contents.GetComponent<EllenAttack>() == null)
        {
            contents.AddComponent<EllenAttack>();
            changed = true;
            Debug.Log("[攻击装配] Ellen.prefab 根已挂 EllenAttack");
        }
        else
        {
            Debug.Log("[攻击装配] Ellen.prefab 已有 EllenAttack，跳过");
        }

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(contents, EllenPrefabPath);
            AssetDatabase.SaveAssets();
        }
        PrefabUtility.UnloadPrefabContents(contents);

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(EllenPrefabPath);
        bool hasAttack = saved != null && saved.GetComponent<EllenAttack>() != null;
        Debug.Log("[攻击装配] Ellen.prefab EllenAttack=" + hasAttack +
                  "（场景中的 Ellen 实例与 EllenNet variant 会自动继承）");
        return hasAttack;
    }
}
