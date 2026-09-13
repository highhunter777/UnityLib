using System;
using LiteFramework;
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
    /// DevReload 语义：旧 env 的 LuaTable 随 Dispose 失效——重载编排"先全关再重建"（§2.3 定案），
    /// 适配器不持有跨 env 状态。
    /// </summary>
    public sealed class LuaBehaviourAdapter : IUIFormLogic
    {
        private readonly LuaTable _logic;
        private readonly LuaFunction _onInit, _onShow, _onUpdate, _onPause, _onCover, _onReveal, _onHide;

        public LuaBehaviourAdapter(LuaTable logic)
        {
            _logic = logic ?? throw new ArgumentNullException(nameof(logic));
            _onInit = GetFn("OnInit");
            _onShow = GetFn("OnShow");
            _onUpdate = GetFn("OnUpdate");
            _onPause = GetFn("OnPause");
            _onCover = GetFn("OnCover");
            _onReveal = GetFn("OnReveal");
            _onHide = GetFn("OnHide");
        }

        public void OnInit(UIForm form, IUIData data) => Call(_onInit, "OnInit", form, ToLuaArg(data));
        public void OnShow(IUIData data) => Call(_onShow, "OnShow", ToLuaArg(data));
        public void OnUpdate(float deltaTime) => Call(_onUpdate, "OnUpdate", deltaTime);
        public void OnPause() => Call(_onPause, "OnPause");
        public void OnCover() => Call(_onCover, "OnCover");
        public void OnReveal() => Call(_onReveal, "OnReveal");
        public void OnHide() => Call(_onHide, "OnHide");

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
