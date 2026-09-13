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
        /// <summary>ui-API 通用派发方法名（payload 表协议见 Dispatch）。</summary>
        private const string UiApiShim = @"
local c = __ui_api_c
__ui_api_c = nil
return {
    OnButton = function(_, name, fn) c('onButton', { name = name, fn = fn }) end,
    OffButton = function(_, name) c('offButton', { name = name }) end,
    SetText = function(_, name, text) c('setText', { name = name, text = text }) end,
    SetVisible = function(_, name, visible) c('setVisible', { name = name, visible = visible }) end,
    SetInteractable = function(_, name, on) c('setInteractable', { name = name, on = on }) end,
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

        /// <summary>ui-API 通用派发（payload 表协议）：onButton{name,fn} / offButton{name} /
        /// setText{name,text} / setVisible{name,visible} / setInteractable{name,on}。未识别方法静默忽略。</summary>
        private void Dispatch(string method, LuaTable payload)
        {
            if (_index == null || payload == null) return;
            switch (method)
            {
                case "onButton":
                    var name = payload.Get<string>("name");
                    _index.BindButton(name, () =>
                    {
                        var fn = payload.Get<LuaFunction>("fn");
                        if (fn != null) fn.Call();
                    });
                    break;
                case "offButton": _index.UnbindButton(payload.Get<string>("name")); break;
                case "setText": _index.SetText(payload.Get<string>("name"), payload.Get<string>("text")); break;
                case "setVisible": _index.SetVisible(payload.Get<string>("name"), payload.Get<bool>("visible")); break;
                case "setInteractable": _index.SetInteractable(payload.Get<string>("name"), payload.Get<bool>("on")); break;
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
