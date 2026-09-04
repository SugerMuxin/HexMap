/// <summary>
/// 角色控制与地图编辑之间的输入仲裁。
/// 角色控制激活（HeroController.active == true）时，地图编辑（HexMapEditor）应让出鼠标。
/// </summary>
public static class HexControlMode
{
    /// <summary>角色控制是否正在占用鼠标输入（左键点击=移动角色）。</summary>
    public static bool heroActive;
}
