using UnityEngine;

/// <summary>
/// HexMap → 通用水下效果 的桥接器（游戏侧适配层）。
/// 实现 IWaterSurfaceProvider：把"六边形格子水面高度"翻译给完全通用的
/// UnderwaterEffect，使 UnderwaterEffect 本身不依赖 HexGrid/HexCell。
///
/// 用法：挂到任意 GameObject（如 HexGrid 上），Inspector 拖：
///   - effect   = 主相机上的 UnderwaterEffect（可留空自动找）
///   - hexGrid  = 场景 HexGrid（可留空自动找）
/// 运行时每帧把本组件同步为 effect 的水面查询源。
/// </summary>
public class HexWaterBridge : MonoBehaviour, IWaterSurfaceProvider
{
    [Tooltip("目标通用水下效果组件（可留空自动查找场景中的第一个）")]
    public UnderwaterEffect effect;
    [Tooltip("HexGrid（可留空自动查找）")]
    public HexGrid hexGrid;

    void OnEnable()
    {
        if (effect == null) effect = FindObjectOfType<UnderwaterEffect>();
        if (hexGrid == null) hexGrid = FindObjectOfType<HexGrid>();
    }

    void Update()
    {
        // 每帧兜底自动查找（应对 Awake/OnEnable 顺序不定），
        // 且不覆盖用户已在 Inspector 上拖好的其他水面来源
        if (effect == null) effect = FindObjectOfType<UnderwaterEffect>();
        if (hexGrid == null) hexGrid = FindObjectOfType<HexGrid>();
        if (effect != null && effect.surfaceProvider == null)
        {
            effect.surfaceProvider = this;
        }
    }

    /// <summary>实现 IWaterSurfaceProvider：返回 worldPoint 所在格的水面高度（无水返回 -1e9）</summary>
    public float GetWaterSurfaceY(Vector3 worldPoint)
    {
        if (hexGrid == null)
        {
            return -1e9f;
        }
        Vector3 local = hexGrid.transform.InverseTransformPoint(worldPoint);
        HexCell cell = hexGrid.GetCell(HexCoordinates.FromPosition(local));
        if (cell == null)
        {
            return -1e9f;
        }
        return cell.IsUnderwater ? cell.WaterSurfaceY : -1e9f;
    }
}
