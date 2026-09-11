using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>演示单位的起点取法。</summary>
public enum PathStartMode
{
    /// <summary>怪物巢穴格（MonsterLairLevel &gt; 0，FEAT-002 出怪点的语义）；没有巢穴时退化为西边缘。</summary>
    MonsterLair = 0,
    /// <summary>地图西边缘中间格。</summary>
    WestEdge = 1,
    /// <summary>Inspector 里填的 offset 坐标。</summary>
    OffsetCoordinates = 2
}

/// <summary>演示单位的终点取法。</summary>
public enum PathTargetMode
{
    /// <summary>地图东边缘中间格（PvZ 里的"基地"方向）。</summary>
    EastEdge = 0,
    /// <summary>Inspector 里填的 offset 坐标。</summary>
    OffsetCoordinates = 1,
    /// <summary>随机一个可通行格。</summary>
    RandomWalkable = 2
}

/// <summary>
/// 寻路演示 / 手动测试入口（FEAT-001 的场景装配件）。
///
/// 职责（只做"把寻路系统接到场景里、看得见、点得动"）：
///  1. 解析起点 / 终点格（巢穴 / 边缘 / 指定 offset 坐标 / 随机可通行格）；
///  2. 调 HexGrid.FindPath + HexUnit.Travel 让单位走过去；
///  3. 用 LineRenderer 画出当前路径（Scene/Game 视图都能看见），单位本体由 HexUnit 的 Gizmo 画路径球；
///  4. 提供 Play 中的手动入口：**鼠标中键**点地面 = 让单位寻路前往该格（中键不与角色点击/种植/地图编辑冲突），
///     P = 重算到终点，O = 换随机终点，G = 显示/隐藏路径线。
///
/// 未来 AI 怪物系统只需复用第 2 步（`hexGrid.FindPath` + `HexUnit.Travel`），
/// 本组件不是 AI 本体，可随时关掉/删除。
/// </summary>
[DisallowMultipleComponent]
public class PathfindingDemo : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("地图（留空自动查找场景里的 HexGrid）")]
    public HexGrid hexGrid;
    [Tooltip("演示单位（留空则运行时用 unitPrefab 生成一个）")]
    public HexUnit unit;
    [Tooltip("演示单位 prefab（unit 为空时使用）")]
    public GameObject unitPrefab;
    [Tooltip("单位实例的父物体（留空自动创建子物体 \"Units Container\"）")]
    public Transform unitContainer;

    [Header("起点 / 终点")]
    public PathStartMode startMode = PathStartMode.MonsterLair;
    public PathTargetMode targetMode = PathTargetMode.EastEdge;
    [Tooltip("startMode = OffsetCoordinates 时用（编辑器里的 x/z）")]
    public Vector2Int startCoordinates = new Vector2Int(0, 7);
    [Tooltip("targetMode = OffsetCoordinates 时用（编辑器里的 x/z）")]
    public Vector2Int targetCoordinates = new Vector2Int(19, 7);

    [Header("运行")]
    [Tooltip("Play 时自动把单位放到起点并出发一次")]
    public bool autoStartOnPlay = true;
    [Tooltip("到达后自动换终点继续走（纯演示；正式 AI 关掉）")]
    public bool loopTravel = true;
    [Tooltip("换终点前的间隔（秒）")]
    public float loopDelay = 1f;

    [Header("交互（Play 中）")]
    [Tooltip("鼠标中键点地面 = 让单位寻路前往该格")]
    public bool middleClickToMove = true;
    [Tooltip("重算到当前终点的按键")]
    public KeyCode repathKey = KeyCode.P;
    [Tooltip("换一个随机可通行终点的按键")]
    public KeyCode randomTargetKey = KeyCode.O;
    [Tooltip("显示/隐藏路径线的按键")]
    public KeyCode toggleLineKey = KeyCode.G;
    [Tooltip("切换寻路模式（仅同高度 ↔ 允许坡道）的按键")]
    public KeyCode toggleModeKey = KeyCode.H;
    [Tooltip("打印寻路结果（格数 / 成本 / 无路原因）")]
    public bool logTravel = true;

    [Header("路径可视化")]
    public bool showPathLine = true;
    public Color pathColor = new Color(1f, 0.85f, 0.2f, 1f);
    public float lineWidth = 0.6f;
    public float lineHeightOffset = 1.2f;

    LineRenderer pathLine;
    HexCell currentTarget;
    float nextLoopTime;
    bool subscribed;

    public HexCell CurrentTarget { get { return currentTarget; } }
    public IList<HexCell> CurrentPath { get { return unit != null ? unit.Path : null; } }

    void Start()
    {
        EnsureRefs();
        Subscribe();
        if (autoStartOnPlay) BeginJourney();
    }

    void OnDisable()
    {
        Unsubscribe();
        if (pathLine != null) pathLine.enabled = false;
    }

    void Update()
    {
        if (hexGrid == null || unit == null) EnsureRefs();
        if (hexGrid == null || unit == null) return;

        if (middleClickToMove) HandleMiddleClick();

        if (Input.GetKeyDown(repathKey))
        {
            BeginJourney(true);
        }
        if (Input.GetKeyDown(randomTargetKey))
        {
            HexCell random = RandomWalkableCell();
            if (random != null) MoveUnitTo(random);
        }
        if (Input.GetKeyDown(toggleLineKey))
        {
            showPathLine = !showPathLine;
            UpdatePathLine();
        }
        if (Input.GetKeyDown(toggleModeKey))
        {
            TogglePathMode();
        }

        if (loopTravel && !unit.isTraveling && Time.time >= nextLoopTime && currentTarget != null)
        {
            HexCell next = RandomWalkableCell();
            if (next == null)
            {
                nextLoopTime = Time.time + Mathf.Max(1f, loopDelay);   // 同高度层里没有别的格，稍后再试
            }
            else
            {
                MoveUnitTo(next);
            }
        }
    }

    // ================= 对外 API（AI / 断言脚本可直接调） =================

    /// <summary>把单位放到起点，然后寻路前往终点。forceRepath = 重新解析终点（否则沿用当前终点）。</summary>
    public bool BeginJourney(bool forceRepath = false)
    {
        EnsureRefs();
        if (hexGrid == null) return false;

        HexCell start = ResolveStartCell();
        if (start == null)
        {
            if (logTravel) Debug.LogWarning("[Pathfinding] 找不到可用起点格（地图未生成？）");
            return false;
        }
        if (unit == null)
        {
            if (logTravel) Debug.LogWarning("[Pathfinding] 没有演示单位（未接线 unit / unitPrefab）");
            return false;
        }

        unit.WarpTo(start);
        HexCell target = (forceRepath || currentTarget == null) ? ResolveTargetCell() : currentTarget;
        if (target == null)
        {
            if (logTravel) Debug.LogWarning("[Pathfinding] 找不到可用终点格");
            return false;
        }
        return MoveUnitTo(target);
    }

    /// <summary>切换寻路模式（同高度 ↔ 允许坡道），并重算到当前终点。返回切换后的模式。</summary>
    public HexPathMode TogglePathMode()
    {
        EnsureRefs();
        if (hexGrid == null) return HexPathMode.SameElevationOnly;
        SetPathMode(hexGrid.pathMode == HexPathMode.SameElevationOnly
            ? HexPathMode.AllowSlope : HexPathMode.SameElevationOnly);
        return hexGrid.pathMode;
    }

    /// <summary>设置寻路模式并重算（终点按新规则重新解析）。</summary>
    public void SetPathMode(HexPathMode mode)
    {
        EnsureRefs();
        if (hexGrid == null) return;
        hexGrid.pathMode = mode;
        if (logTravel)
        {
            Debug.Log("[Pathfinding] 寻路模式 = " + (mode == HexPathMode.SameElevationOnly
                ? "仅同高度（不支持攀爬/跳跃）" : "允许坡道（落差 1；悬崖仍不可通行）"));
        }
        BeginJourney(true);
    }

    /// <summary>让单位寻路前往指定格（找不到路则原地不动并返回 false）。</summary>
    public bool MoveUnitTo(HexCell target)
    {
        EnsureRefs();
        if (hexGrid == null || unit == null || target == null) return false;

        HexCell from = unit.CurrentCell();
        if (from == null)
        {
            unit.WarpTo(target);
            currentTarget = target;
            UpdatePathLine();
            return true;
        }

        bool found = hexGrid.FindPath(from, target, unit.EffectivePathMode());
        if (!found)
        {
            if (logTravel)
            {
                Debug.LogWarning("[Pathfinding] 无可行路径：" + from.coordinates.ToString() + " → " +
                    target.coordinates.ToString() + " —— " + hexGrid.DescribePathFailure(from, target));
            }
            UpdatePathLine();
            return false;
        }

        currentTarget = target;
        nextLoopTime = Time.time + Mathf.Max(0.1f, loopDelay);
        List<HexCell> path = hexGrid.GetPath();
        unit.Travel(path);
        UpdatePathLine();

        if (logTravel)
        {
            Debug.Log("[Pathfinding] 路径 " + path.Count + " 格（成本 " + hexGrid.LastPathCost + "）：" +
                DescribePath(path));
        }
        return true;
    }

    /// <summary>解析起点格。</summary>
    public HexCell ResolveStartCell()
    {
        EnsureRefs();
        if (hexGrid == null) return null;

        switch (startMode)
        {
            case PathStartMode.MonsterLair:
                HexCell lair = FindLairCell();
                if (lair == null) return hexGrid.GetCellByOffset(0, hexGrid.CellCountZ / 2);
                // 巢穴格本身可能不可通行（水下/地形被改）→ 退到同一高度里离它最近的可走格
                return hexGrid.IsWalkable(lair) ? lair : hexGrid.FindNearestWalkable(lair);

            case PathStartMode.OffsetCoordinates:
                HexCell byCoords = hexGrid.GetCellByOffset(startCoordinates.x, startCoordinates.y);
                if (byCoords != null && hexGrid.IsWalkable(byCoords)) return byCoords;
                return hexGrid.FindNearestWalkable(byCoords);

            default:
                return hexGrid.GetCellByOffset(0, hexGrid.CellCountZ / 2);
        }
    }

    /// <summary>解析终点格。</summary>
    public HexCell ResolveTargetCell()
    {
        EnsureRefs();
        if (hexGrid == null) return null;

        int level = CurrentUnitElevation();      // 严格模式下只在同一层里找终点
        switch (targetMode)
        {
            case PathTargetMode.OffsetCoordinates:
                return PickReachable(hexGrid.GetCellByOffset(targetCoordinates.x, targetCoordinates.y), level);

            case PathTargetMode.RandomWalkable:
                return RandomWalkableCell();

            default:
                HexCell east = hexGrid.GetCellByOffset(hexGrid.CellCountX - 1, hexGrid.CellCountZ / 2);
                // 先在东边缘整列里找同一高度的可走格（PvZ 的"基地"就在那一侧）
                if (hexGrid.pathMode == HexPathMode.SameElevationOnly && level != int.MinValue)
                {
                    // 从中间那格开始向两边找（z 优先取 CellCountZ/2）
                    int mid = hexGrid.CellCountZ / 2;
                    for (int k = 0; k < hexGrid.CellCountZ; k++)
                    {
                        int z = (k % 2 == 0) ? mid + k / 2 : mid - (k + 1) / 2;
                        if (z < 0 || z >= hexGrid.CellCountZ) continue;
                        HexCell c = hexGrid.GetCellByOffset(hexGrid.CellCountX - 1, z);
                        if (c != null && c.Elevation == level && hexGrid.IsWalkable(c)) return c;
                    }
                }
                return PickReachable(east, level);
        }
    }

    /// <summary>
    /// 随机一个可通行格。SameElevationOnly 模式下**只在单位当前高度层里随机**
    /// （否则给个走不到的终点毫无意义）；最多尝试 200 次，失败退化为"该层里离地图中心最近的可走格"。
    /// </summary>
    public HexCell RandomWalkableCell()
    {
        EnsureRefs();
        if (hexGrid == null || hexGrid.Cells == null) return null;

        int level = CurrentUnitElevation();
        for (int i = 0; i < 200; i++)
        {
            int x = UnityEngine.Random.Range(0, hexGrid.CellCountX);
            int z = UnityEngine.Random.Range(0, hexGrid.CellCountZ);
            HexCell cell = hexGrid.GetCellByOffset(x, z);
            if (cell == null || !hexGrid.IsWalkable(cell)) continue;
            if (level != int.MinValue && cell.Elevation != level) continue;
            return cell;
        }
        return hexGrid.FindNearestWalkableAtElevation(
            hexGrid.GetCellByOffset(hexGrid.CellCountX / 2, hexGrid.CellCountZ / 2),
            level == int.MinValue ? 0 : level);
    }

    /// <summary>单位当前所在格的高度；单位还没落位（不在任何格上）返回 int.MinValue = 不限高度。</summary>
    public int CurrentUnitElevation()
    {
        if (unit == null) return int.MinValue;
        HexCell cell = unit.CurrentCell();
        return cell != null ? cell.Elevation : int.MinValue;
    }

    /// <summary>把一个候选终点变成"从当前单位位置真能走到的格"：不可通行 / 高度不同 / 无路时退到最近可走格。</summary>
    HexCell PickReachable(HexCell candidate, int level)
    {
        if (candidate == null) return null;
        if (hexGrid.IsWalkable(candidate))
        {
            if (level == int.MinValue || candidate.Elevation == level) return candidate;
        }
        HexCell near = hexGrid.FindNearestWalkableAtElevation(candidate, level == int.MinValue ? 0 : level);
        if (logTravel && near != candidate)
        {
            Debug.Log("[Pathfinding] 目标格 " + candidate.coordinates.ToString() + " 不可用（高度 " +
                candidate.Elevation + "），改用同一高度最近的 " +
                (near != null ? near.coordinates.ToString() : "（无）"));
        }
        return near;
    }

    /// <summary>最后一个怪物巢穴格（MonsterLairLevel &gt; 0）；没有返回 null。</summary>
    public HexCell FindLairCell()
    {
        EnsureRefs();
        if (hexGrid == null || hexGrid.Cells == null) return null;
        HexCell[] cells = hexGrid.Cells;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null && cells[i].MonsterLairLevel > 0) return cells[i];
        }
        return null;
    }

    // ================= 输入 =================

    void HandleMiddleClick()
    {
        if (!Input.GetMouseButtonDown(2)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (Camera.main == null || hexGrid == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, 2000f)) return;

        HexCell cell = hexGrid.GetCellAtWorld(hit.point);
        if (cell == null) return;

        int level = CurrentUnitElevation();
        bool usable = hexGrid.IsWalkable(cell) &&
            (level == int.MinValue || cell.Elevation == level ||
             hexGrid.pathMode != HexPathMode.SameElevationOnly);
        if (!usable)
        {
            HexCell near = hexGrid.FindNearestWalkableAtElevation(cell, level == int.MinValue ? 0 : level);
            if (logTravel)
            {
                Debug.Log("[Pathfinding] 该格不可直接前往（不可通行或与单位不在同一高度 " + cell.Elevation +
                    "），改用同一高度最近的可走格：" + (near != null ? near.coordinates.ToString() : "（无）"));
            }
            cell = near;
        }
        if (cell != null) MoveUnitTo(cell);
    }

    // ================= 装配 / 表现 =================

    void EnsureRefs()
    {
        if (hexGrid == null) hexGrid = FindObjectOfType<HexGrid>();

        if (unit == null)
        {
            unit = GetComponentInChildren<HexUnit>(true);
        }
        if (unit == null && unitPrefab != null)
        {
            if (unitContainer == null)
            {
                Transform existing = transform.Find("Units Container");
                if (existing != null)
                {
                    unitContainer = existing;
                }
                else
                {
                    GameObject go = new GameObject("Units Container");
                    go.transform.SetParent(transform, false);
                    unitContainer = go.transform;
                }
            }
            GameObject instance = Instantiate(unitPrefab, unitContainer, false);
            instance.name = unitPrefab.name + " (Demo)";
            unit = instance.GetComponent<HexUnit>();
            if (unit == null) unit = instance.AddComponent<HexUnit>();
        }

        if (unit != null && unit.hexGrid == null) unit.hexGrid = hexGrid;
        if (unit != null && unitContainer != null && unit.transform.parent == null)
        {
            unit.transform.SetParent(unitContainer, false);
        }
    }

    void Subscribe()
    {
        if (subscribed || unit == null) return;
        unit.TravelFinished += HandleTravelFinished;
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed || unit == null) return;
        unit.TravelFinished -= HandleTravelFinished;
        subscribed = false;
    }

    void HandleTravelFinished(HexUnit u, HexCell cell)
    {
        nextLoopTime = Time.time + Mathf.Max(0.1f, loopDelay);
        if (logTravel)
        {
            Debug.Log("[Pathfinding] 到达 " + (cell != null ? cell.coordinates.ToString() : "?") +
                "（长度=" + u.PathLength + " 格）" + (loopTravel ? "，稍后换终点继续演示" : ""));
        }
    }

    /// <summary>按当前路径刷新可视化（无路径时隐藏）。</summary>
    public void UpdatePathLine()
    {
        if (!showPathLine || unit == null || unit.Path == null || unit.Path.Count == 0)
        {
            if (pathLine != null) pathLine.enabled = false;
            return;
        }

        IList<HexCell> path = unit.Path;
        EnsureLine();
        if (pathLine == null) return;

        pathLine.enabled = true;
        pathLine.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++)
        {
            HexCell cell = path[i];
            Vector3 p = cell != null ? cell.transform.position : Vector3.zero;
            p.y += lineHeightOffset;
            pathLine.SetPosition(i, p);
        }
    }

    void EnsureLine()
    {
        if (pathLine != null) return;

        GameObject go = new GameObject("Path Line");
        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.DontSave;   // 运行时生成，不进场景序列化

        pathLine = go.AddComponent<LineRenderer>();
        pathLine.useWorldSpace = true;
        pathLine.textureMode = LineTextureMode.Stretch;
        pathLine.numCapVertices = 2;
        pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pathLine.receiveShadows = false;
        pathLine.startWidth = lineWidth;
        pathLine.endWidth = lineWidth;
        pathLine.startColor = pathColor;
        pathLine.endColor = pathColor;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader != null)
        {
            Material mat = new Material(shader);
            mat.hideFlags = HideFlags.DontSave;
            pathLine.material = mat;
        }
    }

    static string DescribePath(IList<HexCell> path)
    {
        if (path == null) return "";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0) sb.Append(" → ");
            sb.Append(path[i] != null ? path[i].coordinates.ToString() : "?");
        }
        return sb.ToString();
    }
}
