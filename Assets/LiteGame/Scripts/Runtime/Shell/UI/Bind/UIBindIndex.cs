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

        /// <summary>可交互开关。**G1 修正（2026-09-14，实测定案）**：`Selectable` 优先，回退 `UIWidget.Interactable`
        /// ——`StateButton`/`RedDot` 等自持交互语义的控件不是 Selectable，旧实现会抛（《UI控件Lua用法表》G1）。</summary>
        public void SetInteractable(string name, bool on)
        {
            MarkDriver(name, ControlDriver.Command);
            if (TryGet<Selectable>(name, out var sel)) { sel.interactable = on; return; }
            if (TryGet<UIWidget>(name, out var widget)) { widget.Interactable = on; return; }
            throw new InvalidOperationException($"绑定索引[{name}] 无 Selectable / UIWidget 组件（无法设置可交互）");
        }

        // ---- 批⑦ 受控 API 扩展（P0，《UI控件Lua用法表》G3/G7/G10）----

        /// <summary>进度条：归一化值（0~1）。G7。</summary>
        public void SetProgress(string name, float value01)
        {
            MarkDriver(name, ControlDriver.Command);
            Get<ProgressBar>(name).Set(value01);
        }

        /// <summary>进度条：当前/上限。G7。</summary>
        public void SetProgress(string name, float current, float max)
        {
            MarkDriver(name, ControlDriver.Command);
            Get<ProgressBar>(name).Set(current, max);
        }

        /// <summary>血条（前条瞬时 + 后条延迟滑落）。G7。</summary>
        public void SetHp(string name, float current, float max)
        {
            MarkDriver(name, ControlDriver.Command);
            Get<HpBar>(name).Set(current, max);
        }

        /// <summary>倒计时启动。G10。</summary>
        public void StartCountdown(string name, float seconds)
        {
            MarkDriver(name, ControlDriver.Command);
            Get<Countdown>(name).StartCountdown(seconds);
        }

        /// <summary>倒计时停止（不触发 OnDone）。G10。</summary>
        public void StopCountdown(string name)
        {
            MarkDriver(name, ControlDriver.Command);
            Get<Countdown>(name).Stop();
        }

        /// <summary>轻提示（Toast 单例；**场景无 ToastHost 时记日志不抛**——提示不是关键路径）。G3。</summary>
        public void ShowToast(string text)
        {
            var toast = Toast.Instance;
            if (toast == null)
            {
                Log.Warning($"ShowToast 无 Toast 实例（场景未挂 ToastHost）:{text}", "UI");
                return;
            }
            toast.Show(text);
        }

        /// <summary>气泡（挂点旁短提示）。G3。</summary>
        public void ShowBubble(string name, string text, float duration)
        {
            MarkDriver(name, ControlDriver.Command);
            Get<UIBubble>(name).Show(text, duration);
        }

        /// <summary>飘字（位置取控件自身 anchoredPosition——界面侧把飘字锚点摆好即可）。G3。</summary>
        public void ShowFlyText(string name, string text)
        {
            MarkDriver(name, ControlDriver.Command);
            var pool = Get<FlyTextPool>(name);
            var rt = pool.transform as RectTransform;
            pool.Show(text, rt != null ? rt.anchoredPosition : Vector2.zero);
        }

        public void SetAnchoredPosition(string name, Vector2 pos)
        {
            MarkDriver(name, ControlDriver.Command);
            ResolveRect(name).anchoredPosition = pos;      // 走 ResolveRect：BindNode 不产 RectTransform，Get<RectTransform> 必抛
        }

        // ---- 批⑧ G20 动效口（《动效设计方案》附 A.3，P1）----
        // 纪律：动效只写表现（透明度/位置），不携带任何判定；原语层的 SetUpdate(true)/SetLink(KillOnDisable)
        // 已写死在 UiFx 里（UIClock 轨 + 界面隐藏即杀）。所有权：与 SetText 等同属命令式驱动。

        /// <summary>脉冲：透明度快速呼吸两次（图标/红点提醒）。返回 true = tween 已创建。G20。</summary>
        public bool Pulse(string name, float strength = 0.2f, float duration = 0.16f)
        {
            MarkDriver(name, ControlDriver.Command);
            return UiFx.Pulse(ResolveGraphic(name), strength, duration) != null;
        }

        /// <summary>闪烁：一次性高亮回落。G20。</summary>
        public bool Flash(string name, float duration = 0.3f)
        {
            MarkDriver(name, ControlDriver.Command);
            return UiFx.Flash(ResolveGraphic(name), duration) != null;
        }

        /// <summary>位移入场：从 offset 相对位滑回原位。G20。</summary>
        public bool Slide(string name, Vector2 offset, float duration = 0.25f)
        {
            MarkDriver(name, ControlDriver.Command);
            return UiFx.Slide(ResolveRect(name), offset, duration) != null;
        }

        /// <summary>
        /// 动效目标解析（G20 专用，**不走 <see cref="Get{T}"/>**）：索引里存的是 BindNode 自动检测到的组件，
        /// 带 Button 的节点存的是 <see cref="Button"/>（AutoTypeCandidates 里 Button 优先于 Image）→ Get&lt;Graphic&gt; 会抛。
        /// 三级回退：自身 Graphic → Button.targetGraphic → 自身/子级 Graphic。
        /// 未命中 = KeyNotFoundException；命中但拿不到 Graphic = InvalidOperationException（与既有受控方法同语义）。
        /// </summary>
        public Graphic ResolveGraphic(string name)
        {
            if (!_controls.TryGetValue(name, out var c) || c == null)
                throw new KeyNotFoundException($"绑定索引未命中:{name}——核对 BindNode.BindName / Designer 登记");
            if (c is Graphic g) return g;
            if (c is Button btn && btn.targetGraphic != null) return btn.targetGraphic;
            var found = c.GetComponent<Graphic>() ?? c.GetComponentInChildren<Graphic>(true);
            if (found != null) return found;
            throw new InvalidOperationException(
                $"绑定索引[{name}] 无 Graphic 组件（实得 {c.GetType().Name}）——动效目标必须是 Graphic");
        }

        /// <summary>
        /// RectTransform 解析：UI 层级下任何组件的 transform 都是 RectTransform——
        /// **不要用 Get&lt;RectTransform&gt;**（BindNode.ResolveTarget 不产 RectTransform，必抛）。
        /// 手法同 <see cref="ShowFlyText"/>（`transform as RectTransform`）。
        /// </summary>
        public RectTransform ResolveRect(string name)
        {
            if (!_controls.TryGetValue(name, out var c) || c == null)
                throw new KeyNotFoundException($"绑定索引未命中:{name}——核对 BindNode.BindName / Designer 登记");
            if (c.transform is RectTransform rt) return rt;
            throw new InvalidOperationException(
                $"绑定索引[{name}] 的 transform 不是 RectTransform（实得 {c.transform.GetType().Name}）");
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
