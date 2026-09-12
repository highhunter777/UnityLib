using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 预载流程（M2 最小版）：资源初始化 → 配置加载 → 放行。
    /// M3 锚点：在此处按序追加——预载 Lua（全量 LoadRawFileTextAsync 进字典）→ 执行 main.lua →
    /// 读注册表三件套逐行 require/校验/包适配器 → Fill 注册表（设计方案 §4.3 启动接线）。
    /// </summary>
    public sealed class ProcedurePreload : ProcedureBase<ProcedureOwner>
    {
        private readonly IConfigService _config;

        public ProcedurePreload(IConfigService config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        protected override void RunAsync(Fsm<ProcedureOwner> fsm, CancellationToken ct)
            => RunAsyncCore(fsm, ct).Forget();

        private async UniTask RunAsyncCore(Fsm<ProcedureOwner> fsm, CancellationToken ct)
        {
            try
            {
                await AssetService.InitAsync(ct: ct);
                await _config.LoadAsync(ct);

                // ---- M3 锚点：Lua 预载与注册表填充段（勿在此行上方插入消费逻辑）----

                fsm.ChangeState<ProcedureMain>();
            }
            catch (OperationCanceledException) { /* 正常取消，静默 */ }
            catch (Exception ex)
            {
                Fail(fsm, ex, nameof(RunAsyncCore));
                fsm.ChangeState<ProcedureError>();
            }
        }
    }
}
