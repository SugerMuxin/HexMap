using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HexUI
{
    /// <summary>
    /// UI 管理器（通用层，**零游戏依赖**）。
    ///
    /// 职责：按"面板表"（UIPanelTable）注册面板，统一负责
    ///   打开 / 关闭 / 栈顺序 / 层级容器 / 互斥组 / 模态遮罩 / Esc 与热键 / 阻塞状态广播。
    ///
    /// 分层原则：本类不引用任何游戏类型（HexGrid / PlantingSystem / Mirror …）。
    /// 需要让游戏侧感知"UI 正在模态阻塞输入"时，游戏侧桥接器订阅 <see cref="BlockingStateChanged"/>，
    /// 自行落到 HexControlMode.uiModalActive（见 Assets/Scripts/UI/UICharacterInputGate.cs）。
    ///
    /// 场景结构约定（由 Editor/UISetupTool 一键装配）：
    ///   [UIRoot]                Canvas + CanvasScaler + GraphicRaycaster + UIManager + UICharacterInputGate
    ///     Panels/               常规面板（Background / Normal 层）
    ///     Modal Mask            模态遮罩（半透明全屏 Image + 无过渡 Button）
    ///     Popups/               弹窗（Popup / Overlay 层，位于遮罩之上）
    /// </summary>
    [DisallowMultipleComponent]
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("数据 / 引用")]
        [Tooltip("面板注册表（留空则按 tableResourcePath 从 Resources 加载）")]
        public UIPanelTable table;
        [Tooltip("常规面板容器（Background / Normal 层）")]
        public Transform panelsRoot;
        [Tooltip("弹窗容器（Popup / Overlay 层，位于遮罩之上）")]
        public Transform popupsRoot;
        [Tooltip("模态遮罩（半透明全屏 Image；留空按名字查找或自动创建）")]
        public Image modalMask;
        [Tooltip("面板表的 Resources 路径（不带扩展名）")]
        public string tableResourcePath = "Configs/Tables/UIPanelTable";

        [Header("行为")]
        [Tooltip("点遮罩关闭栈顶模态面板")]
        public bool closeTopOnMaskClick = true;
        [Tooltip("Esc 关闭栈顶可关闭面板")]
        public bool escClosesTop = true;
        [Tooltip("面板首次打开时自动应用中文 OS 字体（见 UIFontProvider）")]
        public bool autoApplyCjkFont = true;
        [Tooltip("打印打开/关闭日志（调试用）")]
        public bool logOpenClose = false;

        /// <summary>面板打开（已打开的面板重复 Open 用新 payload 刷新时也会触发）。</summary>
        public event Action<UIPanel> PanelOpened;
        /// <summary>面板关闭。</summary>
        public event Action<UIPanel> PanelClosed;
        /// <summary>阻塞状态变化（true = 存在 blockGameInput 的面板打开）。游戏侧据此仲裁输入。</summary>
        public event Action<bool> BlockingStateChanged;
        /// <summary>面板栈发生变化。</summary>
        public event Action StackChanged;

        readonly List<UIPanel> openPanels = new List<UIPanel>();
        readonly Dictionary<string, UIPanel> instances = new Dictionary<string, UIPanel>();
        readonly Dictionary<UIPanel, int> openSeq = new Dictionary<UIPanel, int>();
        readonly List<UIPanel> sortBuffer = new List<UIPanel>();
        int seqCounter;
        bool blocking;
        /// <summary>遮罩当前所贴的面板（= 视觉最上层模态面板；无模态时为 null）。</summary>
        UIPanel maskOwner;

        #region 只读状态

        /// <summary>是否有面板正在阻塞游戏输入。</summary>
        public bool HasBlockingPanel { get { return blocking; } }
        /// <summary>当前打开的面板（按打开顺序）。</summary>
        public IReadOnlyList<UIPanel> OpenPanels { get { return openPanels; } }
        public int OpenCount { get { return openPanels.Count; } }
        /// <summary>栈顶（最后打开）的 id（无则 null）。</summary>
        public string TopId { get { UIPanel p = TopPanel; return p != null ? p.id : null; } }
        /// <summary>栈顶面板（无则 null）。</summary>
        public UIPanel TopPanel { get { return openPanels.Count > 0 ? openPanels[openPanels.Count - 1] : null; } }

        #endregion

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[UI] 场景中存在多个 UIManager，保留先者，禁用后来者：" + name);
                enabled = false;
                return;
            }
            Instance = this;
            EnsureRefs();
            PreloadAll();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (table == null) EnsureRefs();
            if (table == null) return;

            // 正在输入框里打字时不响应热键（否则输入名字时按 B 会弹背包）
            if (IsTypingInInputField()) return;

            if (escClosesTop && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseTop();
                return;
            }

            UIPanelTable.Entry[] entries = table.entries;
            if (entries == null) return;
            for (int i = 0; i < entries.Length; i++)
            {
                UIPanelTable.Entry e = entries[i];
                if (e == null || e.hotkey == KeyCode.None) continue;
                if (Input.GetKeyDown(e.hotkey))
                {
                    Toggle(e.id);
                    return;
                }
            }
        }

        #region 引用准备

        /// <summary>补齐引用（表 / 容器 / 遮罩）；可重复调用。</summary>
        public void EnsureRefs()
        {
            if (table == null && !string.IsNullOrEmpty(tableResourcePath))
            {
                table = Resources.Load<UIPanelTable>(tableResourcePath);
            }
            if (panelsRoot == null) panelsRoot = FindOrCreateChild("Panels");
            if (popupsRoot == null) popupsRoot = FindOrCreateChild("Popups");
            if (modalMask == null)
            {
                Transform found = FindChildByName("Modal Mask");
                // 运行期遮罩会跟着模态面板进它的容器，所以还要往容器里找一次（避免重复创建）
                if (found == null && panelsRoot != null) found = FindChildByName(panelsRoot, "Modal Mask");
                if (found == null && popupsRoot != null) found = FindChildByName(popupsRoot, "Modal Mask");
                if (found != null) modalMask = found.GetComponent<Image>();
            }
            if (modalMask == null) modalMask = CreateMask();
            SetupMask();
        }

        /// <summary>测试 / 外部装配用：一次性注入引用。</summary>
        public void Initialize(UIPanelTable panelTable, Transform panels, Transform popups, Image mask)
        {
            table = panelTable;
            panelsRoot = panels;
            popupsRoot = popups;
            modalMask = mask;
            EnsureRefs();
        }

        Transform FindOrCreateChild(string childName)
        {
            Transform found = FindChildByName(childName);
            if (found != null) return found;
            RectTransform self = transform as RectTransform;
            if (self == null) return null;   // 非 UI 根，不自动建子物体
            GameObject go = new GameObject(childName, typeof(RectTransform));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(self, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        Transform FindChildByName(string childName)
        {
            return FindChildByName(transform, childName);
        }

        static Transform FindChildByName(Transform parent, string childName)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c.name == childName) return c;
            }
            return null;
        }

        Image CreateMask()
        {
            RectTransform self = transform as RectTransform;
            if (self == null) return null;
            GameObject go = new GameObject("Modal Mask", typeof(RectTransform), typeof(Image));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.SetParent(self, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.55f);
            // 静态停放位置：常规面板容器**之下**（安全默认值——全屏遮罩绝不能盖住面板）。
            // 运行期真正的位置由 RefreshMask() 在每次打开/关闭模态面板时重算：
            // 遮罩会被挪到"最上层模态面板"正下方（同一容器内）。
            if (panelsRoot != null && panelsRoot.parent == self)
            {
                rt.SetSiblingIndex(panelsRoot.GetSiblingIndex());
            }
            img.gameObject.SetActive(false);
            return img;
        }

        void SetupMask()
        {
            if (modalMask == null) return;
            modalMask.raycastTarget = true;
            Button b = modalMask.GetComponent<Button>();
            if (b == null) b = modalMask.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.RemoveAllListeners();
            if (closeTopOnMaskClick) b.onClick.AddListener(OnMaskClicked);
            if (modalMask.gameObject.activeSelf) modalMask.gameObject.SetActive(false);
        }

        void OnMaskClicked()
        {
            // 关闭"遮罩当前所贴的那个面板"（= 视觉最上层模态面板）。
            // 不能直接用 CloseTop()：它按【打开顺序】取最后一个，而遮罩/排序按
            // 【层 → order → 打开序】决定谁在上面 —— 面板表里 order 不同时两者会不一致，
            // 表现为"点外部关掉的是另一个面板"。
            if (maskOwner != null && maskOwner.IsOpen)
            {
                ClosePanel(maskOwner);
                return;
            }
            CloseTop(true);
        }

        #endregion

        #region 打开 / 关闭

        /// <summary>打开面板（已在打开状态则以新 payload 刷新）。返回是否成功。</summary>
        public bool Open(string id, object payload = null)
        {
            if (string.IsNullOrEmpty(id)) return false;
            EnsureRefs();
            UIPanelTable.Entry entry = table != null ? table.Find(id) : null;
            if (entry == null)
            {
                Debug.LogError("[UI] 面板表里没有 id = " + id + "（先跑菜单 Tools/UI/一键装配）");
                return false;
            }

            UIPanel panel = EnsureInstance(entry);
            if (panel == null) return false;

            bool wasOpen = panel.IsOpen;
            if (!wasOpen)
            {
                CloseExclusive(entry);

                Transform container = ContainerFor(entry.layer);
                if (container != null && panel.transform.parent != container)
                {
                    panel.transform.SetParent(container, false);
                }
                panel.gameObject.SetActive(true);
                if (autoApplyCjkFont) UIFontProvider.ApplyTo(panel.gameObject);
                openPanels.Add(panel);
                openSeq[panel] = ++seqCounter;
                if (logOpenClose) Debug.Log("[UI] 打开 " + id);
            }

            panel.InternalShow(payload);
            Reorder();
            RefreshMask();
            RaiseBlocking();

            if (PanelOpened != null) PanelOpened(panel);
            if (StackChanged != null) StackChanged();
            return true;
        }

        /// <summary>关闭面板。</summary>
        public bool Close(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            UIPanel panel;
            if (!instances.TryGetValue(id, out panel) || panel == null || !panel.IsOpen) return false;
            return ClosePanel(panel);
        }

        /// <summary>关闭栈顶面板（ignoreEscFlag=true 时无视表中 escClose 配置，用于遮罩点击）。</summary>
        public bool CloseTop(bool ignoreEscFlag = false)
        {
            for (int i = openPanels.Count - 1; i >= 0; i--)
            {
                UIPanel p = openPanels[i];
                if (p == null) continue;
                UIPanelTable.Entry e = table != null ? table.Find(p.id) : null;
                if (!ignoreEscFlag && e != null && !e.escClose) continue;
                return ClosePanel(p);
            }
            return false;
        }

        /// <summary>关闭全部面板。</summary>
        public void CloseAll()
        {
            for (int i = openPanels.Count - 1; i >= 0; i--)
            {
                ClosePanel(openPanels[i]);
            }
        }

        /// <summary>开 / 关切换（热键用）。</summary>
        public bool Toggle(string id, object payload = null)
        {
            return IsOpen(id) ? Close(id) : Open(id, payload);
        }

        /// <summary>面板是否处于打开状态。</summary>
        public bool IsOpen(string id)
        {
            UIPanel panel;
            return !string.IsNullOrEmpty(id) && instances.TryGetValue(id, out panel) && panel != null && panel.IsOpen;
        }

        /// <summary>取已实例化的面板（未实例化返回 null）。</summary>
        public UIPanel GetPanel(string id)
        {
            UIPanel panel;
            return (!string.IsNullOrEmpty(id) && instances.TryGetValue(id, out panel)) ? panel : null;
        }

        /// <summary>预实例化表里 preload=true 的面板（Awake 自动调用）。</summary>
        public int PreloadAll()
        {
            if (table == null || table.entries == null) return 0;
            int n = 0;
            for (int i = 0; i < table.entries.Length; i++)
            {
                UIPanelTable.Entry e = table.entries[i];
                if (e == null || !e.preload) continue;
                if (EnsureInstance(e) != null) n++;
            }
            return n;
        }

        bool ClosePanel(UIPanel panel)
        {
            if (panel == null) return false;
            panel.InternalHide();
            panel.gameObject.SetActive(false);
            openPanels.Remove(panel);
            openSeq.Remove(panel);
            if (logOpenClose) Debug.Log("[UI] 关闭 " + panel.id);

            Reorder();          // 先重排兄弟顺序，RefreshMask 才能把遮罩摆到正确的面板下方
            RefreshMask();
            RaiseBlocking();
            if (PanelClosed != null) PanelClosed(panel);
            if (StackChanged != null) StackChanged();
            return true;
        }

        void CloseExclusive(UIPanelTable.Entry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.exclusiveGroup)) return;
            for (int i = openPanels.Count - 1; i >= 0; i--)
            {
                UIPanel p = openPanels[i];
                if (p == null || p.id == entry.id) continue;
                UIPanelTable.Entry other = table.Find(p.id);
                if (other != null && other.exclusiveGroup == entry.exclusiveGroup)
                {
                    ClosePanel(p);
                }
            }
        }

        UIPanel EnsureInstance(UIPanelTable.Entry entry)
        {
            UIPanel panel;
            if (instances.TryGetValue(entry.id, out panel) && panel != null)
            {
                panel.manager = this;
                return panel;
            }

            GameObject prefab = table.ResolvePrefab(entry);
            if (prefab == null)
            {
                Debug.LogError("[UI] 面板 " + entry.id + " 没有可用 prefab（表里未指定 prefab，或 Resources 路径不存在：" + entry.prefabPath + "）");
                return null;
            }

            Transform container = ContainerFor(entry.layer);
            GameObject go = Instantiate(prefab, container != null ? container : transform, false);
            go.name = prefab.name;
            go.SetActive(false);

            panel = go.GetComponent<UIPanel>();
            if (panel == null) panel = go.GetComponentInChildren<UIPanel>(true);
            if (panel == null)
            {
                Debug.LogError("[UI] 面板 prefab " + prefab.name + " 根物体（或其子物体）上没有 UIPanel 脚本");
                DestroyObject(go);
                return null;
            }

            if (string.IsNullOrEmpty(panel.id)) panel.id = entry.id;
            panel.manager = this;
            instances[entry.id] = panel;
            return panel;
        }

        static void DestroyObject(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        Transform ContainerFor(UIPanelLayer layer)
        {
            return layer >= UIPanelLayer.Popup ? popupsRoot : panelsRoot;
        }

        #endregion

        #region 排序 / 遮罩 / 阻塞

        void Reorder()
        {
            SortContainer(panelsRoot);
            SortContainer(popupsRoot);
        }

        void SortContainer(Transform container)
        {
            if (container == null) return;
            sortBuffer.Clear();
            for (int i = 0; i < container.childCount; i++)
            {
                UIPanel p = container.GetChild(i).GetComponent<UIPanel>();
                if (p != null && p.IsOpen) sortBuffer.Add(p);
            }
            sortBuffer.Sort(ComparePanel);
            for (int i = 0; i < sortBuffer.Count; i++)
            {
                sortBuffer[i].transform.SetSiblingIndex(i);
            }
            sortBuffer.Clear();
        }

        int ComparePanel(UIPanel a, UIPanel b)
        {
            UIPanelTable.Entry ea = table != null ? table.Find(a.id) : null;
            UIPanelTable.Entry eb = table != null ? table.Find(b.id) : null;
            int la = ea != null ? (int)ea.layer : 0;
            int lb = eb != null ? (int)eb.layer : 0;
            if (la != lb) return la.CompareTo(lb);
            int oa = ea != null ? ea.order : 0;
            int ob = eb != null ? eb.order : 0;
            if (oa != ob) return oa.CompareTo(ob);
            int sa, sb;
            openSeq.TryGetValue(a, out sa);
            openSeq.TryGetValue(b, out sb);
            return sa.CompareTo(sb);
        }

        /// <summary>
        /// 刷新模态遮罩：显示 / 隐藏，并把遮罩摆到"最上层模态面板"的正下方（同一容器内）。
        ///
        /// 为什么必须"紧贴面板下方"、不能固定夹在 Panels 与 Popups 之间：
        /// 遮罩是**全屏 Image + Button**，谁先接到射线由 sibling 顺序决定。
        /// 固定放在 Panels 之后 ⇒ 遮罩盖住 Panels 里的所有模态面板，
        /// 点击面板任意位置（含"创建主机"按钮）都先命中遮罩，被当成"点了遮罩"而立刻关掉面板，
        /// 表现为"Play 后点 UI 任何位置都会关闭 UI、根本没法操作面板"。
        /// 摆到面板正下方 ⇒ 既挡住它下面的面板与游戏世界，又不挡面板自己。
        /// </summary>
        void RefreshMask()
        {
            if (modalMask == null) return;

            UIPanel topModal = TopModalPanel();
            maskOwner = topModal;
            if (topModal == null)
            {
                if (modalMask.gameObject.activeSelf) modalMask.gameObject.SetActive(false);
                return;
            }

            Transform host = topModal.transform.parent;
            if (host != null && modalMask.transform.parent != host)
            {
                modalMask.transform.SetParent(host, false);
                StretchFull(modalMask.transform as RectTransform);
            }
            if (!modalMask.gameObject.activeSelf) modalMask.gameObject.SetActive(true);

            // 抢占面板当前索引 ⇒ 面板被挤到 +1，遮罩正好落在它下面
            modalMask.transform.SetSiblingIndex(topModal.transform.GetSiblingIndex());
        }

        /// <summary>最上层的模态面板（按与 Reorder 相同的层 / order / 打开序比较；无则 null）。</summary>
        UIPanel TopModalPanel()
        {
            UIPanel best = null;
            for (int i = 0; i < openPanels.Count; i++)
            {
                UIPanel p = openPanels[i];
                if (p == null) continue;
                UIPanelTable.Entry e = table != null ? table.Find(p.id) : null;
                if (e == null || !e.modal) continue;
                if (best == null || ComparePanel(p, best) > 0) best = p;
            }
            return best;
        }

        static void StretchFull(RectTransform rt)
        {
            if (rt == null) return;
            rt.localScale = Vector3.one;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        void RaiseBlocking()
        {
            bool any = false;
            for (int i = 0; i < openPanels.Count; i++)
            {
                UIPanelTable.Entry e = table != null ? table.Find(openPanels[i].id) : null;
                if (e != null && e.blockGameInput) { any = true; break; }
            }
            if (any == blocking) return;
            blocking = any;
            if (BlockingStateChanged != null) BlockingStateChanged(blocking);
        }

        static bool IsTypingInInputField()
        {
            if (EventSystem.current == null) return false;
            GameObject sel = EventSystem.current.currentSelectedGameObject;
            if (sel == null) return false;
            InputField field = sel.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        #endregion
    }
}
