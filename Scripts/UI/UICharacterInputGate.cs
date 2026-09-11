using NaughtyCharacter;
using UnityEngine;

/// <summary>
/// 游戏侧「UI 模态 → 游戏输入」门禁桥接器（挂在场景 [UIRoot] 上）。
///
/// 通用 UI 层（Assets/UISystem）刻意不引用任何游戏类型；本组件订阅
/// `HexUI.UIManager.BlockingStateChanged`，把"有阻塞面板打开"这一事实落到：
///   1. `HexControlMode.uiModalActive` —— 让角色点击移动（HeroController）/ 地图编辑（HexMapEditor）/
///      种植输入（PlantingInput）让出鼠标；
///   2. 每帧冻结本地角色输入（清移动 / 清跳跃 / 回正视角），避免"点 UI 时角色还在跑、视角还在转"；
///   3. 释放鼠标指针（面板要能点）；可选锁定旧 RTS 相机（HexMapCamera.Locked）。
///
/// **完全新增**：不修改 NaughtyCharacter / PlayerController，只调用其公开 API
/// （Character.SetMovementInput / SetJumpInput / SetControlRotation / GetControlRotation）。
/// 执行顺序设为 100：在 PlayerController 于 Character.Update 里写完输入之后再覆盖，
/// 并在 FixedUpdate（Character 的移动 tick）同样覆盖一次。
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class UICharacterInputGate : MonoBehaviour
{
    public static UICharacterInputGate Instance { get; private set; }

    [Tooltip("模态 UI 打开时锁定旧 RTS 相机（HexMapCamera）")]
    public bool lockHexMapCamera = true;
    [Tooltip("模态 UI 打开时释放鼠标指针（需要点 UI）")]
    public bool releaseCursor = true;
    [Tooltip("打印门禁状态切换日志")]
    public bool logState = false;

    bool blocked;
    bool subscribed;
    Character character;
    Vector2 frozenRotation;
    bool hasFrozenRotation;
    CursorLockMode prevLockState;
    bool prevCursorVisible;
    bool hasSavedCursor;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[UI] 场景中存在多个 UICharacterInputGate，禁用后来者：" + name);
            enabled = false;
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        TrySubscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
        Apply(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Unsubscribe();
    }

    void Start()
    {
        // Start 在所有 Awake 之后：此时 UIManager.Instance 必然就绪（同场景）
        TrySubscribe();
        HexUI.UIManager manager = HexUI.UIManager.Instance;
        if (manager != null) Apply(manager.HasBlockingPanel);
    }

    void Update()
    {
        if (!subscribed) TrySubscribe();
    }

    void FixedUpdate()
    {
        FreezeTick();
    }

    void LateUpdate()
    {
        FreezeTick();
    }

    void TrySubscribe()
    {
        HexUI.UIManager manager = HexUI.UIManager.Instance;
        if (manager == null || subscribed) return;
        manager.BlockingStateChanged += Apply;
        subscribed = true;
    }

    void Unsubscribe()
    {
        HexUI.UIManager manager = HexUI.UIManager.Instance;
        if (manager != null) manager.BlockingStateChanged -= Apply;
        subscribed = false;
    }

    /// <summary>模态状态切换（UIManager.BlockingStateChanged 回调）。</summary>
    void Apply(bool state)
    {
        if (blocked == state)
        {
            HexControlMode.uiModalActive = state;   // 兜底同步（外部就地改过静态位时）
            return;
        }
        blocked = state;

        // 1) 输入仲裁位：角色点击移动 / 地图编辑 / 种植输入据此让出鼠标
        HexControlMode.uiModalActive = state;

        // 2) 旧 RTS 相机（保留系统）与 UI 抢鼠标时让位
        if (lockHexMapCamera) HexMapCamera.Locked = state;

        if (state)
        {
            character = FindLocalCharacter();
            if (character != null)
            {
                frozenRotation = character.GetControlRotation();
                hasFrozenRotation = true;
            }
            if (releaseCursor)
            {
                prevLockState = Cursor.lockState;
                prevCursorVisible = Cursor.visible;
                hasSavedCursor = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
        else
        {
            character = null;
            hasFrozenRotation = false;
            if (releaseCursor && hasSavedCursor)
            {
                Cursor.lockState = prevLockState;
                Cursor.visible = prevCursorVisible;
                hasSavedCursor = false;
            }
        }

        if (logState) Debug.Log("[UI] 输入门禁 " + (state ? "ON（阻塞角色输入）" : "OFF"));
    }

    /// <summary>每帧覆盖角色输入（清移动 / 清跳跃 / 视角回正），抵消鼠标视角漂移。</summary>
    void FreezeTick()
    {
        if (!blocked) return;

        if (character == null)
        {
            character = FindLocalCharacter();
            if (character == null) return;
            if (!hasFrozenRotation)
            {
                frozenRotation = character.GetControlRotation();
                hasFrozenRotation = true;
            }
        }

        character.SetMovementInput(Vector3.zero);
        character.SetJumpInput(false);
        if (hasFrozenRotation) character.SetControlRotation(frozenRotation);
    }

    /// <summary>找"本机可操控"的角色：跳过联机远端化身（RemoteSuppressed / 非 isLocalPlayer）。</summary>
    static Character FindLocalCharacter()
    {
        Character[] all = FindObjectsOfType<Character>();
        for (int i = 0; i < all.Length; i++)
        {
            Character c = all[i];
            if (c == null || c.RemoteSuppressed || !c.enabled || !c.gameObject.activeInHierarchy) continue;

            Mirror.NetworkIdentity identity = c.GetComponentInParent<Mirror.NetworkIdentity>();
            if (identity != null && !identity.isLocalPlayer) continue;

            return c;
        }
        return null;
    }
}
