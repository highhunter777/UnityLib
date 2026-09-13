using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>控件数据绑定承载件（设计方案 §4.7 绑定模式）：值 → 控件单向刷。
    /// 内置去重（值未变不刷——结构上防"每帧全量刷 UI"）；同一数据可建多个 binder（金币文本/图标各自刷）。</summary>
    public sealed class UIDataBinder<T> : IDisposable
    {
        private Action<T> _apply;
        private T _last;
        private bool _has;

        public UIDataBinder(Action<T> apply)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        /// <summary>写值：与上次相同则跳过（离散值映射语义，§4.7）。</summary>
        public void Set(T value)
        {
            if (_apply == null) throw new ObjectDisposedException(nameof(UIDataBinder<T>));
            if (_has && EqualityComparer<T>.Default.Equals(_last, value)) return;
            _last = value;
            _has = true;
            _apply(value);
        }

        public void Dispose() => _apply = null;
    }

    /// <summary>
    /// 控件基座（M4c §2.10）：可交互态 + 按压微动效 hook（走 UiFx 原语，不出第二套 tween 路径）。
    /// 组合型控件（StateButton/Stepper/RedDot...）继承本类；简单控件可直接挂。
    /// </summary>
    public class UIWidget : MonoBehaviour, IPointerClickHandler
    {
        [Header("UIWidget 基座")]
        [SerializeField] private bool interactable = true;
        [SerializeField] private bool pressFx = true;      // 点击微动效开关

        /// <summary>可交互态（多态视觉由子类 OnInteractableChanged 呈现）。</summary>
        public bool Interactable
        {
            get => interactable;
            set { if (interactable == value) return; interactable = value; OnInteractableChanged(value); }
        }

        /// <summary>点击微动效（默认脉冲；子类可覆写为按压缩放等）。</summary>
        public virtual void PlayPressFx()
        {
            if (!pressFx) return;
            var g = GetComponent<Graphic>();
            if (g != null) UiFx.Pulse(g);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (interactable) OnClick();
        }

        protected virtual void Awake() { }

        /// <summary>点击回调（子类覆写；UIWidget 自身即可当轻量按钮用）。</summary>
        protected virtual void OnClick() => PlayPressFx();

        protected virtual void OnInteractableChanged(bool value) { }
    }
}
