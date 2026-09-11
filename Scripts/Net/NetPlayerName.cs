using Mirror;
using UnityEngine;

/// <summary>
/// 联机玩家名（挂在 EllenNet prefab 根物体上）。
///
/// 职责：本机玩家名（PlayerPrefs 本地缓存）→ 连接后经 [Command] 上报房主 → [SyncVar] 广播给所有端。
/// 后续别的系统（联机大厅列表、头顶名字、聊天）读 `displayName` 即可。
///
/// 名字缓存与 UI 解耦：缓存 key 由本类统一管理（NetPlayerName.PrefsKey），
/// 大厅 UI 只调 `NetPlayerName.SetLocalName(...)`，不关心 PlayerPrefs。
/// </summary>
[DisallowMultipleComponent]
public class NetPlayerName : NetworkBehaviour
{
    /// <summary>本地缓存的 PlayerPrefs key（大厅 UI 与联机层共用）。</summary>
    public const string PrefsKey = "ui.playerName";
    /// <summary>默认名。</summary>
    public const string DefaultName = "玩家";
    /// <summary>名字最大长度（字符）。</summary>
    public const int MaxLength = 16;

    [SyncVar(hook = nameof(OnNameChanged))]
    public string displayName = "";

    /// <summary>本机玩家名的实例（仅本地玩家会赋值）。</summary>
    public static NetPlayerName Local { get; private set; }

    /// <summary>本机玩家名实例就绪（大厅等 UI 可订阅）。</summary>
    public static event System.Action<NetPlayerName> LocalReady;

    /// <summary>任意玩家名变化（含远端）。</summary>
    public static event System.Action<NetPlayerName> NameChanged;

    /// <summary>本地缓存的名字（PlayerPrefs；未设置返回 ""）。</summary>
    public static string CachedName
    {
        get { return PlayerPrefs.GetString(PrefsKey, ""); }
    }

    /// <summary>名字是否已缓存过。</summary>
    public static bool HasCachedName
    {
        get { return !string.IsNullOrEmpty(PlayerPrefs.GetString(PrefsKey, "")); }
    }

    /// <summary>设置本机玩家名：写缓存；若角色已 spawn 则同步上报房主。</summary>
    public static void SetLocalName(string name)
    {
        string clean = Sanitize(name);
        PlayerPrefs.SetString(PrefsKey, clean);
        PlayerPrefs.Save();
        if (Local != null) Local.CmdSetName(clean);
    }

    public override void OnStartLocalPlayer()
    {
        Local = this;
        if (HasCachedName) CmdSetName(CachedName);
        else displayName = DefaultName;
        if (LocalReady != null) LocalReady(this);
    }

    public override void OnStopLocalPlayer()
    {
        if (Local == this) Local = null;
    }

    /// <summary>本机上报名字（房主权威写入 SyncVar 并广播）。</summary>
    [Command]
    public void CmdSetName(string name)
    {
        displayName = Sanitize(name);
    }

    void OnNameChanged(string oldValue, string newValue)
    {
        if (NameChanged != null) NameChanged(this);
    }

    /// <summary>清洗名字：去首尾空白 / 换行 / 控制字符，限长；空则用默认名。</summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return DefaultName;

        System.Text.StringBuilder sb = new System.Text.StringBuilder(MaxLength);
        for (int i = 0; i < name.Length && sb.Length < MaxLength; i++)
        {
            char c = name[i];
            if (char.IsControl(c)) continue;
            sb.Append(c);
        }
        string result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? DefaultName : result;
    }
}
