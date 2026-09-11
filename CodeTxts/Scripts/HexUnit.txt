using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 六边形地图上的「沿路径移动单位」组件（FEAT-001 的移动部分；Catlike HexMap Part18/19 的实时化版本）。
///
/// 定位：寻路的执行端。未来 AI 怪物（FEAT-002 ZombieUnit 等）**继承或组合**本组件即可获得
/// 「A* 寻路 → 沿曲线平滑移动 → 逐格事件 → 到达事件」的完整能力，不必自己写移动。
///
/// 与教程的差异（实时化）：
///  - 删掉回合制 speed 预算，改为 travelSpeed（每秒走几格）的连续移动；
///  - 逻辑位置 location **随行进逐格更新**（教程是"逻辑立即到达目的地、动画纯表演"），
///    因为实时玩法要靠"进入某格"触发事件（啃植物 / 触发陷阱 / 到达基地）；
///  - 新增 followTerrainHeight：每帧把 Y 对齐脚下格顶面，避免跨海拔时沉入/浮空
///    （教程的做法是任其穿插）。
///
/// EditMode（unity-cli exec / 编辑器非运行态）下不跑协程：Travel 直接把单位落到终点并广播事件，
/// 这样断言脚本不依赖 Play 也能验证移动链路。
/// </summary>
[DisallowMultipleComponent]
public class HexUnit : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("地图（留空自动查找场景里的 HexGrid）")]
    public HexGrid hexGrid;

    [Header("移动方式（寻路规则）")]
    [Tooltip("勾选后本单位的寻路模式独立于地图设置（如：地面怪只走同高度、飞行怪忽略高度）；否则跟随 HexGrid.pathMode")]
    public bool overrideGridPathMode = false;
    [Tooltip("本单位寻路模式（仅 overrideGridPathMode 打开时生效）：SameElevationOnly = 只走相同高度（不攀爬/不跳跃）；AllowSlope = 允许坡道")]
    public HexPathMode pathMode = HexPathMode.SameElevationOnly;

    [Header("移动参数")]
    [Tooltip("移动速度：每秒走过多少格（沿曲线插值；1 = 每秒一格，4 = 教程速度）")]
    public float travelSpeed = 4f;
    [Tooltip("移动时朝向行进方向")]
    public bool faceTravelDirection = true;
    [Tooltip("转向速度（度/秒）")]
    public float turnSpeed = 720f;
    [Tooltip("模型正脸相对 transform.forward 的补偿角（度）")]
    public float facingOffset = 0f;
    [Tooltip("落位 y 偏移（贴地微调，一般 0）")]
    public float yOffset = 0f;
    [Tooltip("每帧把 Y 对齐脚下格顶面（跨海拔不沉不浮；关掉则是教程的曲线插值原样）")]
    public bool followTerrainHeight = true;

    [Header("调试")]
    [Tooltip("在 Scene 视图画出最近一次旅行的路径（选中本对象时）")]
    public bool showPathGizmo = true;
    public Color pathGizmoColor = new Color(1f, 0.85f, 0.2f, 1f);

    [Header("状态（只读）")]
    [Tooltip("当前所在格（移动中随行进逐格更新）")]
    public HexCell location;
    [Tooltip("本次旅行的终点格")]
    public HexCell destination;
    public bool isTraveling;
    [Tooltip("上一次寻路失败的原因（中文，成功后清空）")]
    public string noPathReason;

    /// <summary>开始一次旅行：(unit, 起点格)。EditMode 下与 TravelFinished 同时触发。</summary>
    public event Action<HexUnit, HexCell> TravelStarted;
    /// <summary>进入一个新格：(unit, 刚进入的格)。用于"到达植物格 / 踩上某格"这类 AI 触发。</summary>
    public event Action<HexUnit, HexCell> EnteredCell;
    /// <summary>到达终点：(unit, 终点格)。</summary>
    public event Action<HexUnit, HexCell> TravelFinished;

    readonly List<HexCell> pathToTravel = new List<HexCell>();
    Coroutine travelRoutine;
    int currentCellIndex;

    /// <summary>本次旅行的路径（只读，起点→终点）。</summary>
    public IList<HexCell> Path { get { return pathToTravel; } }
    /// <summary>路径上剩余没走过的格数（用于 AI 判断"快到了 / 还有几格"）。</summary>
    public int RemainingCells { get { return Mathf.Max(0, pathToTravel.Count - 1 - currentCellIndex); } }
    /// <summary>本次旅行的总格数（含起点）。</summary>
    public int PathLength { get { return pathToTravel.Count; } }

    void OnEnable()
    {
        if (hexGrid == null) hexGrid = FindObjectOfType<HexGrid>();
        if (location != null) SnapTo(location);
    }

    void OnDisable()
    {
        StopTravel(false);
    }

    // ================= 对外 API =================

    /// <summary>寻路并前往指定格。返回 false = 目标为空 / 无可行路径（单位原地不动）。</summary>
    public bool TravelTo(HexCell target)
    {
        if (target == null) return false;
        EnsureGrid();
        if (hexGrid == null) return false;

        HexCell from = CurrentCell();
        if (from == null)
        {
            // 不在任何格上（例如场景里刚放下的单位）：直接落位
            WarpTo(target);
            return true;
        }
        if (from == target)
        {
            ArriveInstant(target);
            return true;
        }
        HexPathMode mode = EffectivePathMode();
        if (!hexGrid.FindPath(from, target, mode))
        {
            noPathReason = hexGrid.DescribePathFailure(from, target);
            return false;
        }
        noPathReason = null;
        Travel(hexGrid.GetPath());
        return true;
    }

    /// <summary>寻路并前往 offset 坐标对应的格。</summary>
    public bool TravelTo(int offsetX, int offsetZ)
    {
        EnsureGrid();
        if (hexGrid == null) return false;
        HexCell target = hexGrid.GetCellByOffset(offsetX, offsetZ);
        return TravelTo(target);
    }

    /// <summary>
    /// 沿给定路径移动（起点→终点，含两端；可由 HexGrid.GetPath() 得到）。
    /// 会先停掉正在进行的旅行；路径为空则不动；只有一格则立即落位。
    /// </summary>
    public void Travel(List<HexCell> path)
    {
        StopTravel(true);

        if (path == null || path.Count == 0) return;
        if (path[0] == null) return;

        pathToTravel.Clear();
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i] != null) pathToTravel.Add(path[i]);
        }
        if (pathToTravel.Count == 0) return;

        currentCellIndex = 0;
        location = pathToTravel[0];
        destination = pathToTravel[pathToTravel.Count - 1];
        SnapTo(location);

        if (TravelStarted != null) TravelStarted(this, location);

        if (pathToTravel.Count == 1)
        {
            ArriveInstant(destination);
            return;
        }

        if (!Application.isPlaying)
        {
            // EditMode / 非运行态：不跑协程（协程在编辑器非运行态不推进），直接落到终点
            ArriveInstant(destination);
            return;
        }

        isTraveling = true;
        travelRoutine = StartCoroutine(TravelPath());
    }

    /// <summary>停止移动。snapToNearestCell=true 时把单位落到当前所在格的中心（逻辑位置一并更新）。</summary>
    public void StopTravel(bool snapToNearestCell = true)
    {
        if (travelRoutine != null)
        {
            StopCoroutine(travelRoutine);
            travelRoutine = null;
        }
        if (!isTraveling) return;
        isTraveling = false;

        if (snapToNearestCell)
        {
            HexCell cell = GetCellAt(transform.position);
            if (cell != null)
            {
                location = cell;
                SnapTo(cell);
            }
        }
    }

    /// <summary>把单位放到指定格（含 y 贴地），并同步逻辑位置。不触发任何事件。</summary>
    public void WarpTo(HexCell cell)
    {
        if (cell == null) return;
        location = cell;
        destination = cell;
        SnapTo(cell);
    }

    /// <summary>当前所在格：优先用逻辑位置 location，兜底按世界坐标反查（可能为 null）。</summary>
    public HexCell CurrentCell()
    {
        if (location != null) return location;
        return GetCellAt(transform.position);
    }

    /// <summary>世界坐标 → 格（越界 / 未建图返回 null，不抛异常）。</summary>
    public HexCell GetCellAt(Vector3 worldPosition)
    {
        EnsureGrid();
        if (hexGrid == null) return null;
        return hexGrid.GetCellAtWorld(worldPosition);
    }

    // ================= 内部实现 =================

    void EnsureGrid()
    {
        if (hexGrid == null) hexGrid = FindObjectOfType<HexGrid>();
    }

    /// <summary>本单位实际生效的寻路模式：勾了 overrideGridPathMode 就用自带的，否则跟随地图。</summary>
    public HexPathMode EffectivePathMode()
    {
        if (overrideGridPathMode) return pathMode;
        return hexGrid != null ? hexGrid.pathMode : HexPathMode.SameElevationOnly;
    }

    /// <summary>把单位 y 对齐格中心顶面（与角色/植物落位同口径：cell.transform.position）。</summary>
    public void SnapTo(HexCell cell)
    {
        if (cell == null) return;
        Vector3 p = cell.transform.position;
        p.y += yOffset;
        transform.position = p;
    }

    IEnumerator TravelPath()
    {
        // 曲线：每段从"上一段的终点"到"当前格与下一格的中点"——
        // 这样曲线穿过格与格之间的边界，而不是逐格中心折线（Catlike Part19 §2.1~2.2）
        Vector3 a, b, c = pathToTravel[0].transform.position;
        float t = 0f;   // 注意：t 跨段连续扣减，避免低帧率丢时间导致卡顿

        for (int i = 1; i < pathToTravel.Count; i++)
        {
            a = c;
            b = pathToTravel[i - 1].transform.position;
            c = (b + pathToTravel[i].transform.position) * 0.5f;

            for (; t < 1f; t += Time.deltaTime * travelSpeed)
            {
                SetPosition(Bezier.GetPoint(a, b, c, t), pathToTravel[i - 1]);
                if (faceTravelDirection) FaceDirection(Bezier.GetDerivative(a, b, c, t));
                yield return null;
            }
            t -= 1f;

            // 已跨过与 pathToTravel[i] 的边界 → 逻辑上进入该格
            currentCellIndex = i;
            location = pathToTravel[i];
            if (EnteredCell != null) EnteredCell(this, location);
        }

        // 最后一段：从边界中点走到终点格中心
        a = c;
        b = pathToTravel[pathToTravel.Count - 1].transform.position;
        c = b;
        for (; t < 1f; t += Time.deltaTime * travelSpeed)
        {
            SetPosition(Bezier.GetPoint(a, b, c, t), pathToTravel[pathToTravel.Count - 1]);
            if (faceTravelDirection) FaceDirection(Bezier.GetDerivative(a, b, c, t));
            yield return null;
        }

        travelRoutine = null;
        ArriveInstant(destination);
    }

    /// <summary>立即到达终点（EditMode / 路径只有一格 / 协程结束）。</summary>
    void ArriveInstant(HexCell cell)
    {
        isTraveling = false;
        if (travelRoutine != null)
        {
            StopCoroutine(travelRoutine);
            travelRoutine = null;
        }
        if (cell != null)
        {
            location = cell;
            currentCellIndex = Mathf.Max(0, pathToTravel.Count - 1);
            SnapTo(cell);
        }
        if (TravelFinished != null) TravelFinished(this, cell);
    }

    /// <summary>
    /// 落位：XZ 用曲线点，Y 用"当前逻辑所在格"的顶面（followTerrainHeight）。
    /// 这里直接传逻辑格而不是每帧反查世界坐标——既省一次 FromPosition 取整（偶尔会打
    /// "rounding error!" 警告），也避免曲线在山谷上方时把单位贴到错误的格上。
    /// </summary>
    void SetPosition(Vector3 worldPosition, HexCell groundCell)
    {
        if (followTerrainHeight && groundCell != null)
        {
            worldPosition.y = groundCell.transform.position.y;
        }
        worldPosition.y += yOffset;
        transform.position = worldPosition;
    }

    void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion target =
            Quaternion.LookRotation(direction.normalized, Vector3.up) *
            Quaternion.Euler(0f, facingOffset, 0f);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target, turnSpeed * Time.deltaTime);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!showPathGizmo || pathToTravel.Count == 0) return;
        Gizmos.color = pathGizmoColor;
        for (int i = 0; i < pathToTravel.Count; i++)
        {
            HexCell cell = pathToTravel[i];
            if (cell == null) continue;
            Vector3 p = cell.transform.position + Vector3.up * 1.5f;
            Gizmos.DrawSphere(p, 1.5f);
            if (i > 0 && pathToTravel[i - 1] != null)
            {
                Gizmos.DrawLine(
                    pathToTravel[i - 1].transform.position + Vector3.up * 1.5f, p);
            }
        }
        if (destination != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(destination.transform.position + Vector3.up, 2f);
        }
    }
#endif
}
