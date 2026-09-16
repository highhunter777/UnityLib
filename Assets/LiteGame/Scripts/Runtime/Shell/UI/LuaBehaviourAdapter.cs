using System;
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;
using XLua;

namespace LiteGame
{
    /// <summary>Lua 侧数据包装（M4 §2.3）：Lua 构造的数据表经 C# 工厂包成 <see cref="IUIData"/>；
    /// 适配器回传时解包出原始 LuaTable——Lua 拿到的永远是自己认识的表，不是 C# 包装对象。</summary>
    public sealed class LuaUIData : IUIData
    {
        public LuaTable Table { get; }

        public LuaUIData(LuaTable table)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
        }
    }

    /// <summary>
    /// 生命周期桥（M4 §2.3，手册步骤 3，设计方案 §4.3）：持注册表取出的逻辑表，
    /// 把 IUIFormLogic 七回调翻译成 `self:OnInit/OnShow/...`。
    /// ① 七个 LuaFunction 构造期一次取齐缓存（OnUpdate 每帧路径禁反复 Get）；
    /// ② 方法缺失 = 静默跳过（界面可只写需要的回调）；
    /// ③ 异常防护由 UIForm 层的 SafeCall 统一承担（单回调抛 = 该界面降级，不炸壳）；
    /// ④ 数据回传解包：LuaUIData → 原始 LuaTable；C# 自定义 IUIData 原样传入（userdata）。
    /// ⑤ 受控 API 面（§2.4）：OnInit 建绑定索引 + 挂 `self.ui` 门面表（通用派发器，whitelist 单委托
    ///    Action&lt;string, LuaTable&gt;——EventBridge 同款手法，零新增生成）；OnHide 解绑全部按钮
    ///    （池化复用跨环境的安全垫）。DevReload 语义：重载编排"先全关再重建"，适配器无跨 env 状态。
    /// </summary>
    public sealed class LuaBehaviourAdapter : IUIFormLogic
    {
        /// <summary>ui-API 通用派发方法名（payload 表协议见 Dispatch；批⑦ 已补 G1/G3/G7/G10）。</summary>
        private const string UiApiShim = @"
local c = __ui_api_c
__ui_api_c = nil
return {
    OnButton = function(_, name, fn) c('onButton', { name = name, fn = fn }) end,
    OffButton = function(_, name) c('offButton', { name = name }) end,
    SetText = function(_, name, text) c('setText', { name = name, text = text }) end,
    SetVisible = function(_, name, visible) c('setVisible', { name = name, visible = visible }) end,
    SetInteractable = function(_, name, on) c('setInteractable', { name = name, on = on }) end,
    SetProgress = function(_, name, value) c('setProgress', { name = name, value = value }) end,
    SetProgressRange = function(_, name, cur, max) c('setProgressRange', { name = name, cur = cur, max = max }) end,
    SetHp = function(_, name, cur, max) c('setHp', { name = name, cur = cur, max = max }) end,
    StartCountdown = function(_, name, seconds) c('startCountdown', { name = name, seconds = seconds }) end,
    StopCountdown = function(_, name) c('stopCountdown', { name = name }) end,
    ShowToast = function(_, text) c('showToast', { text = text }) end,
    ShowBubble = function(_, name, text, duration) c('showBubble', { name = name, text = text, duration = duration or 1.5 }) end,
    ShowFlyText = function(_, name, text) c('showFlyText', { name = name, text = text }) end,
}";

        private readonly LuaEnv _env;
        private readonly LuaTable _logic;
        private readonly LuaFunction _onInit, _onShow, _onUpdate, _onPause, _onCover, _onReveal, _onHide;
        private UIBindIndex _index;

        public LuaBehaviourAdapter(LuaEnv env, LuaTable logic)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
            _logic = logic ?? throw new ArgumentNullException(nameof(logic));
            _onInit = GetFn("OnInit");
            _onShow = GetFn("OnShow");
            _onUpdate = GetFn("OnUpdate");
            _onPause = GetFn("OnPause");
            _onCover = GetFn("OnCover");
            _onReveal = GetFn("OnReveal");
            _onHide = GetFn("OnHide");
        }

        public void OnInit(UIForm form, IUIData data)
        {
            _index = BindIndexBuilder.Build(form.Root);       // 路径 A：Lua 界面恒走标记索引

            var api = _env.NewTable();
            var dispatch = new Action<string, LuaTable>(Dispatch);
            _env.Global.Set<string, Action<string, LuaTable>>("__ui_api_c", dispatch);
            var shim = _env.DoString(UiApiShim, "ui_api_shim");
            _env.DoString("__ui_api_c = nil");
            if (shim != null && shim.Length > 0 && shim[0] is LuaTable apiTable)
            {
                _logic.Set<string, LuaTable>("ui", apiTable);   // Lua 侧 self.ui:OnButton / SetText / ...
            }

            Call(_onInit, "OnInit", form, ToLuaArg(data));
        }

        public void OnShow(IUIData data) => Call(_onShow, "OnShow", ToLuaArg(data));
        public void OnUpdate(float deltaTime) => Call(_onUpdate, "OnUpdate", deltaTime);
        public void OnPause() => Call(_onPause, "OnPause");
        public void OnCover() => Call(_onCover, "OnCover");
        public void OnReveal() => Call(_onReveal, "OnReveal");

        public void OnHide()
        {
            _index?.UnbindAll();                              // 池化复用跨环境安全垫：旧 fn 监听全部移除
            Call(_onHide, "OnHide");
        }

        /// <summary>持有的逻辑表（换表释放/诊断用）。</summary>
        public LuaTable Logic => _logic;

        /// <summary>
        /// 换表释放（M4 §2.3 运行期增量重填）：解绑按钮监听 + 释放 Lua 侧引用（逻辑表与七个回调函数）。
        /// 幂等（`LuaBase.Dispose` 有 disposed 守卫）；DevReload 走 `env.Dispose` 兜底，二者不冲突。
        /// **调用前提**：本适配器已不再被任何界面使用（仅在"换表"时调用，见 `UIService.SwapIfStale`）。
        /// </summary>
        public void Release()
        {
            _index?.UnbindAll();
            _onInit?.Dispose();
            _onShow?.Dispose();
            _onUpdate?.Dispose();
            _onPause?.Dispose();
            _onCover?.Dispose();
            _onReveal?.Dispose();
            _onHide?.Dispose();
            _logic?.Dispose();
        }

        /// <summary>ui-API 通用派发（payload 表协议）：onButton{name,fn} / offButton{name} /
        /// setText{name,text} / setVisible{name,visible} / setInteractable{name,on} /
        /// setProgress{name,value} / setProgressRange{name,cur,max} / setHp{name,cur,max} /
        /// startCountdown{name,seconds} / stopCountdown{name} / showToast{text} /
        /// showBubble{name,text,duration} / showFlyText{name,text}。未识别方法静默忽略。</summary>
        private void Dispatch(string method, LuaTable payload)
        {
            if (_index == null || payload == null) return;
            switch (method)
            {
                case "onButton":
                    var name = payload.Get<string, string>("name");
                    _index.BindButton(name, () =>
                    {
                        var fn = payload.Get<LuaFunction>("fn");
                        if (fn != null) fn.Call();
                    });
                    break;
                case "offButton": _index.UnbindButton(payload.Get<string, string>("name")); break;
                case "setText": _index.SetText(payload.Get<string, string>("name"), payload.Get<string, string>("text")); break;
                case "setVisible": _index.SetVisible(payload.Get<string, string>("name"), payload.Get<string, bool>("visible")); break;
                case "setInteractable": _index.SetInteractable(payload.Get<string, string>("name"), payload.Get<string, bool>("on")); break;
                // ---- 批⑦ 扩展 ----
                case "setProgress": _index.SetProgress(payload.Get<string, string>("name"), payload.Get<string, float>("value")); break;
                case "setProgressRange": _index.SetProgress(payload.Get<string, string>("name"), payload.Get<string, float>("cur"), payload.Get<string, float>("max")); break;
                case "setHp": _index.SetHp(payload.Get<string, string>("name"), payload.Get<string, float>("cur"), payload.Get<string, float>("max")); break;
                case "startCountdown": _index.StartCountdown(payload.Get<string, string>("name"), payload.Get<string, float>("seconds")); break;
                case "stopCountdown": _index.StopCountdown(payload.Get<string, string>("name")); break;
                case "showToast": _index.ShowToast(payload.Get<string, string>("text")); break;
                case "showBubble": _index.ShowBubble(payload.Get<string, string>("name"), payload.Get<string, string>("text"), payload.Get<string, float>("duration")); break;
                case "showFlyText": _index.ShowFlyText(payload.Get<string, string>("name"), payload.Get<string, string>("text")); break;
            }
        }

        private LuaFunction GetFn(string name) => _logic.Get<LuaFunction>(name);

        private static void Call(LuaFunction fn, string name, params object[] args)
        {
            if (fn == null) return;                         // 界面未实现该回调：静默跳过
            fn.Call(args);
        }

        private static object ToLuaArg(IUIData data)
            => data is LuaUIData lua ? (object)lua.Table : data;
    }
}
