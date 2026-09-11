using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 背包里的"种子卡片"（挂在卡片 prefab 根物体上）。
/// 展示：图标（可选）/ 名称 / 阳光消耗 / 冷却进度 / 选中高亮 / 锁定灰化。
///
/// 只负责显示与点击上报，业务动作由 InventoryPanel 处理。
/// 卡片 prefab：Assets/Resources/UI/Panels/SeedCardView.prefab
/// （由菜单 Tools/UI/一键装配 背包 UI (bag) 生成）。
/// </summary>
public class SeedCardView : MonoBehaviour, IPointerClickHandler
{
    [Tooltip("卡片根上的背景图（点击区域 + 灰化）")]
    public Image background;
    [Tooltip("图标（可选，未配置 icon 时隐藏）")]
    public Image icon;
    [Tooltip("名称文本")]
    public Text nameText;
    [Tooltip("阳光消耗文本")]
    public Text costText;
    [Tooltip("可种植地形文本（元数据展示）")]
    public Text terrainText;
    [Tooltip("冷却遮罩（垂直填充：剩余比例）")]
    public Image cooldownOverlay;
    [Tooltip("冷却剩余时间文本（可选）")]
    public Text cooldownText;
    [Tooltip("选中高亮框（可选）")]
    public Image selection;

    SeedTable.Entry entry;
    InventoryPanel owner;

    static readonly Color NormalBg = new Color(0.16f, 0.18f, 0.23f, 0.95f);
    static readonly Color LockedBg = new Color(0.12f, 0.12f, 0.13f, 0.85f);
    static readonly Color Affordable = new Color(0.95f, 0.85f, 0.3f, 1f);
    static readonly Color TooExpensive = new Color(0.85f, 0.35f, 0.35f, 1f);

    /// <summary>绑定的种子条目。</summary>
    public SeedTable.Entry Entry { get { return entry; } }

    /// <summary>绑定数据（由 InventoryPanel 调用）。</summary>
    public void Bind(SeedTable.Entry seedEntry, InventoryPanel panel, int index)
    {
        entry = seedEntry;
        owner = panel;

        if (nameText != null)
        {
            string name = seedEntry.config != null ? seedEntry.config.displayName : seedEntry.id;
            nameText.text = string.IsNullOrEmpty(name) ? seedEntry.id : name;
        }
        if (terrainText != null)
        {
            terrainText.text = string.IsNullOrEmpty(seedEntry.terrain) ? "" : seedEntry.terrain;
        }
        if (icon != null)
        {
            Sprite sprite = seedEntry.config != null ? seedEntry.config.icon : null;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
        if (costText != null && seedEntry.config != null)
        {
            costText.text = seedEntry.config.sunCost.ToString();
        }
        if (background != null) background.color = seedEntry.unlocked ? NormalBg : LockedBg;
        SetSelected(false);
        SetCooldown(0f, 0f);
    }

    /// <summary>按阳光刷新"买得起 / 买不起"配色。</summary>
    public void RefreshAffordable(int sun)
    {
        if (costText == null || entry == null || entry.config == null) return;
        bool ok = entry.unlocked && sun >= entry.config.sunCost;
        costText.color = ok ? Affordable : TooExpensive;
    }

    /// <summary>刷新冷却显示（remaining<=0 表示就绪）。</summary>
    public void SetCooldown(float remaining, float total)
    {
        bool cooling = remaining > 0f;
        if (cooldownOverlay != null)
        {
            cooldownOverlay.enabled = cooling;
            if (cooling)
            {
                cooldownOverlay.type = Image.Type.Filled;
                cooldownOverlay.fillMethod = Image.FillMethod.Vertical;
                cooldownOverlay.fillOrigin = (int)Image.OriginVertical.Bottom;
                cooldownOverlay.fillAmount = total > 0f ? Mathf.Clamp01(remaining / total) : 1f;
            }
        }
        if (cooldownText != null)
        {
            cooldownText.enabled = cooling;
            if (cooling) cooldownText.text = remaining.ToString("0.0") + "s";
        }
    }

    /// <summary>选中高亮。</summary>
    public void SetSelected(bool selected)
    {
        if (selection != null) selection.enabled = selected;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
        if (owner != null) owner.OnCardClicked(this);
    }
}
