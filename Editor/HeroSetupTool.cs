using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 角色控制一键装配工具（菜单：Tools/角色控制/...）
/// 1. 把 HeroController 挂到场景中的 mushrole（角色）上
/// 2. 创建第三人称跟随相机 Hero Camera（tag=MainCamera + AudioListener + HeroFollowCamera）
/// </summary>
public static class HeroSetupTool
{
    const string HeroObjectName = "mushrole";
    const string CameraObjectName = "Hero Camera";

    [MenuItem("Tools/角色控制/装配角色与跟随相机")]
    public static void SetupHero()
    {
        // --- 角色 ---
        GameObject hero = GameObject.Find(HeroObjectName);
        if (hero == null)
        {
            EditorUtility.DisplayDialog("角色控制", "场景中找不到 " + HeroObjectName + "，请先把 Hero 放入场景。", "确定");
            return;
        }
        if (hero.GetComponent<HeroController>() == null)
        {
            Undo.AddComponent<HeroController>(hero);
        }
        HeroController controller = hero.GetComponent<HeroController>();
        if (controller.hexGrid == null)
        {
            controller.hexGrid = Object.FindObjectOfType<HexGrid>();
        }
        EditorUtility.SetDirty(hero);

        // --- 跟随相机 ---
        GameObject camGo = GameObject.Find(CameraObjectName);
        if (camGo == null)
        {
            camGo = new GameObject(CameraObjectName);
            Undo.RegisterCreatedObjectUndo(camGo, "Create Hero Camera");
            camGo.transform.position = hero.transform.position + new Vector3(0f, 30f, -30f);
        }
        Camera cam = camGo.GetComponent<Camera>();
        if (cam == null)
        {
            cam = camGo.AddComponent<Camera>();
        }
        cam.tag = "MainCamera";          // 隐藏旧 HexMapCamera 后 Camera.main 仍有效
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 600f;
        if (camGo.GetComponent<AudioListener>() == null)
        {
            camGo.AddComponent<AudioListener>();
        }
        if (camGo.GetComponent<HeroFollowCamera>() == null)
        {
            Undo.AddComponent<HeroFollowCamera>(camGo);
        }
        HeroFollowCamera follow = camGo.GetComponent<HeroFollowCamera>();
        follow.target = hero.transform;
        follow.pitch = 40f;
        follow.yaw = 0f;
        follow.distance = 45f;
        follow.lookHeight = 8f;
        follow.Snap();
        EditorUtility.SetDirty(camGo);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("角色控制",
            "已装配：\\n- " + hero.name + " + HeroController\\n- " + camGo.name + "（HeroFollowCamera）\\n\\n" +
            "使用：隐藏/禁用旧的 HexMapCamera 物体后 Play，左键点击地图移动角色。", "确定");
    }
}
