using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 代码 <-> TXT 同步工具
/// 背景：开发环境会对 .cs 文件加密，但不加密 .txt 文件。
/// 1. 导出：把 Assets 下所有 .cs 的内容按相同目录结构存到 Assets/CodeTxts 下的同名 .txt，
///    便于提交 git 同步到无加密的环境。
/// 2. 还原：把 Assets/CodeTxts 下的 .txt 内容写回对应的 .cs
///    （已存在则覆盖，不存在则在 .txt 对应的原目录新建）。
/// </summary>
public static class CodeTxtSyncTool
{
    private const string CodeTxtsFolderName = "CodeTxts";

    // 菜单：Tools/代码同步/导出 .cs -> .txt
    [MenuItem("Tools/代码同步/导出 .cs 为 .txt (CodeTxts)")]
    private static void ExportCsToTxt()
    {
        string assetsPath = Application.dataPath;
        string codeTxtsRoot = Path.Combine(assetsPath, CodeTxtsFolderName);
        string skipPrefix = CodeTxtsFolderName + Path.DirectorySeparatorChar;
        int count = 0;

        foreach (string csFile in Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories))
        {
            // Assets 下的相对路径，例如 "Scripts/HexCell.cs"
            string relative = csFile.Substring(assetsPath.Length + 1);

            // 跳过 CodeTxts 目录自身（双保险）
            if (relative.StartsWith(skipPrefix, StringComparison.Ordinal))
                continue;

            // 镜像目录结构：Assets/Scripts/HexCell.cs -> Assets/CodeTxts/Scripts/HexCell.txt
            string txtFile = Path.Combine(codeTxtsRoot, relative.Substring(0, relative.Length - 3) + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(txtFile));
            File.Copy(csFile, txtFile, true);
            count++;
        }

        AssetDatabase.Refresh();
        string msg = string.Format("已导出 {0} 个 .cs 文件到 Assets/{1}/\n\n现在可以把 Assets/{1}/ 提交到 git 了。", count, CodeTxtsFolderName);
        Debug.Log("[CodeTxtSync] " + msg);
        EditorUtility.DisplayDialog("代码同步", msg, "确定");
    }

    // 菜单：Tools/代码同步/还原 .txt -> .cs
    [MenuItem("Tools/代码同步/用 .txt 还原 .cs (解密)")]
    private static void RestoreTxtToCs()
    {
        string assetsPath = Application.dataPath;
        string codeTxtsRoot = Path.Combine(assetsPath, CodeTxtsFolderName);
        if (!Directory.Exists(codeTxtsRoot))
        {
            EditorUtility.DisplayDialog("代码同步",
                "未找到 Assets/" + CodeTxtsFolderName + " 目录，请先在开发环境执行“导出 .cs 为 .txt”。", "确定");
            return;
        }

        // CodeTxts 下的相对路径 -> Assets 下对应的 .cs 路径
        var targets = new List<KeyValuePair<string, string>>(); // txt -> cs
        int overwrite = 0;
        foreach (string txtFile in Directory.GetFiles(codeTxtsRoot, "*.txt", SearchOption.AllDirectories))
        {
            string relative = txtFile.Substring(codeTxtsRoot.Length + 1); // 例如 "Scripts/HexCell.txt"
            string csFile = Path.Combine(assetsPath, relative.Substring(0, relative.Length - 4) + ".cs");
            targets.Add(new KeyValuePair<string, string>(txtFile, csFile));
            if (File.Exists(csFile))
                overwrite++;
        }

        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("代码同步", "Assets/" + CodeTxtsFolderName + " 下没有 .txt 文件。", "确定");
            return;
        }

        int created = targets.Count - overwrite;
        string confirmMsg = string.Format(
            "即将用 Assets/{0} 中的 .txt 还原 .cs 文件：\n\n  覆盖已有 .cs：{1} 个\n  新建 .cs：{2} 个\n\n注意：覆盖前请确认 .cs 的最新改动已导出为 .txt。是否继续？",
            CodeTxtsFolderName, overwrite, created);
        if (!EditorUtility.DisplayDialog("代码同步", confirmMsg, "继续", "取消"))
            return;

        foreach (var pair in targets)
        {
            // 项目中不存在该 .cs 时，在 .txt 对应的原目录新建
            Directory.CreateDirectory(Path.GetDirectoryName(pair.Value));
            File.Copy(pair.Key, pair.Value, true);
        }

        AssetDatabase.Refresh();
        string msg = string.Format("还原完成：覆盖 {0} 个 .cs，新建 {1} 个 .cs。", overwrite, created);
        Debug.Log("[CodeTxtSync] " + msg);
        EditorUtility.DisplayDialog("代码同步", msg, "确定");
    }
}
