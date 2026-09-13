using System;
using System.Collections.Generic;
using LiteFramework;
using LiteGame.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame
{
    /// <summary>控件驱动方式（设计方案 §4.7 所有权互斥）：一个控件只能被一种方式驱动。</summary>
    public enum ControlDriver
    {
        None,               // 未驱动
        Bound,              // 数据绑定（UIDataBinder——值变了要刷）
        Command,            // 命令式（SetText/OnButton 等——有事要办）
    }

    /// <summary>
    /// 名字 → 控件索引（M4 §2.4）：受控 API 的查询底座。两种来源——BindNode 标记（路径 A，BindIndexBuilder）
    /// / Designer 字段登记（路径 B，UIBindBase.RegisterControl）。受控语义在此收口：
    /// 绑按钮 = 替换式（重绑先移除旧监听，池化复用安全）；SetText = TMP 优先回退 UGUI Text；未命中/类型不符 = 抛。
    /// **所有权互斥**（设计方案 §4.7/§453）：每个控件登记驱动方式，Bind 后禁命令式、命令式后禁 Bind——
    /// Debug 三宏下违例当场抛（"谁最后生效"竞态极难复现，必须启动期/使用期当场暴露）。
    /// </summary>
    public sealed class UIBindIndex
    {
        public static readonly UIBindIndex Empty = new UIBindIndex(new Dictionary<string, Component>());

        private readonly Dictionary<string, Component> _controls;
        private readonly Dictionary<string, ControlDriver> _drivers = new Dictionary<string, ControlDriver>(16);

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

        /// <summary>所有权登记：首次登记生效；异_kind 违例 Debug 下抛（release 信任 Debug 全绿）。</summary>
        public void MarkDriver(string name, ControlDriver kind)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
            _drivers.TryGetValue(name, out var current);
            if (current != ControlDriver.None && current != kind)
                throw new InvalidOperationException(
                    $"控件[{name}] 所有权互斥违例：已被 {(current == ControlDriver.Bound ? "数据绑定" : "命令式")} 驱动，" +
                    $"禁止再以 {(kind == ControlDriver.Bound ? "数据绑定" : "命令式")} 驱动（设计方案 §4.7——谁最后生效竞态）");
#endif
            _drivers[name] = kind;
        }

        /// <summary>
        /// 绑定驱动接管控件文本（金币走绑定用例）：返回 UIDataBinder，写值经内部通道
        /// （不触发命令式断言）；此后该控件的命令式 SetText 将违例抛。
        /// </summary>
        public UIDataBinder<T> BindText<T>(string name, Func<T, string> format)
        {
            if (format == null) throw new ArgumentNullException(nameof(format));
            MarkDriver(name, ControlDriver.Bound);
            return new UIDataBinder<T>(v => WriteTextRaw(name, format(v)));
        }

        public void BindButton(string name, Action onClick)
        {
            MarkDriver(name, ControlDriver.Command);
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

        /// <summary>命令式写文本（所有权登记：与 Bind 互斥）。</summary>
        public void SetText(string name, string value)
        {
            MarkDriver(name, ControlDriver.Command);
            if (TryGet<TMPro.TMP_Text>(name, out var tmp)) { tmp.text = value; return; }
            if (TryGet<UnityEngine.UI.Text>(name, out var legacy)) { legacy.text = value; return; }
            throw new InvalidOperationException($"绑定索引[{name}] 无 Text/TMP_Text 组件");
        }

        public void SetVisible(string name, bool visible)
        {
            MarkDriver(name, ControlDriver.Command);
            Get<Component>(name).gameObject.SetActive(visible);
        }

        public void SetInteractable(string name, bool on)
        {
            MarkDriver(name, ControlDriver.Command);
            if (TryGet<Selectable>(name, out var sel)) { sel.interactable = on; return; }
            throw new InvalidOperationException($"绑定索引[{name}] 无 Selectable 组件");
        }

        public void SetAnchoredPosition(string name, Vector2 pos)
        {
            MarkDriver(name, ControlDriver.Command);
            if (TryGet<RectTransform>(name, out var rt)) { rt.anchoredPosition = pos; return; }
            throw new InvalidOperationException($"绑定索引[{name}] 无 RectTransform");
        }

        /// <summary>绑定通道内部写（已登记 Bound——不走命令式断言）。</summary>
        private void WriteTextRaw(string name, string value)
        {
            if (TryGet<TMPro.TMP_Text>(name, out var tmp)) { tmp.text = value; return; }
            if (TryGet<UnityEngine.UI.Text>(name, out var legacy)) { legacy.text = value; return; }
        }

        private bool TryGet<T>(string name, out T control) where T : Component
        {
            control = _controls.TryGetValue(name, out var c) ? c as T : null;
            return control != null;
        }
    }
}

