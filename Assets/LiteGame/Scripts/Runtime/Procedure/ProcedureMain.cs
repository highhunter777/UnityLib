using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 主流程占位（M2）。M5 纵向切片起在这里接主菜单 → 进场景 → 战斗 → 结算的流程树。
    /// </summary>
    public sealed class ProcedureMain : ProcedureBase<ProcedureOwner>
    {
        protected override void RunAsync(Fsm<ProcedureOwner> fsm, CancellationToken ct)
        {
            // 空转待命——无 ChangeState 请求即停在本流程
        }
    }
}
