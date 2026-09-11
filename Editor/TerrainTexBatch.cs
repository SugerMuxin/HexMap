using UnityEditor;
using UnityEngine;

/// <summary>
/// Part14 地形贴图 批处理装配（unity -batchmode -executeMethod TerrainTexBatch.Run）：
///  1) cellMat.mat → Custom/TerrainTextured shader + _MainTex = TerrainTextures 数组资产
///  2) HexGridChunk.prefab 的 Terrain(useCollider+useColors) HexMesh 打开 useTerrainTypes
///  3) 导出 CodeTxts（复用 CodeTxtSyncTool 逻辑）
/// </summary>
public static class TerrainTexBatch
{
    public static void Run()
    {
        // 1) material
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Materials/cellMat.mat");
        Shader sh = Shader.Find("Custom/TerrainTextured");
        Texture2DArray arr = AssetDatabase.LoadAssetAtPath<Texture2DArray>(
            "Assets/Resources/Textures/TerrainTextures.asset");
        if (mat == null || sh == null || arr == null)
        {
            Debug.LogError("TerrainTexBatch: 资源缺失 mat=" + (mat != null) +
                " shader=" + (sh != null) + " array=" + (arr != null));
            EditorApplication.Exit(1);
            return;
        }
        mat.shader = sh;
        mat.SetTexture("_MainTex", arr);
        EditorUtility.SetDirty(mat);
        Debug.Log("cellMat.mat -> " + sh.name + " / " + arr.name);

        // 2) prefab: 打开 terrain HexMesh 的 useTerrainTypes
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Resources/Prefabs/HexGridChunk.prefab");
        if (prefab == null)
        {
            Debug.LogError("TerrainTexBatch: HexGridChunk.prefab 缺失");
            EditorApplication.Exit(1);
            return;
        }
        HexMesh[] meshes = prefab.GetComponentsInChildren<HexMesh>(true);
        HexMesh terrain = null;
        foreach (HexMesh hm in meshes)
        {
            if (hm.useCollider && hm.useColors) terrain = hm;
        }
        if (terrain == null)
        {
            Debug.LogError("TerrainTexBatch: prefab 中未找到 terrain HexMesh");
            EditorApplication.Exit(1);
            return;
        }
        terrain.useTerrainTypes = true;
        EditorUtility.SetDirty(terrain);
        bool saved = PrefabUtility.SavePrefabAsset(prefab);
        Debug.Log("HexGridChunk.prefab useTerrainTypes=true saved=" + saved +
            " | terrainMat=" + (terrain.GetComponent<MeshRenderer>() != null
                ? terrain.GetComponent<MeshRenderer>().sharedMaterial.name : "none"));

        // 3) CodeTxts 导出
        CodeTxtSyncTool.BatchExportCsToTxt();
        Debug.Log("TerrainTexBatch done.");
        EditorApplication.Exit(0);
    }
}
