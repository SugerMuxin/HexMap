using UnityEngine;

/// <summary>
/// 摆放式 3D 环境音源（瀑布、河流、风声……）。
///
/// 用法：把含本组件的预制体（AmbientEmitter.prefab）拖到场景声源处，
/// Inspector 配 bank + ambientKey 即可。组件自动按"听者距离"淡入淡出：
///  - 进入 triggerRadius → 平滑淡入循环播放（随机起始相位，多实例不同相）；
///  - 离开 → 平滑淡出并停止（不空转）；
/// 音量实时跟随 AudioService 的 sfx/master 音量。
/// 素材缺失（key 无 clip）时保持静默，仅警告一次。
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AmbientAudioSource : MonoBehaviour
{
    [Header("资源")]
    [Tooltip("SoundBank（留空自动取 AudioService.Instance.bank）")]
    public SoundBank bank;
    [Tooltip("环境音 key，如 ambient/waterfall、ambient/river")]
    public string ambientKey = "ambient/waterfall";

    [Header("触发范围")]
    [Tooltip("听者进入该半径后淡入，离开后淡出")]
    public float triggerRadius = 35f;

    [Header("淡入淡出（秒）")]
    public float fadeInSeconds = 2f;
    public float fadeOutSeconds = 3f;

    [Header("音量")]
    [Range(0f, 2f)] public float volumeScale = 1f;

    AudioSource _src;
    SoundBank.SoundEntry _entry;
    AudioClip _clip;
    bool _hasContent;
    bool _playing;

    float _curVolume;   // 当前插值音量（相对 entry.volume*volumeScale 的 0~1 系数）
    float _targetVolume;
    float _distanceTimer;

    static AudioListener _listener;
    static float _listenerSearchTimer;

    const float DistanceInterval = 0.25f;

    void OnEnable()
    {
        _src = GetComponent<AudioSource>();
        _src.loop = true;
        _src.playOnAwake = false;
        _src.spatialBlend = 1f; // 环境音永远 3D 定位

        ResolveContent();
        _curVolume = 0f;
        _targetVolume = 0f;
        _src.volume = 0f;
    }

    void OnDisable()
    {
        if (_src != null && _src.isPlaying)
        {
            _src.Stop();
        }
        _playing = false;
    }

    void ResolveContent()
    {
        if (bank == null && AudioService.Instance != null)
        {
            bank = AudioService.Instance.bank;
        }
        _entry = bank != null ? bank.GetEntry(ambientKey) : null;
        _clip = _entry != null ? _entry.PickClip() : null;
        _hasContent = _clip != null;
        if (!_hasContent)
        {
            string hint = _entry != null && !string.IsNullOrEmpty(_entry.autoLoadFolder)
                ? "请把素材文件放入 Resources/" + _entry.autoLoadFolder + "/"
                : "请在 AudioBank 中配置 ambientKey 对应的 clips 或 autoLoadFolder";
            Debug.LogWarning("[Audio] 环境音源 " + gameObject.name + "（key=" + ambientKey + "）无可用素材，静默待命。" + hint, gameObject);
        }
    }

    void Update()
    {
        if (!_hasContent || _src == null) return;

        _distanceTimer -= Time.deltaTime;
        if (_distanceTimer <= 0f)
        {
            _distanceTimer = DistanceInterval;
            AudioListener listener = FindListener();
            if (listener != null)
            {
                float dist = (listener.transform.position - transform.position).magnitude;
                bool inside = dist <= triggerRadius;
                _targetVolume = inside ? 1f : 0f;
                if (inside && !_playing)
                {
                    StartPlayback();
                }
            }
        }

        // 淡入淡出插值：每帧走 fade 时长的 1/fade 比例
        float dt = Time.deltaTime;
        float delta = _targetVolume > _curVolume
            ? (fadeInSeconds > 0.001f ? dt / fadeInSeconds : 1f)
            : (fadeOutSeconds > 0.001f ? dt / fadeOutSeconds : 1f);
        _curVolume = Mathf.Clamp01(Mathf.MoveTowards(_curVolume, _targetVolume, delta));

        // 音量实时跟随 AudioService
        float globalScale = 1f;
        if (AudioService.Instance != null)
        {
            globalScale = AudioService.Instance.masterVolume * AudioService.Instance.sfxVolume;
        }
        float vol = _curVolume * volumeScale * (_entry != null ? _entry.volume : 1f) * globalScale;
        _src.volume = Mathf.Clamp01(vol);

        // 淡出到底 → 停止（避免无声空转）
        if (_playing && _targetVolume <= 0f && _curVolume <= 0.005f)
        {
            _src.Stop();
            _playing = false;
        }
    }

    void StartPlayback()
    {
        if (_src == null || _clip == null) return;
        _src.clip = _clip;
        // 随机起始相位：多实例同 key 不齐奏
        try { _src.time = Random.value * _clip.length; }
        catch { /* clip 不可 seek（如流式）时忽略 */ }
        _src.Play();
        _playing = true;
    }

    static AudioListener FindListener()
    {
        _listenerSearchTimer -= Time.deltaTime;
        if (_listener != null && _listener.enabled)
        {
            return _listener;
        }
        if (_listenerSearchTimer <= 0f)
        {
            _listenerSearchTimer = 2f;
            _listener = Object.FindObjectOfType<AudioListener>();
        }
        return _listener;
    }
}
