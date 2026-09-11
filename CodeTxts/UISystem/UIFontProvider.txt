using System;
using UnityEngine;
using UnityEngine.UI;

namespace HexUI
{
    /// <summary>
    /// 中文字体统一入口。
    ///
    /// 背景：本工程 UI 用 legacy `Text`，其默认字体（Arial / LegacyRuntime）在 Windows 上通常能靠
    /// 系统字体回退显示中文，但不保证（换平台 / 精简系统会变豆腐块）。
    /// 这里在运行时创建一个 OS 动态字体（首选微软雅黑），统一替换掉"默认字体"的 Text，
    /// 零字体资产即可稳定显示中文。
    ///
    /// 注意：`Font.CreateDynamicFontFromOSFont` 创建的是运行时对象，**不能序列化进 prefab/场景**，
    /// 因此字体只在运行时应用（UIManager.autoApplyCjkFont 会自动对每个面板调用 ApplyTo）。
    /// 后续若需发布其它平台，换成内置字体资产（TMP 或 OTF）即可，接口不变。
    /// </summary>
    public static class UIFontProvider
    {
        /// <summary>优先尝试的字体名（按顺序）。</summary>
        public static readonly string[] PreferredFontNames =
        {
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "微软雅黑",
            "SimHei",
            "Noto Sans CJK SC",
            "Source Han Sans SC",
            "PingFang SC",
            "Heiti SC"
        };

        static Font cjk;
        static string cjkName;
        static bool warned;

        /// <summary>当前使用的中文字体（找不到 OS 字体时返回 null，调用方需容忍）。</summary>
        public static Font Cjk
        {
            get
            {
                if (cjk != null) return cjk;
                string[] installed = null;
                try { installed = Font.GetOSInstalledFontNames(); }
                catch (Exception) { installed = null; }

                for (int i = 0; i < PreferredFontNames.Length; i++)
                {
                    string name = PreferredFontNames[i];
                    if (installed != null && Array.IndexOf(installed, name) < 0) continue;
                    try
                    {
                        Font f = Font.CreateDynamicFontFromOSFont(name, 24);
                        if (f != null)
                        {
                            cjk = f;
                            cjkName = name;
                            return cjk;
                        }
                    }
                    catch (Exception) { /* 该字体不可用，试下一个 */ }
                }

                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("[UI] 未找到可用的中文 OS 字体，将沿用默认字体（中文可能显示为方块）。" +
                                     "可改用内置字体资产。");
                }
                return null;
            }
        }

        /// <summary>当前实际使用的中文字体名（未使用则为空）。</summary>
        public static string CurrentFontName { get { return cjk != null ? cjkName : ""; } }

        /// <summary>把 root 下"仍是默认字体"的 Text 统一换成本字体（自定义字体不动）。</summary>
        public static void ApplyTo(GameObject root, bool includeInactive = true)
        {
            if (root == null) return;
            Font f = Cjk;
            if (f == null) return;

            Text[] texts = root.GetComponentsInChildren<Text>(includeInactive);
            for (int i = 0; i < texts.Length; i++)
            {
                Text t = texts[i];
                if (t == null) continue;
                if (IsDefaultFont(t.font)) t.font = f;
            }
        }

        /// <summary>是否"未指定 / 内置默认"字体（可安全替换）。</summary>
        public static bool IsDefaultFont(Font f)
        {
            if (f == null) return true;
            string n = f.name;
            if (string.IsNullOrEmpty(n)) return true;
            return n.IndexOf("Arial", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("LegacyRuntime", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
