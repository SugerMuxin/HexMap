using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音频资源库（ScriptableObject）——把"素材"与"代码"解耦。
///
/// 用法：
///  1. Inspector 在 entries 里按 语义 key 配置条目（如 "footstep/grass"、"bgm/main"）；
///  2. 每个条目二选一（可同时）提供素材：
///     - clips[]：手动把 AudioClip 拖进来；
///     - autoLoadFolder：填 Resources 下的目录（如 "Audio/SFX/Footsteps/Grass"），
///       运行时自动 Resources.LoadAll —— 之后往该目录丢素材文件即可，无需再改配置。
///  3. 代码只按 key 取素材，不直接引用具体 clip：素材缺失时播放静默跳过并一次性警告，
///     系统其它部分不受影响。
/// </summary>
[CreateAssetMenu(fileName = "AudioBank", menuName = "Audio/Sound Bank")]
public class SoundBank : ScriptableObject
{
    [System.Serializable]
    public class SoundEntry
    {
        [Tooltip("语义键：如 bgm/main、footstep/grass、ambient/waterfall、combat/hit")]
        public string key;

        [Tooltip("手动拖入的 AudioClip（可选；与 autoLoadFolder 结果合并）")]
        public AudioClip[] clips;

        [Tooltip("自动加载目录（相对 Resources，如 Audio/SFX/Footsteps/Grass）。\n" +
                 "把素材文件丢进该目录即可，运行时自动 LoadAll 该目录下全部 AudioClip")]
        public string autoLoadFolder;

        [Header("播放参数")]
        [Range(0f, 2f)] public float volume = 1f;
        [Range(0f, 2f)] public float pitchMin = 0.95f;
        [Range(0f, 2f)] public float pitchMax = 1.05f;

        [Header("3D 空间化")]
        [Tooltip("true = 3D 定位音（按声源与听者距离衰减）；false = 2D（BGM/UI）")]
        public bool spatial = true;
        [Tooltip("3D 最小听距（低于此距离不再增大音量）")]
        public float minDistance = 1f;
        [Tooltip("3D 最大听距（超过此距离几乎听不见）")]
        public float maxDistance = 40f;

        /// <summary>合并后的可用 clip 列表（手动 clips + autoLoadFolder 自动加载），懒加载缓存。</summary>
        [System.NonSerialized] AudioClip[] _resolved;
        [System.NonSerialized] bool _resolvedDirty = true;

        public void InvalidateCache() { _resolved = null; _resolvedDirty = true; }

        public bool HasAnyClip { get { return (ResolveClips() != null && ResolveClips().Length > 0); } }

        /// <summary>解析全部可用 clip：手动 clips 在前，autoLoadFolder 自动加载的在后。</summary>
        public AudioClip[] ResolveClips()
        {
            if (!_resolvedDirty && _resolved != null)
            {
                return _resolved;
            }
            List<AudioClip> list = new List<AudioClip>();
            if (clips != null)
            {
                foreach (AudioClip c in clips)
                {
                    if (c != null && !list.Contains(c)) list.Add(c);
                }
            }
            if (!string.IsNullOrEmpty(autoLoadFolder))
            {
                // Resources.LoadAll 会递归子目录。素材缺失时返回空数组，不报错。
                AudioClip[] loaded = Resources.LoadAll<AudioClip>(autoLoadFolder);
                if (loaded != null)
                {
                    foreach (AudioClip c in loaded)
                    {
                        if (c != null && !list.Contains(c)) list.Add(c);
                    }
                }
            }
            _resolved = list.ToArray();
            _resolvedDirty = false;
            return _resolved;
        }

        /// <summary>随机挑一个 clip（防机械重复）；无素材返回 null。</summary>
        public AudioClip PickClip()
        {
            AudioClip[] all = ResolveClips();
            if (all == null || all.Length == 0) return null;
            return all[Random.Range(0, all.Length)];
        }
    }

    [Tooltip("音频条目表。代码按 key 查找；同名 key 后者覆盖前者（启动时校验重复）")]
    public SoundEntry[] entries = new SoundEntry[0];

    Dictionary<string, SoundEntry> _lookup;

    /// <summary>按 key 取条目；无则返回 null（不报错——素材/配置未就绪时静默）。</summary>
    public SoundEntry GetEntry(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_lookup == null)
        {
            _lookup = new Dictionary<string, SoundEntry>();
            if (entries != null)
            {
                foreach (SoundEntry e in entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.key)) continue;
                    _lookup[e.key] = e; // 后者覆盖前者
                }
            }
        }
        SoundEntry found;
        _lookup.TryGetValue(key, out found);
        return found;
    }

    /// <summary>编辑器 / 热重载后调用，使运行时缓存失效。</summary>
    public void InvalidateLookup() { _lookup = null; }
}
