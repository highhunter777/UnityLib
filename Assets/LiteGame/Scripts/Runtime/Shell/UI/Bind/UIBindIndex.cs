using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>
    /// 名字 → 控件索引（M4 §2.4）：受控 API 的查询底座。两种来源——BindNode 标记（路径 A，BindIndexBuilder）
    /// / Designer 字段登记（路径 B，UIBindBase.RegisterControl）。受控语义在此收口：
    /// 绑按钮 = 替换式（重绑先移除旧监听，池化复用安全）；SetText = TMP 优先回退 UGUI Text；未命中/类型不符 = 抛。
    /// </summary>
    public sealed class UIBindIndex
    {
        public static readonly UIBindIndex Empty = new UIBindIndex(new Dictionary<string, Component>());

        private readonly Dictionary<string, Component> _controls;

        public UIBindIndex(Dictionary<string, Component> controls)
        {
            _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        }

        public T Get<T>(string name) where T : Component
        {
            if (!_controls.TryGetValue(name, out var c) || c == null)
                throw new KeyNotFoundException($"绑定索引未命中:{name}——核对 BindNode.BindName / Designer 登记");
            if (c is T typed) return typed;
            throw new InvalidOperationException(
                $"绑定索引[{name}] 类型不符:期望 {typeof(T).Name} 实得 {c.GetType().Name}");
        }

        public bool Has(string name) => _controls.ContainsKey(name);

        /// <summary>替换式绑按钮：同名重绑先移除旧监听（池化复用/重绑安全）；onClick 抛经 SafeCall 隔离。</summary>
        public void BindButton(string name, Action onClick)
        {
            var btn = Get<Button>(name);
            btn.onClick.RemoveAllListeners();
            UnityEngine.Events.UnityAction h = () => SafeCall.Invoke(onClick, $"OnButton[{name}]");
            btn.onClick.AddListener(h);
        }

        public void UnbindButton(string name)
        {
            if (TryGet<Button>(name, out var btn)) btn.onClick.RemoveAllListeners();
        }

        /// <summary>解绑全部按钮监听（界面 OnHide 时调用——池化复用跨环境的安全垫）。</summary>
        public void UnbindAll()
        {
            foreach (var c in _controls.Values)
                if (c is Button btn) btn.onClick.RemoveAllListeners();
        }

        public void SetText(string name, string value)
        {
            if (TryGet<TMPro.TMP_Text>(name, out var tmp)) { tmp.text = value; return; }
            if (TryGet<UnityEngine.UI.Text>(name, out var legacy)) { legacy.text = value; return; }
            throw new InvalidOperationException($"绑定索引[{name}] 无 Text/TMP_Text 组件");
        }

        public void SetVisible(string name, bool visible)
            => Get<Component>(name).gameObject.SetActive(visible);

        public void SetInteractable(string name, bool on)
        {
            if (TryGet<Selectable>(name, out var sel)) { sel.interactable = on; return; }
            throw new InvalidOperationException($"绑定索引[{name}] 无 Selectable 组件");
        }

        public void SetAnchoredPosition(string name, Vector2 pos)
        {
            if (TryGet<RectTransform>(name, out var rt)) { rt.anchoredPosition = pos; return; }
            throw new InvalidOperationException($"绑定索引[{name}] 无 RectTransform");
        }

        private bool TryGet<T>(string name, out T control) where T : Component
        {
            control = _controls.TryGetValue(name, out var c) ? c as T : null;
            return control != null;
        }
    }
}
