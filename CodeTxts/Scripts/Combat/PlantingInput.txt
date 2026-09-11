using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 种植交互入口（**临时版**：正式选卡 / 阳光 UI 由 FEAT-005 的 FGUI 取代）。
///
/// 职责：
///  1. 选卡 —— 数字键 1..9 选 availablePlants[i]（再按一次取消）、右键 / Esc 取消选卡；
///  2. 输入仲裁 —— 种植模式（选卡非空）时置 HexControlMode.plantingActive = true，
///     让 HexController(HeroController) / HexMapEditor 让出左键，避免"点一下既种植物又移动角色"；
///  3. 左键点击地形 → PlantingSystem.TryPlantSelected(cell)，失败打印原因。
///
/// 射线口径与 HexMapEditor / HeroController 完全一致：只认地形 MeshCollider
/// （植物与地形特征都已剥掉自带 Collider），屏幕点在 UI 上时不响应。
///
/// 装配：与 PlantingSystem 同物体（"[Planting]"）。FGUI 接好后把 numberKeySelect 关掉即可，
/// 或整个组件禁用（HexControlMode.plantingActive 在 OnDisable 里复位）。
/// </summary>
[DisallowMultipleComponent]
public class PlantingInput : MonoBehaviour
{
    [Tooltip("种植系统（留空自动取同物体 / 场景中的实例）")]
    public PlantingSystem system;
    [Tooltip("地图（留空自动取 PlantingSystem.hexGrid 或场景里的 HexGrid）")]
    public HexGrid hexGrid;
    [Tooltip("取消选卡的按键")]
    public KeyCode cancelKey = KeyCode.Escape;
    [Tooltip("右键取消选卡")]
    public bool rightClickCancels = true;
    [Tooltip("数字键 1..9 选卡（临时快捷；FGUI 接好后可关）")]
    public bool numberKeySelect = true;
    [Tooltip("射线最大距离")]
    public float rayMaxDistance = 2000f;
    [Tooltip("打印种植失败原因（调试用）")]
    public bool logFailures = true;

    void OnEnable()
    {
        if (system == null) system = GetComponent<PlantingSystem>();
        if (system == null) system = FindObjectOfType<PlantingSystem>();
    }

    void OnDisable()
    {
        HexControlMode.plantingActive = false;
    }

    void Update()
    {
        if (system == null)
        {
            system = FindObjectOfType<PlantingSystem>();
            HexControlMode.plantingActive = false;
            if (system == null) return;
        }
        if (hexGrid == null) hexGrid = system.hexGrid != null ? system.hexGrid : FindObjectOfType<HexGrid>();

        // 输入仲裁：种植模式占用左键
        HexControlMode.plantingActive = system.IsPlantingMode;

        HandleSelectKeys();

        if (!system.IsPlantingMode) return;

        if (rightClickCancels && Input.GetMouseButtonDown(1))
        {
            system.ClearSelection();
            return;
        }
        if (Input.GetKeyDown(cancelKey))
        {
            system.ClearSelection();
            return;
        }

        // 输入仲裁：UI 模态面板打开时不让按键/左键落到种植上（遮罩本身也已挡住点击）
        if (!HexControlMode.uiModalActive && Input.GetMouseButtonDown(0) &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            TryPlantAtMouse();
        }
    }

    void HandleSelectKeys()
    {
        if (!numberKeySelect || system.availablePlants == null) return;
        int count = Mathf.Min(system.availablePlants.Length, 9);
        for (int i = 0; i < count; i++)
        {
            if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) continue;
            PlantConfig config = system.availablePlants[i];
            if (config == null) continue;
            // 再按一次同键 = 取消选卡（退出种植模式）
            if (system.SelectedPlant == config) system.ClearSelection();
            else system.SelectPlant(config);
            return;
        }
    }

    /// <summary>在鼠标位置种植（公开给验证脚本调用）。</summary>
    public bool TryPlantAtMouse()
    {
        if (system == null || Camera.main == null) return false;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, rayMaxDistance))
        {
            if (logFailures) Debug.Log("[Planting] 射线未命中地形");
            return false;
        }
        return TryPlantAtWorld(hit.point);
    }

    /// <summary>在世界坐标所在格种植（公开给验证脚本调用）。</summary>
    public bool TryPlantAtWorld(Vector3 worldPosition)
    {
        if (system == null) return false;
        HexCell cell = system.GetCellAtWorld(worldPosition);
        return TryPlantAtCell(cell);
    }

    /// <summary>在指定格种植（公开给验证脚本 / FGUI 点击调用）。</summary>
    public bool TryPlantAtCell(HexCell cell)
    {
        if (system == null) return false;
        PlantResult result;
        bool ok = system.TryPlantSelected(cell, out result);
        if (!ok && logFailures)
        {
            string where = cell != null ? " @ " + cell.coordinates.ToString() : "";
            Debug.Log("[Planting] 种植失败：" + PlantingSystem.Describe(result) + where);
        }
        return ok;
    }
}
