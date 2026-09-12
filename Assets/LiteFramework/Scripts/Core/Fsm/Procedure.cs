using System;
using System.Threading;

namespace LiteFramework
{
    /// <summary>
    /// 流程宿主契约：框架只认这一个成员（<see cref="ProcedureBase{TOwner}.Fail"/> 写入），
    /// **业务字段一律加在业务的 owner 实现里**（LiteGame.ProcedureOwner），不回填框架——
    /// 2026-09-10 泛型化重构钉下的边界线（此前框架 ProcedureOwner 是 sealed，业务状态只能往框架塞）。
    /// </summary>
    public interface IProcedureOwner
    {
        /// <summary>最近一次流程失败的原因（Fail 写入；错误流程读取后据此提示）。</summary>
        Exception LastError { get; set; }
    }

    /// <summary>
    /// 流程基类（**泛型化**：owner 类型由业务给定，框架只约束 <see cref="IProcedureOwner"/>）。
    /// OnEnter/OnLeave sealed：进流程即 new CancellationTokenSource → RunAsync；离场即 Cancel + Dispose。
    /// RunAsync 是 void 签名——Core 引不了 UniTask（零依赖纪律的代价），子类必须"一行转发"给 async 主体，
    /// **禁止 async void**（异常调用方接不住，见 M0 指导 §6）。
    /// 流程依赖不从 Owner 取（那是局部服务定位器）——流程实例在装配点构造，依赖走构造注入存为本类字段。
    /// </summary>
    public abstract class ProcedureBase<TOwner> : FsmState<TOwner> where TOwner : IProcedureOwner
    {
        private CancellationTokenSource _cts;

        public sealed override void OnEnter(Fsm<TOwner> fsm)   // sealed:子类只写 RunAsync
        {
            _cts = new CancellationTokenSource();
            RunAsync(fsm, _cts.Token);
        }

        public sealed override void OnLeave(Fsm<TOwner> fsm)
        {
            _cts?.Cancel(); _cts?.Dispose(); _cts = null;
        }

        protected abstract void RunAsync(Fsm<TOwner> fsm, CancellationToken ct);

        protected void Fail(Fsm<TOwner> fsm, Exception ex, string where)
        {
            Log.Fatal(ex, where, "Procedure");
            fsm.Owner.LastError = ex;                       // 经 IProcedureOwner 契约写入（实现由业务提供）
            // 错误流转：业务流程在 catch 中 Fail(...) 后自行 ChangeState<ProcedureError>()（ProcedureError
            // 定义在 LiteGame——Core 不引用业务类型，故本方法只写状态不切流程，M2 已定此形态）
        }
    }
}
