using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 错误流程：任何流程 Fail 后的落点。显示失败原因（M4 接错误 UI，M2 停机 + 日志可见），
    /// 不自动重试不静默退出——启动链失败必须人工排查后重跑。
    /// 失败原因经 **payload** 传来（`ProcedureArgs.Error`）——取代已退休的 `ProcedureOwner.LastError`。
    /// </summary>
    public sealed class ProcedureError : ProcedureStageBase<ProcedureId, ProcedureArgs>
    {
        protected override void RunAsync(IStageHost<ProcedureId, ProcedureArgs> m, in ProcedureArgs req, CancellationToken ct)
            => RunAsyncCore(m, req.Error, ct).Forget();

        private async UniTask RunAsyncCore(IStageHost<ProcedureId, ProcedureArgs> m, Exception error, CancellationToken ct)
        {
            try
            {
                Log.Error($"启动失败，停机待查:{error?.Message ?? "(payload 无 Error)"}", "Procedure");
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
        }
    }
}
