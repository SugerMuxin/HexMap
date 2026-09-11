using UnityEngine;
using UnityEngine.UI;

namespace HexUI.Samples
{
    /// <summary>
    /// UI 框架自检面板（示例 / 冒烟测试用，可随时删除）。
    /// 演示 UIPanel 生命周期、子控件查找、按钮代码绑定、面板互相打开（栈 + 互斥），
    /// 用于验证打开 / 关闭 / 栈顺序 / 遮罩 / 热键链路。
    /// 装配：菜单 Tools/UI/一键装配 UI 框架 (UIRoot) 生成
    /// Assets/Resources/UI/Panels/UIDemoPanel.prefab，并注册 id = "demo"（热键 F1）与 "demo.second"。
    /// </summary>
    public class UIDemoPanel : UIPanel
    {
        Text title;
        Text body;
        Button closeButton;
        Button pushButton;

        protected override void OnCreate()
        {
            title = FindChild<Text>("Title");
            body = FindChild<Text>("Body");
            closeButton = FindChild<Button>("CloseButton");
            pushButton = FindChild<Button>("PushButton");

            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (pushButton != null) pushButton.onClick.AddListener(OnPushClicked);
        }

        void OnPushClicked()
        {
            // 用同一个 prefab 再开一个实例（id=demo.second），演示栈与层级排序
            OpenOther("demo.second", "我是第二层实例（同一 prefab，不同 id）——Esc 关掉我会回到前一层。");
        }

        protected override void OnShow(object payload)
        {
            if (body != null)
            {
                body.text = payload != null
                    ? payload.ToString()
                    : "F1 开关本面板（再按一次关闭）· Esc 关闭栈顶 · 当前栈深见标题。";
            }
        }

        public override void OnRefresh()
        {
            if (title == null || manager == null) return;
            title.text = "UI 框架自检 · 我的 id：" + id
                         + " · 栈深 " + manager.OpenCount
                         + (manager.HasBlockingPanel ? " · 已阻塞游戏输入" : " · 未阻塞");
        }
    }
}
