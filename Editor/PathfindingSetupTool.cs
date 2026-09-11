using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键装配「FEAT-001 · 六边形寻路（PvZ 寻路）」：
///   1) 建占位资源：单位材质 + 占位寻路单位 prefab（胶囊 + 球，无 Collider）+ 无；
///   2) 场景建 [Pathfinding] 根对象，挂 PathfindingDemo，子物体 Units Container 放入一个单位实例，
///      接线 hexGrid / unit / unitPrefab / unitContainer（场景标记为脏，由用户 Ctrl+S 保存）；
///   3) 另提供通行规则预设菜单（PvZ 仅草/泥 / 恢复默认全陆地）与装配状态检查。
///
/// 幂等：已存在的资产与场景对象不重复创建，也不覆盖用户已填的引用与数值
/// （unit / unitPrefab / unitContainer / hexGrid 只在为空时写入）。
///
/// 菜单：
///   Tools/寻路系统/一键装配 (PvZ 寻路)
///   Tools/寻路系统/一键装配并保存场景
///   Tools/寻路系统/清理场景中的寻路单位
///   Tools/寻路系统/应用 PvZ 通行预设（仅草/泥可行走）
///   Tools/寻路系统/恢复默认通行预设（全部陆地）
///   Tools/寻路系统/切换为「仅同高度」寻路（默认） / 切换为「允许坡道」寻路
///   Tools/寻路系统/检查寻路装配状态
///
/// 说明：占位单位只用于跑通链路，换模型只需改场景里那个实例 / 换 prefab，寻路代码不用动。
/// </summary>
public static class PathfindingSetupTool
{
    const string MaterialsDir = "Assets/Resources/Materials/Units";
    const string PrefabsDir = "Assets/Resources/Prefabs/Units";
    const string AgentPrefabPath = PrefabsDir + "/PathAgent.prefab";

    const string RootName = "[Pathfinding]";
    const string ContainerName = "Units Container";
    const float AgentTravelSpeed = 4f;

    [MenuItem("Tools/寻路系统/一键装配 (PvZ 寻路)")]
    public static void Setup()
    {
        EnsureFolder(MaterialsDir);
        EnsureFolder(PrefabsDir);
        GameObject agentPrefab = EnsureAgentPrefab();
        AssetDatabase.SaveAssets();

        bool sceneOk = SetupScene(agentPrefab);
        if (sceneOk)
        {
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        Debug.Log("[PathfindingSetup] 完成：prefab=" + AgentPrefabPath +
                  "，场景对象=" + RootName + "（场景已标记为脏，按 Ctrl+S 保存）。" +
                  " 用法：Play 后单位会自动从起点走到终点；中键点地面 = 让它寻路过去；" +
                  "P = 重算到终点，O = 换随机终点，G = 显示/隐藏路径线。");
    }

    [MenuItem("Tools/寻路系统/一键装配并保存场景")]
    public static void SetupAndSave()
    {
        Setup();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[PathfindingSetup] 场景已保存。");
    }

    [MenuItem("Tools/寻路系统/清理场景中的寻路单位")]
    public static void ClearDemoUnits()
    {
        GameObject root = FindRootIncludingInactive(RootName);
        if (root == null)
        {
            Debug.Log("[PathfindingSetup] 场景里没有 " + RootName + "。");
            return;
        }
        PathfindingDemo demo = root.GetComponent<PathfindingDemo>();
        if (demo != null) demo.unit = null;

        Transform container = root.transform.Find(ContainerName);
        if (container == null)
        {
            Debug.Log("[PathfindingSetup] 没有 " + ContainerName + "。");
            return;
        }
        int count = container.childCount;
        for (int i = count - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(container.GetChild(i).gameObject);
        }
        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[PathfindingSetup] 已清理 " + count + " 个寻路单位实例（场景已标记为脏）。");
    }

    [MenuItem("Tools/寻路系统/应用 PvZ 通行预设（仅草/泥可行走）")]
    public static void ApplyPvZPreset()
    {
        HexGrid grid = Object.FindObjectOfType<HexGrid>(true);
        if (grid == null)
        {
            Debug.LogError("[PathfindingSetup] 场景里找不到 HexGrid。");
            return;
        }
        Undo.RecordObject(grid, "应用 PvZ 通行预设");
        grid.ApplyPvZWalkablePreset();
        EditorUtility.SetDirty(grid);
        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Debug.Log("[PathfindingSetup] HexGrid.walkableTerrain 已设为「仅草/泥可行走」" +
                  "（沙/石/雪 + 水下 + 悬崖不可通行）。注意：新地图默认地形全是沙（0），" +
                  "应用此预设后需要先用地图编辑器刷出草(1)/泥(2)，或载入带草/泥的 .map，否则无路可走。");
    }

    [MenuItem("Tools/寻路系统/恢复默认通行预设（全部陆地）")]
    public static void ApplyDefaultPreset()
    {
        HexGrid grid = Object.FindObjectOfType<HexGrid>(true);
        if (grid == null)
        {
            Debug.LogError("[PathfindingSetup] 场景里找不到 HexGrid。");
            return;
        }
        Undo.RecordObject(grid, "恢复默认通行预设");
        grid.ApplyDefaultWalkablePreset();
        EditorUtility.SetDirty(grid);
        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Debug.Log("[PathfindingSetup] HexGrid.walkableTerrain 已恢复为「全部陆地可通行」" +
                  "（水下与悬崖仍不可通行，可在 HexGrid 上关闭）。");
    }

    [MenuItem("Tools/寻路系统/切换为「仅同高度」寻路（默认）")]
    public static void SetSameElevationMode()
    {
        SetPathMode(HexPathMode.SameElevationOnly);
    }

    [MenuItem("Tools/寻路系统/切换为「允许坡道」寻路")]
    public static void SetAllowSlopeMode()
    {
        SetPathMode(HexPathMode.AllowSlope);
    }

    static void SetPathMode(HexPathMode mode)
    {
        HexGrid grid = Object.FindObjectOfType<HexGrid>(true);
        if (grid == null)
        {
            Debug.LogError("[PathfindingSetup] 场景里找不到 HexGrid。");
            return;
        }
        Undo.RecordObject(grid, "切换寻路模式");
        grid.pathMode = mode;
        EditorUtility.SetDirty(grid);
        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Debug.Log("[PathfindingSetup] HexGrid.pathMode = " + mode + "（" +
                  (mode == HexPathMode.SameElevationOnly
                      ? "只在相同高度的格之间寻路，不支持攀爬/跳跃"
                      : "允许沿坡道上下（落差 1），悬崖仍不可通行") + "）。运行时也可用 Play 中的 H 键切换。");
    }

    [MenuItem("Tools/寻路系统/检查寻路装配状态")]
    public static void CheckStatus()
    {
        HexGrid grid = Object.FindObjectOfType<HexGrid>(true);
        GameObject root = GameObject.Find(RootName);
        PathfindingDemo demo = root != null ? root.GetComponent<PathfindingDemo>() : null;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("[PathfindingSetup] 装配状态：");
        sb.AppendLine("  HexGrid: " + (grid != null ? grid.name : "❌ 缺失"));
        if (grid != null)
        {
            sb.AppendLine("  寻路模式 pathMode: " + grid.pathMode + "（" +
                          (grid.pathMode == HexPathMode.SameElevationOnly
                              ? "仅同高度 · 不支持攀爬/跳跃" : "允许坡道") + "）");
            sb.AppendLine("  通行地形 walkableTerrain: " + DescribeTerrain(grid.walkableTerrain) +
                          " / 水下阻挡=" + grid.blockUnderwater + " / 悬崖阻挡=" + grid.blockCliffs +
                          " / 启发式权重(生效)=" + grid.EffectiveHeuristicWeight());
        }
        sb.AppendLine("  " + RootName + ": " + (root != null ? "✅" : "❌ 缺失（跑一次一键装配）"));
        if (demo != null)
        {
            sb.AppendLine("    unit=" + (demo.unit != null ? demo.unit.name : "（空，运行时用 prefab 生成）") +
                          " / unitPrefab=" + (demo.unitPrefab != null ? demo.unitPrefab.name : "❌") +
                          " / unitContainer=" + (demo.unitContainer != null ? demo.unitContainer.name : "❌") +
                          " / hexGrid=" + (demo.hexGrid != null ? demo.hexGrid.name : "❌"));
        }
        sb.AppendLine("  占位 prefab: " + (AssetDatabase.LoadAssetAtPath<GameObject>(AgentPrefabPath) != null
            ? AgentPrefabPath : "❌ 缺失"));
        Debug.Log(sb.ToString());
    }

    static string DescribeTerrain(bool[] terrain)
    {
        if (terrain == null) return "（空 = 全通行）";
        string[] names = { "沙", "草", "泥", "石", "雪" };
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < names.Length; i++)
        {
            bool walkable = i < terrain.Length && terrain[i];
            sb.Append(names[i]).Append(walkable ? "✓" : "✗").Append(" ");
        }
        return sb.ToString();
    }

    // ================= 资源 =================

    static GameObject EnsureAgentPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(AgentPrefabPath);
        if (existing != null) return existing;

        Material body = EnsureMaterial("Unit_Body", new Color(0.45f, 0.38f, 0.32f));
        Material head = EnsureMaterial("Unit_Head", new Color(0.78f, 0.72f, 0.60f));

        GameObject root = new GameObject("PathAgent");
        AddPrimitive(root.transform, PrimitiveType.Capsule, "Body",
            new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), body);
        AddPrimitive(root.transform, PrimitiveType.Sphere, "Head",
            new Vector3(0f, 2.2f, 0f), new Vector3(0.7f, 0.7f, 0.7f), head);

        HexUnit unit = root.AddComponent<HexUnit>();
        unit.travelSpeed = AgentTravelSpeed;
        unit.turnSpeed = 720f;

        GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, AgentPrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log("[PathfindingSetup] 新建占位寻路单位 prefab " + AgentPrefabPath);
        return asset;
    }

    static Material EnsureMaterial(string name, Color color)
    {
        EnsureFolder(MaterialsDir);
        string path = MaterialsDir + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;   // 已存在：保留用户调过的颜色

        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogError("[PathfindingSetup] 找不到可用 shader（Standard / Legacy Diffuse / Unlit）");
            return null;
        }

        mat = new Material(shader);
        mat.name = name;
        mat.color = color;
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
        AssetDatabase.CreateAsset(mat, path);
        Debug.Log("[PathfindingSetup] 新建材质 " + path + "（shader=" + shader.name + "）");
        return mat;
    }

    /// <summary>拼一个图元子物体：剥掉自带 Collider（不抢编辑/点击射线的最近命中）、指定材质。</summary>
    static GameObject AddPrimitive(Transform parent, PrimitiveType type, string name,
        Vector3 localPosition, Vector3 localScale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;

        Collider col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        if (mat != null)
        {
            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }
        return go;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string[] parts = folder.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    // ================= 场景装配 =================

    /// <summary>
    /// 找场景根对象下指定名字的对象——**包括 inactive 的**。
    /// ⚠️ 必须用这个而不是 GameObject.Find：GameObject.Find 找不到 inactive 对象，
    /// 于是"把 [Pathfinding] 关掉后再点一键装配"会又建一个 → 场景里出现重复根对象（曾发生过）。
    /// </summary>
    static GameObject FindRootIncludingInactive(string name)
    {
        GameObject firstInactive = null;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || roots[i].name != name) continue;
                if (roots[i].activeSelf) return roots[i];       // 优先取启用的
                if (firstInactive == null) firstInactive = roots[i];
            }
        }
        if (firstInactive != null) return firstInactive;

        // 兜底：可能挂在别的场景 / 非根层级
        GameObject[] all = Object.FindObjectsOfType<GameObject>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name) return all[i];
        }
        return null;
    }

    /// <summary>删除同名重复根对象（只删带 PathfindingDemo 的，即本工具自己建的）。</summary>
    static int RemoveDuplicateRoots(string name, GameObject keep)
    {
        int removed = 0;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = roots.Length - 1; i >= 0; i--)
        {
            GameObject go = roots[i];
            if (go == null || go == keep || go.name != name) continue;
            if (go.GetComponent<PathfindingDemo>() == null) continue;   // 不是本工具的对象，留给人
            Debug.LogWarning("[PathfindingSetup] 发现重复的 " + name + "（active=" + go.activeSelf +
                             "），已删除多余的那个。原因通常是：对象被设为 inactive 后又点了「一键装配」" +
                             "（旧版工具用 GameObject.Find，找不到 inactive 对象就会又建一个）。");
            Undo.DestroyObjectImmediate(go);
            removed++;
        }
        return removed;
    }

    static bool SetupScene(GameObject agentPrefab)
    {
        HexGrid grid = Object.FindObjectOfType<HexGrid>(true);
        if (grid == null)
        {
            Debug.LogError("[PathfindingSetup] 场景里找不到 HexGrid，无法接线。");
            return false;
        }

        GameObject root = FindRootIncludingInactive(RootName);
        int deduped = RemoveDuplicateRoots(RootName, root);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "创建 " + RootName);
        }
        else if (!root.activeSelf)
        {
            root.SetActive(true);      // 复用时要保证是启用的，否则 Play 里看不到
        }
        if (deduped > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        PathfindingDemo demo = root.GetComponent<PathfindingDemo>();
        if (demo == null) demo = Undo.AddComponent<PathfindingDemo>(root);

        Transform container = root.transform.Find(ContainerName);
        if (container == null)
        {
            GameObject go = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(go, "创建 " + ContainerName);
            go.transform.SetParent(root.transform, false);
            container = go.transform;
        }

        if (demo.hexGrid == null) demo.hexGrid = grid;
        if (demo.unitContainer == null) demo.unitContainer = container;
        if (demo.unitPrefab == null) demo.unitPrefab = agentPrefab;

        // 场景里放一个单位实例（没有才建），让"不动手也能看见单位"；引用只在为空时写
        HexUnit existingUnit = container.GetComponentInChildren<HexUnit>(true);
        if (existingUnit == null && agentPrefab != null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(agentPrefab, container);
            instance.name = agentPrefab.name;
            existingUnit = instance.GetComponent<HexUnit>();
        }
        if (existingUnit != null)
        {
            if (existingUnit.hexGrid == null) existingUnit.hexGrid = grid;
            if (demo.unit == null) demo.unit = existingUnit;
        }

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(demo);
        if (existingUnit != null) EditorUtility.SetDirty(existingUnit);
        Debug.Log("[PathfindingSetup] 已装配 " + RootName + "（PathfindingDemo + " + ContainerName +
                  "，hexGrid=" + grid.name + "，单位=" +
                  (demo.unit != null ? demo.unit.name : "无") + "）");
        return true;
    }
}
