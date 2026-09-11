
using System.IO;
using Unity.VisualScripting;
using UnityEngine;

public class HexCell : MonoBehaviour
{

    public HexCoordinates coordinates;

    public RectTransform uiRect;

    public HexGridChunk chunk;
    //Color _color;
    int terrainTypeIndex;

    public int TerrainTypeIndex
    {
        get
        {
            return terrainTypeIndex;
        }
        set
        {
            if (terrainTypeIndex != value)
            {
                terrainTypeIndex = value;
                Refresh();
            }
        }
    }

    [SerializeField]
    HexCell[] neighbors;

    private int elevation = int.MinValue;

    public int Elevation
    {
        get
        {
            return elevation;
        }
        set
        {
            if (elevation == value)
                return;
            elevation = value;

            RefreshPosition();

            ValidateRivers();
            Refresh();
        }
    }

    void RefreshPosition() {
        Vector3 position = transform.localPosition;
        position.y = elevation * HexMetrics.elevationStep;
        position.y += (HexMetrics.SampleNoise(position).y * 2f - 1f) *
            HexMetrics.elevationPerturbStrength;
        transform.localPosition = position;

        Vector3 uiPosition = uiRect.localPosition;
        uiPosition.z = elevation * -HexMetrics.elevationStep;
        uiRect.localPosition = uiPosition;
    }

    void ValidateRivers() {
        if (hasOutgoingRiver && elevation < GetNeighbor(outgoingRiver).elevation)
        {
            RemoveOutgoingRiver();
        }
        if (hasIncomingRiver && elevation > GetNeighbor(incomingRiver).elevation)
        {
            RemoveIncomingRiver();
        }
    }


    public Vector3 Position
    {
        get
        {
            return transform.localPosition;
        }
    }

    public float RiverSurfaceY
    {
        get
        {
            return
                (elevation + HexMetrics.waterElevationOffset) * HexMetrics.elevationStep;
        }
    }

    public float WaterSurfaceY
    {
        get
        {
            return
                (waterLevel + HexMetrics.waterElevationOffset) * HexMetrics.elevationStep;
        }
    }

    int urbanLevel = 0;
    int farmLevel = 0;
    int plantLevel = 0;
    int monsterLairLevel = 0;

    public int UrbanLevel {
        get {
            return urbanLevel;
        }
        set {
            if (urbanLevel !=value) { 
                urbanLevel = value;
                RefreshSelfOnly();
            }
        }
    }

    public int FarmLevel {
        get {
            return farmLevel;
        }
        set {
            if (farmLevel != value) {
                farmLevel = value;
                RefreshSelfOnly();
            }
        }
    }

    public int PlantLevel {
        get {
            return plantLevel;
        }
        set {
            if (plantLevel != value) {
                plantLevel = value;
                RefreshSelfOnly();
            }
        }
    }

    /// <summary>
    /// 怪物巢穴等级：0 = 无；1..n 对应 HexFeatureManager.monsterLairPrefabs 的样式索引。
    /// 放置点语义（僵尸巢穴等）。
    /// </summary>
    public int MonsterLairLevel {
        get {
            return monsterLairLevel;
        }
        set {
            if (monsterLairLevel != value) {
                monsterLairLevel = value;
                RefreshSelfOnly();
            }
        }
    }



    public HexCell GetNeighbor(HexDirection direction) { 
        return neighbors[(int)direction];
    }

    public void SetNeighbor(HexDirection direction, HexCell cell)
    {
        neighbors[(int)direction] = cell;
        cell.neighbors[(int)direction.Opposite()] = this;
    }

    /// <summary>
    /// 取邻居（防御式）：neighbors 未初始化 / 下标越界时返回 null 而不是抛异常。
    /// 寻路等"数据可不可信都要能跑"的场景用这个；固定流程仍可用 GetNeighbor。
    /// </summary>
    public HexCell GetNeighborSafe(HexDirection direction)
    {
        if (neighbors == null) return null;
        int i = (int)direction;
        if (i < 0 || i >= neighbors.Length) return null;
        return neighbors[i];
    }

    public HexEdgeType GetEdgeType(HexDirection direction)
    {
        return HexMetrics.GetEdgeType(elevation, neighbors[(int)direction].elevation);
    }

    public HexEdgeType GetEdgeType(HexCell otherCell)
    {
        return HexMetrics.GetEdgeType(
            elevation, otherCell.elevation
        );
    }

    void Refresh(){
        if (chunk) {
            chunk.Refresh();
            for (int i = 0; i < neighbors.Length; i++) {
                HexCell neighbor = neighbors[i];
                if (neighbor != null && neighbor.chunk != chunk)
                {
                    neighbor.chunk.Refresh();
                }
            }
        }
    }


    #region River About

    public float StreamBedY
    {
        get
        {
            return (elevation + HexMetrics.streamBedElevationOffset) * HexMetrics.elevationStep;
        }
    }


    bool hasIncomingRiver, hasOutgoingRiver;

    HexDirection incomingRiver, outgoingRiver;

    public bool HasIncomingRiver
    {
        get
        {
            return hasIncomingRiver;
        }
    }

    public bool HasOutgoingRiver
    {
        get
        {
            return hasOutgoingRiver;
        }
    }

    public HexDirection IncomingRiver
    {
        get
        {
            return incomingRiver;
        }
    }

    public HexDirection OutgoingRiver
    {
        get
        {
            return outgoingRiver;
        }
    }

    public bool HasRiver
    {
        get
        {
            return hasIncomingRiver || hasOutgoingRiver;
        }
    } 


    public bool HasRiverBeginOrEnd
    {
        get
        {
            return hasIncomingRiver != hasOutgoingRiver;
        }
    }

    public bool HasRiverThroughEdge(HexDirection direction)
    {
        return hasIncomingRiver && incomingRiver == direction ||
            hasOutgoingRiver && outgoingRiver == direction;
    }


    public void RemoveIncomingRiver()
    {
        if (!hasIncomingRiver)
        {
            return;
        }
        hasIncomingRiver = false;
        RefreshSelfOnly();

        HexCell neighbor = GetNeighbor(incomingRiver);
        if ((neighbor))
        {
            neighbor.hasOutgoingRiver = false;
            neighbor.RefreshSelfOnly();
        }

    }

    public void RemoveOutgoingRiver()
    {
        if (!hasOutgoingRiver)
        {
            return;
        }
        hasOutgoingRiver = false;
        RefreshSelfOnly();

        HexCell neighbor = GetNeighbor(outgoingRiver);
        if (neighbor != null)
        {
            neighbor.hasIncomingRiver = false;
            neighbor.RefreshSelfOnly();
        }
    }

    public void RefreshSelfOnly() {
        chunk.Refresh();
    }

    public void RemoveRiver() {
        RemoveIncomingRiver();
        RemoveOutgoingRiver();
    }

    public void SetOutgoingRiver(HexDirection direction) {
        if (hasOutgoingRiver && outgoingRiver == direction) {
            return;
        }
        HexCell neighbor = GetNeighbor(direction);
        if(!neighbor || elevation < neighbor.elevation)
        {
            return;
        }
        RemoveOutgoingRiver();
        if (hasIncomingRiver && incomingRiver == direction) {
            RemoveIncomingRiver();
        }

        hasOutgoingRiver = true;
        outgoingRiver = direction;
        RefreshSelfOnly();

        neighbor.RemoveIncomingRiver();
        neighbor.hasIncomingRiver = true;
        neighbor.incomingRiver = direction.Opposite();
        neighbor.RefreshSelfOnly();
    }
    #endregion

    #region Roads


    public bool HasRoads
    {
        get {
            return false;
        }
    }

    #endregion

    #region Water Area
    int waterLevel;

    public int WaterLevel
    {
        get {
            return waterLevel;
        }
        set {
            if (waterLevel == value) {
                return;
            }
            waterLevel = value;
            Refresh();
        }
    }

    public bool IsUnderwater
    {
        get {
            return waterLevel > elevation;
        }
    }



    #endregion

    #region PathfindingSearch (FEAT-001 寻路搜索临时数据)

    // 说明：寻路过程中的临时数据一律用「私有字段 + 公开属性」——Unity 只序列化公开字段，
    // 所以这些数据既不进 .map 存档（Save/Load 也没写它们），也不进场景 / prefab 序列化。
    // 仅在 HexGrid 完成一次搜索后有意义；HexGrid 每次搜索前会把所有格重置一遍。

    [System.NonSerialized] int searchDistance = int.MaxValue;
    [System.NonSerialized] int searchHeuristic;
    [System.NonSerialized] HexCell searchPathFrom;
    [System.NonSerialized] HexCell searchNextWithSamePriority;

    /// <summary>从搜索起点走到本格的累计移动成本（不可达 = int.MaxValue）。仅供寻路内部/调试读取。</summary>
    public int Distance
    {
        get { return searchDistance; }
        set { searchDistance = value; }
    }

    /// <summary>到目标的直线六边形距离 × 权重（A* 启发式；Dijkstra 式全场搜索时为 0）。</summary>
    public int SearchHeuristic
    {
        get { return searchHeuristic; }
        set { searchHeuristic = value; }
    }

    /// <summary>A* 排序键 = 累计成本 + 启发式（越小越先被展开）。</summary>
    public int SearchPriority
    {
        get { return searchDistance + searchHeuristic; }
    }

    /// <summary>路径回溯链：到达本格的前一格（搜索起点为 null）。</summary>
    public HexCell PathFrom
    {
        get { return searchPathFrom; }
        set { searchPathFrom = value; }
    }

    /// <summary>同优先级链表的下一个格（HexCellPriorityQueue 内部使用）。</summary>
    public HexCell NextWithSamePriority
    {
        get { return searchNextWithSamePriority; }
        set { searchNextWithSamePriority = value; }
    }

    /// <summary>清空本格的搜索临时数据（HexGrid 每次搜索开始 / 地图重建时调用）。</summary>
    public void ResetSearchData()
    {
        searchDistance = int.MaxValue;
        searchHeuristic = 0;
        searchPathFrom = null;
        searchNextWithSamePriority = null;
    }

    #endregion

    #region Data

    public void Save(BinaryWriter writer) {
        writer.Write((byte)terrainTypeIndex);
        writer.Write((byte)elevation);
        writer.Write((byte)waterLevel);
        //writer.Write((byte)urbanLevel);
        //writer.Write((byte)farmLevel);
        //writer.Write((byte)plantLevel);
        //writer.Write((byte)specialIndex);
        //writer.Write(walled);

        writer.Write(hasIncomingRiver);
        writer.Write((byte)incomingRiver);
        writer.Write(hasOutgoingRiver);
        writer.Write((byte)outgoingRiver);
        writer.Write((byte)urbanLevel);
        writer.Write((byte)farmLevel);
        writer.Write((byte)plantLevel);
        writer.Write((byte)monsterLairLevel);
        /*for (int i = 0; i < roads.Length; i++)
        {
            writer.Write(roads[i]);
        }*/
    }

    public void Load(BinaryReader reader, int header) {
        terrainTypeIndex = reader.ReadByte();
        elevation = reader.ReadByte();
        waterLevel = reader.ReadByte();

        hasIncomingRiver = reader.ReadBoolean();
        incomingRiver = (HexDirection)reader.ReadByte();

        hasOutgoingRiver = reader.ReadBoolean();
        outgoingRiver = (HexDirection)reader.ReadByte();
        // 每格字节数由 HexGrid.Load 按文件剩余字节探测后归一为 header 语义：
        //   header<=0 → 7B/格 最老格式（无 urban/farm/plant，本教程 Part14 前）
        //   header==1 → 8B/格 中间格式（含 urban）
        //   header==2 → 10B/格 旧格式（含 urban/farm/plant）
        //   header>=3 → 11B/格 当前格式（含 urban/farm/plant + monsterLair）
        if (header >= 3)
        {
            urbanLevel = (int)reader.ReadByte();
            farmLevel = (int)reader.ReadByte();
            plantLevel = (int)reader.ReadByte();
            monsterLairLevel = (int)reader.ReadByte();
        }
        else if (header == 2)
        {
            urbanLevel = (int)reader.ReadByte();
            farmLevel = (int)reader.ReadByte();
            plantLevel = (int)reader.ReadByte();
            monsterLairLevel = 0;
        }
        else if (header == 1)
        {
            urbanLevel = (int)reader.ReadByte();
            farmLevel = 0;
            plantLevel = 0;
            monsterLairLevel = 0;
        }
        else
        {
            urbanLevel = 0;
            farmLevel = 0;
            plantLevel = 0;
            monsterLairLevel = 0;
        }
        RefreshPosition();
    }

    #endregion

}

