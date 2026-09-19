using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 转场编排层（《UI扩展能力设计》§1.5，Layer 2）：通用状态机 + 待办队列（容量 1）+ 超时兜底 + 交互门 + 统计。
    ///
    /// **不注册 `ITickable`**——由 <see cref="UIService"/> 在自身 Tick 的**帧末**转发：既避免双驱动，
    /// 也让界面 `OnUpdate` 里发起的请求能在同一帧帧末被推进（§1.5.2"帧末 Advance 应用"）。
    ///
    /// 与既有 `_loading`/`_closing` 守卫**正交**：那两个按 formId 防"同一界面并发开/关"，
    /// 本层管"全局表现编排"（同一时刻只有一个事务）。
    ///
    /// 四条硬规则（§1.5.4）：① 同帧 last-wins（首个请求立即开始，后续走排队/丢弃，不重复 Request 给机器）
    /// ② 排队 1 个（目标 = 当前 Incoming 则忽略；再来的丢弃并计数）③ 交互门由壳在阶段钩子里统一管
    /// ④ 超时（默认 2s）强制收尾并广播 <c>Completed=false</c>。
    /// </summary>
    public sealed class UITransitionRunner : IModuleStats
    {
        /// <summary>超时兜底默认时长（§1.5.4 规则④）。</summary>
        public const float DefaultMaxDuration = 2f;

        private readonly StageMachine<TransitionId, TransitionReq> _machine;
        private readonly ITransitionStrategy _strategy;
        private readonly IReplaceTransition _replace;
        private readonly float _maxDuration;

        private TransitionContext _current;
        private TransitionContext _queued;
        private int _dropped;

        /// <summary>事务开始（Lua 侧 begin 事件的来源）。</summary>
        public event Action<TransitionContext> Began;

        /// <summary>事务结束（与 <see cref="Began"/> 成对；超时也发，`Completed=false`）。</summary>
        public event Action<TransitionOutcome> Finished;

        public UITransitionRunner(ITransitionStrategy strategy,
                                  IReplaceTransition replaceTransition = null,
                                  float maxDuration = DefaultMaxDuration,
                                  string name = "UITransition")
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _replace = replaceTransition;
            _maxDuration = maxDuration > 0f ? maxDuration : DefaultMaxDuration;

            _machine = new StageMachine<TransitionId, TransitionReq>(name,
                (TransitionId.Idle, new IdleTransitionStage()),
                (TransitionId.Out, new OutTransitionStage()),
                (TransitionId.In, new InTransitionStage()));
            _machine.Start(TransitionId.Idle);
        }

        // ---- 诊断读数（DevHUD / 验收）----

        /// <summary>当前转场阶段。</summary>
        public TransitionId Phase => _machine.Current;

        /// <summary>是否有事务在执行。</summary>
        public bool Busy => _current != null;

        /// <summary>待办队列长度（0 或 1）。</summary>
        public int QueueLength => _queued != null ? 1 : 0;

        /// <summary>因队列满被丢弃的请求数。</summary>
        public int DroppedCount => _dropped;

        /// <summary>累计转场迁移次数。</summary>
        public long TotalTransitions => _machine.TransitionCount;

        /// <summary>
        /// 发起一次转场。空闲 → 立即开始；忙 → 排队 1 个（再来的丢弃并计数）。
        /// 返回的 Task 在事务收尾时完成（超时/被丢/被忽略也会完成，`Completed=false`）。
        /// </summary>
        public UniTask<TransitionOutcome> PlayAsync(TransitionMode mode, UIForm outgoing, UIForm incoming)
        {
            var ctx = new TransitionContext
            {
                Mode = mode,
                Outgoing = outgoing,
                Incoming = incoming,
                MaxDuration = _maxDuration,
            };
            ctx.Play = BuildPlay(ctx);

            if (_current == null)
            {
                Start(ctx);
                return ctx.Tcs.Task;
            }

            if (incoming != null && ReferenceEquals(incoming, _current.Incoming))
            {
                Log.Warning($"转场中重复请求同一界面[{incoming.Id}]——忽略", "UI");
                ctx.Tcs.TrySetResult(OutcomeOf(ctx));
                return ctx.Tcs.Task;
            }

            if (_queued == null)
            {
                _queued = ctx;
                Log.Info($"转场进行中——请求已排队(mode={mode})", "UI");
            }
            else
            {
                _dropped++;
                Log.Warning($"转场队列已满——请求丢弃(mode={mode}，累计 {_dropped})", "UI");
                ctx.Tcs.TrySetResult(OutcomeOf(ctx));
            }
            return ctx.Tcs.Task;
        }

        /// <summary>
        /// 帧末驱动（<see cref="UIService"/> 的 Tick 末尾调用）。
        /// 顺序：超时/完成判定 → 机器 Tick（推进迁移）→ 收尾 → 取队列。
        ///
        /// **调用方约定**：收尾会同步 `TrySetResult`，从而可能在本方法内同步恢复
        /// `ShowAsync/CloseAsync` 的续体（进而关闭/回收界面）。所以本方法必须位于其余帧逻辑之后——
        /// UIService 已保证（`RaiseUpdate` 循环结束后才转发），届时 `foreach (_forms.Values)` 已结束，重入安全。
        /// </summary>
        public void Tick(float realDelta)
        {
            if (_current != null && !_current.Finalized)
            {
                var c = _current;
                bool inTransition = _machine.Current != TransitionId.Idle;

                if (inTransition && !c.TimedOut && !c.Done && _machine.StageTime > c.MaxDuration)
                {
                    c.TimedOut = true;
                    Log.Error($"转场超时({c.MaxDuration:0.##}s)——强制收尾(mode={c.Mode})", "UI");
                    _machine.Request(TransitionId.Idle);
                }
                else if (inTransition && !c.TimedOut && c.Done)
                {
                    _machine.Request(TransitionId.Idle);
                }
            }

            _machine.Tick(realDelta);

            if (_current != null && !_current.Finalized
                && _machine.Current == TransitionId.Idle && !_machine.HasPending)
            {
                Finalize(_current);
            }

            // 队列取出放帧末最后一步：保证"一帧最多一变"（§1.5.2）
            if (_current == null && _queued != null)
            {
                var next = _queued;
                _queued = null;
                Start(next);
            }
        }

        // ---- 内部 ----

        private void Start(TransitionContext ctx)
        {
            _current = ctx;
            Began?.Invoke(ctx);
            _machine.Request(ctx.Mode == TransitionMode.Pop ? TransitionId.Out : TransitionId.In,
                             new TransitionReq(ctx));
        }

        private void Finalize(TransitionContext ctx)
        {
            ctx.Finalized = true;
            RestoreGate(ctx.Outgoing);          // 规则③：不依赖策略自觉（池中/已回收界面恢复也无害）
            RestoreGate(ctx.Incoming);
            _current = null;

            var outcome = OutcomeOf(ctx);
            ctx.Tcs.TrySetResult(outcome);
            Finished?.Invoke(outcome);
        }

        private static void RestoreGate(UIForm form)
        {
            if (form != null && form.CanvasGroup != null) form.CanvasGroup.blocksRaycasts = true;
        }

        private static TransitionOutcome OutcomeOf(TransitionContext ctx) => new TransitionOutcome
        {
            Mode = ctx.Mode,
            Outgoing = ctx.Outgoing,
            Incoming = ctx.Incoming,
            Completed = ctx.Completed && !ctx.TimedOut,
            TimedOut = ctx.TimedOut,
        };

        private Func<UniTask> BuildPlay(TransitionContext c) => c.Mode switch
        {
            TransitionMode.Pop => () => _strategy.PlayClose(c.Outgoing),
            TransitionMode.Push => () => _strategy.PlayShow(c.Incoming),
            TransitionMode.Replace => () => PlayReplace(c),
            _ => () => UniTask.CompletedTask,
        };

        /// <summary>Replace 的"两组并发"：策略实现了 <see cref="IReplaceTransition"/> 走定制，否则壳合成。</summary>
        private UniTask PlayReplace(TransitionContext c)
            => _replace != null
                ? _replace.PlayReplace(c.Outgoing, c.Incoming)
                : UniTask.WhenAll(_strategy.PlayClose(c.Outgoing), _strategy.PlayShow(c.Incoming));

        public string StatsName => "UITransition";

        public void Snapshot(Dictionary<string, string> into)
        {
            into["阶段"] = _machine.Current.ToString();
            into["阶段时长"] = _machine.StageTime.ToString("0.0");
            into["忙"] = Busy ? "是" : "否";
            into["队列"] = QueueLength.ToString();
            into["丢弃"] = _dropped.ToString();
            into["累计"] = _machine.TransitionCount.ToString();
        }
    }
}
