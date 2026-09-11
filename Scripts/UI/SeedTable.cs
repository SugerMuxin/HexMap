using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 种子（可种植植物）表（脚本对象，游戏侧）。背包 UI 的数据源。
///
/// 一行 = 一颗种子：
///   - `config`  指向 PlantConfig（种植数值与 prefab 都在那儿）
///   - `terrain` 可种植地形说明（**仅 UI 展示用元数据**；真正的可种植地形由
///               PlantingSystem.plantableTerrain 统一决定，本表不改变种植规则）
///   - `unlocked` 是否已解锁（首版恒 true；为后续掉落 / 解锁留位）
///   - `order`   背包里显示顺序
///
/// 数据来源：`Assets/Resources/Configs/Tables/SeedTable.csv` 经 Editor/TableImporter 导入。
/// </summary>
[CreateAssetMenu(fileName = "SeedTable", menuName = "HexMap/种植/种子表", order = 3)]
public class SeedTable : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("种子 id（唯一，通常 = PlantConfig 资产名后缀）")]
        public string id;
        [Tooltip("植物配置（数值 / prefab / 图标来源）")]
        public PlantConfig config;
        [Tooltip("可种植地形说明（仅 UI 展示，如 草|泥；不改种植规则）")]
        public string terrain = "草|泥";
        [Tooltip("是否已解锁（首版恒 true，为后续掉落 / 解锁留位）")]
        public bool unlocked = true;
        [Tooltip("背包显示顺序（小在前）")]
        public int order;
    }

    [Tooltip("种子表路径（Resources 下，不带扩展名）")]
    public string resourcePath = "Configs/Tables/SeedTable";

    public Entry[] seeds = new Entry[0];

    /// <summary>按 id 查种子（找不到返回 null）。</summary>
    public Entry Find(string id)
    {
        if (string.IsNullOrEmpty(id) || seeds == null) return null;
        for (int i = 0; i < seeds.Length; i++)
        {
            if (seeds[i] != null && seeds[i].id == id) return seeds[i];
        }
        return null;
    }

    /// <summary>按显示顺序排好的全部条目（过滤空项）。</summary>
    public List<Entry> OrderedSeeds()
    {
        List<Entry> list = new List<Entry>();
        if (seeds != null)
        {
            for (int i = 0; i < seeds.Length; i++)
            {
                if (seeds[i] != null && seeds[i].config != null) list.Add(seeds[i]);
            }
        }
        list.Sort((a, b) => a.order.CompareTo(b.order));
        return list;
    }

    /// <summary>已解锁的植物配置列表（可直接灌给 PlantingSystem.availablePlants）。</summary>
    public PlantConfig[] GetUnlockedConfigs()
    {
        List<Entry> list = OrderedSeeds();
        List<PlantConfig> configs = new List<PlantConfig>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].unlocked && list[i].config != null) configs.Add(list[i].config);
        }
        return configs.ToArray();
    }

    /// <summary>种子数量（含未解锁）。</summary>
    public int Count { get { return seeds != null ? seeds.Length : 0; } }
}
