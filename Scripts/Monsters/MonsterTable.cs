using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 怪物表（ScriptableObject，游戏侧）：id → <see cref="MonsterConfig"/>。
///
/// 范式与 `SeedTable` 完全一致（种植系统的种子表）：CSV 是唯一编辑入口，
/// 本资产是运行时查询入口。场景配置表（<see cref="SceneConfigTable"/>）用 monsterId 引用本表。
///
/// 数据来源：`Assets/Resources/Configs/Tables/MonsterTable.csv`
///          经 Editor/TableImporter → `Tools/UI/数据表/导入 怪物表 CSV`。
/// </summary>
[CreateAssetMenu(fileName = "MonsterTable", menuName = "HexMap/怪物/怪物表", order = 21)]
public class MonsterTable : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("怪物 id（唯一，= MonsterConfig 资产名后缀）")]
        public string id;
        [Tooltip("怪物配置（数值 / prefab / 图标都在那儿）")]
        public MonsterConfig config;
        [Tooltip("是否已解锁（首版恒 true，为后续解锁留位）")]
        public bool unlocked = true;
        [Tooltip("显示顺序（小在前）")]
        public int order;
    }

    [Tooltip("怪物表路径（Resources 下，不带扩展名）")]
    public string resourcePath = MonsterTableResourcePath;

    public const string MonsterTableResourcePath = "Configs/Tables/MonsterTable";
    public const string MonsterConfigResourceDir = "Configs/Monsters";
    public const string MonsterConfigNamePrefix = "MonsterConfig_";

    public Entry[] monsters = new Entry[0];

    /// <summary>按 id 查条目（找不到返回 null）。</summary>
    public Entry Find(string id)
    {
        if (string.IsNullOrEmpty(id) || monsters == null) return null;
        for (int i = 0; i < monsters.Length; i++)
        {
            if (monsters[i] != null && monsters[i].id == id) return monsters[i];
        }
        return null;
    }

    /// <summary>按 id 查配置（找不到返回 null）。</summary>
    public MonsterConfig FindConfig(string id)
    {
        Entry e = Find(id);
        return e != null ? e.config : null;
    }

    /// <summary>按显示顺序排好的全部条目（过滤空项）。</summary>
    public List<Entry> OrderedEntries()
    {
        List<Entry> list = new List<Entry>();
        if (monsters != null)
        {
            for (int i = 0; i < monsters.Length; i++)
            {
                if (monsters[i] != null && monsters[i].config != null) list.Add(monsters[i]);
            }
        }
        list.Sort((a, b) => a.order.CompareTo(b.order));
        return list;
    }

    /// <summary>已解锁的怪物配置列表。</summary>
    public MonsterConfig[] GetUnlockedConfigs()
    {
        List<Entry> list = OrderedEntries();
        List<MonsterConfig> configs = new List<MonsterConfig>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].unlocked && list[i].config != null) configs.Add(list[i].config);
        }
        return configs.ToArray();
    }

    /// <summary>条目数量（含未解锁）。</summary>
    public int Count { get { return monsters != null ? monsters.Length : 0; } }

    /// <summary>从 Resources 加载怪物表资产（找不到返回 null）。</summary>
    public static MonsterTable Load()
    {
        return Resources.Load<MonsterTable>(MonsterTableResourcePath);
    }

    /// <summary>按 id 约定路径加载怪物配置资产（`Configs/Monsters/MonsterConfig_{id}`）。</summary>
    public static MonsterConfig LoadConfigById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return Resources.Load<MonsterConfig>(MonsterConfigResourceDir + "/" + MonsterConfigNamePrefix + id);
    }
}
