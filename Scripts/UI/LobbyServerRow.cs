using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 联机大厅里的"服务器行"（挂在 LobbyServerRow.prefab 根物体）。
/// 显示：房主名 / 人数 / 地图 / 地址。点击 → LobbyPanel 发起连接。
/// </summary>
public class LobbyServerRow : MonoBehaviour, IPointerClickHandler
{
    public Text label;
    public Image background;

    LobbyServerInfo info;
    LobbyPanel owner;

    static readonly Color NormalColor = new Color(0.17f, 0.20f, 0.26f, 0.95f);
    static readonly Color FullColor = new Color(0.26f, 0.16f, 0.16f, 0.95f);

    public LobbyServerInfo Info { get { return info; } }
    public string Address { get { return info.address; } }

    public void Bind(LobbyServerInfo serverInfo, LobbyPanel panel)
    {
        info = serverInfo;
        owner = panel;

        if (label != null)
        {
            string map = string.IsNullOrEmpty(info.mapName) ? "未命名地图" : info.mapName;
            string count = info.maxPlayers > 0 ? info.playerCount + "/" + info.maxPlayers : info.playerCount.ToString();
            label.text = info.hostName + "   ·   人数 " + count + "   ·   " + map + "   ·   " + info.address;
        }

        bool full = info.maxPlayers > 0 && info.playerCount >= info.maxPlayers;
        if (background != null) background.color = full ? FullColor : NormalColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
        if (owner != null) owner.OnServerRowClicked(this);
    }
}
