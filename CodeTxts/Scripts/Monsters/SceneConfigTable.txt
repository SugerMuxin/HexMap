using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>刷怪点选择方式。</summary>
public enum MonsterSpawnPointMode
{
    /// <summary>怪物巢穴格按顺序轮流（MonsterLairLevel &gt; 0；无巢穴自动回退到地图西边缘）。</summary>
    LairRoundRobin = 0,

    /// <summary>怪物巢穴格里随机挑一个。</summary>
    LairRandom = 1,

    /// <summary>地图西边缘（无巢穴、或想让怪物从地图外进场时用）。</summary>
    WestEdge = 2,

    /// <summary>指定 offset 坐标格（下面 offsetCoordinates）。</summary>
    OffsetCoordinates = 3,

    /// <summary>地图东边缘。</summary>
    EastEdge = 4
}

/// <summary>
/// 场景刷怪配置（一行 = 一条刷怪规则）。序列化内嵌在 <see cref="SceneConfigTable"/> 里，
/// 因此不需要单独的资产文件 —— 「一张场景配置表管所有关卡」。
///
/// 字段语义：
///   scene        → 哪个场景生效（场景名，见 UnityEngine.SceneManagement.Scene.name；空 = 任意场景都生效）
///   monsterId    → 用哪种怪物（引用怪物表 id；Inspector 里同时显示解析到的 monster 资产）
///   count        → 总诞生数量
///   interval     → 诞生频率：每隔多少秒生成一只
///   maxAlive     → 同时存活上限（0 = 不限）
///   waves        → 分几波（&lt;=1 = 不分波，连续刷完 count 只）
///   waveInterval → 波与波之间的间隔（秒）
///   waitForClear → true = 等本波全部死光再倒计时下一波；false = 固定 waveInterval 到点就下一波
///   startDelay   → 首只延迟（秒）
/// </summary>
[Serializable]
public class MonsterSceneConfigEntry
{
    [Tooltip("行 id（唯一；CSV 按它稳定 upsert，重命名会新建一行）")]
    public string id;

    [Tooltip("生效场景名（空 = 任意场景）。见 SceneManager.GetActiveScene().name")]
    public string scene = "";

    [Tooltip("怪物 id（引用怪物表；CSV 的 monster 列）")]
    public string monsterId;

    [Tooltip("怪物配置（由 monsterId 解析；Inspector 可直接拖）")]
    public MonsterConfig monster;

    [Header("数量与频率")]
    [Tooltip("总诞生数量")]
    public int count = 5;
    [Tooltip("诞生频率：每隔多少秒生成一只")]
    public float interval = 3f;
    [Tooltip("同时存活上限（0 = 不限）")]
    public int maxAlive = 0;
    [Tooltip("首只延迟（秒）")]
    public float startDelay = 1f;

    [Header("波次（waves <= 1 表示不分波，连续刷）")]
    [Tooltip("分几波")]
    public int waves = 1;
    [Tooltip("波与波之间的间隔（秒）")]
    public float waveInterval = 15f;
    [Tooltip("true = 等本波全部死光再倒计时下一波；false = 固定 waveInterval 到点就下一波")]
    public bool waitForClear = true;

    [Header("刷怪点")]
    [Tooltip("刷怪点选择方式")]
    public MonsterSpawnPointMode spawnPoint = MonsterSpawnPointMode.LairRoundRobin;
    [Tooltip("OffsetCoordinates 模式下使用的 offset 坐标")]
    public Vector2Int offsetCoordinates = Vector2Int.zero;

    [Header("杂项")]
    [Tooltip("是否启用本行")]
    public bool enabled = true;
    [Tooltip("顺序（小在前；UI 显示顺序）")]
    public int order;

    /// <summary>本行是否是可用的配置（启用 + 有怪物 + prefab 已接线）。</summary>
    public bool IsUsable
    {
        get { return enabled && monster != null && monster.IsValid && count > 0; }
    }

    /// <summary>本行是否对指定场景生效（scene 空 = 任意场景）。</summary>
    public bool MatchesScene(string sceneName)
    {
        if (string.IsNullOrEmpty(scene)) return true;
        if (string.IsNullOrEmpty(sceneName)) return false;
        return string.Equals(scene.Trim(), sceneName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>每波刷新数量（不分波时 = count）。</summary>
    public int PerWaveCount
    {
        get
        {
            if (waves <= 1) return Mathf.Max(0, count);
            return Mathf.CeilToInt(count / (float)waves);
        }
    }
}

/// <summary>
/// 场景配置表（ScriptableObject，游戏侧）：**「场景配置表」就是它** —— 一张表管所有关卡的出怪规则。
///
/// 数据来源：`Assets/Resources/Configs/Tables/SceneConfigTable.csv`
///          经 Editor/TableImporter → `Tools/UI/数据表/导入 场景配置表 CSV`。
///
/// 运行期：<see cref="MonsterSpawner"/> 按「当前场景名」取行（scene 为空的行对所有场景生效），
/// 逐行按 count / interval / maxAlive / waves 刷怪。
/// </summary>
[CreateAssetMenu(fileName = "SceneConfigTable", menuName = "HexMap/怪物/场景配置表", order = 22)]
public class SceneConfigTable : ScriptableObject
{
    public const string ResourcePath = "Configs/Tables/SceneConfigTable";

    [Tooltip("本表路径（Resources 下，不带扩展名）")]
    public string resourcePath = ResourcePath;

    public MonsterSceneConfigEntry[] entries = new MonsterSceneConfigEntry[0];

    /// <summary>按 id 查行（找不到返回 null）。</summary>
    public MonsterSceneConfigEntry Find(string id)
    {
        if (string.IsNullOrEmpty(id) || entries == null) return null;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].id == id) return entries[i];
        }
        return null;
    }

    /// <summary>按 id 取行，没有就新建一个（CSV 稳定 upsert 用）。</summary>
    public MonsterSceneConfigEntry GetOrCreate(string id)
    {
        MonsterSceneConfigEntry e = Find(id);
        if (e != null) return e;
        List<MonsterSceneConfigEntry> list = new List<MonsterSceneConfigEntry>(entries ?? new MonsterSceneConfigEntry[0]);
        e = new MonsterSceneConfigEntry { id = id };
        list.Add(e);
        entries = list.ToArray();
        return e;
    }

    /// <summary>
    /// 取对指定场景生效的行（已按 order 排序）。场景名为空时只返回 scene 为空的行。
    /// </summary>
    public List<MonsterSceneConfigEntry> GetForScene(string sceneName)
    {
        List<MonsterSceneConfigEntry> list = new List<MonsterSceneConfigEntry>();
        if (entries == null) return list;
        for (int i = 0; i < entries.Length; i++)
        {
            MonsterSceneConfigEntry e = entries[i];
            if (e == null) continue;
            if (e.MatchesScene(sceneName)) list.Add(e);
        }
        list.Sort((a, b) => a.order.CompareTo(b.order));
        return list;
    }

    /// <summary>取对指定场景生效且可用的行。</summary>
    public List<MonsterSceneConfigEntry> GetUsableForScene(string sceneName)
    {
        List<MonsterSceneConfigEntry> list = GetForScene(sceneName);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!list[i].IsUsable) list.RemoveAt(i);
        }
        return list;
    }

    /// <summary>表内行数。</summary>
    public int Count { get { return entries != null ? entries.Length : 0; } }

    /// <summary>从 Resources 加载本表（找不到返回 null，不抛异常）。</summary>
    public static SceneConfigTable Load()
    {
        return Resources.Load<SceneConfigTable>(ResourcePath);
    }

    /// <summary>当前活动场景名（EditMode / 无场景时返回 ""）。</summary>
    public static string ActiveSceneName()
    {
        try
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }
        catch (Exception)
        {
            return "";
        }
    }
}
