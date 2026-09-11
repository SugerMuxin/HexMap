using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 一键装配「PvZ 3D · 种植系统」（FEAT-003）：
///   1) 建占位资源：植物材质 + 植物 prefab（射手 / 坚果墙）+ PlantConfig 资产；
///   2) 场景建 [Planting] 根对象，挂 PlantingSystem + PlantingInput 并接线
///      （hexGrid / plantContainer / availablePlants），场景标记为脏由用户 Ctrl+S。
///
/// 幂等：已存在的资产与组件不会重复创建，也不覆盖用户已填的引用与数值
/// （availablePlants / hexGrid / plantContainer 只在为空时写入）。
///
/// 菜单：
///   Tools/种植系统/一键装配 (PvZ 种植)          —— 建资产 + 装配场景（不存场景）
///   Tools/种植系统/一键装配并保存场景
///   Tools/种植系统/清理场景中的植物实例          —— 清掉 [Planting]/Plants Container 下的残留
///
/// 说明：植物 prefab 只做占位（图元拼的低模），用户可随时换成自己的模型——
/// 直接把 PlantConfig.prefab 换掉即可，其余接线不用动。
/// </summary>
public static class PlantingSetupTool
{
    const string MaterialsDir = "Assets/Resources/Materials/Plants";
    const string PrefabsDir = "Assets/Resources/Prefabs/Plants";
    const string ConfigsDir = "Assets/Resources/Configs/Plants";

    const string ShooterstPrefabPath = PrefabsDir + "/PlantShooter.prefab";
    const string WallPrefabPath = PrefabsDir + "/PlantWall.prefab";
    const string ShooterConfigPath = ConfigsDir + "/PlantConfig_Shooter.asset";
    const string WallConfigPath = ConfigsDir + "/PlantConfig_Wall.asset";

    const string RootName = "[Planting]";
    const string ContainerName = "Plants Container";

    [MenuItem("Tools/种植系统/一键装配 (PvZ 种植)")]
    public static void Setup()
    {
        PlantConfig shooter = EnsureShooterConfig();
        PlantConfig wall = EnsureWallConfig();
        AssetDatabase.SaveAssets();

        bool sceneOk = SetupScene(new PlantConfig[] { shooter, wall });
        if (sceneOk)
        {
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        Debug.Log("[PlantingSetup] 完成：prefab=" + PrefabsDir + "，config=" + ConfigsDir +
                  "，场景对象=" + RootName + "（场景已标记为脏，按 Ctrl+S 保存）。" +
                  " 用法：按 1/2 选卡进入种植模式 → 左键点草/泥格种植；右键/Esc 取消。");
    }

    [MenuItem("Tools/种植系统/一键装配并保存场景")]
    public static void SetupAndSave()
    {
        Setup();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[PlantingSetup] 场景已保存。");
    }

    [MenuItem("Tools/种植系统/清理场景中的植物实例")]
    public static void ClearPlantedInstances()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.Log("[PlantingSetup] 场景里没有 " + RootName + "。");
            return;
        }
        Transform container = root.transform.Find(ContainerName);
        if (container == null)
        {
            Debug.Log("[PlantingSetup] 没有 " + ContainerName + "。");
            return;
        }
        int count = container.childCount;
        for (int i = count - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(container.GetChild(i).gameObject);
        }
        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[PlantingSetup] 已清理 " + count + " 个植物实例（场景已标记为脏）。");
    }

    // ================= 资源：材质 / prefab / config =================

    static PlantConfig EnsureShooterConfig()
    {
        Material leaf = EnsureMaterial("Plant_Leaf", new Color(0.36f, 0.72f, 0.24f));
        Material stem = EnsureMaterial("Plant_Stem", new Color(0.20f, 0.45f, 0.15f));
        GameObject prefab = EnsureShooterPrefab(leaf, stem);
        return EnsureConfig(ShooterConfigPath, cfg =>
        {
            cfg.displayName = "射手";
            cfg.description = "自动攻击射程内的敌人（攻击行为由 FEAT-004 战斗系统接入）。";
            cfg.sunCost = 50;
            cfg.cooldown = 3f;
            cfg.maxHealth = 100;
            cfg.attackDamage = 10;
            cfg.attackInterval = 1.4f;
            cfg.attackRange = 12f;
            cfg.projectileSpeed = 40f;
            cfg.prefab = prefab;
        });
    }

    static PlantConfig EnsureWallConfig()
    {
        Material body = EnsureMaterial("Plant_Body", new Color(0.72f, 0.52f, 0.28f));
        GameObject prefab = EnsureWallPrefab(body);
        return EnsureConfig(WallConfigPath, cfg =>
        {
            cfg.displayName = "坚果墙";
            cfg.description = "高血量阻挡单位，不攻击。";
            cfg.sunCost = 50;
            cfg.cooldown = 5f;
            cfg.maxHealth = 400;
            cfg.attackDamage = 0;
            cfg.attackInterval = 0f;
            cfg.attackRange = 0f;
            cfg.projectileSpeed = 0f;
            cfg.prefab = prefab;
        });
    }

    static Material EnsureMaterial(string name, Color color)
    {
        EnsureFolder(MaterialsDir);
        string path = MaterialsDir + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null)
        {
            return mat;   // 已存在：保留用户调过的颜色
        }

        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogError("[PlantingSetup] 找不到可用 shader（Standard / Legacy Diffuse / Unlit）");
            return null;
        }

        mat = new Material(shader);
        mat.name = name;
        mat.color = color;
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
        AssetDatabase.CreateAsset(mat, path);
        Debug.Log("[PlantingSetup] 新建材质 " + path + "（shader=" + shader.name + "）");
        return mat;
    }

    static GameObject EnsureShooterPrefab(Material leaf, Material stem)
    {
        EnsureFolder(PrefabsDir);
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ShooterstPrefabPath);
        if (existing != null) return existing;

        GameObject root = new GameObject("PlantShooter");
        AddPrimitive(root.transform, PrimitiveType.Cylinder, "Stem",
            new Vector3(0f, 0.6f, 0f), new Vector3(0.25f, 0.6f, 0.25f), stem);
        AddPrimitive(root.transform, PrimitiveType.Sphere, "Head",
            new Vector3(0f, 1.35f, 0f), new Vector3(0.9f, 0.9f, 0.9f), leaf);
        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(root.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 1.35f, 0.5f);   // FEAT-004 投射物出生点

        PlantUnit unit = root.AddComponent<PlantUnit>();
        unit.health = root.AddComponent<Health>();
        unit.centerHeight = 1f;
        PlantShooter shooter = root.AddComponent<PlantShooter>();
        shooter.muzzle = muzzle.transform;

        GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, ShooterstPrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log("[PlantingSetup] 新建植物 prefab " + ShooterstPrefabPath);
        return asset;
    }

    static GameObject EnsureWallPrefab(Material body)
    {
        EnsureFolder(PrefabsDir);
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
        if (existing != null) return existing;

        GameObject root = new GameObject("PlantWall");
        AddPrimitive(root.transform, PrimitiveType.Sphere, "Body",
            new Vector3(0f, 0.7f, 0f), new Vector3(1.6f, 1.4f, 1.6f), body);

        PlantUnit unit = root.AddComponent<PlantUnit>();
        unit.health = root.AddComponent<Health>();
        unit.centerHeight = 0.7f;

        GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, WallPrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log("[PlantingSetup] 新建植物 prefab " + WallPrefabPath);
        return asset;
    }

    /// <summary>拼一个图元子物体：剥掉自带 Collider（不抢编辑/点击射线）、指定材质。</summary>
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

    static PlantConfig EnsureConfig(string path, System.Action<PlantConfig> init)
    {
        EnsureFolder(ConfigsDir);
        PlantConfig cfg = AssetDatabase.LoadAssetAtPath<PlantConfig>(path);
        if (cfg != null)
        {
            return cfg;   // 已存在：保留用户数值
        }
        cfg = ScriptableObject.CreateInstance<PlantConfig>();
        init(cfg);
        AssetDatabase.CreateAsset(cfg, path);
        Debug.Log("[PlantingSetup] 新建植物配置 " + path);
        return cfg;
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

    static bool SetupScene(PlantConfig[] configs)
    {
        HexGrid grid = Object.FindObjectOfType<HexGrid>(true);
        if (grid == null)
        {
            Debug.LogError("[PlantingSetup] 场景里找不到 HexGrid，无法接线。");
            return false;
        }

        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "创建 " + RootName);
        }

        PlantingSystem system = root.GetComponent<PlantingSystem>();
        if (system == null) system = Undo.AddComponent<PlantingSystem>(root);

        PlantingInput input = root.GetComponent<PlantingInput>();
        if (input == null) input = Undo.AddComponent<PlantingInput>(root);

        Transform container = root.transform.Find(ContainerName);
        if (container == null)
        {
            GameObject go = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(go, "创建 " + ContainerName);
            go.transform.SetParent(root.transform, false);
            container = go.transform;
        }

        if (system.hexGrid == null) system.hexGrid = grid;
        if (system.plantContainer == null) system.plantContainer = container;
        if (system.availablePlants == null || system.availablePlants.Length == 0)
        {
            system.availablePlants = configs;
        }
        if (input.system == null) input.system = system;
        if (input.hexGrid == null) input.hexGrid = grid;

        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(system);
        EditorUtility.SetDirty(input);
        Debug.Log("[PlantingSetup] 已装配 " + RootName + "（PlantingSystem + PlantingInput，hexGrid=" +
                  grid.name + "，卡数=" + (system.availablePlants != null ? system.availablePlants.Length : 0) + "）");
        return true;
    }
}
