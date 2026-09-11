using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.IO;
enum OptionalToggle
{
    Ignore, Yes, No
}

public class HexMapEditor : MonoBehaviour
{

    private bool isEditor = false;
    public Transform[] UIS;
    public HexGrid hexGrid;
    private int activeElevation;
    private int activeWaterLevel;

    int brushSize = 0;

    bool isDrag;
    HexDirection dragDirection;
    HexCell previousCell;

    OptionalToggle riverMode;

    bool applyElevation = false;
    bool applyWaterLevel = false;

    int activeTerrainTypeIndex = -1;

    private int activeUrbanLevel;
    private int activeFarmLevel;
    private int activePlantLevel;
    private int activeLairLevel;

    bool applyUrbanLevel;
    bool applyFarmLevel;
    bool applyPlantLevel;
    bool applyLairLevel;



    private void Start()
    {
        ShowEditorUi(false);
        ShowUI(false);
        // 特征等级滑块按整档吸附（0~3 共 4 档），避免拖到 0.92 被 (int) 截断成 0
        UnityEngine.UI.Slider[] sliders = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Slider>(true);
        for (int si = 0; si < sliders.Length; si++)
        {
            string sn = sliders[si].name;
            if (sn == "UrbanSlider" || sn == "FramSlider" || sn == "PlantSlider" || sn == "LairSlider")
            {
                sliders[si].wholeNumbers = true;
                sliders[si].value = Mathf.RoundToInt(sliders[si].value);
            }
        }
    }


    private void Update()
    {
        if(Input.GetKeyDown(KeyCode.F12))
        {
            isEditor = !isEditor;
            ShowEditorUi(isEditor);
        }
        if (!isEditor) {
            // 输入仲裁：角色控制（HeroController）激活 / 联机客户端只读 / 种植模式（PlantingInput）/ UI 模态面板占用鼠标时，地图编辑让出鼠标
            if (HexControlMode.heroActive || HexControlMode.netClientReadOnly || HexControlMode.plantingActive || HexControlMode.uiModalActive)
            {
                previousCell = null;
                return;
            }
        }
        if (Input.GetMouseButton(0) && !EventSystem.current.IsPointerOverGameObject())
        {
            HandleInput();
        }
        else {
            previousCell = null;
        }
    }

    void HandleInput()
    {
        Ray inputRay = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(inputRay, out hit))
        {
            HexCell currentCell = hexGrid.GetCell(hit.point);
            if (previousCell != null) {
                Debug.LogWarning($"currentCell : {currentCell.Position}");
            }
            if (previousCell && previousCell != currentCell)
            {
                ValidateDrag(currentCell);
            }
            else {
                isDrag = false;
            }

            EditorCells(currentCell);
            previousCell = currentCell;
        }
        else {
            previousCell = null;
        }
    }

    void EditorCells(HexCell center) {
        int centerX = center.coordinates.X;
        int centerZ = center.coordinates.Z;

        for (int r = 0, z = centerZ - brushSize; z <= centerZ; z++, r++)
        {
            for (int x = centerX - r; x <= centerX + brushSize; x++)
            {
                EditCell(hexGrid.GetCell(new HexCoordinates(x, z)));
            }
        }
        for (int r = 0, z = centerZ + brushSize; z > centerZ; z--, r++)
        {
            for (int x = centerX - brushSize; x <= centerX + r; x++)
            {
                EditCell(hexGrid.GetCell(new HexCoordinates(x, z)));
            }
        }
    }


    void EditCell(HexCell cell) {
        if (cell) {
            if (activeTerrainTypeIndex >= 0)
            {
                cell.TerrainTypeIndex = activeTerrainTypeIndex;
            }
            if (applyElevation)
            {
                cell.Elevation = activeElevation;
            }
            if (applyWaterLevel)
            {
                cell.WaterLevel = activeWaterLevel;
            }
            if (applyUrbanLevel)
            {
                cell.UrbanLevel = activeUrbanLevel;
            }
            if (applyFarmLevel)
            {
                cell.FarmLevel = activeFarmLevel;
            }
            if (applyLairLevel)
            {
                cell.MonsterLairLevel = activeLairLevel;
            }
            if (applyPlantLevel)
            {
                cell.PlantLevel = activePlantLevel;
            }
            else if (isDrag && riverMode == OptionalToggle.Yes)
            {
                HexCell otherCell = cell.GetNeighbor(dragDirection.Opposite());
                if (otherCell)
                {
                    otherCell.SetOutgoingRiver(dragDirection);
                }
            }
            //hexGrid.Refresh();

        }

    }


    public void SetElevation(float elevation) {
        activeElevation = (int)elevation;
    }

    public void SetApplyElevation(bool value) {
        applyElevation = value;
    }

    public void SetBrushSize(float size)
    {
        brushSize = (int)size;
    }

    public void ShowUI(bool visible)
    {
        hexGrid.ShowUI(visible);
    }

    public void SetRiverMode(int mode)
    {
        riverMode = (OptionalToggle)mode;
    }

    void ValidateDrag(HexCell currentCell) {
        for (
            dragDirection = HexDirection.NE;
            dragDirection <= HexDirection.NW;
            dragDirection++
        )
        {
            if (previousCell.GetNeighbor(dragDirection) == currentCell)
            {
                isDrag = true;
                return;
            }
        }
        isDrag = false;
    }

    public void SetApplyWaterLevel(bool toggle) {
        applyWaterLevel = toggle;
    }

    public void SetWaterLevel(float level) {
        activeWaterLevel = (int)level;
    }

    public void SetTerrainTypeIndex(int index)
    {
        activeTerrainTypeIndex = index;
    }

    public void SetApplyUrbanLevel(bool toggle) { 
        applyUrbanLevel = toggle;
    }

    public void SetUrbanLevel(float level)
    {
        activeUrbanLevel = Mathf.RoundToInt(level);
    }

    public void SetApplyFarmLevel(bool toggle)
    {
        applyFarmLevel = toggle;
    }

    public void SetFarmLevel(float level)
    {
        activeFarmLevel = Mathf.RoundToInt(level);
    }

    public void SetApplyPlantLevel(bool toggle)
    {
        applyPlantLevel = toggle;
    }

    public void SetPlantLevel(float level)
    {
        activePlantLevel = Mathf.RoundToInt(level);
    }

    /// <summary>怪物巢穴（僵尸巢穴等放置点）：开关是否应用。</summary>
    public void SetApplyLair(bool toggle)
    {
        applyLairLevel = toggle;
    }

    /// <summary>怪物巢穴等级：0 = 清除；1..n 选 monsterLairPrefabs[等级-1]。</summary>
    public void SetLairLevel(float level)
    {
        activeLairLevel = Mathf.RoundToInt(level);
    }

    /// <summary>
    /// 特征放置密度：true=密集(中心+6方向)，false=稀疏(仅中心)。
    /// </summary>
    public void SetFeatureDensity(bool dense)
    {
        HexMetrics.denseFeatures = dense;
        if (hexGrid)
        {
            hexGrid.RefreshAll();
        }
    }

    private void ShowEditorUi(bool show) {
        for (int i = 0; i < UIS.Length; i++)
        {
            UIS[i].gameObject.SetActive(show);
        }
    }

    public void Save() {
        Debug.Log(Application.persistentDataPath);
        string path = Path.Combine(Application.persistentDataPath, "test.map");
        using (BinaryWriter writer =
                new BinaryWriter(File.Open(path, FileMode.Create))) {
            writer.Write(3);
            hexGrid.Save(writer);
        }
    }

    public void Load() { 
        string path = Path.Combine(Application.persistentDataPath, "test.map");
        using (BinaryReader reader = new BinaryReader(File.Open(path,FileMode.Open)))
        {
            int header = reader.ReadInt32();
            if (header <= 3)
            {
                hexGrid.Load(reader, header);
                HexMapCamera.ValidatePosition();
            }
            else {
                Debug.LogWarning("Unknown map format " + header);
            }

        }
    }





}
