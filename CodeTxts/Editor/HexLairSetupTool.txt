using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一键装配「地形特征 · 怪物巢穴（MonsterLair）」：
///   1) 把 MonsterLair.prefab 写入 HexGridChunk 预制体的 HexFeatureManager.monsterLairPrefabs；
///   2) 在 Features Editor 面板复制 Plant 行，生成 Lair 开关 + LairSlider
///      并绑定到 HexMapEditor.SetApplyLair / SetLairLevel。
///
/// 幂等：重复运行不会重复创建控件；已存在的控件会被重新定位与重绑。
/// 位置：Lair 行放在 Dense 之上（面板顶部留白处），不改动任何现有控件位置。
///
/// 菜单：
///   Tools/地形特征/装配怪物巢穴 (UI+预制体)            —— 改内存 + 标记场景为脏（请自行 Ctrl+S）
///   Tools/地形特征/装配怪物巢穴 (UI+预制体) 并保存场景  —— 同时保存场景
/// </summary>
public static class HexLairSetupTool
{
    const string LairPrefabPath = "Assets/Resources/Prefabs/ScenePrefabs/MonsterLair.prefab";
    const string ChunkPrefabPath = "Assets/Resources/Prefabs/HexGridChunk.prefab";

    // 新行位置（x 与 Plant 行一致；y 放在 Dense(205) 之上，避开面板底部与按钮）
    static readonly Vector2 LairRowPos = new Vector2(-70.12f, 305f);
    static readonly Vector2 LairSliderPos = new Vector2(-55.53f, 260f);

    // Features Editor 面板（上下 stretch 锚点）至少要这么高：
    // 顶部容纳 Lair 行(+305+半高) 与 LairSlider(+260)，底部容纳 Sure/Cancel(-189-半高)。
    const float RequiredPanelHeight = 700f;

    [MenuItem("Tools/地形特征/装配怪物巢穴 (UI+预制体)")]
    public static void Setup()
    {
        bool okPrefab = WirePrefab();
        bool okUI = WireUI();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[HexLairSetup] 完成：prefab=" + okPrefab + " UI=" + okUI +
                  "。场景已标记为脏，按 Ctrl+S 保存。");
    }

    [MenuItem("Tools/地形特征/装配怪物巢穴 (UI+预制体) 并保存场景")]
    public static void SetupAndSave()
    {
        Setup();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[HexLairSetup] 场景已保存。");
    }

    static bool WirePrefab()
    {
        Transform lair = AssetDatabase.LoadAssetAtPath<Transform>(LairPrefabPath);
        if (lair == null)
        {
            Debug.LogError("[HexLairSetup] 找不到巢穴预制体：" + LairPrefabPath);
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(ChunkPrefabPath);
        if (root == null)
        {
            Debug.LogError("[HexLairSetup] 无法加载预制体：" + ChunkPrefabPath);
            return false;
        }
        try
        {
            HexFeatureManager fm = root.GetComponentInChildren<HexFeatureManager>(true);
            if (fm == null)
            {
                Debug.LogError("[HexLairSetup] HexGridChunk 预制体上没有 HexFeatureManager");
                return false;
            }

            List<Transform> list = new List<Transform>(fm.monsterLairPrefabs ?? new Transform[0]);
            if (!list.Contains(lair))
            {
                list.Add(lair);
            }
            fm.monsterLairPrefabs = list.ToArray();

            PrefabUtility.SaveAsPrefabAsset(root, ChunkPrefabPath);
            Debug.Log("[HexLairSetup] 已写入 HexGridChunk.monsterLairPrefabs（" + list.Count +
                      " 项，首项 " + lair.name + "）");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static bool WireUI()
    {
        HexMapEditor editor = Object.FindObjectOfType<HexMapEditor>(true);
        if (editor == null)
        {
            Debug.LogError("[HexLairSetup] 场景里找不到 HexMapEditor");
            return false;
        }

        Transform panel = FindFeaturesPanel();
        if (panel == null)
        {
            Debug.LogError("[HexLairSetup] 找不到 Features Editor 面板（Canvas > Panel）");
            return false;
        }

        RectTransform plantRow = panel.Find("Plant") as RectTransform;
        RectTransform plantSlider = panel.Find("PlantSlider") as RectTransform;
        if (plantRow == null || plantSlider == null)
        {
            Debug.LogError("[HexLairSetup] 找不到 Plant / PlantSlider 模板行");
            return false;
        }

        // ---- Lair 开关 ----
        RectTransform lairRow = panel.Find("Lair") as RectTransform;
        if (lairRow == null)
        {
            lairRow = Object.Instantiate(plantRow, panel, false);
            lairRow.name = "Lair";
            Undo.RegisterCreatedObjectUndo(lairRow.gameObject, "装配怪物巢穴 UI");
        }
        lairRow.anchoredPosition = LairRowPos;
        SetLabel(lairRow, "Lair");

        Toggle tg = lairRow.GetComponent<Toggle>();
        if (tg != null)
        {
            ClearPersistentCalls(tg);
            tg.SetIsOnWithoutNotify(false);
            UnityEventTools.AddPersistentListener<bool>(tg.onValueChanged, editor.SetApplyLair);
            EditorUtility.SetDirty(tg);
        }

        // ---- LairSlider ----
        RectTransform lairSlider = panel.Find("LairSlider") as RectTransform;
        if (lairSlider == null)
        {
            lairSlider = Object.Instantiate(plantSlider, panel, false);
            lairSlider.name = "LairSlider";
            Undo.RegisterCreatedObjectUndo(lairSlider.gameObject, "装配怪物巢穴 UI");
        }
        lairSlider.anchoredPosition = LairSliderPos;

        Slider sl = lairSlider.GetComponent<Slider>();
        if (sl != null)
        {
            ClearPersistentCalls(sl);
            sl.minValue = 0f;
            sl.maxValue = 3f;
            sl.wholeNumbers = true;
            sl.SetValueWithoutNotify(0f);
            UnityEventTools.AddPersistentListener<float>(sl.onValueChanged, editor.SetLairLevel);
            EditorUtility.SetDirty(sl);
        }

        // ---- 面板背景加高，保证新行落在面板内（幂等：仅在当前过矮时调整） ----
        // 面板是上下 stretch 锚点 → 实际高度 = 画布高 + sizeDelta.y；
        // 画布高取当前 Game 视图（Screen.height），避免写死 1080 在别的分辨率下溢出。
        RectTransform panelRt = panel as RectTransform;
        if (panelRt != null)
        {
            float canvasHeight = Screen.height > 0 ? Screen.height : 1080f;
            float currentHeight = canvasHeight + panelRt.sizeDelta.y;
            if (currentHeight < RequiredPanelHeight)
            {
                Vector2 sd = panelRt.sizeDelta;
                sd.y = RequiredPanelHeight - canvasHeight;
                panelRt.sizeDelta = sd;
                Debug.Log("[HexLairSetup] 面板高度 " + currentHeight + " → " + RequiredPanelHeight);
                EditorUtility.SetDirty(panelRt);
            }
        }

        EditorUtility.SetDirty(editor);
        Debug.Log("[HexLairSetup] 已在 Features Editor 生成/绑定 Lair 开关与 LairSlider");
        return true;
    }

    static Transform FindFeaturesPanel()
    {
        Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i].name == "Features Editor" && canvases[i].transform.childCount > 0)
            {
                return canvases[i].transform.GetChild(0);
            }
        }
        return null;
    }

    static void SetLabel(Transform row, string text)
    {
        Transform label = row.Find("Label");
        if (label == null)
        {
            return;
        }
        Text t = label.GetComponent<Text>();
        if (t != null)
        {
            t.text = text;
        }
    }

    /// <summary>
    /// 清空控件 UnityEvent 的持久化监听（克隆模板行时会带来源行的监听，必须清掉再重绑）。
    /// 采用版本无关的路径探测：不同 Unity 版本 Toggle/Slider 的序列化字段名可能是
    /// onValueChanged 或 m_OnValueChanged，因此按后缀匹配 m_PersistentCalls.m_Calls。
    /// </summary>
    static void ClearPersistentCalls(Component comp)
    {
        SerializedObject so = new SerializedObject(comp);
        string path = null;
        SerializedProperty it = so.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.propertyPath.EndsWith("m_PersistentCalls.m_Calls"))
            {
                path = it.propertyPath;
                break;
            }
        }
        if (path == null)
        {
            return;
        }
        SerializedProperty calls = so.FindProperty(path);
        if (calls != null && calls.isArray && calls.arraySize > 0)
        {
            calls.ClearArray();
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
