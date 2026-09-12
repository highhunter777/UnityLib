using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 启动流程：**唯一受信装配点**（设计方案 §3.3）。依赖在 GameEntry.Awake 装配点构造注入存为本类字段——
    /// 流程依赖不从 Owner 取（那是局部服务定位器，与"容器不静态暴露"同罪）。
    /// 职责：注册业务服务 → Seal 封注册面 → 移交 Preload。**Start 由 GameEntry.Start() 触发**
    /// （晚于全部组件 Awake 的 RegisterInstance——流程顺序契约，避免密封后注册违例）。
    /// </summary>
    public sealed class ProcedureLaunch : ProcedureBase<ProcedureOwner>
    {
        private readonly ServiceContainer _container;
        private readonly ConfigService _config;
        private readonly SceneService _scenes;
        private readonly UiLuaRegistry _uiRegistry;
        private readonly ContentLuaRegistry _contentRegistry;
        private readonly StrategyLuaRegistry _strategyRegistry;

        public ProcedureLaunch(ServiceContainer container, ConfigService config, SceneService scenes,
            UiLuaRegistry uiRegistry, ContentLuaRegistry contentRegistry, StrategyLuaRegistry strategyRegistry)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
            _uiRegistry = uiRegistry ?? throw new ArgumentNullException(nameof(uiRegistry));
            _contentRegistry = contentRegistry ?? throw new ArgumentNullException(nameof(contentRegistry));
            _strategyRegistry = strategyRegistry ?? throw new ArgumentNullException(nameof(strategyRegistry));
        }

        protected override void RunAsync(Fsm<ProcedureOwner> fsm, CancellationToken ct)
            => RunAsyncCore(fsm, ct).Forget();          // 一行转发，仅此而已——禁止 async void（M0 指导 §6）

        private async UniTask RunAsyncCore(Fsm<ProcedureOwner> fsm, CancellationToken ct)
        {
            try
            {
                _container.RegisterInstance<ConfigService>(_config);
                _container.RegisterInstance<IConfigService>(_config);
                _container.RegisterInstance<SceneService>(_scenes);
                // 三注册表（M3 §2.4：注册表实例装配在唯一受信装配点；元素同为 LuaTable，标记接口区分）
                _container.RegisterInstance<IUILuaRegistry>(_uiRegistry);
                _container.RegisterInstance<IContentLuaRegistry>(_contentRegistry);
                _container.RegisterInstance<IStrategyLuaRegistry>(_strategyRegistry);
                _container.Seal();                       // 注册面冻结；此后 Resolve 不受限

                Bridge.Bind(() => _config.Tables);       // 服务桥装配期绑定（M2 C# 骨架，M3 绑成 Lua 全局表）

                fsm.ChangeState<ProcedurePreload>();
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
