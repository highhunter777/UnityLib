using System;
using System.Threading;

namespace LiteFramework
{
    /// <summary>
    /// 流程阶段基类（取代 `ProcedureBase&lt;TOwner&gt;`，2026-09-17 A 路线重构）：
    /// 保留原设计的两条核心语义——**进流程即建 CTS 并跑 `RunAsync`、离场即 Cancel + Dispose**
    /// （`OnEnter`/`OnLeave` 不再 virtual，子类只写 `RunAsync`，避免漏掉取消语义）。
    ///
    /// 与旧版的差异：业务流程状态**不再经 owner 载体**——每次迁移的数据走 <typeparamref name="TReq"/> payload
    /// （`ProcedureArgs`），编译期强类型，且不依赖"owner 上可能为 null 的字段"。
    /// `IProcedureOwner`/`ProcedureOwner` 随之退休（决策记录见《通用流程状态机施工图》§5）。
    ///
    /// `RunAsync` 是 void 签名——Core 引不了 UniTask（零依赖纪律），子类必须"一行转发"给 async 主体，
    /// **禁止 async void**（异常调用方接不住，M0 指导 §6）。
    /// 流程依赖不从 payload 取（那是服务定位器的变体）——依赖走构造注入存为本类字段。
    /// </summary>
    public abstract class ProcedureStageBase<TId, TReq> : IStage<TId, TReq>
        where TId : struct
    {
        private CancellationTokenSource _cts;

        public virtual void OnInit(IStageHost<TId, TReq> m) { }

        public void OnEnter(IStageHost<TId, TReq> m, in TReq req)
        {
            _cts = new CancellationTokenSource();
            RunAsync(m, in req, _cts.Token);
        }

        public void OnLeave(IStageHost<TId, TReq> m)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public virtual void OnUpdate(IStageHost<TId, TReq> m, float elapseSeconds) { }

        protected abstract void RunAsync(IStageHost<TId, TReq> m, in TReq req, CancellationToken ct);

        /// <summary>
        /// 失败落点：记 Fatal（日志可见）。**不切流程**——错误流转由业务在 catch 里自行
        /// `m.Request(ProcedureId.Error, new ProcedureArgs(ex))`（Core 不引用业务类型，M2 既定形态）。
        /// </summary>
        protected void Fail(IStageHost<TId, TReq> m, Exception ex, string where)
        {
            Log.Fatal(ex, where, "Procedure");
        }
    }
}
