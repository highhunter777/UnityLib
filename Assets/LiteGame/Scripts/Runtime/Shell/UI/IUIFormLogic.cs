using System;

namespace LiteGame
{
    /// <summary>
    /// 界面逻辑七回调（M4 §2.0）：C# 界面逻辑与 LuaBehaviourAdapter（§2.3）同面。
    /// 调用一律经 <see cref="UIForm"/> 的 SafeCall 防护——单个回调抛异常 = 该界面降级，不炸壳
    /// （错误语义同事件桥，§3.4 降级细则）。
    /// </summary>
    public interface IUIFormLogic
    {
        void OnInit(UIForm form, object data);   // 实例化后一次（首次加载；池化复用不重复调）
        void OnShow(object data);                // 激活（Loading→Active）/恢复（Paused→Active）
        void OnUpdate(float deltaTime);          // 仅 Active 态（UIService.Tick 驱动）
        void OnPause();                          // 手动暂停
        void OnCover();                          // 被更高层级组全屏界面遮盖（批量语义）
        void OnReveal();                         // 遮盖解除
        void OnHide();                           // 关闭（进入 Closing→Recycled 前）
    }

    /// <summary>空逻辑（无 C# 逻辑、Lua 逻辑未接前的占位——§2.3 前壳可独立运行）。</summary>
    public sealed class NullUIFormLogic : IUIFormLogic
    {
        public static readonly NullUIFormLogic Instance = new NullUIFormLogic();

        private NullUIFormLogic() { }

        public void OnInit(UIForm form, object data) { }
        public void OnShow(object data) { }
        public void OnUpdate(float deltaTime) { }
        public void OnPause() { }
        public void OnCover() { }
        public void OnReveal() { }
        public void OnHide() { }
    }
}
