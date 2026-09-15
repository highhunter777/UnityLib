using System;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 界面运行时实例（M4 §2.1）：七态机 + 逻辑持有 + 画布就位。
    /// 状态迁移全部走本类守卫方法（非法迁移当场抛——fail-fast 精神 §3.4）；
    /// 逻辑回调统一经 SafeCall（单个回调抛 = 该界面降级，不炸壳——错误语义同事件桥）。
    /// 画布契约：prefab 根自带 Canvas（overrideSorting）+ CanvasGroup 最佳；缺失则壳补齐，sortingOrder 由层级组分配。
    /// </summary>
    public sealed class UIForm
    {
        public int Id { get; }
        public UIFormInfo Info { get; }
        public UIFormState State { get; private set; }
        public GameObject Root { get; }
        public Canvas Canvas { get; }
        public CanvasGroup CanvasGroup { get; }
        public IUIFormLogic Logic { get; set; } = NullUIFormLogic.Instance;

        public UIForm(UIFormInfo info, GameObject root)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            Id = info.Id;
            Root = root ? root : throw new ArgumentNullException(nameof(root));
            Canvas = root.GetComponent<Canvas>() ?? root.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.overrideSorting = true;
            CanvasGroup = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
        }

        // ---- 生命周期迁移（UIService 编排调用；守卫 + 逻辑回调定序）----

        /// <summary>首次打开：Loading → OnInit → OnShow → Active。</summary>
        internal void EnterActiveFromLoading(IUIData data)
        {
            Transit(UIFormState.Loading, UIFormState.Active);
            SafeCall.Invoke(() => Logic.OnInit(this, data), $"UIForm[{Id}].OnInit");
            SafeCall.Invoke(() => Logic.OnShow(data), $"UIForm[{Id}].OnShow");
        }

        /// <summary>池化复用：Recycled →（SetActive true）→ OnShow → Active。OnInit 不重跑。</summary>
        internal void EnterActiveFromRecycled(IUIData data)
        {
            Transit(UIFormState.Recycled, UIFormState.Active);
            Root.SetActive(true);
            SafeCall.Invoke(() => Logic.OnShow(data), $"UIForm[{Id}].OnShow");
        }

        internal void EnterPaused()
        {
            Transit(UIFormState.Active, UIFormState.Paused);
            SafeCall.Invoke(() => Logic.OnPause(), $"UIForm[{Id}].OnPause");
        }

        internal void EnterActiveFromPaused()
        {
            Transit(UIFormState.Paused, UIFormState.Active);
            SafeCall.Invoke(() => Logic.OnShow(null), $"UIForm[{Id}].OnShow");
        }

        internal void EnterCovered()
        {
            Transit(UIFormState.Active, UIFormState.Covered);
            SafeCall.Invoke(() => Logic.OnCover(), $"UIForm[{Id}].OnCover");
        }

        internal void EnterActiveFromCovered()
        {
            Transit(UIFormState.Covered, UIFormState.Active);
            SafeCall.Invoke(() => Logic.OnReveal(), $"UIForm[{Id}].OnReveal");
        }

        /// <summary>开始关闭：OnHide 已调，停在 Closing——§2.2 转场策略可在此等待动画后再 Recycle。</summary>
        internal void EnterClosing()
        {
            Transit(UIFormState.Active, UIFormState.Closing);
            SafeCall.Invoke(() => Logic.OnHide(), $"UIForm[{Id}].OnHide");

            // 界面级订阅清零：OnHide 之后、落池之前（与按钮 UnbindAll 同一时点语义——
            // 池化复用跨环境的安全垫，防"回收期间事件打进已关闭界面"）。
            _subs?.Dispose();
            _subs = null;                              // 置空以支持池化复用：下次显示时按需重建
        }

        private SubscriptionBag _subs;

        /// <summary>
        /// 界面级订阅袋：订阅的事件随界面关闭自动清零（EnterClosing 统一 Dispose），池化复用安全。
        /// 用法：<c>form.Subscriptions.Add(events.Subscribe&lt;XxxEvent&gt;(OnXxx));</c>
        /// C# 侧界面逻辑订阅事件一律挂这里，不要裸订阅——否则关界面后通道仍持回调（泄漏 +
        /// 复用后回调打进新界面）。Dispose 后误用会当场抛 ObjectDisposedException。
        /// （Lua 侧界面逻辑不适用：走 <c>events.on</c> 并在界面 OnHide 里调用其返回的注销委托。）
        /// </summary>
        public SubscriptionBag Subscriptions => _subs ??= new SubscriptionBag();

        /// <summary>落池：Closing → Recycled（SetActive false）。</summary>
        internal void Recycle()
        {
            Transit(UIFormState.Closing, UIFormState.Recycled);
            Root.SetActive(false);
        }

        /// <summary>OnUpdate 派发（UIService.Tick，仅 Active 态会走到这里）。</summary>
        internal void RaiseUpdate(float deltaTime)
        {
            SafeCall.Invoke(() => Logic.OnUpdate(deltaTime), $"UIForm[{Id}].OnUpdate");
        }

        internal void AssignDepth(int sortingOrder)
        {
            if (Canvas != null) Canvas.sortingOrder = sortingOrder;
        }

        private void Transit(UIFormState from, UIFormState to)
        {
            if (State != from)
                throw new InvalidOperationException($"UIForm[{Id}] 非法状态迁移:{State} → {to}（期望自 {from}）");
            State = to;
        }
    }
}
