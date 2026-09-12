#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
using System.Collections.Generic;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>M0 自测（Play 模式冒烟）。挂 GameEntry 同 GameObject。
    /// **只测"装配后真实状态"的用例**——装配链/DI 注册即发现/Tickables 驱动只有 Play 下才成立，
    /// 这正是从 Editor 菜单改为 mono 脚本的原因；纯逻辑断言的权威在 Tests/（xUnit，§14.6）。
    /// 结果走 Log（tag "SelfTest"）：通过 Info("M0 PASS")，失败逐条 Error——DevHUD ErrorCount 可见。
    /// 归属：业务程序集（LiteGame.Runtime）的工程冒烟件——修正 2026-09-10 前的 namespace/程序集错位。</summary>
    public sealed class M0SelfTestRunner : MonoBehaviour
    {
        public bool RunOnStart = true;
        private IReadOnlyList<ITickable> _tickables;
        private IReadOnlyList<IModuleStats> _stats;
        private int _pass, _fail;

        /// <summary>GameEntry 装配期注入（与 DevHUD/DebugTuner 同款模式，不碰容器）。</summary>
        public void Inject(IReadOnlyList<ITickable> tickables, IReadOnlyList<IModuleStats> stats)
            { _tickables = tickables; _stats = stats; }

        private void Start() { if (RunOnStart) RunAll(); }

        [ContextMenu("Run Self Test")]
        public void RunAll()
        {
            _pass = 0; _fail = 0;

            // ---- 装配链 ----
            Check("FileSys 已 Init（PathOf 不抛）", PathOfDoesNotThrow());
           
            Check("Tickables 非空", _tickables != null && _tickables.Count >= 5);   // 泵+时钟×2+FSM 至少 5
            Check("Stats 非空", _stats != null && _stats.Count >= 1);

            // ---- 注册即发现：关键件在驱动列表里（类型匹配，不匹配 StatsName 字符串）----
            bool hasEvent = false, hasWorld = false, hasUi = false, hasFsm = false;
            foreach (var t in _tickables)
            {
                if (t is IEventCenter) hasEvent = true;
                if (t is IWorldClock) hasWorld = true;
                if (t is IUIClock) hasUi = true;
                if (t is Fsm<ProcedureOwner>) hasFsm = true;
            }
            Check("事件中心已注册", hasEvent);
            Check("双时钟已注册", hasWorld && hasUi);
            Check("FSM 已注册", hasFsm);

            // ---- 事件中心收发冒烟（从 Tickables 取回具体实现）----
            foreach (var t in _tickables)
            {
                if (t is IEventCenter events)
                {
                    var probe = new ProbeEvent();
                    var received = 0;
                    var unsub = events.Subscribe<ProbeEvent>(_ => received++);
                    events.Publish(probe);                       // FireNow：同步收到
                    unsub(); unsub();                            // 幂等注销不抛
                    events.Publish(probe);                       // 注销后不再收到
                    Check("事件收发冒烟（订阅/派发/幂等注销）", received == 1);
                    break;
                }
            }

            if (_fail == 0) Log.Info($"M0 PASS（{_pass} 项）", "SelfTest");
            else Log.Error($"M0 FAIL：{_fail}/{_pass + _fail} 项", "SelfTest");
        }

        private sealed class ProbeEvent { }

        private static bool PathOfDoesNotThrow()
        {
            try { FileSys.PathOf("selftest", "probe.txt"); return true; }
            catch { return false; }
        }

        private void Check(string what, bool ok)
        {
            if (ok) _pass++;
            else { _fail++; Log.Error($"SelfTest FAIL: {what}", "SelfTest"); }
        }
    }
}
#endif
