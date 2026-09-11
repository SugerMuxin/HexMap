using UnityEngine;

/// <summary>
/// 二次贝塞尔曲线工具（Catlike HexMap Part19 §2.2）。
///
/// 用途：让沿格子路径移动的单位走"边到边"的平滑曲线，而不是逐格中心的折线——
/// 相邻曲线段在格与格之间切线连续，所以转向是渐变减速的，不会突然拐直角。
///
/// 纯静态数学，不引用任何游戏类型（可跨项目复用）。
/// </summary>
public static class Bezier
{
    /// <summary>二次贝塞尔取点：(1-t)²·A + 2(1-t)t·B + t²·C。t 不钳制（调用方保证 0..1，插值中恒成立）。</summary>
    public static Vector3 GetPoint(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float r = 1f - t;
        return r * r * a + 2f * r * t * b + t * t * c;
    }

    /// <summary>同上，但把 t 钳制到 0..1（外部输入用）。</summary>
    public static Vector3 GetPointClamped(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        return GetPoint(a, b, c, Mathf.Clamp01(t));
    }

    /// <summary>曲线在 t 处的一阶导数（切线方向）：2·((1-t)(B-A) + t(C-B))。用于朝向/速度。</summary>
    public static Vector3 GetDerivative(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        return 2f * ((1f - t) * (b - a) + t * (c - b));
    }
}
