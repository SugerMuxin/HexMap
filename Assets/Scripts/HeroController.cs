using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 角色控制（最小版）：鼠标点击地图任意格 -> 角色直线移动到该格中心。
/// - 不做寻路：角色沿起点到终点的直线插值移动（中途可能穿坡，寻路后续自行扩展）。
/// - 点击转向：左键点击地面时，角色平滑转向点击点（方向由点击决定，不随鼠标悬停实时转动）。
/// - 第一人称（firstPersonView，由 HeroFollowCamera 切换时设置）：实时面向鼠标，作为视角控制。
/// - 移动中可再次点击更换目标（从当前点直线前往新目标）。
/// </summary>
public class HeroController : MonoBehaviour
{
    [Header("引用")]
    public HexGrid hexGrid;                 // 地图（Inspector 赋值或运行时自动查找）

    [Header("移动参数")]
    [Tooltip("移动速度（世界单位/秒）。格直径约 17.3，一格约需 17.3/speed 秒")]
    public float speed = 14f;
    [Tooltip("到达判定距离（世界单位）")]
    public float arriveDistance = 0.15f;
    [Tooltip("是否启用角色控制。取消勾选后鼠标交还给地图编辑（HexMapEditor）")]
    public bool active = true;

    [Header("转向参数")]
    [Tooltip("实时面向鼠标（默认关）。开启后角色每帧转向鼠标指向的地面点，第三人称下会造成画面持续转动，一般不需要")]
    public bool faceMouse = false;
    [Tooltip("第一人称视角模式（由 HeroFollowCamera 在切换第一/第三人称时自动设置；第一人称下实时面向鼠标=转动视角）")]
    public bool firstPersonView = false;
    [Tooltip("转向速度（度/秒）")]
    public float turnSpeed = 360f;
    [Tooltip("模型正脸相对 transform.forward 的补偿角（度）。若脸朝向反了设 180")]
    public float facingOffset = 0f;

    [Header("状态（只读）")]
    public HexCell currentCell;             // 角色当前所在格
    public bool isMoving;

    Vector3 destination;                    // 目标点（世界坐标，格中心顶面）
    bool hasDestination;
    Vector3? facingTarget;                  // 需要持续转向的世界点（点击后设置，转到位后清除）

    void OnEnable()
    {
        HexControlMode.heroActive = active;
    }

    void OnDisable()
    {
        HexControlMode.heroActive = false;
        isMoving = false;
        facingTarget = null;
    }

    void Start()
    {
        if (hexGrid == null)
        {
            hexGrid = FindObjectOfType<HexGrid>();
        }
        if (hexGrid != null)
        {
            currentCell = GetCellAt(transform.position);
            SnapToGround();
        }
    }

    void Update()
    {
        // 鼠标左键：点击地图移动（不与 UI 交互冲突；角色控制激活时才响应）
        if (
            active && isActiveAndEnabled &&
            Input.GetMouseButtonDown(0) &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
        )
        {
            TryMoveToClick();
        }

        // 转向
        bool liveFace = faceMouse || firstPersonView;
        if (liveFace)
        {
            // 实时面向鼠标（第一人称视角控制 / 手动开启）：悬停即转
            facingTarget = null;
            FaceMousePoint();
        }
        else if (facingTarget.HasValue)
        {
            // 点击转向：平滑转到点击点后停止（鼠标悬停不转）
            if (FaceTowardStep(facingTarget.Value))
            {
                facingTarget = null; // 已到位
            }
        }

        MoveAlong();
    }

    // ================= 面向 / 转向 =================

    /// <summary>设置持续转向目标点（平滑转过去，转到位停止）。供点击移动与外部调用。</summary>
    public void FaceTo(Vector3 worldPoint)
    {
        facingTarget = worldPoint;
    }

    /// <summary>立即转向世界坐标点（测试/传送复位用）</summary>
    public void SnapFacing(Vector3 worldPoint)
    {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }
        transform.rotation =
            Quaternion.LookRotation(dir.normalized, Vector3.up) *
            Quaternion.Euler(0f, facingOffset, 0f);
        facingTarget = null;
    }

    void FaceMousePoint()
    {
        if (!isActiveAndEnabled || Camera.main == null)
        {
            return;
        }
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return; // 鼠标在 UI 上不转向
        }
        // 经过角色位置的水平面：鼠标在天空/地图外也能给出朝向
        Plane ground = new Plane(Vector3.up, transform.position);
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        float enter;
        if (ground.Raycast(ray, out enter))
        {
            RotateToward(ray.GetPoint(enter), turnSpeed * Time.deltaTime);
        }
    }

    /// <summary>单步转向世界点。返回 true 表示已转到（角度差足够小）。</summary>
    bool FaceTowardStep(Vector3 worldPoint)
    {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            return true;
        }
        Quaternion target =
            Quaternion.LookRotation(dir.normalized, Vector3.up) *
            Quaternion.Euler(0f, facingOffset, 0f);
        float angle = Quaternion.Angle(transform.rotation, target);
        if (angle < 0.5f)
        {
            return true;
        }
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, target, turnSpeed * Time.deltaTime);
        return false;
    }

    void RotateToward(Vector3 worldPoint, float maxDegreesDelta)
    {
        Vector3 dir = worldPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            return;
        }
        Quaternion target =
            Quaternion.LookRotation(dir.normalized, Vector3.up) *
            Quaternion.Euler(0f, facingOffset, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, maxDegreesDelta);
    }

    // ================= 移动 =================

    /// <summary>点击地面 -> 转向点击点并移动（公开给输入/验证调用）</summary>
    public bool TryMoveToClick()
    {
        if (Camera.main == null)
        {
            return false;
        }
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        // 只有地形 chunk 有 collider（Terrain useCollider=1），命中即落在某格上
        if (!Physics.Raycast(ray, out hit, 1000f))
        {
            return false;
        }
        bool moved = MoveToWorld(hit.point);
        if (moved)
        {
            // 朝向点击的地面点（点击驱动方向；鼠标悬停不再实时转向）
            facingTarget = hit.point;
        }
        return moved;
    }

    /// <summary>移动到世界坐标对应的格子中心（供点击与后续寻路调用）</summary>
    public bool MoveToWorld(Vector3 worldPosition)
    {
        if (hexGrid == null)
        {
            return false;
        }
        HexCell cell = GetCellAt(worldPosition);
        if (cell == null)
        {
            return false;
        }
        return MoveTo(cell);
    }

    /// <summary>移动到指定格的中心顶面</summary>
    public bool MoveTo(HexCell cell)
    {
        if (cell == null || !active)
        {
            return false;
        }
        // 格中心顶面 = cell 的 transform 位置（含海拔与垂直扰动）
        destination = cell.transform.position;
        hasDestination = true;
        return true;
    }

    void MoveAlong()
    {
        if (!hasDestination)
        {
            return;
        }
        Vector3 position = transform.position;
        Vector3 to = destination - position;

        float distance = to.magnitude;
        if (distance <= arriveDistance)
        {
            hasDestination = false;
            isMoving = false;
            transform.position = destination;
            currentCell = GetCellAt(destination);
            return;
        }

        float step = speed * Time.deltaTime;
        if (step >= distance)
        {
            transform.position = destination;
            hasDestination = false;
            isMoving = false;
            currentCell = GetCellAt(destination);
            return;
        }

        // 直线插值（含 Y：从起点高度线性过渡到目标格顶面）
        transform.position = position + to * (step / distance);
        isMoving = true;
    }

    /// <summary>世界坐标 -> 所在格（带边界检查，越界返回 null）</summary>
    HexCell GetCellAt(Vector3 worldPosition)
    {
        if (hexGrid == null)
        {
            return null;
        }
        Vector3 local = hexGrid.transform.InverseTransformPoint(worldPosition);
        HexCoordinates coords = HexCoordinates.FromPosition(local);
        return hexGrid.GetCell(coords);
    }

    /// <summary>出生时贴地：把角色 y 对齐到脚下格的顶面</summary>
    void SnapToGround()
    {
        if (currentCell == null)
        {
            return;
        }
        Vector3 p = transform.position;
        p.y = currentCell.transform.position.y;
        transform.position = p;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (hasDestination)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(destination, 1f);
            Gizmos.DrawLine(transform.position, destination);
        }
        // 面向方向
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.forward * 6f);
    }
#endif
}
