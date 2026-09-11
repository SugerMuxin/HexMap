/// <summary>
/// 角色控制与地图编辑之间的输入仲裁。
/// 角色控制激活（HeroController.active == true）时，地图编辑（HexMapEditor）应让出鼠标。
/// </summary>
public static class HexControlMode
{
    /// <summary>角色控制是否正在占用鼠标输入（左键点击=移动角色）。</summary>
    public static bool heroActive;

    /// <summary>联机客户端（非房主）是否禁止地图编辑（由 NetGameManager 在会话开始/结束时置位）。</summary>
    public static bool netClientReadOnly;

    /// <summary>
    /// 种植模式（选卡 / 临时选卡）是否正在占用鼠标输入（左键点击=种植）。
    /// 由 PlantingInput 每帧按 PlantingSystem.IsPlantingMode 置位；角色控制与地图编辑据此让出左键。
    /// </summary>
    public static bool plantingActive;

    /// <summary>
    /// UI 模态（UIManager 有 blockGameInput 面板打开）是否正在占用鼠标 / 键盘输入。
    /// 由游戏侧桥接器 UICharacterInputGate 订阅 HexUI.UIManager.BlockingStateChanged 置位；
    /// 角色点击移动 / 地图编辑 / 种植输入据此让出鼠标。
    /// 无模态面板时恒为 false —— 对既有行为零影响。
    /// </summary>
    public static bool uiModalActive;
}
