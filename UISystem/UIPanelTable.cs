using System;
using UnityEngine;

namespace HexUI
{
    /// <summary>
    /// 面板注册表（ScriptableObject）。**表驱动 UI 的唯一入口**：
    /// UIManager 只认表里的 id，打开/关闭/热键/层级/互斥/模态全部由表配置决定。
    ///
    /// 数据来源：由 CSV（Assets/Resources/Configs/Tables/UIPanelTable.csv）经
    /// Editor/TableImporter 导入生成/更新；也可在 Inspector 手工增删（便于快速试验）。
    /// </summary>
    [CreateAssetMenu(fileName = "UIPanelTable", menuName = "HexMap/UI/面板表", order = 0)]
    public class UIPanelTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("面板 id（代码 Open/Close 用；建议与 prefab 同名）")]
            public string id;

            [Tooltip("面板 prefab（优先使用；为空时用 prefabPath 从 Resources 加载）")]
            public GameObject prefab;

            [Tooltip("Resources 相对路径（不带扩展名），如 UI/Panels/InventoryPanel")]
            public string prefabPath;

            [Tooltip("层级（决定容器与遮挡关系）")]
            public UIPanelLayer layer = UIPanelLayer.Normal;

            [Tooltip("模态：打开时显示遮罩并阻塞下层点击")]
            public bool modal = true;

            [Tooltip("互斥组：打开本面板时自动关闭同组其它面板（留空 = 不互斥）")]
            public string exclusiveGroup = "";

            [Tooltip("Esc 可关闭（仅对栈顶生效）")]
            public bool escClose = true;

            [Tooltip("热键（KeyCode.None = 无；按一次开，再按一次关）")]
            public KeyCode hotkey = KeyCode.None;

            [Tooltip("打开时阻塞游戏输入（由游戏侧 UICharacterInputGate 落到 HexControlMode.uiModalActive）")]
            public bool blockGameInput = true;

            [Tooltip("启动时预热（预实例化，避免首次打开卡顿）")]
            public bool preload;

            [Tooltip("同层内排序（小在前）")]
            public int order;
        }

        public Entry[] entries = new Entry[0];

        /// <summary>按 id 查配置（不存在返回 null）。</summary>
        public Entry Find(string id)
        {
            if (string.IsNullOrEmpty(id) || entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].id == id) return entries[i];
            }
            return null;
        }

        /// <summary>取（或创建）指定 id 的配置项。</summary>
        public Entry GetOrCreate(string id)
        {
            Entry e = Find(id);
            if (e != null) return e;
            e = new Entry { id = id };
            Entry[] next = new Entry[(entries == null ? 0 : entries.Length) + 1];
            if (entries != null) Array.Copy(entries, next, entries.Length);
            next[next.Length - 1] = e;
            entries = next;
            return e;
        }

        /// <summary>解析可用 prefab（引用优先，其次 Resources 路径；都没有返回 null）。</summary>
        public GameObject ResolvePrefab(Entry entry)
        {
            if (entry == null) return null;
            if (entry.prefab != null) return entry.prefab;
            if (!string.IsNullOrEmpty(entry.prefabPath))
            {
                GameObject go = Resources.Load<GameObject>(entry.prefabPath);
                if (go != null) entry.prefab = go;   // 缓存回填，避免重复加载
                return go;
            }
            return null;
        }
    }
}
