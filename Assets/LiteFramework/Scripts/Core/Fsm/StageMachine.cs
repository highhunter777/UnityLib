using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 阶段契约（通用流程状态机，2026-09-17 A 路线重构）：状态逻辑仍写成类，**状态标识改为枚举**
    /// （<typeparamref name="TId"/>）——这是为了能携带 <typeparamref name="TReq"/> payload：
    /// 旧 `ChangeState&lt;T&gt;()` 的"类型即标识"放不下每次迁移的数据，只能退回 owner 字段或字符串键字典
    /// （后者已删，见旧 Fsm 注释）。状态对象仍是**无状态单例**——实例字段一律禁止，可变数据走 payload 或宿主。
    /// </summary>
    public interface IStage<TId, TReq> where TId : struct, Enum
    {
        void OnInit(IStageHost<TId, TReq> m);
        void OnEnter(IStageHost<TId, TReq> m, in TReq req);
        void OnUpdate(IStageHost<TId, TReq> m, float elapseSeconds);
        void OnLeave(IStageHost<TId, TReq> m);
    }

    /// <summary>
    /// 通用阶段状态机（取代 `Fsm&lt;TOwner&gt;`，2026-09-17）：四个正交维度全部参数化——
    /// **阶段标识**（枚举 TId）／**数据载体**（payload，随迁移参数走，不进 owner、不用字典）／
    /// **推进方式**（`Tick` 每帧 or 调用方直接 `Advance()`，后者即"信号驱动"）／
    /// **调度与完成**（本内核不管——队列/合并/超时/可等待 由驱动层与可选组件实现，见施工图 §7）。
    ///
    /// 迁移语义（沿用并保持旧 Fsm 的既有契约，7 条逐条保住）：
    /// ① `Start` 前 `Tick` 静默；`Start` 前 `Request` 抛；
    /// ② `Request` 只入队（**两段式**：不迁移）；`Advance` 应用队首 —— `Tick` = `OnUpdate` → 有挂起则 `Advance`，
    ///    故"帧末应用、一帧最多一变"由 `Tick` 保证，"信号驱动"则由调用方自己调 `Advance`；
    /// ③ 同帧多次 `Request` = **last-wins**（payload 同时被最后一次覆盖）；
    /// ④ `OnEnter` 内 `Request` → 写 pending，**下帧末（或下次 `Advance`）生效**，不递归；
    /// ⑤ 重入（请求 = 当前）→ 调用时抛；
    /// ⑥ `OnLeave` 期间 `Request` → 抛（离场中改道自相矛盾）；
    /// ⑦ 未注册 id → 调用时抛，不等帧末。
    ///
    /// **异常策略不在内核**：阶段回调不捕获异常（"脊柱"语义，dev 炸响优于跛行）。
    /// 需要"表现/作业不阻塞"的场景，由**驱动层**捕获 + 超时兜底（流程侧由 `ProcedureStageBase.RunAsync` 自兜）。
    ///
    /// 宿主与快照：`ITickable`（每帧驱动）+ `IModuleStats`（HUD 读数，禁每帧分配）。
    /// </summary>
    public sealed class StageMachine<TId, TReq> : ITickable, IModuleStats, IStageHost<TId, TReq>
        where TId : struct, Enum
    {
        private readonly string _name;
        private readonly Dictionary<TId, IStage<TId, TReq>> _stages;

        private IStage<TId, TReq> _current;
        private TId _currentId;
        private bool _hasPending;
        private TId _pendingId;
        private TReq _pendingReq;             // ★ payload：与 pending 一起交接（last-wins 时同步覆盖）
        private bool _inLeave;                // OnLeave 执行中（改道禁令窗口）
        private float _stageTime;
        private long _transitionCount;

        public StageMachine(string name, params (TId id, IStage<TId, TReq> stage)[] stages)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (stages == null || stages.Length == 0) throw new ArgumentException("状态机至少需要一个阶段", nameof(stages));

            _name = name;
            _stages = new Dictionary<TId, IStage<TId, TReq>>(stages.Length);
            foreach (var (id, stage) in stages)
            {
                if (stage == null) throw new ArgumentException($"阶段 {id} 的实例为 null", nameof(stages));
                if (_stages.ContainsKey(id)) throw new ArgumentException($"重复阶段 id:{id}", nameof(stages));
                _stages.Add(id, stage);
            }
            foreach (var s in _stages.Values) s.OnInit(this);   // 全部阶段先于 Start 初始化一遍
        }

        /// <summary>当前阶段 id（未 Start 时无意义，配 <see cref="Started"/> 判读）。</summary>
        public TId Current => _currentId;

        /// <summary>是否已 Start（ConfigPanel/HUD 读未启动态用）。</summary>
        public bool Started => _current != null;

        /// <summary>是否有挂起请求（驱动层可据此决定要不要 Advance）。</summary>
        public bool HasPending => _hasPending;

        /// <summary>挂起目标 id（无挂起时无意义）。</summary>
        public TId PendingId => _pendingId;

        /// <summary>当前阶段已持续时长（`Advance` 后归零）。</summary>
        public float StageTime => _stageTime;

        /// <summary>累计迁移次数（诊断）。</summary>
        public long TransitionCount => _transitionCount;

        /// <summary>启动（一次）；全部阶段已 OnInit。</summary>
        public void Start(TId initialId)
        {
            if (_current != null) throw new InvalidOperationException($"{_name}:Start 只能调用一次");
            var stage = GetStage(initialId);
            _current = stage;
            _currentId = initialId;
            _stageTime = 0f;
            stage.OnEnter(this, default);                       // 初始进入无 leave，立即执行（payload 用 default）
        }

        /// <summary>
        /// 发起迁移请求（**只入队**，不迁移）。同帧多次 = last-wins；payload 随之覆盖。
        /// 未 Start / 重入 / 未注册 / OnLeave 窗口内 → 当场抛。
        /// </summary>
        public void Request(TId nextId, in TReq req)
        {
            if (_current == null) throw new InvalidOperationException($"{_name}:Start 之前禁止 Request");
            if (_inLeave) throw new InvalidOperationException($"{_name}:OnLeave 期间禁止 Request（离场中改道自相矛盾）");
            GetStage(nextId);                                   // 未注册 → 当场抛（不等帧末）
            if (EqualityComparer<TId>.Default.Equals(nextId, _currentId))
                throw new InvalidOperationException($"{_name}:重入禁止({nextId})——重启语义请拆阶段");

            _pendingId = nextId;                                // last-wins
            _pendingReq = req;                                  // payload 同步 last-wins
            _hasPending = true;
        }

        /// <summary>无 payload 的迁移请求（= <c>Request(nextId, default)</c>）。</summary>
        public void Request(TId nextId) => Request(nextId, default);

        /// <summary>
        /// 应用队列里的挂起请求：`OnLeave`(旧) → `OnEnter`(新, in payload)。
        /// **信号驱动的入口就是它**（调用方自己决定何时推进）；`Tick` 内部也会调。
        /// </summary>
        public void Advance()
        {
            if (!_hasPending) return;

            var next = GetStage(_pendingId);
            var nextId = _pendingId;
            var req = _pendingReq;
            _hasPending = false;
            _pendingReq = default;                              // 交接后清引用（防 payload 里的对象被长期持有）

            _inLeave = true;
            try { _current.OnLeave(this); }
            finally { _inLeave = false; }

            _transitionCount++;
            _current = next;
            _currentId = nextId;
            _stageTime = 0f;
            next.OnEnter(this, in req);                         // 此处再 Request → 写 pending，下次 Advance 生效（不递归）
        }

        /// <summary>每帧驱动：`OnUpdate` → 有挂起则 `Advance`（一帧最多一变）。</summary>
        public void Tick(float realDelta)
        {
            if (_current == null) return;                       // 未启动：静默跳过（创建与 Start 应同帧）
            _stageTime += realDelta;
            _current.OnUpdate(this, realDelta);
            if (_hasPending) Advance();
        }

        /// <summary>
        /// 事件处理（可选接口 <see cref="IEventSink{TEvt}"/>）：平面机只有一个活动阶段，故等价于"问它一次"。
        /// 层级机的冒泡在 <see cref="HierarchicalStageMachine{TId,TReq}.Raise{TEvt}"/>——本方法保证
        /// "flat 是 HSM 的退化形态"在事件语义上也成立。
        /// </summary>
        public bool Raise<TEvt>(in TEvt e)
            => _current is IEventSink<TEvt> sink && sink.TryHandle(in e);

        private IStage<TId, TReq> GetStage(TId id)
        {
            if (!_stages.TryGetValue(id, out var s))
                throw new InvalidOperationException($"{_name}:阶段未注册 {id}");
            return s;
        }

        // ---- IModuleStats（HUD：注册即发现；禁每帧分配）----

        public string StatsName => _name;

        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            into["当前阶段"] = _current == null ? "(未启动)" : _currentId.ToString();
            into["阶段时长"] = _stageTime.ToString("0.0");
            into["阶段数"] = _stages.Count.ToString();
            into["累计切换"] = _transitionCount.ToString();
        }
    }
}
