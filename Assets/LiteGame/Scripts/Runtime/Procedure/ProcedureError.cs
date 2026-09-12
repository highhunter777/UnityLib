using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 错误流程：任何流程 Fail 后的落点。显示 LastError（M4 接错误 UI，M2 停机 + 日志可见），
    /// 不自动重试不静默退出——启动链失败必须人工排查后重跑。
    /// </summary>
    public sealed class ProcedureError : ProcedureBase<ProcedureOwner>
    {
        protected override void RunAsync(Fsm<ProcedureOwner> fsm, CancellationToken ct)
            => RunAsyncCore(fsm, ct).Forget();

        private async UniTask RunAsyncCore(Fsm<ProcedureOwner> fsm, CancellationToken ct)
        {
            try
            {
                Log.Error($"启动失败，停机待查:{fsm.Owner.LastError?.Message ?? "(无 LastError)"}", "Procedure");
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
        }
    }
}
