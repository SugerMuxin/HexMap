using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 一键装配「AI 怪物系统」：
///   1) 怪物 prefab（优先复用 `Resources/Prefabs/Monsters/` 下已有的怪物 prefab，缺则建占位）
///      —— 补挂 <see cref="MonsterUnit"/>（会自动带上 HexUnit + Health），保留原有模型 / 碰撞体；
///   2) 怪物配置 <see cref="MonsterConfig"/> + 怪物表 <see cref="MonsterTable"/>；
///   3) **场景配置表** <see cref="SceneConfigTable"/>（当前场景一条默认刷怪规则）；
///   4) 两份 CSV 基线（MonsterTable.csv / SceneConfigTable.csv，不存在才生成，供 Excel 编辑）；
///   5) 场景对象 `[Monsters]`（MonsterSpawner + 容器）+ 玩家侧
///      <see cref="PlayerTarget"/> / <see cref="PlayerMeleeAttackBridge"/> 接线。
///
/// 幂等：已存在的资产 / 组件 / 配置行不会重复创建，也不覆盖用户已填的引用与数值
/// （只补空引用）。场景只标记为脏，请自行 Ctrl+S。
///
/// 菜单：
///   Tools/怪物系统/一键装配 (AI 怪物)
///   Tools/怪物系统/一键装配并保存场景
///   Tools/怪物系统/清理场景中的怪物实例
///   Tools/怪物系统/检查怪物系统装配状态
/// </summary>
public static class MonsterSetupTool
{
    const string MonstersPrefabsDir = "Assets/Resources/Prefabs/Monsters";
    const string ConfigsDir = "Assets/Resources/Configs/Monsters";
    const string TablesDir = "Assets/Resources/Configs/Tables";
    const string MaterialsDir = "Assets/Resources/Materials/Monsters";

    /// <summary>用户放置怪物 prefab 的目录（需求：怪物的 prefab 放在 Monsters 文件夹中）。</summary>
    const string UserMonsterPrefabPath = MonstersPrefabsDir + "/Zombie1.prefab";
    const string PlaceholderPrefabPath = MonstersPrefabsDir + "/MonsterPlaceholder.prefab";

    const string DefaultMonsterId = "Zombie";
    const string DefaultConfigPath = ConfigsDir + "/" + MonsterTable.MonsterConfigNamePrefix + DefaultMonsterId + ".asset";
    const string MonsterTableAssetPath = "Assets/Resources/Configs/Tables/MonsterTable.asset";
    const string SceneConfigTableAssetPath = "Assets/Resources/Configs/Tables/SceneConfigTable.asset";
    const string MonsterTableCsvPath = "Assets/Resources/Configs/Tables/MonsterTable.csv";
    const string SceneConfigCsvPath = "Assets/Resources/Configs/Tables/SceneConfigTable.csv";

    const string RootName = "[Monsters]";
    const string ContainerName = "Monsters Container";

    /// <summary>装配时是否给玩家补一个 Health（怪物 attackPlayers 打开时要扣玩家的血；已有则跳过）。</summary>
    const bool AddPlayerHealth = true;
    const int PlayerMaxHealth = 100;

    [MenuItem("Tools/怪物系统/一键装配 (AI 怪物)")]
    public static void Setup()
    {
        GameObject monsterPrefab = EnsureMonsterPrefab();
        MonsterConfig config = EnsureMonsterConfig(monsterPrefab);
        MonsterTable table = EnsureMonsterTable(config);
        SceneConfigTable sceneTable = EnsureSceneConfigTable(config);
        AssetDatabase.SaveAssets();

        EnsureCsvBaselines();
        bool sceneOk = WireScene(table, sceneTable);
        if (sceneOk)
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        Debug.Log("[MonsterSetup] 完成：prefab=" + MonstersPrefabsDir + "，config=" + ConfigsDir +
                  "，场景对象=" + RootName + "，场景配置表=" + SceneConfigTableAssetPath +
                  "（场景已标记为脏，按 Ctrl+S 保存）。" +
                  " 玩法：编辑 " + SceneConfigCsvPath + " 调「数量 / 频率 / 波次」→ 菜单 Tools/UI/数据表/导入 场景配置表 CSV → Play。" +
                  " 场景里要先用地形编辑器在巢穴（Lair）上放置出怪点。");
    }

    [MenuItem("Tools/怪物系统/一键装配并保存场景")]
    public static void SetupAndSave()
    {
        Setup();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[MonsterSetup] 场景已保存。");
    }

    [MenuItem("Tools/怪物系统/清理场景中的怪物实例")]
    public static void ClearMonsterInstances()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.Log("[MonsterSetup] 场景里没有 " + RootName + "。");
            return;
        }
        Transform container = root.transform.Find(ContainerName);
        if (container == null)
        {
            Debug.Log("[MonsterSetup] 没有 " + ContainerName + "。");
            return;
        }
        // 容器子物体里既有运行时残留，也可能有 Play 后未清理的怪物
        int count = 0;
        MonsterSpawner spawner = root.GetComponent<MonsterSpawner>();
        if (spawner != null) spawner.ClearMonsters(true);
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(container.GetChild(i).gameObject);
            count++;
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[MonsterSetup] 已清理 " + count + " 个怪物实例（场景已标记为脏）。");
    }

    /// <summary>
    /// 诊断「怪物能不能走到玩家」：列出玩家 / 巢穴 / 每个巢穴的可达性，
    /// 并在巢穴被水或悬崖隔断时给出具体处理办法。**只读，不改场景。**
    /// Play 模式下运行最准（用真实玩家位置）；EditMode 下用场景里玩家对象的当前位置。
    /// </summary>
    [MenuItem("Tools/怪物系统/诊断出生点可达性")]
    public static void DiagnoseSpawnReachability()
    {
        MonsterSpawner spawner = Object.FindObjectOfType<MonsterSpawner>(true);
        if (spawner == null)
        {
            Debug.LogWarning("[MonsterSetup] 场景里没有 MonsterSpawner（[Monsters]）。先跑「一键装配 (AI 怪物)」。");
            return;
        }
        spawner.sceneConfigTable = spawner.sceneConfigTable != null
            ? spawner.sceneConfigTable
            : AssetDatabase.LoadAssetAtPath<SceneConfigTable>(TablesDir + "/SceneConfigTable.asset");

        Debug.Log("[MonsterSetup] 出生点可达性诊断\n" + spawner.DiagnoseSpawnReachability());
    }

    [MenuItem("Tools/怪物系统/检查怪物系统装配状态")]
    public static void CheckStatus()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("[MonsterSetup] 装配状态：");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UserMonsterPrefabPath);
        GameObject fallback = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderPrefabPath);
        sb.AppendLine("  怪物 prefab（Monsters 目录）: " + (prefab != null ? UserMonsterPrefabPath : "（缺，将用 " + PlaceholderPrefabPath + "）"));
        GameObject usePrefab = prefab != null ? prefab : fallback;
        if (usePrefab != null)
        {
            sb.AppendLine("    - MonsterUnit: " + (usePrefab.GetComponent<MonsterUnit>() != null) +
                          "，Collider: " + (usePrefab.GetComponent<Collider>() != null) +
                          "，Renderer: " + (usePrefab.GetComponent<Renderer>() != null));
        }

        MonsterConfig cfg = AssetDatabase.LoadAssetAtPath<MonsterConfig>(DefaultConfigPath);
        sb.AppendLine("  怪物配置: " + (cfg != null
            ? DefaultConfigPath + "（prefab=" + (cfg.prefab != null ? cfg.prefab.name : "未接线") + "，血量 " + cfg.maxHealth +
              "，移速 " + cfg.moveSpeed + "，锁敌 " + cfg.lockMode + "，攻击玩家 " + cfg.attackPlayers + "）"
            : "（缺）"));

        MonsterTable table = AssetDatabase.LoadAssetAtPath<MonsterTable>(MonsterTableAssetPath);
        sb.AppendLine("  怪物表: " + (table != null ? MonsterTableAssetPath + "（" + table.Count + " 条）" : "（缺）"));

        SceneConfigTable st = AssetDatabase.LoadAssetAtPath<SceneConfigTable>(SceneConfigTableAssetPath);
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (st == null) sb.AppendLine("  场景配置表: （缺）");
        else
        {
            sb.AppendLine("  场景配置表: " + SceneConfigTableAssetPath + "（" + st.Count + " 行）");
            List<MonsterSceneConfigEntry> rows = st.GetForScene(sceneName);
            sb.AppendLine("    当前场景 \"" + sceneName + "\" 生效行: " + rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                MonsterSceneConfigEntry e = rows[i];
                sb.AppendLine("      - " + e.id + "：怪物=" + (e.monster != null ? e.monster.name : "<null:" + e.monsterId + ">") +
                              "，数量=" + e.count + "，频率=" + e.interval + "s，上限=" + e.maxAlive +
                              "，波次=" + e.waves + "，点=" + e.spawnPoint + "，启用=" + e.enabled +
                              (e.IsUsable ? "" : "  ⚠ 不可用"));
            }
        }

        GameObject root = GameObject.Find(RootName);
        MonsterSpawner spawner = root != null ? root.GetComponent<MonsterSpawner>() : null;
        sb.AppendLine("  场景对象 " + RootName + ": " + (root != null) + "，MonsterSpawner: " + (spawner != null));
        if (spawner != null)
        {
            sb.AppendLine("    hexGrid=" + (spawner.hexGrid != null) +
                          "，场景配置表=" + (spawner.sceneConfigTable != null) +
                          "，怪物表=" + (spawner.monsterTable != null) +
                          "，容器=" + (spawner.spawnContainer != null));
        }

        PlayerTarget pt = Object.FindObjectOfType<PlayerTarget>(true);
        sb.AppendLine("  玩家目标 PlayerTarget: " + (pt != null ? FullPath(pt.gameObject) : "（缺）"));
        if (pt != null)
        {
            PlayerMeleeAttackBridge bridge = pt.GetComponent<PlayerMeleeAttackBridge>();
            sb.AppendLine("    - PlayerMeleeAttackBridge: " + (bridge != null) +
                          (bridge != null && bridge.attack == null ? "  ⚠ 未找到 EllenAttack（玩家攻击不会触发判定）" : "") +
                          "，Health: " + (pt.GetComponentInChildren<Health>(true) != null));
        }

        HexGrid grid = Object.FindObjectOfType<HexGrid>(true);
        if (grid != null)
        {
            int lairs = 0, walkable = 0;
            HexCell[] cells = grid.Cells;
            if (cells == null || cells.Length == 0)
            {
                sb.AppendLine("  地图: 未建图（编辑器非运行态 / 尚未 CreateMap）→ 巢穴格与可通行格数需在 Play 或建图后再看");
            }
            else
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] == null) continue;
                    if (cells[i].MonsterLairLevel > 0) lairs++;
                    if (grid.IsWalkable(cells[i])) walkable++;
                }
                sb.AppendLine("  地图: 格 " + cells.Length + "，可通行 " + walkable + "，怪物巢穴格 " + lairs +
                              (lairs == 0 ? "  ⚠ 没巢穴 → 出怪点会回退到地图西边缘（菜单 Tools/地形特征 画 Lair）" : ""));
                if (lairs > 0 && walkable <= lairs)
                {
                    sb.AppendLine("    ⚠ 可通行格过少：检查 HexGrid.walkableTerrain 通行预设（菜单 Tools/寻路系统/恢复默认通行预设）");
                }
            }
        }

        Debug.Log(sb.ToString());
    }

    // ================= 资产 =================

    static GameObject EnsureMonsterPrefab()
    {
        EnsureFolder(MonstersPrefabsDir);

        // 优先补挂用户自己的怪物 prefab（需求：prefab 放在 Monsters 文件夹）
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(UserMonsterPrefabPath);
        if (existing != null)
        {
            if (existing.GetComponent<MonsterUnit>() == null)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(UserMonsterPrefabPath);
                try
                {
                    if (root.GetComponent<MonsterUnit>() == null) root.AddComponent<MonsterUnit>();
                    PrefabUtility.SaveAsPrefabAsset(root, UserMonsterPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                AssetDatabase.SaveAssets();
                existing = AssetDatabase.LoadAssetAtPath<GameObject>(UserMonsterPrefabPath);
                Debug.Log("[MonsterSetup] 已给 " + UserMonsterPrefabPath + " 补挂 MonsterUnit（模型 / 碰撞体原样保留）。");
            }
            return existing;
        }

        // 没有用户 prefab：建一个占位怪物（胶囊 + 眼睛，方便目视）
        GameObject placeholder = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderPrefabPath);
        if (placeholder != null) return placeholder;

        Material body = EnsureMaterial("Monster_Body", new Color(0.35f, 0.55f, 0.30f));
        Material eye = EnsureMaterial("Monster_Eye", new Color(0.85f, 0.20f, 0.18f));

        GameObject go = new GameObject("MonsterPlaceholder");
        AddPrimitive(go.transform, PrimitiveType.Capsule, "Body",
            new Vector3(0f, 1f, 0f), new Vector3(0.9f, 1f, 0.9f), body);
        AddPrimitive(go.transform, PrimitiveType.Sphere, "EyeL",
            new Vector3(-0.18f, 1.55f, 0.38f), Vector3.one * 0.18f, eye);
        AddPrimitive(go.transform, PrimitiveType.Sphere, "EyeR",
            new Vector3(0.18f, 1.55f, 0.38f), Vector3.one * 0.18f, eye);
        go.AddComponent<MonsterUnit>();

        GameObject asset = PrefabUtility.SaveAsPrefabAsset(go, PlaceholderPrefabPath);
        Object.DestroyImmediate(go);
        Debug.Log("[MonsterSetup] 新建占位怪物 prefab " + PlaceholderPrefabPath +
                  "（把你的怪物 prefab 放进 " + MonstersPrefabsDir + " 后可改 MonsterConfig.prefab 替换）。");
        return asset;
    }

    static MonsterConfig EnsureMonsterConfig(GameObject prefab)
    {
        EnsureFolder(ConfigsDir);
        MonsterConfig cfg = AssetDatabase.LoadAssetAtPath<MonsterConfig>(DefaultConfigPath);
        if (cfg != null)
        {
            // 已存在：只补空引用，不改用户数值
            if (cfg.prefab == null && prefab != null)
            {
                cfg.prefab = prefab;
                EditorUtility.SetDirty(cfg);
            }
            return cfg;
        }

        cfg = ScriptableObject.CreateInstance<MonsterConfig>();
        cfg.displayName = "僵尸";
        cfg.description = "从怪物巢穴诞生的怪物：锁定离它最近的玩家并沿六边形路径追击，" +
                          "被更近的玩家攻击时转换锁定目标。";
        cfg.prefab = prefab;
        cfg.maxHealth = 100;
        cfg.moveSpeed = 4f;
        cfg.turnSpeed = 720f;
        cfg.lockMode = MonsterLockMode.FirstUntilAttacked;
        cfg.detectRange = 0f;
        cfg.retargetInterval = 0.5f;
        cfg.switchOnlyWhenCloser = true;
        cfg.repathInterval = 0.5f;
        cfg.arriveDistance = 1f;
        cfg.attackRange = 6f;
        cfg.attackPlayers = false;
        cfg.attackDamage = 10;
        cfg.attackInterval = 1.5f;
        cfg.stripColliders = false;
        cfg.corpseLifetime = 1.5f;
        AssetDatabase.CreateAsset(cfg, DefaultConfigPath);
        Debug.Log("[MonsterSetup] 新建怪物配置 " + DefaultConfigPath);
        return cfg;
    }

    static MonsterTable EnsureMonsterTable(MonsterConfig config)
    {
        MonsterTable table = AssetDatabase.LoadAssetAtPath<MonsterTable>(MonsterTableAssetPath);
        if (table == null)
        {
            EnsureFolder(TablesDir);
            table = ScriptableObject.CreateInstance<MonsterTable>();
            AssetDatabase.CreateAsset(table, MonsterTableAssetPath);
            Debug.Log("[MonsterSetup] 新建怪物表 " + MonsterTableAssetPath);
        }

        if (table.Find(DefaultMonsterId) == null)
        {
            List<MonsterTable.Entry> list = new List<MonsterTable.Entry>(table.monsters ?? new MonsterTable.Entry[0]);
            list.Add(new MonsterTable.Entry
            {
                id = DefaultMonsterId,
                config = config,
                unlocked = true,
                order = list.Count
            });
            table.monsters = list.ToArray();
            EditorUtility.SetDirty(table);
            Debug.Log("[MonsterSetup] 怪物表新增条目 " + DefaultMonsterId);
        }
        else
        {
            MonsterTable.Entry e = table.Find(DefaultMonsterId);
            if (e.config == null && config != null)
            {
                e.config = config;
                EditorUtility.SetDirty(table);
            }
        }
        return table;
    }

    static SceneConfigTable EnsureSceneConfigTable(MonsterConfig config)
    {
        SceneConfigTable table = AssetDatabase.LoadAssetAtPath<SceneConfigTable>(SceneConfigTableAssetPath);
        if (table == null)
        {
            EnsureFolder(TablesDir);
            table = ScriptableObject.CreateInstance<SceneConfigTable>();
            AssetDatabase.CreateAsset(table, SceneConfigTableAssetPath);
            Debug.Log("[MonsterSetup] 新建场景配置表 " + SceneConfigTableAssetPath);
        }

        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        List<MonsterSceneConfigEntry> list = new List<MonsterSceneConfigEntry>(table.entries ?? new MonsterSceneConfigEntry[0]);

        // 已有一条「本场景 + 本怪物」的行就不动（保留用户配置）
        for (int i = 0; i < list.Count; i++)
        {
            MonsterSceneConfigEntry x = list[i];
            if (x == null) continue;
            if (x.MatchesScene(sceneName) && x.monsterId == DefaultMonsterId)
            {
                if (x.monster == null && config != null)
                {
                    x.monster = config;
                    EditorUtility.SetDirty(table);
                }
                return table;
            }
        }

        string id = (string.IsNullOrEmpty(sceneName) ? "default" : sceneName) + "_" + DefaultMonsterId;
        MonsterSceneConfigEntry e = new MonsterSceneConfigEntry
        {
            id = id,
            scene = sceneName,
            monsterId = DefaultMonsterId,
            monster = config,
            count = 8,
            interval = 3f,
            maxAlive = 4,
            startDelay = 2f,
            waves = 1,
            waveInterval = 15f,
            waitForClear = true,
            spawnPoint = MonsterSpawnPointMode.LairRoundRobin,
            enabled = true,
            order = list.Count
        };
        list.Add(e);
        table.entries = list.ToArray();
        EditorUtility.SetDirty(table);
        Debug.Log("[MonsterSetup] 场景配置表新增行 " + id + "（场景 " + sceneName + "，怪物 " + DefaultMonsterId +
                  "，数量 8 / 频率 3s / 上限 4）");
        return table;
    }

    static void EnsureCsvBaselines()
    {
        if (!File.Exists(MonsterTableCsvPath))
        {
            TableImporter.ExportMonsters();
        }
        if (!File.Exists(SceneConfigCsvPath))
        {
            TableImporter.ExportSceneConfigs();
        }
    }

    // ================= 场景 =================

    static bool WireScene(MonsterTable monsterTable, SceneConfigTable sceneTable)
    {
        HexGrid grid = Object.FindObjectOfType<HexGrid>(true);
        if (grid == null)
        {
            Debug.LogError("[MonsterSetup] 场景里找不到 HexGrid，无法接线。");
            return false;
        }

        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "创建 " + RootName);
        }

        MonsterSpawner spawner = root.GetComponent<MonsterSpawner>();
        if (spawner == null) spawner = Undo.AddComponent<MonsterSpawner>(root);

        Transform container = root.transform.Find(ContainerName);
        if (container == null)
        {
            GameObject go = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(go, "创建 " + ContainerName);
            go.transform.SetParent(root.transform, false);
            container = go.transform;
        }

        if (spawner.hexGrid == null) spawner.hexGrid = grid;
        if (spawner.sceneConfigTable == null) spawner.sceneConfigTable = sceneTable;
        if (spawner.monsterTable == null) spawner.monsterTable = monsterTable;
        if (spawner.spawnContainer == null) spawner.spawnContainer = container;
        EditorUtility.SetDirty(spawner);

        bool playerOk = WirePlayer();
        EditorUtility.SetDirty(root);
        Debug.Log("[MonsterSetup] 已装配 " + RootName + "（MonsterSpawner，玩家接线=" + playerOk + "）。");
        return true;
    }

    static bool WirePlayer()
    {
        GameObject player = FindPlayerObject();
        if (player == null)
        {
            Debug.LogWarning("[MonsterSetup] 场景里找不到玩家（无 CharacterController / 无 PlayerTarget），" +
                             "已跳过玩家接线。请手动在玩家身上挂 PlayerTarget + PlayerMeleeAttackBridge。");
            return false;
        }

        PlayerTarget target = player.GetComponent<PlayerTarget>();
        if (target == null) target = Undo.AddComponent<PlayerTarget>(player);
        target.aimHeight = 1f;

        PlayerMeleeAttackBridge bridge = player.GetComponent<PlayerMeleeAttackBridge>();
        if (bridge == null) bridge = Undo.AddComponent<PlayerMeleeAttackBridge>(player);
        if (bridge.owner == null) bridge.owner = target;
        if (bridge.attack == null) bridge.attack = player.GetComponentInChildren<EllenAttack>(true);

        if (AddPlayerHealth && player.GetComponentInChildren<Health>(true) == null)
        {
            Health h = Undo.AddComponent<Health>(player);
            h.SetMax(PlayerMaxHealth, true);
            Debug.Log("[MonsterSetup] 已给玩家补挂 Health（" + PlayerMaxHealth +
                      "，供「怪物攻击玩家」开关使用；不需要可自行移除）。");
        }

        EditorUtility.SetDirty(target);
        EditorUtility.SetDirty(bridge);
        Debug.Log("[MonsterSetup] 玩家接线完成：" + FullPath(player) +
                  "（PlayerTarget + PlayerMeleeAttackBridge" +
                  (bridge.attack != null ? " + EllenAttack 已找到）" : "，⚠ 未找到 EllenAttack）"));
        return true;
    }

    /// <summary>找玩家：已有 PlayerTarget &gt; 有 CharacterController 的对象 &gt; 常见命名兜底。</summary>
    static GameObject FindPlayerObject()
    {
        PlayerTarget existing = Object.FindObjectOfType<PlayerTarget>(true);
        if (existing != null) return existing.gameObject;

        CharacterController cc = Object.FindObjectOfType<CharacterController>(true);
        if (cc != null) return cc.gameObject;

        string[] names = { "Role", "Ellen", "Hero" };
        for (int i = 0; i < names.Length; i++)
        {
            GameObject go = GameObject.Find(names[i]);
            if (go != null) return go;
        }
        return null;
    }

    // ================= 小工具 =================

    static Material EnsureMaterial(string name, Color color)
    {
        EnsureFolder(MaterialsDir);
        string path = MaterialsDir + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogError("[MonsterSetup] 找不到可用 shader（Standard / Legacy Diffuse / Unlit）");
            return null;
        }
        mat = new Material(shader);
        mat.name = name;
        mat.color = color;
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.15f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

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

    static string FullPath(GameObject go)
    {
        string path = go.name;
        Transform t = go.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}
