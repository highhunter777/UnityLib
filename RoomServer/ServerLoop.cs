using System;
using System.Threading;

namespace RoomServer
{
    /// <summary>
    /// 60Hz 权威循环宿主（《M10实施指导》风险 5："PeriodicTimer 节拍漂移 → 绝对时间锚定"）。
    ///
    /// 锚定式：<c>nextTickMs = startMs + n × TickPeriodMs</c>（**不是** tick += interval），
    /// 每轮按锚点 Sleep 剩余时间——单轮抖动不累积。
    ///
    /// 两条节拍纪律（2026-09-19 修）：
    /// - **整数时间基**：<c>TickPeriodMs = 1000 / 60 = 16</c>（丢掉的 0.67ms/帧用**有界追上**补回，
    ///   而不是靠 double 乘法精确表达 16.667ms——后者在 tick 很大时（跑满数天）会有累积舍入误差，
    ///   且每帧一次浮点乘法本身没必要）。1/60 秒无法用整数毫秒整除，这里选择"整数锚点 + 追帧"，
    ///   让**帧率长期精确、代价可观测**（<see cref="LoopStats.DroppedTimeMs"/>），而不是让误差静默累积。
    /// - **追赶必须有上限**（<see cref="MaxCatchUp"/>）：无论 <see cref="Run"/> 还是常驻 <see cref="LoopBody"/>，
    ///   落后超过上限即**丢弃时间债**（锚点前移到当前时刻），否则过载时就是无界追帧 = 持续满核。
    /// </summary>
    public sealed class ServerLoop
    {
        /// <summary>逻辑帧率（与 <c>SimConfig.TickRate</c> 同源约定；ServerLoop 不引 LiteSim，故本地复述）。</summary>
        public const int SimTickHz = 60;

        /// <summary>锚点粒度（毫秒）：整数帧周期（1000/60 的整数部分）。缺口由追帧补。</summary>
        public const long TickPeriodMs = 1000 / SimTickHz;

        /// <summary>单轮最多补帧数（防"落后 → 补跑 → 更落后"的雪崩；超出即丢弃时间债）。</summary>
        public const int MaxCatchUp = 5;

        private readonly ServerHost _host;
        private readonly Action _pump;
        private readonly Thread _thread;
        private volatile bool _running;

        /// <summary>节拍统计（常驻线程写、外部读——用 <see cref="LoopStats"/> 聚合，避免逐字段竞态读）。</summary>
        public sealed class LoopStats
        {
            /// <summary>实际跑过的权威帧数（= Pump 次数，含补帧）。</summary>
            public long Ticks;

            /// <summary>累计丢弃的时间债（毫秒；>0 = 服务器跟不上，Ops 观测项）。</summary>
            public long DroppedTimeMs;

            /// <summary>因超过 <see cref="MaxCatchUp"/> 而放弃追帧的次数。</summary>
            public long CatchUpAbandoned;
        }

        private readonly LoopStats _stats = new LoopStats();

        /// <summary>只读快照（外部轮询用；取的是当前值，不保证与 Ticks 同一瞬间一致）。</summary>
        public LoopStats Stats => _stats;

        /// <param name="pumpOverride">单帧体注入点（null = <see cref="ServerHost.Pump"/>）。
        /// 存在的意义是让"过载 → 有界追赶 → 丢债"这条路径可被用例驱动（慢帧模拟），不是生产配置。</param>
        public ServerLoop(ServerHost host, Action pumpOverride = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _pump = pumpOverride ?? host.Pump;
            _thread = new Thread(LoopBody) { IsBackground = true, Name = "RoomServer.Loop" };
        }

        public void Start() { _running = true; _thread.Start(); }
        public void Stop() { _running = false; _thread.Join(2000); }

        /// <summary>跑满 <paramref name="durationMs"/> 毫秒后返回（验收脚本/用例用）。</summary>
        public void Run(long durationMs)
        {
            long start = MonotonicMs();
            long end = start + durationMs;
            long tick = 0;
            while (true)
            {
                long next = start + tick * TickPeriodMs;
                if (next >= end) break;
                SleepUntil(next);
                _pump();
                _stats.Ticks++;              // 累计（跨多次 Run 调用/常驻全程）
                tick++;

                // 追赶：落后则连续补跑（不跳帧），上限 MaxCatchUp；超限即丢债（见类注释）
                int catchUp = 0;
                while (MonotonicMs() > start + tick * TickPeriodMs && catchUp < MaxCatchUp)
                {
                    _pump();
                    _stats.Ticks++;
                    tick++;
                    catchUp++;
                }
                if (catchUp >= MaxCatchUp)
                {
                    long debt = MonotonicMs() - (start + tick * TickPeriodMs);
                    if (debt > 0)
                    {
                        long lost = Math.Min(debt, tick * TickPeriodMs + TickPeriodMs);   // 债 = 被跳过的锚点跨度
                        _stats.DroppedTimeMs += lost;
                        _stats.CatchUpAbandoned++;
                        start += lost;                        // 锚点前移：把丢掉的债从时间轴上去掉
                    }
                }
            }
        }

        /// <summary>常驻循环：与 <see cref="Run(long)"/> 同一套锚定/追赶/丢债规则（此前缺追赶上限 = 过载时无界满核）。</summary>
        private void LoopBody()
        {
            long start = MonotonicMs();
            long tick = 0;
            while (_running)
            {
                SleepUntil(start + tick * TickPeriodMs);
                _pump();
                _stats.Ticks++;
                tick++;

                int catchUp = 0;
                while (_running && MonotonicMs() > start + tick * TickPeriodMs && catchUp < MaxCatchUp)
                {
                    _pump();
                    _stats.Ticks++;
                    tick++;
                    catchUp++;
                }
                if (catchUp >= MaxCatchUp)
                {
                    long debt = MonotonicMs() - (start + tick * TickPeriodMs);
                    if (debt > 0)
                    {
                        long lost = Math.Min(debt, tick * TickPeriodMs + TickPeriodMs);
                        _stats.DroppedTimeMs += lost;
                        _stats.CatchUpAbandoned++;
                        start += lost;
                    }
                }
                if (tick > long.MaxValue - 1_000_000_000L)   // 防极端长期运行下锚点溢出（数百年量级，纯防御）
                {
                    start = MonotonicMs();
                    tick = 0;
                }
            }
        }

        private static void SleepUntil(long targetMs)
        {
            long remain = targetMs - MonotonicMs();
            if (remain <= 0) return;
            if (remain > 1) Thread.Sleep((int)(remain - 1));   // 粗睡到 1ms 内，再自旋收尾（Windows 定时器粒度 ~15ms）
            while (MonotonicMs() < targetMs) Thread.SpinWait(64);
        }

        private static long MonotonicMs() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000L
            / System.Diagnostics.Stopwatch.Frequency;
    }
}
