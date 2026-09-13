using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>
    /// 时间轴执行器实现（M4 §2.7，手册步骤 7；契约 = M1 ITimelineRunner）：
    /// 有序动作点推进（剧情与技能共用）。回调注册等待模型（决策 ②——不用协程）；
    /// 推进量 = 注入时钟 ScaledDelta（决策 ①——时停冻结语义单源）；
    /// 同刻动作点按注册序稳定触发；步骤回调经 SafeCall 隔离（单步抛不炸后续步骤）；
    /// 回调内可 Stop（立即中断推进）。
    /// </summary>
    public sealed class GameTimelineRunner : ITimelineRunner
    {
        private readonly IGameClock _clock;
        private readonly List<GameTimeline> _running = new List<GameTimeline>(8);

        public GameTimelineRunner(IGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public ITimeline CreateTimeline()
        {
            return new GameTimeline(_clock, this);
        }

        internal void Track(GameTimeline timeline)
        {
            if (!_running.Contains(timeline)) _running.Add(timeline);
        }

        internal void Untrack(GameTimeline timeline) => _running.Remove(timeline);

        public void Tick(float realDelta)
        {
            float dt = _clock.ScaledDelta;
            if (dt <= 0f) return;
            for (int i = _running.Count - 1; i >= 0; i--)
                _running[i].Advance(dt);
        }

        public string StatsName => "Timeline";

        public void Snapshot(Dictionary<string, string> into)
        {
            into["运行中"] = _running.Count.ToString();
        }
    }

    /// <summary>时间轴实例：At 链式注册动作点 → Start 推进 → Stop 可中断。</summary>
    public sealed class GameTimeline : ITimeline
    {
        private struct Step
        {
            public float At;
            public int Seq;                            // 注册序（同刻稳定排序）
            public Action Action;
        }

        private readonly IGameClock _clock;
        private readonly GameTimelineRunner _runner;
        private readonly List<Step> _steps = new List<Step>(8);
        private int _seq;
        private int _cursor;
        private float _elapsed;
        private bool _started;
        private bool _completed;

        internal GameTimeline(IGameClock clock, GameTimelineRunner runner)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        public ITimeline At(float seconds, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (_started) throw new InvalidOperationException("时间轴已启动——禁止追加动作点（重建新轴）");
            _steps.Add(new Step { At = Math.Max(0f, seconds), Seq = _seq++, Action = action });
            return this;
        }

        public void Start()
        {
            if (_started) throw new InvalidOperationException("时间轴重复 Start");
            if (_steps.Count == 0) throw new InvalidOperationException("空时间轴不可 Start");
            _steps.Sort((a, b) => a.At != b.At
                ? a.At.CompareTo(b.At)
                : a.Seq.CompareTo(b.Seq));                 // 同刻按注册序（稳定语义）
            _elapsed = 0f;
            _cursor = 0;
            _started = true;
            _runner.Track(this);
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;                              // 可中断：中断≠完成（Finished 不置位）
            _runner.Untrack(this);
        }

        public bool Finished => _completed;                // 自然跑完才置位；中断不算

        /// <summary>到期容差（0.1ms）：浮点累积漂移防护（同 GameScheduler.DueEpsilon）。</summary>
        private const float DueEpsilon = 1e-3f;

        internal void Advance(float dt)
        {
            _elapsed += dt;
            while (_cursor < _steps.Count && _steps[_cursor].At <= _elapsed + DueEpsilon)
            {
                var step = _steps[_cursor];
                _cursor++;
                SafeCall.Invoke(step.Action, $"Timeline[{step.Seq}]");
                if (!_started) return;                     // 回调内 Stop——立即中断推进
            }
            if (_cursor >= _steps.Count)
            {
                _started = false;
                _completed = true;
                _runner.Untrack(this);
            }
        }
    }
}
