using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HexMap → 通用音频层 的地面材质桥接器（游戏侧适配层，参照 HexWaterBridge 模式）。
/// 实现 IGroundKindProvider：把"世界坐标 → 脚下格 → 地形类型/水面"翻译成通用 GroundKind，
/// 使通用音频组件（脚步声等）不依赖 HexGrid/HexCell。
///
/// 映射规则（按优先级）：
///  1. 格越界 / HexGrid 缺失        → defaultKind
///  2. 格在水下（cell.IsUnderwater）→ GroundKind.Water（涉水/入水判定共用）
///  3. terrainTypeIndex 命中 overrides 表 → 对应 kind
///  4. 其余                          → defaultKind
///
/// 说明：terrainTypeIndex 已有语义（Part14 贴图顺序：0沙 1草 2泥 3石 4雪）。
/// 未在 overrides 里填写的类型默认归 defaultKind（Grass）；需要按材质区分脚步音时，
/// 在 Inspector 的 overrides 里按 index → 材质逐项填写即可（如 3 → Stone）。
///
/// 用法：挂到 HexGrid 所在物体（或任意物体），Inspector 拖 hexGrid（可留空自动查找）。
/// </summary>
public class HexGroundAudioBridge : MonoBehaviour, IGroundKindProvider
{
    [Tooltip("HexGrid（可留空自动查找）")]
    public HexGrid hexGrid;

    [Tooltip("未分类地形的默认地面材质（当前 terrainType 无语义，全部走这里）")]
    public GroundKind defaultKind = GroundKind.Grass;

    [System.Serializable]
    public class TerrainKindOverride
    {
        [Tooltip("terrainTypeIndex（0~255）")]
        public int terrainTypeIndex;
        public GroundKind kind = GroundKind.Grass;
    }

    [Tooltip("terrainTypeIndex → 地面材质 覆盖表（地形语义定稿后填写）")]
    public List<TerrainKindOverride> overrides = new List<TerrainKindOverride>();

    void OnEnable()
    {
        if (hexGrid == null) hexGrid = FindObjectOfType<HexGrid>();
    }

    void Update()
    {
        // 每帧兜底自动查找（应对 Awake/OnEnable 顺序不定），不覆盖已拖好的引用
        if (hexGrid == null) hexGrid = FindObjectOfType<HexGrid>();
    }

    /// <summary>实现 IGroundKindProvider：返回 worldPoint 所在格的地面材质。</summary>
    public GroundKind GetGroundKind(Vector3 worldPoint)
    {
        if (hexGrid == null)
        {
            return defaultKind;
        }
        Vector3 local = hexGrid.transform.InverseTransformPoint(worldPoint);
        HexCell cell = null;
        try
        {
            cell = hexGrid.GetCell(HexCoordinates.FromPosition(local));
        }
        catch (System.Exception)
        {
            // HexGrid.GetCell 只按 cellCountX/Z 查界、不校验 cells 数组 —— 网格未生成
            //（EditMode / Awake 前）或序列化尺寸与数组不一致时会抛 IndexOutOfRange。
            // Play 中 Awake 会先重建网格故正常，但桥接器是"通用边界"，保证永不抛异常：
            // 查不到格就当默认地面（无声/默认脚步），绝不影响游戏逻辑。
            return defaultKind;
        }
        if (cell == null)
        {
            return defaultKind;
        }
        // 水下优先（水覆盖一切地形色）
        if (cell.IsUnderwater)
        {
            return GroundKind.Water;
        }
        int idx = cell.TerrainTypeIndex;
        if (overrides != null)
        {
            for (int i = 0; i < overrides.Count; i++)
            {
                TerrainKindOverride o = overrides[i];
                if (o != null && o.terrainTypeIndex == idx)
                {
                    return o.kind;
                }
            }
        }
        return defaultKind;
    }
}

