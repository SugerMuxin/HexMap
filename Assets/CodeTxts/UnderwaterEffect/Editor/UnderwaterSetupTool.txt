using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 通用水下效果装配工具（菜单：Tools/水下效果/...）。
/// 与地图网格/游戏逻辑零耦合——纯通用：找主相机 → 挂 UnderwaterEffect → 可选设角色探测点。
/// 水面高度来源由使用者按场景提供：
///   a) 把水面物体拖到 Surface Object（取它的世界 Y）
///   b) 填 Surface Y 常数
///   c) 挂实现 IWaterSurfaceProvider 的桥接器（HexMap 项目用 HexWaterBridge）
/// </summary>
public static class UnderwaterSetupTool
{
    [MenuItem("Tools/水下效果/装配到主相机 (UnderwaterEffect)")]
    public static void Setup()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            foreach (Camera c in Object.FindObjectsOfType<Camera>())
            {
                if (c != null && c.enabled)
                {
                    cam = c;
                    break;
                }
            }
        }
        if (cam == null)
        {
            EditorUtility.DisplayDialog("水下效果", "场景中找不到启用的相机，请先创建相机。", "确定");
            return;
        }

        GameObject camGo = cam.gameObject;
        UnderwaterEffect effect = camGo.GetComponent<UnderwaterEffect>();
        if (effect == null)
        {
            effect = Undo.AddComponent<UnderwaterEffect>(camGo);
        }
        if (effect.probeTransform == null)
        {
            // 自动找角色（挂 CharacterController 的物体，如 Ellen）
            var cc = Object.FindObjectOfType<CharacterController>();
            if (cc != null)
            {
                effect.probeTransform = cc.transform;
            }
        }

        EditorUtility.SetDirty(camGo);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        string probeName = effect.probeTransform != null ? effect.probeTransform.name : "（无，仅按相机位置判定）";
        EditorUtility.DisplayDialog("水下效果",
            "已装配 UnderwaterEffect → " + camGo.name + "\\n\\n" +
            "- 角色探测点: " + probeName + "\\n\\n" +
            "下一步设置水面高度（三选一）：\\n" +
            "  1) 拖水面物体到 Surface Object\\n" +
            "  2) 填 Surface Y 常数\\n" +
            "  3) 挂 HexWaterBridge（HexMap 项目自动同步格子水面）", "确定");
    }

    [MenuItem("Tools/水下效果/重置参数为默认")]
    public static void ResetDefaults()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            EditorUtility.DisplayDialog("水下效果", "找不到主相机（Camera.main）。", "确定");
            return;
        }
        UnderwaterEffect effect = cam.GetComponent<UnderwaterEffect>();
        if (effect == null)
        {
            EditorUtility.DisplayDialog("水下效果", cam.name + " 上没有 UnderwaterEffect，无需重置。", "确定");
            return;
        }
        effect.ResetToDefaults();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("水下效果", "已重置 " + cam.name + " 的 UnderwaterEffect 参数为默认值。", "确定");
    }

    [MenuItem("Tools/水下效果/从主相机移除")]
    public static void Remove()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            EditorUtility.DisplayDialog("水下效果", "找不到主相机（Camera.main）。", "确定");
            return;
        }
        UnderwaterEffect effect = cam.GetComponent<UnderwaterEffect>();
        if (effect == null)
        {
            EditorUtility.DisplayDialog("水下效果", cam.name + " 上没有 UnderwaterEffect。", "确定");
            return;
        }
        Undo.DestroyObjectImmediate(effect);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("水下效果", "已从 " + cam.name + " 移除 UnderwaterEffect。", "确定");
    }
}
