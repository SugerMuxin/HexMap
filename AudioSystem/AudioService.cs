using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局音频门面（通用、零游戏类型依赖）——BGM 通道 + SFX 源池 + 音量记忆。
///
/// 能力：
///  - PlayBgm / StopBgm：BGM 循环播放，淡入淡出；
///  - PlaySfxAtPoint：3D 定位一次性音效（脚步声、水花、命中……播在声源世界坐标）；
///  - PlaySfxFollow：3D 音效跟随某 Transform 播放直到自然结束（挂在角色身上的持续声等）；
///  - PlaySfx2D：无空间化音效（UI 等）；
///  - master / sfx / bgm 三档音量，PlayerPrefs 记忆，运行时即时生效。
///
/// 容错：素材缺失（bank 无 key / 无 clip）时静默跳过并一次性警告，绝不影响游戏逻辑。
/// 装配：挂到场景根对象（如 "[AudioSystem]"），Inspector 拖入 AudioBank 即可。
/// 通用层不引用任何游戏类型：资源按 语义 key 组织（见 SoundBank）。
/// </summary>
public class AudioService : MonoBehaviour
{
    public static AudioService Instance { get; private set; }

    [Header("资源库")]
    [Tooltip("SoundBank 资源（含全部音效/BGM 条目）")]
    public SoundBank bank;

    [Header("BGM")]
    [Tooltip("启动时自动播放 BGM")]
    public bool autoPlayBgm = true;
    [Tooltip("自动播放的 BGM key")]
    public string bgmKey = "bgm/main";
    [Range(0f, 8f)] public float bgmFadeSeconds = 1.5f;

    [Header("SFX 池")]
    [Tooltip("池内 AudioSource 数量（决定同时可发声的 3D 音效上限，脚步连发也够用）")]
    public int sfxSourceCount = 16;

    [Header("音量（PlayerPrefs 记忆）")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [Range(0f, 1f)] public float bgmVolume = 1f;

    const string PrefMaster = "Audio.masterVolume";
    const string PrefSfx = "Audio.sfxVolume";
    const string PrefBgm = "Audio.bgmVolume";

    AudioSource _bgmSource;
    AudioSource[] _pool;
    int _cursor;
    Coroutine _bgmFade;
    readonly List<ActiveSfx> _active = new List<ActiveSfx>();
    readonly HashSet<string> _warnedKeys = new HashSet<string>();

    /// <summary>当前 BGM 条目（音量重算用）</summary>
    SoundBank.SoundEntry _bgmEntryCache;

    class ActiveSfx
    {
        public AudioSource src;
        public SoundBank.SoundEntry entry;
        public Transform follow;
        public bool spatial;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // 重复实例（如多场景装配）——保活先来的
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadVolumes();

        // BGM 源（2D，挂本物体）
        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.playOnAwake = false;
        _bgmSource.loop = true;
        _bgmSource.spatialBlend = 0f;

        // SFX 池：每个源一个子物体（3D 定位需要各自独立的 transform.position）
        _pool = new AudioSource[Mathf.Max(1, sfxSourceCount)];
        for (int i = 0; i < _pool.Length; i++)
        {
            GameObject go = new GameObject("SfxSource" + i);
            go.transform.SetParent(transform, false);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            _pool[i] = src;
        }
    }

    void Start()
    {
        if (autoPlayBgm && !string.IsNullOrEmpty(bgmKey))
        {
            PlayBgm(bgmKey);
        }
    }

    void Update()
    {
        // 跟随型音效：源位置持续贴到目标；播放结束自动回收（池源可被复用）
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            ActiveSfx a = _active[i];
            if (a.src == null)
            {
                _active.RemoveAt(i);
                continue;
            }
            if (a.follow != null)
            {
                a.src.transform.position = a.follow.position;
            }
            if (!a.src.isPlaying)
            {
                _active.RemoveAt(i);
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ---------------- 音量 ----------------

    void LoadVolumes()
    {
        masterVolume = PlayerPrefs.GetFloat(PrefMaster, 1f);
        sfxVolume = PlayerPrefs.GetFloat(PrefSfx, 1f);
        bgmVolume = PlayerPrefs.GetFloat(PrefBgm, 1f);
    }

    void SaveVolumes()
    {
        PlayerPrefs.SetFloat(PrefMaster, masterVolume);
        PlayerPrefs.SetFloat(PrefSfx, sfxVolume);
        PlayerPrefs.SetFloat(PrefBgm, bgmVolume);
        PlayerPrefs.Save();
    }

    /// <summary>设置总音量（0~1），立即生效并记忆。</summary>
    public void SetMasterVolume(float v)
    {
        masterVolume = Mathf.Clamp01(v);
        ApplyVolumes();
        SaveVolumes();
    }

    /// <summary>设置音效音量（0~1），立即生效并记忆。</summary>
    public void SetSfxVolume(float v)
    {
        sfxVolume = Mathf.Clamp01(v);
        ApplyVolumes();
        SaveVolumes();
    }

    /// <summary>设置 BGM 音量（0~1），立即生效并记忆。</summary>
    public void SetBgmVolume(float v)
    {
        bgmVolume = Mathf.Clamp01(v);
        ApplyVolumes();
        SaveVolumes();
    }

    void ApplyVolumes()
    {
        if (_bgmSource != null)
        {
            float entryVol = _bgmEntryCache != null ? _bgmEntryCache.volume : 1f;
            _bgmSource.volume = Mathf.Clamp01(entryVol * bgmVolume * masterVolume);
        }
        foreach (ActiveSfx a in _active)
        {
            if (a.src != null && a.entry != null)
            {
                a.src.volume = CalcVolume(a.entry, a.spatial);
            }
        }
    }

    // ---------------- BGM ----------------

    /// <summary>播放 BGM（循环）。同 key 已在播则忽略；素材缺失静默。</summary>
    public void PlayBgm(string key)
    {
        SoundBank.SoundEntry entry = GetEntrySafe(key);
        AudioClip clip = entry != null ? entry.PickClip() : null;
        if (clip == null)
        {
            WarnOnce(key);
            return;
        }
        _bgmEntryCache = entry;
        _bgmSource.clip = clip;
        _bgmSource.loop = true;
        _bgmSource.spatialBlend = 0f;
        if (!_bgmSource.isPlaying)
        {
            _bgmSource.Play();
        }
        FadeTo(_bgmSource, entry.volume * bgmVolume * masterVolume, bgmFadeSeconds);
    }

    /// <summary>淡出并停止 BGM。</summary>
    public void StopBgm()
    {
        _bgmEntryCache = null;
        if (_bgmSource == null) return;
        if (_bgmFade != null) StopCoroutine(_bgmFade);
        _bgmFade = StartCoroutine(FadeOutAndStop(_bgmSource, bgmFadeSeconds));
    }

    void FadeTo(AudioSource src, float targetVolume, float seconds)
    {
        if (_bgmFade != null) StopCoroutine(_bgmFade);
        _bgmFade = StartCoroutine(FadeRoutine(src, targetVolume, seconds));
    }

    IEnumerator FadeRoutine(AudioSource src, float targetVolume, float seconds)
    {
        if (seconds <= 0.001f)
        {
            src.volume = targetVolume;
            yield break;
        }
        float from = src.volume;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / seconds;
            src.volume = Mathf.Lerp(from, targetVolume, Mathf.Clamp01(t));
            yield return null;
        }
        src.volume = targetVolume;
    }

    IEnumerator FadeOutAndStop(AudioSource src, float seconds)
    {
        yield return FadeRoutine(src, 0f, seconds);
        if (src != null)
        {
            src.Stop();
        }
    }

    // ---------------- SFX ----------------

    /// <summary>3D 定位一次性音效（播在声源世界坐标，按听者距离衰减）。素材缺失静默。</summary>
    public void PlaySfxAtPoint(string key, Vector3 position)
    {
        AudioSource src = PlaySfxCore(key, null, position, true, false);
        if (src != null)
        {
            src.Play();
        }
    }

    /// <summary>3D 音效跟随目标 Transform 播放到自然结束（挂在移动角色上的持续声等）。</summary>
    public void PlaySfxFollow(string key, Transform follow, bool loop = false)
    {
        if (follow == null) return;
        AudioSource src = PlaySfxCore(key, follow, follow.position, true, loop);
        if (src != null)
        {
            src.loop = loop;
            src.Play();
        }
    }

    /// <summary>2D 无空间化一次性音效（UI 等）。</summary>
    public void PlaySfx2D(string key)
    {
        AudioSource src = PlaySfxCore(key, null, Vector3.zero, false, false);
        if (src != null)
        {
            src.Play();
        }
    }

    AudioSource PlaySfxCore(string key, Transform follow, Vector3 position, bool spatial, bool loop)
    {
        if (bank == null) return null;
        SoundBank.SoundEntry entry = bank.GetEntry(key);
        AudioClip clip = entry != null ? entry.PickClip() : null;
        if (clip == null)
        {
            WarnOnce(key);
            return null;
        }

        AudioSource src = NextFreeSource();
        if (src == null) return null; // 池全忙且无空闲——丢这一步（脚步高频场景可接受）

        // 若抢占的是仍在 _active 中的源，先移除旧记录（防止旧 entry 参数串台）
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].src == src)
            {
                _active.RemoveAt(i);
                break;
            }
        }

        src.clip = clip;
        src.loop = loop;
        src.spatialBlend = spatial ? 1f : 0f;
        src.transform.position = spatial ? position : Vector3.zero;
        if (follow != null)
        {
            src.transform.position = follow.position;
        }
        src.pitch = Random.Range(entry.pitchMin, entry.pitchMax);
        src.volume = CalcVolume(entry, spatial);
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = Mathf.Max(0.01f, entry.minDistance);
        src.maxDistance = Mathf.Max(src.minDistance, entry.maxDistance);

        _active.Add(new ActiveSfx { src = src, entry = entry, follow = follow, spatial = spatial });
        return src;
    }

    AudioSource NextFreeSource()
    {
        if (_pool == null || _pool.Length == 0) return null;
        // 先找空闲；全忙则轮转抢占最旧的
        for (int i = 0; i < _pool.Length; i++)
        {
            if (!_pool[i].isPlaying) return _pool[i];
        }
        AudioSource victim = _pool[_cursor];
        _cursor = (_cursor + 1) % _pool.Length;
        return victim;
    }

    float CalcVolume(SoundBank.SoundEntry entry, bool spatial)
    {
        float v = entry.volume * masterVolume;
        if (spatial) v *= sfxVolume;
        return Mathf.Clamp01(v);
    }

    // ---------------- 内部 ----------------

    SoundBank.SoundEntry GetEntrySafe(string key)
    {
        return bank != null ? bank.GetEntry(key) : null;
    }

    /// <summary>素材缺失时每个 key 只警告一次（防每步刷屏），并提示补素材路径。</summary>
    void WarnOnce(string key)
    {
        if (string.IsNullOrEmpty(key) || _warnedKeys.Contains(key)) return;
        _warnedKeys.Add(key);
        SoundBank.SoundEntry e = GetEntrySafe(key);
        string hint = e != null && !string.IsNullOrEmpty(e.autoLoadFolder)
            ? "请把素材文件放入 Resources/" + e.autoLoadFolder + "/"
            : "请先在 AudioBank 中为 key 配置 clips 或 autoLoadFolder";
        Debug.LogWarning("[Audio] 音效 key 无可用素材，已静默跳过: " + key + "（" + hint + "）");
    }
}
