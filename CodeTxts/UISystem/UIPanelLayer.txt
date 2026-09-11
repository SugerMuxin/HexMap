namespace HexUI
{
    /// <summary>
    /// 面板层级（决定面板所在的容器与同级排序）。
    ///
    /// 结构约定：[UIRoot] 下 Panels（Background/Normal）、Popups（Popup/Overlay），另有全屏遮罩 Modal Mask。
    /// **模态遮罩的运行期位置不是固定的**：UIManager.RefreshMask() 每次打开 / 关闭模态面板时，
    /// 都会把遮罩挪到"最上层模态面板"的正下方（同一容器内）。
    /// 这样遮罩才既能挡住它下面的面板与游戏世界，又不会盖住模态面板自己
    /// ——全屏遮罩一旦盖在面板之上，点击面板任意位置都会被当成"点了遮罩"而立刻关掉面板。
    /// </summary>
    public enum UIPanelLayer
    {
        /// <summary>全屏背景层（在模态遮罩之下）。</summary>
        Background = 0,
        /// <summary>常规面板层（模态面板打开时在遮罩之下）。</summary>
        Normal = 10,
        /// <summary>弹窗层（在常规面板之上）。</summary>
        Popup = 20,
        /// <summary>顶层（提示 / 过场）。</summary>
        Overlay = 30
    }
}
