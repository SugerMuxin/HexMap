using System.Collections.Generic;
using System.IO;
using System.Text;
using HexUI;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 数据表管线（CSV ←→ 资产）。**表驱动 UI / 种植配置的唯一入口**。
///
/// 循环：
///   1. 导出：把当前资产写成 CSV（首次生成基线 / 备份现状）——`Tools/UI/数据表/全部导出 CSV（从资产）`
///   2. 编辑 CSV（Excel / VSCode，UTF-8）
///   3. 导入：CSV 写回资产 ——`Tools/UI/数据表/全部导入 CSV（到资产）`
///
/// 三张表：
///   SeedTable.csv   → Assets/Resources/Configs/Plants/PlantConfig_{id}.asset（**按 id 稳定 upsert，
///                     已存在资产保留 GUID / 文件名 / 用户额外改动，只覆盖表管字段**）+ SeedTable.asset
///   UIPanelTable.csv→ Assets/Resources/Configs/Tables/UIPanelTable.asset（按 id upsert，不删除 CSV 外的条目）
///   UITextTable.csv → Assets/Resources/Configs/Tables/UITextTable.asset（全量替换）
///
/// 说明：CSV 写在 Resources 下（体积极小，且便于将来运行时读文案 / 表格）；Excel 打开需 UTF-8 BOM，
/// 导出时已带 BOM。
/// </summary>
public static class TableImporter
{
    const string TablesDir = "Assets/Resources/Configs/Tables";
    const string PlantsDir = "Assets/Resources/Configs/Plants";

    const string SeedsCsv = TablesDir + "/SeedTable.csv";
    const string PanelsCsv = TablesDir + "/UIPanelTable.csv";
    const string TextsCsv = TablesDir + "/UITextTable.csv";

    const string SeedsAsset = TablesDir + "/SeedTable.asset";
    const string PanelsAsset = TablesDir + "/UIPanelTable.asset";
    const string TextsAsset = TablesDir + "/UITextTable.asset";

    // 怪物系统（AI 怪物）：怪物属性表 + 场景配置表
    const string MonstersDir = "Assets/Resources/Configs/Monsters";
    const string MonstersCsv = TablesDir + "/MonsterTable.csv";
    const string SceneConfigsCsv = TablesDir + "/SceneConfigTable.csv";
    const string MonstersAsset = TablesDir + "/MonsterTable.asset";
    const string SceneConfigsAsset = TablesDir + "/SceneConfigTable.asset";

    const string MenuRoot = "Tools/UI/数据表/";

    // ================= 菜单 =================

    [MenuItem(MenuRoot + "全部导出 CSV（从资产）", false, 10)]
    public static void ExportAll()
    {
        ExportSeeds();
        ExportPanels();
        ExportTexts();
        ExportMonsters();
        ExportSceneConfigs();
        Debug.Log("[Table] 全部导出完成 → " + TablesDir);
    }

    [MenuItem(MenuRoot + "全部导入 CSV（到资产）", false, 11)]
    public static void ImportAll()
    {
        ImportSeeds();
        ImportPanels();
        ImportTexts();
        ImportMonsters();
        ImportSceneConfigs();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Table] 全部导入完成（资产已保存）。");
    }

    [MenuItem(MenuRoot + "导出 种子表 CSV", false, 20)]
    public static void ExportSeeds()
    {
        EnsureTablesDir();
        List<string[]> rows = new List<string[]>();
        rows.Add(new string[]
        {
            "id", "configPath", "name", "desc", "icon", "prefab",
            "sunCost", "cooldown", "maxHealth", "damage", "attackInterval", "attackRange",
            "projectileSpeed", "terrain", "unlock", "order"
        });

        SeedTable seedTable = AssetDatabase.LoadAssetAtPath<SeedTable>(SeedsAsset);
        if (seedTable != null && seedTable.seeds != null)
        {
            List<SeedTable.Entry> list = seedTable.OrderedSeeds();
            for (int i = 0; i < list.Count; i++)
            {
                SeedTable.Entry e = list[i];
                rows.Add(SeedRow(e.id, AssetDatabase.GetAssetPath(e.config), e.config,
                                 e.terrain, e.unlocked, e.order));
            }
        }
        else
        {
            // 没有种子表：直接把 Plants 目录下的 PlantConfig 全数列出来做基线
            string[] guids = AssetDatabase.FindAssets("t:PlantConfig", new[] { PlantsDir });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                PlantConfig cfg = AssetDatabase.LoadAssetAtPath<PlantConfig>(path);
                if (cfg == null) continue;
                string id = Path.GetFileNameWithoutExtension(path);
                if (id.StartsWith("PlantConfig_")) id = id.Substring("PlantConfig_".Length);
                rows.Add(SeedRow(id, path, cfg, "草|泥", true, i));
            }
        }

        WriteCsv(SeedsCsv, rows);
        Debug.Log("[Table] 种子表已导出 → " + SeedsCsv + "（" + (rows.Count - 1) + " 行）");
    }

    [MenuItem(MenuRoot + "导出 面板表 CSV", false, 30)]
    public static void ExportPanels()
    {
        EnsureTablesDir();
        List<string[]> rows = new List<string[]>();
        rows.Add(new string[]
        {
            "id", "prefabPath", "layer", "modal", "exclusiveGroup",
            "escClose", "hotkey", "blockGameInput", "preload", "order"
        });

        UIPanelTable table = AssetDatabase.LoadAssetAtPath<UIPanelTable>(PanelsAsset);
        if (table != null && table.entries != null)
        {
            for (int i = 0; i < table.entries.Length; i++)
            {
                UIPanelTable.Entry e = table.entries[i];
                if (e == null) continue;
                string prefabPath = e.prefab != null
                    ? e.prefab.name
                    : "";
                rows.Add(new string[]
                {
                    e.id ?? "", e.prefabPath ?? prefabPath, e.layer.ToString(),
                    e.modal ? "1" : "0", e.exclusiveGroup ?? "",
                    e.escClose ? "1" : "0", e.hotkey.ToString(),
                    e.blockGameInput ? "1" : "0", e.preload ? "1" : "0", e.order.ToString()
                });
            }
        }

        WriteCsv(PanelsCsv, rows);
        Debug.Log("[Table] 面板表已导出 → " + PanelsCsv + "（" + (rows.Count - 1) + " 行）");
    }

    [MenuItem(MenuRoot + "导出 文案表 CSV", false, 40)]
    public static void ExportTexts()
    {
        EnsureTablesDir();
        List<string[]> rows = new List<string[]>();
        rows.Add(new string[] { "key", "zh", "en" });

        UITextTable table = AssetDatabase.LoadAssetAtPath<UITextTable>(TextsAsset);
        if (table != null && table.entries != null)
        {
            for (int i = 0; i < table.entries.Length; i++)
            {
                UITextTable.Entry e = table.entries[i];
                if (e == null) continue;
                rows.Add(new string[] { e.key ?? "", e.zh ?? "", e.en ?? "" });
            }
        }

        WriteCsv(TextsCsv, rows);
        Debug.Log("[Table] 文案表已导出 → " + TextsCsv + "（" + (rows.Count - 1) + " 行）");
    }

    [MenuItem(MenuRoot + "导入 种子表 CSV", false, 21)]
    public static void ImportSeeds()
    {
        List<string[]> rows = ReadCsv(SeedsCsv);
        if (rows == null) return;

        string[] header = rows[0];
        int cId = TableCsv.Index(header, "id");
        int cConfig = TableCsv.Index(header, "configPath");
        int cName = TableCsv.Index(header, "name");
        int cDesc = TableCsv.Index(header, "desc");
        int cIcon = TableCsv.Index(header, "icon");
        int cPrefab = TableCsv.Index(header, "prefab");
        int cSun = TableCsv.Index(header, "sunCost");
        int cCd = TableCsv.Index(header, "cooldown");
        int cHp = TableCsv.Index(header, "maxHealth");
        int cDmg = TableCsv.Index(header, "damage");
        int cInt = TableCsv.Index(header, "attackInterval");
        int cRange = TableCsv.Index(header, "attackRange");
        int cSpeed = TableCsv.Index(header, "projectileSpeed");
        int cTerrain = TableCsv.Index(header, "terrain");
        int cUnlock = TableCsv.Index(header, "unlock");
        int cOrder = TableCsv.Index(header, "order");

        if (cId < 0)
        {
            Debug.LogError("[Table] " + SeedsCsv + " 缺少 id 列，导入中止。");
            return;
        }

        SeedTable seedTable = EnsureSeedTableAsset();
        List<SeedTable.Entry> entries = new List<SeedTable.Entry>();
        int created = 0, updated = 0;

        for (int i = 1; i < rows.Count; i++)
        {
            string[] row = rows[i];
            string id = TableCsv.Get(row, cId);
            if (string.IsNullOrEmpty(id)) continue;

            string path = TableCsv.Get(row, cConfig);
            if (string.IsNullOrEmpty(path)) path = PlantsDir + "/PlantConfig_" + id + ".asset";

            PlantConfig cfg = AssetDatabase.LoadAssetAtPath<PlantConfig>(path);
            if (cfg == null)
            {
                EnsureFolderFor(path);
                cfg = ScriptableObject.CreateInstance<PlantConfig>();
                AssetDatabase.CreateAsset(cfg, path);
                created++;
            }
            else updated++;

            // 只覆盖"表管字段"：name / desc / icon / prefab / 数值
            cfg.displayName = TableCsv.Get(row, cName);
            cfg.description = TableCsv.Get(row, cDesc);
            string iconPath = TableCsv.Get(row, cIcon);
            if (!string.IsNullOrEmpty(iconPath)) cfg.icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            string prefabPath = TableCsv.Get(row, cPrefab);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) Debug.LogWarning("[Table] 种子 " + id + " 的 prefab 路径无效：" + prefabPath);
                else cfg.prefab = prefab;
            }
            cfg.sunCost = TableCsv.GetInt(row, cSun, cfg.sunCost);
            cfg.cooldown = TableCsv.GetFloat(row, cCd, cfg.cooldown);
            cfg.maxHealth = TableCsv.GetInt(row, cHp, cfg.maxHealth);
            cfg.attackDamage = TableCsv.GetInt(row, cDmg, cfg.attackDamage);
            cfg.attackInterval = TableCsv.GetFloat(row, cInt, cfg.attackInterval);
            cfg.attackRange = TableCsv.GetFloat(row, cRange, cfg.attackRange);
            cfg.projectileSpeed = TableCsv.GetFloat(row, cSpeed, cfg.projectileSpeed);
            EditorUtility.SetDirty(cfg);

            SeedTable.Entry e = seedTable.Find(id);
            if (e == null) e = new SeedTable.Entry { id = id };
            e.id = id;
            e.config = cfg;
            e.terrain = TableCsv.Get(row, cTerrain);
            if (string.IsNullOrEmpty(e.terrain)) e.terrain = "草|泥";
            e.unlocked = TableCsv.GetBool(row, cUnlock, true);
            e.order = TableCsv.GetInt(row, cOrder, entries.Count);
            entries.Add(e);
        }

        seedTable.seeds = entries.ToArray();
        EditorUtility.SetDirty(seedTable);
        AssetDatabase.SaveAssets();
        Debug.Log("[Table] 种子表导入完成：新建 config " + created + " 个，更新 " + updated +
                  " 个，种子条目 " + entries.Count + " 个（已存在资产的 GUID / 文件名保持不变）。");
    }

    [MenuItem(MenuRoot + "导入 面板表 CSV", false, 31)]
    public static void ImportPanels()
    {
        List<string[]> rows = ReadCsv(PanelsCsv);
        if (rows == null) return;

        string[] header = rows[0];
        int cId = TableCsv.Index(header, "id");
        int cPath = TableCsv.Index(header, "prefabPath");
        int cLayer = TableCsv.Index(header, "layer");
        int cModal = TableCsv.Index(header, "modal");
        int cGroup = TableCsv.Index(header, "exclusiveGroup");
        int cEsc = TableCsv.Index(header, "escClose");
        int cHot = TableCsv.Index(header, "hotkey");
        int cBlock = TableCsv.Index(header, "blockGameInput");
        int cPre = TableCsv.Index(header, "preload");
        int cOrder = TableCsv.Index(header, "order");

        if (cId < 0)
        {
            Debug.LogError("[Table] " + PanelsCsv + " 缺少 id 列，导入中止。");
            return;
        }

        UIPanelTable table = EnsurePanelTableAsset();
        int updated = 0;

        for (int i = 1; i < rows.Count; i++)
        {
            string[] row = rows[i];
            string id = TableCsv.Get(row, cId);
            if (string.IsNullOrEmpty(id)) continue;

            UIPanelTable.Entry e = table.GetOrCreate(id);
            e.id = id;

            string prefabPath = TableCsv.Get(row, cPath);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                e.prefabPath = prefabPath;
                GameObject prefab = Resources.Load<GameObject>(prefabPath);
                if (prefab != null) e.prefab = prefab;
                else Debug.LogWarning("[Table] 面板 " + id + " 的 Resources 路径加载失败：" + prefabPath);
            }

            e.layer = TableCsv.GetEnum(row, cLayer, UIPanelLayer.Normal);
            e.modal = TableCsv.GetBool(row, cModal, true);
            e.exclusiveGroup = TableCsv.Get(row, cGroup);
            e.escClose = TableCsv.GetBool(row, cEsc, true);
            e.hotkey = TableCsv.GetKeyCode(row, cHot, KeyCode.None);
            e.blockGameInput = TableCsv.GetBool(row, cBlock, true);
            e.preload = TableCsv.GetBool(row, cPre, false);
            e.order = TableCsv.GetInt(row, cOrder, e.order);
            updated++;
        }

        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        Debug.Log("[Table] 面板表导入完成：更新 " + updated + " 条（CSV 之外的既有条目保留不删）。");
    }

    [MenuItem(MenuRoot + "导入 文案表 CSV", false, 41)]
    public static void ImportTexts()
    {
        List<string[]> rows = ReadCsv(TextsCsv);
        if (rows == null) return;

        string[] header = rows[0];
        int cKey = TableCsv.Index(header, "key");
        int cZh = TableCsv.Index(header, "zh");
        int cEn = TableCsv.Index(header, "en");
        if (cKey < 0)
        {
            Debug.LogError("[Table] " + TextsCsv + " 缺少 key 列，导入中止。");
            return;
        }

        UITextTable table = EnsureTextTableAsset();
        List<UITextTable.Entry> entries = new List<UITextTable.Entry>();
        for (int i = 1; i < rows.Count; i++)
        {
            string key = TableCsv.Get(rows[i], cKey);
            if (string.IsNullOrEmpty(key)) continue;
            entries.Add(new UITextTable.Entry
            {
                key = key,
                zh = TableCsv.Get(rows[i], cZh),
                en = TableCsv.Get(rows[i], cEn)
            });
        }

        table.entries = entries.ToArray();
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        UIText.ClearCache();
        Debug.Log("[Table] 文案表导入完成：" + entries.Count + " 条（运行期缓存已清）。");
    }

    // ================= 怪物系统：怪物表 / 场景配置表 =================

    [MenuItem(MenuRoot + "导出 怪物表 CSV", false, 50)]
    public static void ExportMonsters()
    {
        EnsureTablesDir();
        List<string[]> rows = new List<string[]>();
        rows.Add(new string[]
        {
            "id", "configPath", "name", "desc", "prefab",
            "maxHealth", "moveSpeed", "turnSpeed", "yOffset",
            "lockMode", "detectRange", "retargetInterval", "switchOnlyWhenCloser",
            "repathInterval", "arriveDistance", "attackRange",
            "attackPlayers", "attackDamage", "attackInterval",
            "stripColliders", "corpseLifetime", "unlock", "order"
        });

        MonsterTable table = AssetDatabase.LoadAssetAtPath<MonsterTable>(MonstersAsset);
        if (table != null && table.monsters != null)
        {
            List<MonsterTable.Entry> list = table.OrderedEntries();
            for (int i = 0; i < list.Count; i++)
            {
                MonsterTable.Entry e = list[i];
                rows.Add(MonsterRow(e.id, AssetDatabase.GetAssetPath(e.config), e.config, e.unlocked, e.order));
            }
        }
        else
        {
            // 没有怪物表：直接把 Configs/Monsters 下的 MonsterConfig 全数列出来做基线
            string[] guids = AssetDatabase.FindAssets("t:MonsterConfig", new[] { MonstersDir });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                MonsterConfig cfg = AssetDatabase.LoadAssetAtPath<MonsterConfig>(path);
                if (cfg == null) continue;
                string id = Path.GetFileNameWithoutExtension(path);
                if (id.StartsWith(MonsterTable.MonsterConfigNamePrefix))
                {
                    id = id.Substring(MonsterTable.MonsterConfigNamePrefix.Length);
                }
                rows.Add(MonsterRow(id, path, cfg, true, i));
            }
        }

        WriteCsv(MonstersCsv, rows);
        Debug.Log("[Table] 怪物表已导出 → " + MonstersCsv + "（" + (rows.Count - 1) + " 行）");
    }

    [MenuItem(MenuRoot + "导入 怪物表 CSV", false, 51)]
    public static void ImportMonsters()
    {
        List<string[]> rows = ReadCsv(MonstersCsv);
        if (rows == null) return;

        string[] header = rows[0];
        int cId = TableCsv.Index(header, "id");
        int cConfig = TableCsv.Index(header, "configPath");
        int cName = TableCsv.Index(header, "name");
        int cDesc = TableCsv.Index(header, "desc");
        int cPrefab = TableCsv.Index(header, "prefab");
        int cHp = TableCsv.Index(header, "maxHealth");
        int cSpeed = TableCsv.Index(header, "moveSpeed");
        int cTurn = TableCsv.Index(header, "turnSpeed");
        int cY = TableCsv.Index(header, "yOffset");
        int cLock = TableCsv.Index(header, "lockMode");
        int cDetect = TableCsv.Index(header, "detectRange");
        int cRetarget = TableCsv.Index(header, "retargetInterval");
        int cCloser = TableCsv.Index(header, "switchOnlyWhenCloser");
        int cRepath = TableCsv.Index(header, "repathInterval");
        int cArrive = TableCsv.Index(header, "arriveDistance");
        int cRange = TableCsv.Index(header, "attackRange");
        int cAtkPlayers = TableCsv.Index(header, "attackPlayers");
        int cDmg = TableCsv.Index(header, "attackDamage");
        int cAtkInterval = TableCsv.Index(header, "attackInterval");
        int cStrip = TableCsv.Index(header, "stripColliders");
        int cCorpse = TableCsv.Index(header, "corpseLifetime");
        int cUnlock = TableCsv.Index(header, "unlock");
        int cOrder = TableCsv.Index(header, "order");

        if (cId < 0)
        {
            Debug.LogError("[Table] " + MonstersCsv + " 缺少 id 列，导入中止。");
            return;
        }

        MonsterTable table = EnsureMonsterTableAsset();
        List<MonsterTable.Entry> entries = new List<MonsterTable.Entry>();
        int created = 0, updated = 0;

        for (int i = 1; i < rows.Count; i++)
        {
            string[] row = rows[i];
            string id = TableCsv.Get(row, cId);
            if (string.IsNullOrEmpty(id)) continue;

            string path = TableCsv.Get(row, cConfig);
            if (string.IsNullOrEmpty(path))
            {
                path = MonstersDir + "/" + MonsterTable.MonsterConfigNamePrefix + id + ".asset";
            }

            MonsterConfig cfg = AssetDatabase.LoadAssetAtPath<MonsterConfig>(path);
            if (cfg == null)
            {
                EnsureFolderFor(path);
                cfg = ScriptableObject.CreateInstance<MonsterConfig>();
                AssetDatabase.CreateAsset(cfg, path);
                created++;
            }
            else updated++;

            // 只覆盖「表管字段」：name / desc / prefab / 数值
            cfg.displayName = TableCsv.Get(row, cName);
            cfg.description = TableCsv.Get(row, cDesc);
            string prefabPath = TableCsv.Get(row, cPrefab);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning("[Table] 怪物 " + id + " 的 prefab 路径无效：" + prefabPath);
                }
                else cfg.prefab = prefab;
            }
            cfg.maxHealth = TableCsv.GetInt(row, cHp, cfg.maxHealth);
            cfg.moveSpeed = TableCsv.GetFloat(row, cSpeed, cfg.moveSpeed);
            cfg.turnSpeed = TableCsv.GetFloat(row, cTurn, cfg.turnSpeed);
            cfg.yOffset = TableCsv.GetFloat(row, cY, cfg.yOffset);
            cfg.lockMode = TableCsv.GetEnum(row, cLock, cfg.lockMode);
            cfg.detectRange = TableCsv.GetFloat(row, cDetect, cfg.detectRange);
            cfg.retargetInterval = TableCsv.GetFloat(row, cRetarget, cfg.retargetInterval);
            cfg.switchOnlyWhenCloser = TableCsv.GetBool(row, cCloser, cfg.switchOnlyWhenCloser);
            cfg.repathInterval = TableCsv.GetFloat(row, cRepath, cfg.repathInterval);
            cfg.arriveDistance = TableCsv.GetFloat(row, cArrive, cfg.arriveDistance);
            cfg.attackRange = TableCsv.GetFloat(row, cRange, cfg.attackRange);
            cfg.attackPlayers = TableCsv.GetBool(row, cAtkPlayers, cfg.attackPlayers);
            cfg.attackDamage = TableCsv.GetInt(row, cDmg, cfg.attackDamage);
            cfg.attackInterval = TableCsv.GetFloat(row, cAtkInterval, cfg.attackInterval);
            cfg.stripColliders = TableCsv.GetBool(row, cStrip, cfg.stripColliders);
            cfg.corpseLifetime = TableCsv.GetFloat(row, cCorpse, cfg.corpseLifetime);
            EditorUtility.SetDirty(cfg);

            MonsterTable.Entry e = table.Find(id);
            if (e == null) e = new MonsterTable.Entry { id = id };
            e.id = id;
            e.config = cfg;
            e.unlocked = TableCsv.GetBool(row, cUnlock, true);
            e.order = TableCsv.GetInt(row, cOrder, entries.Count);
            entries.Add(e);
        }

        table.monsters = entries.ToArray();
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        Debug.Log("[Table] 怪物表导入完成：新建 config " + created + " 个，更新 " + updated +
                  " 个，条目 " + entries.Count + " 个（已存在资产的 GUID / 文件名保持不变）。");
    }

    [MenuItem(MenuRoot + "导出 场景配置表 CSV", false, 52)]
    public static void ExportSceneConfigs()
    {
        EnsureTablesDir();
        List<string[]> rows = new List<string[]>();
        rows.Add(new string[]
        {
            "id", "scene", "monster", "count", "interval", "maxAlive", "startDelay",
            "waves", "waveInterval", "waitForClear", "spawnPoint",
            "offsetX", "offsetZ", "enabled", "order"
        });

        SceneConfigTable table = AssetDatabase.LoadAssetAtPath<SceneConfigTable>(SceneConfigsAsset);
        if (table != null && table.entries != null)
        {
            List<MonsterSceneConfigEntry> list = new List<MonsterSceneConfigEntry>();
            for (int i = 0; i < table.entries.Length; i++)
            {
                if (table.entries[i] != null) list.Add(table.entries[i]);
            }
            list.Sort((a, b) => a.order.CompareTo(b.order));
            for (int i = 0; i < list.Count; i++)
            {
                rows.Add(SceneConfigRow(list[i]));
            }
        }

        WriteCsv(SceneConfigsCsv, rows);
        Debug.Log("[Table] 场景配置表已导出 → " + SceneConfigsCsv + "（" + (rows.Count - 1) + " 行）");
    }

    [MenuItem(MenuRoot + "导入 场景配置表 CSV", false, 53)]
    public static void ImportSceneConfigs()
    {
        List<string[]> rows = ReadCsv(SceneConfigsCsv);
        if (rows == null) return;

        string[] header = rows[0];
        int cId = TableCsv.Index(header, "id");
        int cScene = TableCsv.Index(header, "scene");
        int cMonster = TableCsv.Index(header, "monster");
        int cCount = TableCsv.Index(header, "count");
        int cInterval = TableCsv.Index(header, "interval");
        int cMaxAlive = TableCsv.Index(header, "maxAlive");
        int cStartDelay = TableCsv.Index(header, "startDelay");
        int cWaves = TableCsv.Index(header, "waves");
        int cWaveInterval = TableCsv.Index(header, "waveInterval");
        int cWaitClear = TableCsv.Index(header, "waitForClear");
        int cSpawnPoint = TableCsv.Index(header, "spawnPoint");
        int cOffX = TableCsv.Index(header, "offsetX");
        int cOffZ = TableCsv.Index(header, "offsetZ");
        int cEnabled = TableCsv.Index(header, "enabled");
        int cOrder = TableCsv.Index(header, "order");

        if (cId < 0)
        {
            Debug.LogError("[Table] " + SceneConfigsCsv + " 缺少 id 列，导入中止。");
            return;
        }

        SceneConfigTable table = EnsureSceneConfigTableAsset();
        MonsterTable monsterTable = EnsureMonsterTableAsset();
        List<MonsterSceneConfigEntry> entries = new List<MonsterSceneConfigEntry>();

        for (int i = 1; i < rows.Count; i++)
        {
            string[] row = rows[i];
            string id = TableCsv.Get(row, cId);
            if (string.IsNullOrEmpty(id)) continue;

            MonsterSceneConfigEntry e = new MonsterSceneConfigEntry { id = id };
            e.scene = TableCsv.Get(row, cScene);
            e.monsterId = TableCsv.Get(row, cMonster);
            if (!string.IsNullOrEmpty(e.monsterId))
            {
                MonsterConfig cfg = monsterTable != null ? monsterTable.FindConfig(e.monsterId) : null;
                if (cfg == null) cfg = MonsterTable.LoadConfigById(e.monsterId);
                if (cfg == null)
                {
                    Debug.LogWarning("[Table] 场景配置行 " + id + " 引用了未知怪物 id：" + e.monsterId +
                                     "（先建立怪物表条目，或检查 MonsterConfig_" + e.monsterId + ".asset 是否存在）");
                }
                e.monster = cfg;
            }
            e.count = TableCsv.GetInt(row, cCount, e.count);
            e.interval = TableCsv.GetFloat(row, cInterval, e.interval);
            e.maxAlive = TableCsv.GetInt(row, cMaxAlive, e.maxAlive);
            e.startDelay = TableCsv.GetFloat(row, cStartDelay, e.startDelay);
            e.waves = TableCsv.GetInt(row, cWaves, e.waves);
            e.waveInterval = TableCsv.GetFloat(row, cWaveInterval, e.waveInterval);
            e.waitForClear = TableCsv.GetBool(row, cWaitClear, e.waitForClear);
            e.spawnPoint = TableCsv.GetEnum(row, cSpawnPoint, e.spawnPoint);
            e.offsetCoordinates = new Vector2Int(
                TableCsv.GetInt(row, cOffX, 0), TableCsv.GetInt(row, cOffZ, 0));
            e.enabled = TableCsv.GetBool(row, cEnabled, true);
            e.order = TableCsv.GetInt(row, cOrder, entries.Count);
            entries.Add(e);
        }

        table.entries = entries.ToArray();
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        Debug.Log("[Table] 场景配置表导入完成：" + entries.Count + " 行（CSV 为准，全量替换）。");
    }

    // ================= 内部 =================

    static string[] MonsterRow(string id, string configPath, MonsterConfig cfg, bool unlocked, int order)
    {
        return new string[]
        {
            id ?? "", configPath ?? "",
            cfg != null ? cfg.displayName : "",
            cfg != null ? cfg.description : "",
            cfg != null && cfg.prefab != null ? AssetDatabase.GetAssetPath(cfg.prefab) : "",
            cfg != null ? cfg.maxHealth.ToString() : "0",
            Num(cfg != null ? cfg.moveSpeed : 0f),
            Num(cfg != null ? cfg.turnSpeed : 0f),
            Num(cfg != null ? cfg.yOffset : 0f),
            cfg != null ? cfg.lockMode.ToString() : MonsterLockMode.FirstUntilAttacked.ToString(),
            Num(cfg != null ? cfg.detectRange : 0f),
            Num(cfg != null ? cfg.retargetInterval : 0f),
            Bool(cfg == null || cfg.switchOnlyWhenCloser),
            Num(cfg != null ? cfg.repathInterval : 0f),
            Num(cfg != null ? cfg.arriveDistance : 0f),
            Num(cfg != null ? cfg.attackRange : 0f),
            Bool(cfg != null && cfg.attackPlayers),
            cfg != null ? cfg.attackDamage.ToString() : "0",
            Num(cfg != null ? cfg.attackInterval : 0f),
            Bool(cfg != null && cfg.stripColliders),
            Num(cfg != null ? cfg.corpseLifetime : 0f),
            unlocked ? "1" : "0",
            order.ToString()
        };
    }

    static string[] SceneConfigRow(MonsterSceneConfigEntry e)
    {
        string monsterId = e.monsterId;
        if (string.IsNullOrEmpty(monsterId) && e.monster != null && e.monster.name.StartsWith(MonsterTable.MonsterConfigNamePrefix))
        {
            monsterId = e.monster.name.Substring(MonsterTable.MonsterConfigNamePrefix.Length);
        }
        return new string[]
        {
            e.id ?? "", e.scene ?? "", monsterId ?? "",
            e.count.ToString(),
            Num(e.interval),
            e.maxAlive.ToString(),
            Num(e.startDelay),
            e.waves.ToString(),
            Num(e.waveInterval),
            Bool(e.waitForClear),
            e.spawnPoint.ToString(),
            e.offsetCoordinates.x.ToString(),
            e.offsetCoordinates.y.ToString(),
            Bool(e.enabled),
            e.order.ToString()
        };
    }

    static string Num(float v)
    {
        return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    static string Bool(bool v)
    {
        return v ? "1" : "0";
    }

    static string[] SeedRow(string id, string configPath, PlantConfig cfg, string terrain, bool unlocked, int order)
    {
        return new string[]
        {
            id ?? "", configPath ?? "",
            cfg != null ? cfg.displayName : "",
            cfg != null ? cfg.description : "",
            cfg != null && cfg.icon != null ? AssetDatabase.GetAssetPath(cfg.icon) : "",
            cfg != null && cfg.prefab != null ? AssetDatabase.GetAssetPath(cfg.prefab) : "",
            cfg != null ? cfg.sunCost.ToString() : "0",
            cfg != null ? cfg.cooldown.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "0",
            cfg != null ? cfg.maxHealth.ToString() : "0",
            cfg != null ? cfg.attackDamage.ToString() : "0",
            cfg != null ? cfg.attackInterval.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "0",
            cfg != null ? cfg.attackRange.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "0",
            cfg != null ? cfg.projectileSpeed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "0",
            string.IsNullOrEmpty(terrain) ? "草|泥" : terrain,
            unlocked ? "1" : "0",
            order.ToString()
        };
    }

    static SeedTable EnsureSeedTableAsset()
    {
        SeedTable t = AssetDatabase.LoadAssetAtPath<SeedTable>(SeedsAsset);
        if (t != null) return t;
        EnsureTablesDir();
        t = ScriptableObject.CreateInstance<SeedTable>();
        AssetDatabase.CreateAsset(t, SeedsAsset);
        return t;
    }

    static UIPanelTable EnsurePanelTableAsset()
    {
        UIPanelTable t = AssetDatabase.LoadAssetAtPath<UIPanelTable>(PanelsAsset);
        if (t != null) return t;
        EnsureTablesDir();
        t = ScriptableObject.CreateInstance<UIPanelTable>();
        AssetDatabase.CreateAsset(t, PanelsAsset);
        return t;
    }

    static UITextTable EnsureTextTableAsset()
    {
        UITextTable t = AssetDatabase.LoadAssetAtPath<UITextTable>(TextsAsset);
        if (t != null) return t;
        EnsureTablesDir();
        t = ScriptableObject.CreateInstance<UITextTable>();
        AssetDatabase.CreateAsset(t, TextsAsset);
        return t;
    }

    static MonsterTable EnsureMonsterTableAsset()
    {
        MonsterTable t = AssetDatabase.LoadAssetAtPath<MonsterTable>(MonstersAsset);
        if (t != null) return t;
        EnsureTablesDir();
        t = ScriptableObject.CreateInstance<MonsterTable>();
        AssetDatabase.CreateAsset(t, MonstersAsset);
        return t;
    }

    static SceneConfigTable EnsureSceneConfigTableAsset()
    {
        SceneConfigTable t = AssetDatabase.LoadAssetAtPath<SceneConfigTable>(SceneConfigsAsset);
        if (t != null) return t;
        EnsureTablesDir();
        t = ScriptableObject.CreateInstance<SceneConfigTable>();
        AssetDatabase.CreateAsset(t, SceneConfigsAsset);
        return t;
    }

    static List<string[]> ReadCsv(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            Debug.LogError("[Table] 找不到 CSV：" + csvPath + "（先执行\"导出\"生成基线）");
            return null;
        }
        string text = File.ReadAllText(csvPath, Encoding.UTF8);
        List<string[]> rows = TableCsv.Parse(text);
        if (rows.Count < 2)
        {
            Debug.LogWarning("[Table] " + csvPath + " 只有表头，没有数据行。");
            return rows;
        }
        return rows;
    }

    static void WriteCsv(string csvPath, List<string[]> rows)
    {
        EnsureFolderFor(csvPath);
        // UTF-8 带 BOM：Excel 打开中文不乱码
        File.WriteAllText(csvPath, TableCsv.Serialize(rows), new UTF8Encoding(true));
        AssetDatabase.Refresh();
    }

    static void EnsureTablesDir()
    {
        EnsureFolderFor(TablesDir + "/placeholder.txt");
    }

    static void EnsureFolderFor(string assetFilePath)
    {
        string abs = Path.GetDirectoryName(Path.GetFullPath(assetFilePath));
        if (!Directory.Exists(abs)) Directory.CreateDirectory(abs);
    }
}
