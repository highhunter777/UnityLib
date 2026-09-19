using System;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>阶段共用动作（Out/In 两个阶段完全同构，收口在一处）。</summary>
    internal static class TransitionStageOps
    {
        /// <summary>关交互门（§1.5.4 规则③：由壳统一管，不依赖策略自觉；幂等）。</summary>
        internal static void CloseGate(UIForm form)
        {
            if (form != null && form.CanvasGroup != null) form.CanvasGroup.blocksRaycasts = false;
        }

        /// <summary>启动表现（fire-and-forget）。结局只写回 ctx——**不抛穿**（动效不携带判定，动效方案原则 1）。</summary>
        internal static void StartPlay(TransitionContext c)
        {
            if (c.Play == null)
            {
                c.Done = true;
                c.Completed = true;
                return;
            }
            RunAsync(c).Forget();
        }

        private static async UniTaskVoid RunAsync(TransitionContext c)
        {
            try
            {
                await c.Play();
                c.Completed = true;
            }
            catch (OperationCanceledException)
            {
                c.Completed = false;
            }
            catch (Exception ex)
            {
                Log.Error($"转场表现失败(mode={c.Mode}):{ex.Message}", "UI");
                c.Completed = false;
            }
            finally
            {
                c.Done = true;
            }
        }
    }

    /// <summary>Idle：无事务。收尾（恢复交互门 / 完成 TCS / 取队列）由 <see cref="UITransitionRunner"/> 在本阶段做。</summary>
    internal sealed class IdleTransitionStage : IStage<TransitionId, TransitionReq>
    {
        public void OnInit(IStageHost<TransitionId, TransitionReq> m) { }
        public void OnEnter(IStageHost<TransitionId, TransitionReq> m, in TransitionReq req) { }
        public void OnUpdate(IStageHost<TransitionId, TransitionReq> m, float elapseSeconds) { }
        public void OnLeave(IStageHost<TransitionId, TransitionReq> m) { }
    }

    /// <summary>Out：离场（Pop）。<c>OnEnter</c> = 关交互门 + 启动 PlayClose。
    /// 轮询与收尾刻意留空——那两件事要读 payload 而 <c>OnUpdate/OnLeave</c> 拿不到（Runner 在帧末做）。</summary>
    internal sealed class OutTransitionStage : IStage<TransitionId, TransitionReq>
    {
        public void OnInit(IStageHost<TransitionId, TransitionReq> m) { }

        public void OnEnter(IStageHost<TransitionId, TransitionReq> m, in TransitionReq req)
        {
            var c = req.Ctx;
            if (c == null || c.GateClosed) return;
            c.GateClosed = true;
            TransitionStageOps.CloseGate(c.Outgoing);
            TransitionStageOps.CloseGate(c.Incoming);
            TransitionStageOps.StartPlay(c);
        }

        public void OnUpdate(IStageHost<TransitionId, TransitionReq> m, float elapseSeconds) { }
        public void OnLeave(IStageHost<TransitionId, TransitionReq> m) { }
    }

    /// <summary>In：入场（Push）/ 切换（Replace，两组并发在 Runner 绑定的 Play 里；**不设第四态**）。</summary>
    internal sealed class InTransitionStage : IStage<TransitionId, TransitionReq>
    {
        public void OnInit(IStageHost<TransitionId, TransitionReq> m) { }

        public void OnEnter(IStageHost<TransitionId, TransitionReq> m, in TransitionReq req)
        {
            var c = req.Ctx;
            if (c == null || c.GateClosed) return;
            c.GateClosed = true;
            TransitionStageOps.CloseGate(c.Outgoing);   // Replace：两组都关
            TransitionStageOps.CloseGate(c.Incoming);
            TransitionStageOps.StartPlay(c);
        }

        public void OnUpdate(IStageHost<TransitionId, TransitionReq> m, float elapseSeconds) { }
        public void OnLeave(IStageHost<TransitionId, TransitionReq> m) { }
    }
}
