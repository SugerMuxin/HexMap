using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// `.map` 存档工具（联机大厅"选地图"用）。
///
/// 与既有 `SaveLoadMenu` **完全同格式**（首行 int header=3，随后 HexGrid.Save/Load 的字节流），
/// 目录同为 `Application.persistentDataPath`，扩展名同为 `.map` —— 所以大厅里能直接看到
/// 你用菜单存的地图，反过来也一样。
///
/// 本类只做读/列/写，不修改 SaveLoadMenu（它保持原样可用）。
/// </summary>
public static class MapFileUtil
{
    public const string Extension = ".map";
    /// <summary>与 SaveLoadMenu.Save 一致的存档版本号。</summary>
    public const int Header = 3;

    /// <summary>当前地图名（本进程内，供大厅/发现广播显示；未载入过为空）。</summary>
    public static string CurrentMapName = "";

    /// <summary>存档目录。</summary>
    public static string Directory
    {
        get { return Application.persistentDataPath; }
    }

    /// <summary>列出全部地图名（按名称排序；目录不存在返回空数组）。</summary>
    public static string[] ListMapNames()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return new string[0];
            string[] files = System.IO.Directory.GetFiles(Directory, "*" + Extension);
            List<string> names = new List<string>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileNameWithoutExtension(files[i]);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[UI] 列地图失败：" + e.Message);
            return new string[0];
        }
    }

    /// <summary>地图文件完整路径。</summary>
    public static string GetPath(string mapName)
    {
        return Path.Combine(Directory, mapName + Extension);
    }

    /// <summary>地图是否存在。</summary>
    public static bool Exists(string mapName)
    {
        return !string.IsNullOrEmpty(mapName) && File.Exists(GetPath(mapName));
    }

    /// <summary>载入地图（失败返回 false，不抛异常）。成功后记录 CurrentMapName。</summary>
    public static bool Load(HexGrid grid, string mapName)
    {
        if (grid == null || string.IsNullOrEmpty(mapName)) return false;
        string path = GetPath(mapName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("[UI] 地图不存在：" + path);
            return false;
        }

        try
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
            {
                int header = reader.ReadInt32();
                if (header > Header)
                {
                    Debug.LogWarning("[UI] 未知地图格式 header=" + header + "（" + path + "）");
                    return false;
                }
                grid.Load(reader, header);
            }

            // 地图重建会销毁旧 HexCell → 先清掉旧植物，避免占用表留下失效引用（浮空植物）
            if (PlantingSystem.Instance != null) PlantingSystem.Instance.ClearPlants();

            CurrentMapName = mapName;
            Debug.Log("[UI] 已载入地图 " + mapName + "（" + Header + " 号格式）");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[UI] 载入地图失败 " + mapName + "：" + e);
            return false;
        }
    }

    /// <summary>另存地图（与 SaveLoadMenu 同格式）。</summary>
    public static bool Save(HexGrid grid, string mapName)
    {
        if (grid == null || string.IsNullOrEmpty(mapName)) return false;
        try
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(GetPath(mapName), FileMode.Create)))
            {
                writer.Write(Header);
                grid.Save(writer);
            }
            CurrentMapName = mapName;
            Debug.Log("[UI] 已保存地图 " + mapName);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[UI] 保存地图失败 " + mapName + "：" + e);
            return false;
        }
    }
}
