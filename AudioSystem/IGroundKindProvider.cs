using UnityEngine;

/// <summary>
/// 脚下地面材质分类（通用枚举，与具体地形系统无关）。
/// 通用音频触发组件（如脚步声）只依赖这个分类，
/// 具体游戏如何把"世界坐标 → GroundKind"由实现方（桥接器）决定。
/// </summary>
public enum GroundKind
{
    Grass = 0,
    Stone,
    Sand,
    Water,
    /// <summary>未分类的地面。脚步等默认按 Grass 处理。</summary>
    Default,
}

/// <summary>
/// 地面材质查询接口：任意"知道脚下是什么地面"的对象实现它。
/// 与 IWaterSurfaceProvider（UnderwaterEffect）同思路——通用模块只认接口，
/// 游戏侧用桥接器实现，把 HexGrid/地形系统等细节挡在通用层之外。
/// </summary>
public interface IGroundKindProvider
{
    /// <summary>返回 worldPoint 处脚下的地面材质分类。</summary>
    GroundKind GetGroundKind(Vector3 worldPoint);
}
