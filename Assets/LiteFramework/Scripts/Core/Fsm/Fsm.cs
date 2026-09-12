using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    /// <summary>
    /// 状态基类。状态对象 = 无状态单例:实例被整个 FSM 共享,
    /// 可变数据一律放 Fsm 数据字典(跨状态交接)或 Owner(状态私有),禁止状态类加实例字段。
    /// </summary>
    public abstract class FsmState<TOwner>
    {
        protected FsmState() { }
        public virtual void OnInit(Fsm<TOwner> fsm) { }
        public virtual void OnEnter(Fsm<TOwner> fsm) { }
        public virtual void OnUpdate(Fsm<TOwner> fsm, float elapseSeconds) { }
        public virtual void OnLeave(Fsm<TOwner> fsm) { }
    }

    /// <summary>
    /// 泛型状态机。切换语义(写给未来读代码的人):
    /// ① ChangeState 只记 pending,Tick 末尾(OnUpdate 之后)统一 leave/enter——同帧多次请求 last-wins,一帧最多一变;
    /// ② OnEnter 内请求 → 挂下帧末(期间新状态收到一次 OnUpdate);OnLeave 内请求 → 抛(离场中改道自相矛盾);
    /// ③ 重入(切向当前状态)与未注册类型均在调用时抛,不等到帧末;
    /// ④ 状态回调不捕获异常——流程是脊柱,dev 炸响优于跛行;隔离边界在 GameEntry 的 tick 驱动层。
    /// </summary>
    public sealed class Fsm<TOwner> : ITickable, IModuleStats
    {
        private readonly string _name;
        private readonly TOwner _owner;
        private readonly Dictionary<Type, FsmState<TOwner>> _states;
        private FsmState<TOwner> _current;
        private FsmState<TOwner> _pending;      // 延迟切换;null = 无挂起
        private bool _inLeave;                  // OnLeave 执行中(改道禁令窗口)
        private float _stateTime;
        private long _transitionCount;

        public Fsm(string name, TOwner owner, params FsmState<TOwner>[] states)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (states == null || states.Length == 0) throw new ArgumentException("FSM 至少需要一个状态", nameof(states));
            _name = name; _owner = owner;
            _states = new Dictionary<Type, FsmState<TOwner>>(states.Length);
            foreach (var s in states)
            {
                if (s == null) throw new ArgumentException("包含 null 状态", nameof(states));
                if (_states.ContainsKey(s.GetType()))
                    throw new ArgumentException($"重复状态类型:{s.GetType().Name}", nameof(states));
                _states.Add(s.GetType(), s);
            }
            foreach (var s in _states.Values) s.OnInit(this);   // 全部状态先于 Start 初始化一遍
        }

        public TOwner Owner => _owner;
        public FsmState<TOwner> CurrentState => _current;       
        public float CurrentStateTime => _stateTime;

        public void Start<TState>() where TState : FsmState<TOwner>
        {
            if (_current != null) throw new InvalidOperationException($"FSM {_name}:Start 只能调用一次");
            var state = GetState<TState>();
            _current = state;
            state.OnEnter(this);                                // 初始进入无 leave,立即执行
        }

        public void ChangeState<TState>() where TState : FsmState<TOwner>
        {
            if (_current == null) throw new InvalidOperationException($"FSM {_name}:Start 之前禁止 ChangeState");
            if (_inLeave) throw new InvalidOperationException($"FSM {_name}:OnLeave 期间禁止 ChangeState");
            var state = GetState<TState>();                     // 未注册类型此处当场抛
            if (state == _current)
                throw new InvalidOperationException($"FSM {_name}:重入禁止({typeof(TState).Name})——重启语义请拆状态");
            _pending = state;                                   // last-wins:同帧多次请求,最后一次生效
        }

        public void Tick(float realDelta)
        {
            if (_current == null) return;                       // 未启动:静默跳过(创建与 Start 应同帧)
            _stateTime += realDelta;
            _current.OnUpdate(this, realDelta);                 // 流程计时用真实间隔；受变速的时间是 Sim 的事（§3.9）
            if (_pending != null) ApplyPending();
        }

        private void ApplyPending()
        {
            var next = _pending;
            _pending = null;
            _inLeave = true;
            try { _current.OnLeave(this); }
            finally { _inLeave = false; }
            _transitionCount++;
            _current = next;
            _stateTime = 0f;
            next.OnEnter(this);                                 // 此处 ChangeState → 写 _pending,下帧末生效(不递归)
        }

        private FsmState<TOwner> GetState<TState>() where TState : FsmState<TOwner>
        {
            if (!_states.TryGetValue(typeof(TState), out var s))
                throw new InvalidOperationException($"FSM {_name}:状态未注册 {typeof(TState).Name}");
            return s;
        }

        // ---- 流程间传参:走 TOwner 具名对象(业务 owner 字段，如经 IProcedureOwner 契约的 LastError),不走字符串键字典 ----
        // 旧 SetData/GetData(Dictionary<string, object>)已删:字符串键零编译期检查、object 装箱、
        // 静默 cast 错误——具名 Owner 字段三个问题一次消失。需要跨状态共享的动态数据,加 Owner 字段。

        public string StatsName => _name;

        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            into["当前状态"] = _current?.GetType().Name ?? "(未启动)";
            into["状态时长"] = _stateTime.ToString("0.0");
            into["状态数"] = _states.Count.ToString();
            into["累计切换"] = _transitionCount.ToString();
        }
    }
}
