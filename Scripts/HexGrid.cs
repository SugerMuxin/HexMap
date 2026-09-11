
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 寻路（移动）方式——两种模式，可运行时切换。
///
/// 高度语义：`HexCell.Elevation` 是整数层级（世界里每级 = HexMetrics.elevationStep = 5 单位，
/// 中间还有梯田过渡）。相邻两格高度相同 = Flat 边；相差 1 = 坡道；相差 ≥ 2 = 悬崖。
/// </summary>
public enum HexPathMode
{
    /// <summary>
    /// 只在「相同高度」的格之间寻路（默认）：相邻格高度不同一律不可通行——
    /// 不沿坡道上下、也不越过悬崖，即**不支持攀爬 / 跳跃**。
    /// 结果：单位只能在同一个海拔层（连通的高原 / 平地）内活动。
    /// </summary>
    SameElevationOnly = 0,

    /// <summary>
    /// 允许沿坡道上下（相邻落差 1 可通行，成本 slopeMoveCost）；悬崖（落差 ≥ 2）默认仍不可通行。
    /// 想允许跨 2 级以上的"跳跃"，需要额外关掉 HexGrid.blockCliffs（按需求默认不支持）。
    /// </summary>
    AllowSlope = 1
}

public class HexGrid : MonoBehaviour
{

    //public int chunkCountX = 4, chunkCountZ = 3;
    public int cellCountX = 20, cellCountZ = 15;
    //int cellCountX, cellCountZ;

    HexGridChunk[] chunks;
    int chunkCountX, chunkCountZ;
    //public int width = 6;
    //public int height = 6;

    public Color TouchedColor;

    public HexGridChunk chunkPrefab;

    public HexCell cellPrefab;

    public Text cellLabelPrefab;

    public Texture2D noiseSource;

    Canvas gridCanvas;

    HexCell[] cells;

    HexMesh hexMesh;

    public int seed;

    private void Awake()
    {
        HexMetrics.noiseSource = noiseSource;
        HexMetrics.InitializeHashGrid(seed);
        CreateMap(cellCountX, cellCountZ);
    }

    private void OnEnable()
    {
        if (!HexMetrics.noiseSource)
        {
            HexMetrics.noiseSource = noiseSource;
            HexMetrics.InitializeHashGrid(seed);
        }
    }

    public bool CreateMap(int x,int z) {
        if (
            x <= 0 || x % HexMetrics.chunkSizeX != 0 ||
            z <= 0 || z % HexMetrics.chunkSizeZ != 0
        )
        {
            Debug.LogError("Unsupported map size.");
            return false;
        }

        cellCountX = x;
        cellCountZ = z;

        if (chunks != null) {
            for (int i = 0; i < chunks.Length; i++)
            {
                Destroy(chunks[i].gameObject);
            }
        }

        //cellCountX = chunkCountX * HexMetrics.chunkSizeX;
        //cellCountZ = chunkCountZ * HexMetrics.chunkSizeZ;
        chunkCountX = cellCountX / HexMetrics.chunkSizeX;
        chunkCountZ = cellCountZ / HexMetrics.chunkSizeZ;
        CreateChunks();

        CreateCells();
        ClearPath();
        return true;
    }


    void CreateChunks() {
        chunks = new HexGridChunk[chunkCountX * chunkCountZ];

        for (int z = 0,i =0; z < chunkCountZ; z++)
        {
            for (int x = 0; x < chunkCountX; x++)
            {
                HexGridChunk chunk = chunks[i++] = Instantiate<HexGridChunk>(chunkPrefab);
                chunk.transform.SetParent(transform);
            }
        }
    }


    void CreateCells() {
        cells = new HexCell[cellCountX * cellCountZ];

        for (int z = 0, i = 0; z < cellCountZ; z++)
        {
            for (int x = 0; x < cellCountX; x++)
            {
                CreateCell(x, z, i++);
            }
        }
    }

    void CreateCell(int x,int z,int i) {
        Vector3 position;
        position.x = (x+z*0.5f - z/2) * (HexMetrics.innerRadius*2f);
        position.y = 0;
        position.z = z * (HexMetrics.outerRadius*1.5f);

        HexCell cell = cells[i] = Instantiate<HexCell>(cellPrefab);
        //cell.transform.SetParent(transform, false);
        cell.transform.localPosition = position;
        cell.coordinates = HexCoordinates.FromOffsetCoordinates(x, z);

        if (x > 0) {
            cell.SetNeighbor(HexDirection.W, cells[i-1]);
        }
        if (z > 0) {
            if ((z & 1) == 0)
            {
                cell.SetNeighbor(HexDirection.SE, cells[i - cellCountX]);
                if (x > 0)
                {
                    cell.SetNeighbor(HexDirection.SW, cells[i - cellCountX - 1]);
                }
            }
            else {
                cell.SetNeighbor(HexDirection.SW, cells[i - cellCountX]);
                if (x < cellCountX - 1)
                {
                    cell.SetNeighbor(HexDirection.SE, cells[i - cellCountX + 1]);
                }
            }
        }

        Text label = Instantiate<Text>(cellLabelPrefab);
        //label.rectTransform.SetParent(gridCanvas.transform, false);
        label.rectTransform.anchoredPosition = new Vector2(position.x, position.z);
        label.text = cell.coordinates.ToStringOnSeparateLines();

        cell.uiRect = label.rectTransform;

        cell.Elevation = 0;

        AddCellToChunk(x,z,cell);
    }

    void AddCellToChunk(int x,int z,HexCell cell) {
        int chunkX = x / HexMetrics.chunkSizeX;
        int chunkZ = z / HexMetrics.chunkSizeZ;
        HexGridChunk chunk = chunks[chunkX + chunkZ * chunkCountX];

        int localX = x - chunkX * HexMetrics.chunkSizeX;
        int localZ = z - chunkZ * HexMetrics.chunkSizeZ;
        chunk.AddCell(localX + localZ * HexMetrics.chunkSizeX, cell);
    }



    public HexCell GetCell(Vector3 position) {
        position = transform.InverseTransformPoint(position);
        HexCoordinates coordinates = HexCoordinates.FromPosition(position);
        Debug.Log("Touched at " + coordinates.ToString());
        int index = coordinates.X + coordinates.Z * cellCountX + coordinates.Z / 2;
        HexCell cell = cells[index];
        return cell;
    }

    public HexCell GetCell(HexCoordinates coordinates)
    {
        if (coordinates == null || cells == null) return null;   // 地图未生成时（EditMode / Awake 前）安全返回
        int z = coordinates.Z;
        if(z<0||z>=cellCountZ)
        {
            return null;
        }
        int x = coordinates.X + z / 2;
        if (x < 0 || x >= cellCountX)
        {
            return null;
        }
        return cells[x + z * cellCountX];
    }

    public void ShowUI(bool visible)
    {
        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i].ShowUI(visible);
        }
    }

    /// <summary>刷新所有 chunk（特征密度切换等全局变化时调用）。</summary>
    public void RefreshAll()
    {
        if (chunks == null) return;
        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i].Refresh();
        }
    }


    #region Pathfinding (FEAT-001)

    [Header("寻路（FEAT-001）")]
    [Tooltip("可通行地形（下标 = terrainTypeIndex：0沙 1草 2泥 3石 4雪）。默认陆地全通行；若按 PvZ 玩法限制「仅草/泥可行走」，用菜单 Tools/寻路系统/应用 PvZ 通行预设")]
    public bool[] walkableTerrain = { true, true, true, true, true };
    [Tooltip("水下格不可通行")]
    public bool blockUnderwater = true;
    [Tooltip("寻路模式：SameElevationOnly = 只在相同高度的格之间走（不支持攀爬/跳跃，默认）；AllowSlope = 允许沿坡道上下（落差 1），悬崖仍不可通行")]
    public HexPathMode pathMode = HexPathMode.SameElevationOnly;
    [Tooltip("悬崖（相邻落差 ≥ 2，即需要「跳跃」）不可通行。仅在 pathMode = AllowSlope 时才有意义；SameElevationOnly 下任何高度差都不可通行")]
    public bool blockCliffs = true;
    [Tooltip("平地移动成本（>0）")]
    public int flatMoveCost = 5;
    [Tooltip("坡道移动成本（>0）")]
    public int slopeMoveCost = 10;
    [Tooltip("启发式权重：越大搜索越快。超过最小移动成本就不再保证最短路径，故代码会 clamp 到 ≤ 最小移动成本（始终最优）")]
    public int heuristicWeight = 5;

    /// <summary>PvZ 玩法预设：只有草 / 泥可行走（fix-plan §2.3.1 的简化规则；.map 存档格式不变）。</summary>
    public static readonly bool[] PvZWalkableTerrain = { false, true, true, false, false };

    /// <summary>
    /// 额外的通行性过滤（AI / 玩法层挂钩，如"该格已被单位占用"、"该格被封锁"）。
    /// 所有过滤器都返回 true 才可通行；不注册时对既有行为零影响。
    /// </summary>
    public delegate bool WalkabilityFilter(HexCell cell);

    readonly List<WalkabilityFilter> walkabilityFilters = new List<WalkabilityFilter>();
    readonly HexCellPriorityQueue searchFrontier = new HexCellPriorityQueue();

    HexCell currentPathFrom, currentPathTo;
    bool currentPathExists;
    int lastPathCost;

    /// <summary>异常路径保护上限（正常地图不可能触及）。</summary>
    const int pathGuardLimit = 100000;

    public HexCell[] Cells { get { return cells; } }
    public int CellCountX { get { return cellCountX; } }
    public int CellCountZ { get { return cellCountZ; } }

    /// <summary>上一次 FindPath 是否找到路径。</summary>
    public bool HasPath { get { return currentPathExists; } }
    public HexCell PathFrom { get { return currentPathFrom; } }
    public HexCell PathTo { get { return currentPathTo; } }
    /// <summary>上一次 FindPath 的路径总移动成本（注意 ≠ 格数：平地 5 / 坡道 10）。</summary>
    public int LastPathCost { get { return lastPathCost; } }

    // ---------------- 通行性判定 ----------------

    /// <summary>地形本身是否可通行（不含水下与过滤器判定）。</summary>
    public bool IsTerrainWalkable(int terrainTypeIndex)
    {
        if (walkableTerrain == null) return true;
        if (terrainTypeIndex < 0 || terrainTypeIndex >= walkableTerrain.Length) return true;  // 未知地形不阻塞寻路
        return walkableTerrain[terrainTypeIndex];
    }

    /// <summary>
    /// 「格」是否可通行（寻路基本判定）：非空 + 地形可通行 + 非水下（可关）+ 通过所有过滤器。
    /// 「边」级判定（悬崖阻挡 / 移动成本）见 TryGetMoveCost。
    /// </summary>
    public bool IsWalkable(HexCell cell)
    {
        if (cell == null) return false;
        if (blockUnderwater && cell.IsUnderwater) return false;
        if (!IsTerrainWalkable(cell.TerrainTypeIndex)) return false;
        for (int i = 0; i < walkabilityFilters.Count; i++)
        {
            WalkabilityFilter filter = walkabilityFilters[i];
            if (filter != null && !filter(cell)) return false;
        }
        return true;
    }

    public void AddWalkabilityFilter(WalkabilityFilter filter)
    {
        if (filter == null || walkabilityFilters.Contains(filter)) return;
        walkabilityFilters.Add(filter);
    }

    public void RemoveWalkabilityFilter(WalkabilityFilter filter)
    {
        if (filter == null) return;
        walkabilityFilters.Remove(filter);
    }

    public void ClearWalkabilityFilters()
    {
        walkabilityFilters.Clear();
    }

    /// <summary>两格是否在同一高度（SameElevationOnly 模式唯一可通行条件）。</summary>
    public static bool IsSameElevation(HexCell a, HexCell b)
    {
        return a != null && b != null && a.Elevation == b.Elevation;
    }

    /// <summary>从 from 跨到相邻格 neighbor 是否允许，并给出成本（按本图当前 pathMode）。</summary>
    public bool TryGetMoveCost(HexCell from, HexCell neighbor, HexDirection direction, out int moveCost)
    {
        return TryGetMoveCost(from, neighbor, direction, pathMode, out moveCost);
    }

    /// <summary>
    /// 从 from 跨到相邻格 neighbor 是否允许，并给出成本。
    ///
    /// 规则（两种模式）：
    ///   - 高度相同（Flat 边）：可通行，成本 flatMoveCost —— 两种模式一致。
    ///   - SameElevationOnly：**任何高度差都不可通行**（不爬上坡、不越过悬崖、也不跳下）= 不支持攀爬/跳跃。
    ///   - AllowSlope：落差 1（坡道）可通行，成本 slopeMoveCost；落差 ≥ 2（悬崖 = 需要跳跃）
    ///     在 blockCliffs 打开时不可通行（默认打开，符合"不支持跳跃"）。
    /// </summary>
    public bool TryGetMoveCost(HexCell from, HexCell neighbor, HexDirection direction,
        HexPathMode mode, out int moveCost)
    {
        moveCost = 0;
        if (from == null || neighbor == null) return false;
        if (!IsWalkable(neighbor)) return false;

        HexEdgeType edge = from.GetEdgeType(neighbor);

        if (edge == HexEdgeType.Flat)
        {
            moveCost = Mathf.Max(1, flatMoveCost);
            return true;
        }

        if (mode == HexPathMode.SameElevationOnly) return false;   // 高度不同：不支持上/下（含坡道与悬崖）

        if (edge == HexEdgeType.Cliff && blockCliffs) return false; // 落差 ≥ 2 = 跳跃，默认不支持
        moveCost = Mathf.Max(1, slopeMoveCost);
        return true;
    }

    /// <summary>实际生效的启发式权重：clamp 到 ≤ 最小移动成本 → 启发式可采纳 → A* 必得最短路径。</summary>
    public int EffectiveHeuristicWeight()
    {
        int minCost = Mathf.Max(1, Mathf.Min(flatMoveCost, slopeMoveCost));
        return Mathf.Clamp(heuristicWeight, 0, minCost);
    }

    /// <summary>应用 PvZ 通行预设（仅草 / 泥可行走）。</summary>
    public void ApplyPvZWalkablePreset()
    {
        walkableTerrain = new bool[] { false, true, true, false, false };
    }

    /// <summary>恢复默认通行预设（0沙 1草 2泥 3石 4雪 全部陆地可通行）。</summary>
    public void ApplyDefaultWalkablePreset()
    {
        walkableTerrain = new bool[] { true, true, true, true, true };
    }

    // ---------------- 寻路 ----------------

    /// <summary>
    /// A* 寻路（同步；实时版——没有回合制 speed 预算，成本只累加移动成本）。
    /// 找到路径返回 true，之后用 GetPath() 取「起点→终点」格列表；无路返回 false。
    /// 起点允许不可通行（单位可能站在水里 / 被围住，允许它先走出来），终点必须可通行。
    /// </summary>
    public bool FindPath(HexCell fromCell, HexCell toCell)
    {
        return FindPath(fromCell, toCell, pathMode);
    }

    /// <summary>按指定模式寻路（不改本图的 pathMode）——单位可自带移动方式（如飞行/地面怪）。</summary>
    public bool FindPath(HexCell fromCell, HexCell toCell, HexPathMode mode)
    {
        return Search(fromCell, toCell, EffectiveHeuristicWeight(), mode);
    }

    /// <summary>便捷版：直接返回路径格列表（起点→终点，含两端）；无路返回 null。</summary>
    public List<HexCell> FindPathList(HexCell fromCell, HexCell toCell)
    {
        return FindPathList(fromCell, toCell, pathMode);
    }

    /// <summary>便捷版（指定模式）。</summary>
    public List<HexCell> FindPathList(HexCell fromCell, HexCell toCell, HexPathMode mode)
    {
        return FindPath(fromCell, toCell, mode) ? GetPath() : null;
    }

    /// <summary>
    /// 距离场 / 流场：从 origin 做全场搜索（启发式 0 = Dijkstra），把所有可达格的
    /// HexCell.Distance 填成累计移动成本（不可达保持 int.MaxValue），返回可达格数。
    /// 供 AI 用：多个单位共用一张距离场，比每个单位各跑一次 A* 便宜。
    /// </summary>
    public int ComputeDistanceField(HexCell origin)
    {
        return ComputeDistanceField(origin, pathMode);
    }

    /// <summary>距离场（指定模式）。</summary>
    public int ComputeDistanceField(HexCell origin, HexPathMode mode)
    {
        Search(origin, null, 0, mode);
        int reachable = 0;
        if (cells != null)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != null && cells[i].Distance != int.MaxValue) reachable++;
            }
        }
        return reachable;
    }

    /// <summary>
    /// 核心搜索（Catlike HexMap Part16 §2~4 的 A* + 优先级队列；toCell == null 时退化为全场距离场）。
    /// 会先重置所有格的搜索临时数据，所以任意时刻只保留最近一次搜索的结果。
    /// </summary>
    bool Search(HexCell fromCell, HexCell toCell, int heuristicWeight, HexPathMode mode)
    {
        ClearPath();
        if (fromCell == null || cells == null) return false;
        if (toCell != null && !IsWalkable(toCell)) return false;

        if (fromCell == toCell)
        {
            currentPathFrom = fromCell;
            currentPathTo = toCell;
            currentPathExists = true;
            lastPathCost = 0;
            return true;
        }

        fromCell.Distance = 0;
        searchFrontier.Enqueue(fromCell);

        int guard = 0;
        while (searchFrontier.Count > 0)
        {
            if (++guard > pathGuardLimit) break;      // 异常保护（正常地图不可能触及）
            HexCell current = searchFrontier.Dequeue();
            if (current == null) break;

            if (current == toCell)
            {
                currentPathFrom = fromCell;
                currentPathTo = toCell;
                currentPathExists = true;
                lastPathCost = toCell.Distance;
                return true;
            }

            for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
            {
                HexCell neighbor = current.GetNeighborSafe(d);
                int moveCost;
                if (!TryGetMoveCost(current, neighbor, d, mode, out moveCost)) continue;

                int distance = current.Distance + moveCost;
                if (neighbor.Distance == int.MaxValue)
                {
                    neighbor.Distance = distance;
                    neighbor.PathFrom = current;
                    if (toCell != null && heuristicWeight > 0 &&
                        neighbor.coordinates != null && toCell.coordinates != null)
                    {
                        neighbor.SearchHeuristic =
                            neighbor.coordinates.DistanceTo(toCell.coordinates) * heuristicWeight;
                    }
                    searchFrontier.Enqueue(neighbor);
                }
                else if (distance < neighbor.Distance)
                {
                    int oldPriority = neighbor.SearchPriority;
                    neighbor.Distance = distance;
                    neighbor.PathFrom = current;
                    searchFrontier.Change(neighbor, oldPriority);
                }
            }
        }
        return false;
    }

    /// <summary>当前路径（起点→终点，含两端）；无路径返回 null。</summary>
    public List<HexCell> GetPath()
    {
        if (!currentPathExists) return null;
        return GetPath(new List<HexCell>());
    }

    /// <summary>把当前路径写入 result（复用列表避免 GC），返回同一个列表。</summary>
    public List<HexCell> GetPath(List<HexCell> result)
    {
        if (result == null) result = new List<HexCell>();
        result.Clear();
        if (!currentPathExists || currentPathFrom == null || currentPathTo == null) return result;

        int guard = 0;
        for (HexCell c = currentPathTo; c != null && c != currentPathFrom; c = c.PathFrom)
        {
            result.Add(c);
            if (++guard > pathGuardLimit) break;
        }
        result.Add(currentPathFrom);
        result.Reverse();
        return result;
    }

    /// <summary>清空路径与所有格的搜索临时数据（重建地图 / Load 后自动调用）。</summary>
    public void ClearPath()
    {
        currentPathExists = false;
        currentPathFrom = null;
        currentPathTo = null;
        lastPathCost = 0;
        if (searchFrontier != null) searchFrontier.Clear();
        if (cells == null) return;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null) cells[i].ResetSearchData();
        }
    }

    /// <summary>
    /// 「为什么没路」的中文原因（UI 提示 / 日志 / AI 决策用）。必须在 FindPath 返回 false 之后调用。
    /// 按本图当前 pathMode 判断；用显式模式寻路时请用下面的 mode 重载（否则原因与查询模式不一致）。
    /// </summary>
    public string DescribePathFailure(HexCell fromCell, HexCell toCell)
    {
        return DescribePathFailure(fromCell, toCell, pathMode);
    }

    /// <summary>「为什么没路」的中文原因（按指定模式判断）。</summary>
    public string DescribePathFailure(HexCell fromCell, HexCell toCell, HexPathMode mode)
    {
        if (cells == null) return "地图尚未生成";
        if (fromCell == null) return "起点无效（单位不在任何格上）";
        if (toCell == null) return "终点无效";
        if (toCell == fromCell) return "起点即终点";
        if (!IsWalkable(toCell)) return "终点格不可通行（地形/水下/被占用）";
        if (mode == HexPathMode.SameElevationOnly)
        {
            if (toCell.Elevation != fromCell.Elevation)
            {
                return "起点与终点不在同一高度（" + fromCell.Elevation + " vs " + toCell.Elevation +
                    "）——当前为「仅同高度」寻路，不支持攀爬/跳跃";
            }
            int blockedLevel;
            if (IsBlockedByElevationStep(fromCell, out blockedLevel))
            {
                return "被高度差阻断：起点所在的 " + fromCell.Elevation + " 层连通区旁边就是 " +
                    blockedLevel + " 层——当前为「仅同高度」寻路，不支持攀爬/跳跃";
            }
        }
        else if (blockCliffs && IsReachableIgnoringCliffs(fromCell, toCell))
        {
            return "被悬崖阻断（相邻落差 ≥ 2，需要跳跃）——当前不允许跳跃";
        }
        return "无可通行路径（被水 / 悬崖 / 不可通行地形或障碍隔断）";
    }

    readonly Queue<HexCell> elevationProbeQueue = new Queue<HexCell>();
    readonly HashSet<HexCell> elevationProbeVisited = new HashSet<HexCell>();

    /// <summary>
    /// （仅用于诊断）把"高度差（含悬崖）也当作可走"来试探 fromCell 能否到达 toCell——
    /// 用来判断这次"没路"是不是被高度差 / 悬崖挡住的。用独立队列，不改动任何格的搜索数据。
    /// </summary>
    public bool IsReachableIgnoringCliffs(HexCell fromCell, HexCell toCell)
    {
        if (fromCell == null || toCell == null) return false;
        if (fromCell == toCell) return true;

        elevationProbeQueue.Clear();
        elevationProbeVisited.Clear();
        elevationProbeQueue.Enqueue(fromCell);
        elevationProbeVisited.Add(fromCell);

        int guard = 0;
        while (elevationProbeQueue.Count > 0)
        {
            if (++guard > pathGuardLimit) break;
            HexCell cell = elevationProbeQueue.Dequeue();
            for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
            {
                HexCell neighbor = cell.GetNeighborSafe(d);
                if (neighbor == null || !IsWalkable(neighbor)) continue;   // 仍然只走"可站立的格"
                if (neighbor == toCell) return true;
                if (elevationProbeVisited.Add(neighbor)) elevationProbeQueue.Enqueue(neighbor);
            }
        }
        return false;
    }

    /// <summary>
    /// （仅用于诊断）从 fromCell 沿"同高度可通行"扩散，看连通区旁边是否存在**只差高度**就能进入的可通行格。
    /// 即：严格模式下这次"没路"是不是被高度差挡住的。用独立队列，不改动任何格的搜索数据。
    /// </summary>
    public bool IsBlockedByElevationStep(HexCell fromCell, out int blockedElevation)
    {
        blockedElevation = int.MinValue;
        if (fromCell == null) return false;

        elevationProbeQueue.Clear();
        elevationProbeVisited.Clear();
        elevationProbeQueue.Enqueue(fromCell);
        elevationProbeVisited.Add(fromCell);

        int guard = 0;
        while (elevationProbeQueue.Count > 0)
        {
            if (++guard > pathGuardLimit) break;
            HexCell cell = elevationProbeQueue.Dequeue();
            for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
            {
                HexCell neighbor = cell.GetNeighborSafe(d);
                if (neighbor == null || !IsWalkable(neighbor)) continue;

                if (neighbor.Elevation != cell.Elevation)
                {
                    blockedElevation = neighbor.Elevation;   // 旁边就是别的海拔层（走不过去）
                    return true;
                }
                if (elevationProbeVisited.Add(neighbor)) elevationProbeQueue.Enqueue(neighbor);
            }
        }
        return false;
    }

    /// <summary>直线六边形距离（忽略障碍）；带障碍的最短路径成本见 FindPath 后的 LastPathCost。</summary>
    public int GetHexDistance(HexCell fromCell, HexCell toCell)
    {
        if (fromCell == null || toCell == null ||
            fromCell.coordinates == null || toCell.coordinates == null)
        {
            return -1;
        }
        return fromCell.coordinates.DistanceTo(toCell.coordinates);
    }

    /// <summary>世界坐标 → 所在格（越界 / 未建图返回 null，不抛异常、不打日志）。</summary>
    public HexCell GetCellAtWorld(Vector3 worldPosition)
    {
        if (cells == null) return null;
        try
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return GetCell(HexCoordinates.FromPosition(local));
        }
        catch (System.Exception)
        {
            return null;   // HexGrid.GetCell 不校验 cells 数组越界
        }
    }

    /// <summary>offset 坐标（编辑器里那种 x/z）→ 格；越界返回 null。</summary>
    public HexCell GetCellByOffset(int x, int z)
    {
        return GetCell(HexCoordinates.FromOffsetCoordinates(x, z));
    }

    /// <summary>
    /// 找离 nearTo 最近的可通行格（终点格被占用 / 不可通行时的兜底，例如僵尸要去的位置被植物占住）。
    /// maxHexDistance &lt; 0 表示不限；找不到返回 null。
    /// SameElevationOnly 模式下**只在 nearTo 同高度的格里找**（否则给一个走不到的格没有意义）。
    /// </summary>
    public HexCell FindNearestWalkable(HexCell nearTo, int maxHexDistance = -1)
    {
        return FindNearestWalkable(nearTo, maxHexDistance, pathMode);
    }

    /// <summary>找离 nearTo 最近的可通行格（指定模式）。</summary>
    public HexCell FindNearestWalkable(HexCell nearTo, int maxHexDistance, HexPathMode mode)
    {
        return FindNearestWalkableCore(nearTo, int.MinValue, maxHexDistance, mode);
    }

    /// <summary>
    /// 找离 nearTo 最近、且**位于指定高度层**的可通行格。
    /// AI 常用：目标格被占 / 在别的海拔上时，改去"同一层里最近的可走格"。
    /// </summary>
    public HexCell FindNearestWalkableAtElevation(HexCell nearTo, int elevation, int maxHexDistance = -1)
    {
        return FindNearestWalkableCore(nearTo, elevation, maxHexDistance, pathMode);
    }

    // requiredElevation = int.MinValue 表示"按模式的默认语义"（严格模式 = 与 nearTo 同高度）
    HexCell FindNearestWalkableCore(HexCell nearTo, int requiredElevation, int maxHexDistance, HexPathMode mode)
    {
        if (cells == null) return null;
        if (requiredElevation == int.MinValue)
        {
            if (mode != HexPathMode.SameElevationOnly) requiredElevation = -1;   // 不限高度
            else if (nearTo != null) requiredElevation = nearTo.Elevation;
        }

        HexCell best = null;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < cells.Length; i++)
        {
            HexCell cell = cells[i];
            if (cell == null || !IsWalkable(cell)) continue;
            if (requiredElevation >= 0 && cell.Elevation != requiredElevation) continue;

            int d = 0;
            if (nearTo != null && nearTo.coordinates != null && cell.coordinates != null)
            {
                d = cell.coordinates.DistanceTo(nearTo.coordinates);
            }
            if (maxHexDistance >= 0 && d > maxHexDistance) continue;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = cell;
            }
        }
        return best;
    }

    #endregion

    #region Data
    public void Save(BinaryWriter writer)
    {
        writer.Write(cellCountX);
        writer.Write(cellCountZ);
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].Save(writer);
        }
    }

    public void Load(BinaryReader reader,int header)
    {

        int x = 20, z = 15;
        if (header >= 1)
        {
            x = reader.ReadInt32();
            z = reader.ReadInt32();
        }
        if (x != cellCountX || z != cellCountZ)  //如果地图刚好相同，则无需重新创建了//
        {
            if (!CreateMap(x, z))
            {
                return;
            }
        }

        // 老地图兼容：按文件剩余字节数自适应每格格式
        //   7B 最老(无 urban/farm/plant) / 8B 中间(含 urban) / 10B 旧(含 urban/farm/plant) / 11B 当前(+monsterLair)
        // 避免历史存档（hdr=1 无 urban、或 Net 下发 hdr=1 但 11B/格）解析错位。
        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        long perCell = cells.Length > 0 ? remaining / cells.Length : 11;
        int cellHeader;
        if (perCell >= 11) cellHeader = 3;
        else if (perCell >= 10) cellHeader = 2;
        else if (perCell == 8) cellHeader = 1;
        else cellHeader = 0;

        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].Load(reader, cellHeader);
        }
        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i].Refresh();
        }
        ClearPath();
    }

    #endregion
}

