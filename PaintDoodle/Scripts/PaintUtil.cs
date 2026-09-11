using UnityEngine;

namespace PaintDoodle
{
    /// <summary>共享常量：层、世界参数。所有可涂画物体放同一层，靠 raycast 命中 + mesh uv 定位。</summary>
    public static class PaintUtil
    {
        public static int LiquidLayer {
            get { return LayerMask.NameToLayer("PaintLiquid"); }
        } // 液体 billboard 专用渲染层（只被液体相机看见）
        public static int LiquidMask  = 1 << LiquidLayer;

        public static int PaintableLayer
        {
            get { return LayerMask.NameToLayer("Paintable"); }
        } // 可涂画表面层（地面/墙/任意物体）—— 主相机可见 + 被 raycast
        public static int PaintableMask  = 1 << PaintableLayer;

        public const float GroundY    = 0f;               // 演示场地水平面高度
        public const float GroundHalf = 20f;              // 演示场地半边长

        public const float PaintGravity = 21f;            // 液滴模拟重力（LiquidSim 默认值，供弹道解算对齐）

        public static void AssignLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform)
                AssignLayer(t.gameObject, layer);
        }

        /// <summary>表面"一 uv 全幅"对应的世界尺寸（用 AABB 最长边近似），用于世界半径→uv 半径换算。</summary>
        public static float WorldPerUv(Renderer r)
        {
            Bounds b = r.bounds;
            float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            return m > 0.01f ? m : 1f;
        }
    }
}
