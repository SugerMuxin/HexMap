using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 极简 CSV 读写工具（Editor 专用，够用即止，不引第三方库）。
///
/// 约定：
///   - 首行为表头（列名，大小写不敏感；列顺序可任意调整）
///   - 支持双引号包裹字段（字段内含逗号 / 换行 / 双引号），双引号用 "" 转义
///   - 空行自动跳过
///
/// 用途：UISystem / 种植系统的"表驱动配置"源文件格式 ——
///   Assets/Resources/Configs/Tables/*.csv  ←→  同名 .asset（见 Editor/TableImporter.cs）
/// </summary>
public static class TableCsv
{
    /// <summary>解析为行 × 列。首行是表头。</summary>
    public static List<string[]> Parse(string text)
    {
        List<string[]> rows = new List<string[]>();
        if (string.IsNullOrEmpty(text)) return rows;

        // 去 BOM（Excel / UTF8Encoding(true) 写出的文件带 BOM）
        if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

        List<string> fields = new List<string>();
        StringBuilder field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                }
                else if (c == '\r')
                {
                    // 吞掉 CRLF 的 CR
                }
                else if (c == '\n')
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                    rows.Add(fields.ToArray());
                    fields.Clear();
                }
                else field.Append(c);
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }

        // 过滤空行
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            bool empty = true;
            for (int j = 0; j < rows[i].Length; j++)
            {
                if (!string.IsNullOrEmpty(rows[i][j])) { empty = false; break; }
            }
            if (empty) rows.RemoveAt(i);
        }
        return rows;
    }

    /// <summary>序列化为 CSV 文本（LF 换行；字段按需加引号）。</summary>
    public static string Serialize(List<string[]> rows)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            string[] row = rows[i];
            for (int j = 0; j < row.Length; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append(Escape(row[j]));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>转义单个字段（含逗号 / 引号 / 换行时用双引号包裹）。</summary>
    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        bool needQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0
                         || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0
                         || value[0] == ' ' || value[value.Length - 1] == ' ';
        if (!needQuote) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>取列下标（找不到返回 -1）。</summary>
    public static int Index(string[] header, string name)
    {
        if (header == null) return -1;
        for (int i = 0; i < header.Length; i++)
        {
            if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>取值（越界返回 ""）。</summary>
    public static string Get(string[] row, int index)
    {
        if (row == null || index < 0 || index >= row.Length) return "";
        return row[index] == null ? "" : row[index].Trim();
    }

    public static int GetInt(string[] row, int index, int fallback)
    {
        string s = Get(row, index);
        int v;
        return int.TryParse(s, out v) ? v : fallback;
    }

    public static float GetFloat(string[] row, int index, float fallback)
    {
        string s = Get(row, index);
        float v;
        return float.TryParse(s, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
    }

    public static bool GetBool(string[] row, int index, bool fallback)
    {
        string s = Get(row, index).ToLowerInvariant();
        if (s.Length == 0) return fallback;
        if (s == "1" || s == "true" || s == "yes" || s == "y" || s == "是" || s == "开") return true;
        if (s == "0" || s == "false" || s == "no" || s == "n" || s == "否" || s == "关") return false;
        return fallback;
    }

    /// <summary>解析枚举（支持名字或数字；失败用 fallback）。</summary>
    public static TEnum GetEnum<TEnum>(string[] row, int index, TEnum fallback) where TEnum : struct
    {
        string s = Get(row, index);
        if (s.Length == 0) return fallback;
        TEnum e;
        if (Enum.TryParse<TEnum>(s, true, out e)) return e;
        int num;
        if (int.TryParse(s, out num) && Enum.IsDefined(typeof(TEnum), num)) return (TEnum)(object)num;
        return fallback;
    }

    /// <summary>解析 KeyCode（支持 "F1" / "KeyCode.F1" / "None" / 数字）。</summary>
    public static UnityEngine.KeyCode GetKeyCode(string[] row, int index, UnityEngine.KeyCode fallback)
    {
        string s = Get(row, index);
        if (s.Length == 0) return fallback;
        if (s.StartsWith("KeyCode.", StringComparison.OrdinalIgnoreCase)) s = s.Substring("KeyCode.".Length);
        return GetEnum<UnityEngine.KeyCode>(row, index, fallback);
    }
}
