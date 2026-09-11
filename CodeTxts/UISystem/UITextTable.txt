using System;
using UnityEngine;

namespace HexUI
{
    /// <summary>
    /// UI 文案表（脚本对象）。**通用层**：只认 key → 文本，不认识任何游戏类型。
    ///
    /// 数据来源：`Assets/Resources/Configs/Tables/UITextTable.csv`（列：key,zh,en）
    /// 经 `Editor/TableImporter` 导入生成；运行期经 <see cref="UIText.Get"/> 读取。
    /// 中文为当前实际显示语言（en 列预留给后续多语言）。
    /// </summary>
    [CreateAssetMenu(fileName = "UITextTable", menuName = "HexMap/UI/文案表", order = 2)]
    public class UITextTable : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("文案 key（如 bag.title）")]
            public string key;
            [Tooltip("中文（当前显示语言）")]
            [TextArea(1, 3)] public string zh;
            [Tooltip("英文（预留，暂未启用）")]
            [TextArea(1, 3)] public string en;
        }

        [Tooltip("文案路径（Resources 下，不带扩展名）")]
        public string resourcePath = "Configs/Tables/UITextTable";

        public Entry[] entries = new Entry[0];

        /// <summary>按 key 取中文文案；找不到返回 fallback（默认返回 key 本身，便于发现漏配）。</summary>
        public string Get(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key)) return fallback ?? "";
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    Entry e = entries[i];
                    if (e != null && e.key == key)
                    {
                        if (!string.IsNullOrEmpty(e.zh)) return e.zh;
                        break;
                    }
                }
            }
            return fallback ?? key;
        }

        /// <summary>按 key 取配置项（找不到返回 null）。</summary>
        public Entry Find(string key)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].key == key) return entries[i];
            }
            return null;
        }
    }

    /// <summary>
    /// 文案静态入口（懒加载 UITextTable；表缺失时返回 fallback，不抛异常）。
    /// 面板里统一写 `UIText.Get("bag.title", "背包")`，改文案只改 CSV 再导入。
    /// </summary>
    public static class UIText
    {
        public const string DefaultResourcePath = "Configs/Tables/UITextTable";

        static UITextTable table;
        static bool tried;
        static readonly System.Collections.Generic.HashSet<string> missingLogged =
            new System.Collections.Generic.HashSet<string>();

        /// <summary>当前文案表（无则 null）。</summary>
        public static UITextTable Table
        {
            get { Ensure(); return table; }
        }

        static void Ensure()
        {
            if (table != null || tried) return;
            tried = true;
            table = Resources.Load<UITextTable>(DefaultResourcePath);
        }

        /// <summary>手动注入（测试用）。</summary>
        public static void SetTable(UITextTable t)
        {
            table = t;
            tried = true;
        }

        /// <summary>取文案；表里没有该 key 时返回 fallback（并把缺失 key 记一次警告）。</summary>
        public static string Get(string key, string fallback = null)
        {
            Ensure();
            if (table == null) return fallback ?? key;
            UITextTable.Entry e = table.Find(key);
            if (e != null && !string.IsNullOrEmpty(e.zh)) return e.zh;
            if (missingLogged.Add(key))
            {
                Debug.LogWarning("[UI] 文案表缺少 key：" + key + "（已回退到默认文本）");
            }
            return fallback ?? key;
        }

        /// <summary>清缓存（导入文案表后调用，立即生效）。</summary>
        public static void ClearCache()
        {
            table = null;
            tried = false;
            missingLogged.Clear();
        }
    }
}
