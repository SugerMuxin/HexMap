using System;
using UnityEngine;

namespace HexUI
{
    /// <summary>
    /// 面板基类（挂在面板 prefab 根物体上）。
    ///
    /// 生命周期（由 UIManager 驱动，注意 prefab 实例默认 inactive，
    /// 因此**不要**在 Awake 里做初始化 —— 用 OnCreate）：
    ///   OnCreate()            首次打开前调用一次（找子控件、绑按钮、订阅事件）
    ///   OnShow(payload)       每次打开调用（payload 由 Open(id, payload) 传入，可为 null）
    ///   OnRefresh()           紧跟 OnShow 调用；外部数据变化时也可手动调
    ///   OnHide()              每次关闭调用（退订放这里或 OnDestroy）
    ///
    /// 通用层零游戏依赖：面板要访问游戏数据，请经游戏侧桥接器/接口，别直接引用 HexGrid 等类型。
    /// </summary>
    public abstract class UIPanel : MonoBehaviour
    {
        [Tooltip("面板 id（与面板表的 id 对应；装配工具自动填）")]
        public string id;

        [NonSerialized] public UIManager manager;

        bool created;

        /// <summary>面板是否处于打开状态（已实例化且激活）。</summary>
        public bool IsOpen { get { return gameObject.activeSelf; } }

        /// <summary>面板是否已完成 OnCreate。</summary>
        public bool IsCreated { get { return created; } }

        /// <summary>UIManager 调用：显示（内部会按需触发 OnCreate）。</summary>
        public void InternalShow(object payload)
        {
            if (!created)
            {
                created = true;
                OnCreate();
            }
            OnShow(payload);
            OnRefresh();
        }

        /// <summary>UIManager 调用：隐藏。</summary>
        public void InternalHide()
        {
            OnHide();
        }

        /// <summary>首次打开前调用一次（找子控件 / 绑按钮 / 订阅事件）。</summary>
        protected virtual void OnCreate() { }

        /// <summary>每次打开调用（payload 可为 null）。</summary>
        protected virtual void OnShow(object payload) { }

        /// <summary>每次关闭调用。</summary>
        protected virtual void OnHide() { }

        /// <summary>刷新显示（打开时自动调用；数据变化时可手动调用）。</summary>
        public virtual void OnRefresh() { }

        /// <summary>关闭自己（由表里的 id 定位）。</summary>
        public void Close()
        {
            if (manager != null) manager.Close(id);
        }

        /// <summary>打开另一个面板（走 UIManager 的层级/互斥规则）。</summary>
        public void OpenOther(string otherId, object payload = null)
        {
            if (manager != null) manager.Open(otherId, payload);
        }

        /// <summary>在当前面板内查找子控件（按名称，含未激活）。</summary>
        protected T FindChild<T>(string childName) where T : Component
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == childName)
                {
                    T c = all[i].GetComponent<T>();
                    if (c != null) return c;
                }
            }
            Debug.LogWarning("[UI] 面板 " + id + " 找不到子控件 " + childName + " (" + typeof(T).Name + ")");
            return null;
        }
    }
}
