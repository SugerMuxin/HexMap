using UnityEditor;
using UnityEngine;

/// <summary>
/// Part 14 地形纹理数组向导：把若干张同规格纹理打包为一个 Texture2DArray 资产。
/// 交互：菜单 Assets/Create/Texture Array（选纹理 + 保存路径）。
/// 批处理：TextureArrayWizard.BuildDefaultTerrainArray() —— 固定读取
///   Assets/Resources/Textures/Terrain/{sand,grass,mud,stone,snow}.png
///   输出  Assets/Resources/Textures/TerrainTextures.asset
/// </summary>
public class TextureArrayWizard : ScriptableWizard
{
    public Texture2D[] textures;

    [MenuItem("Assets/Create/Texture Array")]
    static void CreateWizard()
    {
        ScriptableWizard.DisplayWizard<TextureArrayWizard>("Create Texture Array", "Create");
    }

    void OnWizardCreate()
    {
        if (textures == null || textures.Length == 0)
        {
            return;
        }
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Texture Array", "Texture Array", "asset", "Save Texture Array"
        );
        if (path.Length == 0)
        {
            return;
        }
        Create(path, textures);
    }

    /// <summary>把 textures 打包成 Texture2DArray 并保存为资产（已存在则覆盖重建）。</summary>
    public static Texture2DArray Create(string assetPath, Texture2D[] textures)
    {
        if (textures == null || textures.Length == 0)
        {
            return null;
        }
        Texture2D t = textures[0];
        Texture2DArray textureArray = new Texture2DArray(
            t.width, t.height, textures.Length, t.format, t.mipmapCount > 1
        );
        textureArray.anisoLevel = t.anisoLevel;
        textureArray.filterMode = t.filterMode;
        textureArray.wrapMode = t.wrapMode;

        for (int i = 0; i < textures.Length; i++)
        {
            for (int m = 0; m < t.mipmapCount; m++)
            {
                Graphics.CopyTexture(textures[i], 0, m, textureArray, i, m);
            }
        }

        if (AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath) != null)
        {
            AssetDatabase.DeleteAsset(assetPath);
        }
        AssetDatabase.CreateAsset(textureArray, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath);
        AssetDatabase.Refresh();
        return textureArray;
    }

    /// <summary>批处理入口（unity -batchmode -executeMethod）：生成默认地形纹理数组。</summary>
    public static void BuildDefaultTerrainArray()
    {
        string[] names = { "sand", "grass", "mud", "stone", "snow" };
        const string folder = "Assets/Resources/Textures/Terrain";
        Texture2D[] textures = new Texture2D[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + names[i] + ".png");
            if (textures[i] == null)
            {
                Debug.LogError("缺少地形纹理: " + folder + "/" + names[i] + ".png");
                EditorApplication.Exit(1);
                return;
            }
        }
        Texture2DArray arr = Create("Assets/Resources/Textures/TerrainTextures.asset", textures);
        if (arr == null)
        {
            Debug.LogError("纹理数组创建失败");
            EditorApplication.Exit(1);
            return;
        }
        Debug.Log("TerrainTextures GUID=" +
            AssetDatabase.AssetPathToGUID("Assets/Resources/Textures/TerrainTextures.asset"));
        EditorApplication.Exit(0);
    }
}
