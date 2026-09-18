using System;
using System.Threading;

namespace RoomServer
{
    /// <summary>
    /// 60Hz 权威循环宿主（《M10实施指导》风险 5："PeriodicTimer 节拍漂移 → 绝对时间锚定"）。
    ///
    /// 锚定式：<c>nextTick = startMs + n × 16.667ms</c>（**不是** tick += interval），
    /// 每轮按锚点 Sleep 剩余时间——单轮抖动不累积。落后超过一帧时**追赶但不跳帧**
    /// （房间的 StepFrame 每次都推进一帧，落后时连跑几轮补上；补跑上限 <see cref="MaxCatchUp"/> 防雪崩）。
    /// <see cref="Run(long)"/> 供验收脚本用（跑满时长返回，帧号线性度可断言）；<see cref="Start"/>/<see cref="Stop"/> 供常驻形态用。
    /// </summary>
    public sealed class ServerLoop
    {
        /// <summary>单轮最多补帧数（防"落后 → 补跑 → 更落后"的雪崩；超出即丢弃时间债）。</summary>
        public const int MaxCatchUp = 5;

        private readonly ServerHost _host;
        private readonly Thread _thread;
        private volatile bool _running;

        /// <summary>实际跑过的权威帧数（= 调用 StepFrame 的次数，含补帧）。</summary>
        public long Ticks;

        /// <summary>因补帧上限而丢弃的时间债（毫秒；>0 说明服务器跟不上，Ops 观测项）。</summary>
        public long DroppedTimeMs;

        public ServerLoop(ServerHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _thread = new Thread(LoopBody) { IsBackground = true, Name = "RoomServer.Loop" };
        }

        public void Start() { _running = true; _thread.Start(); }
        public void Stop() { _running = false; _thread.Join(2000); }

        /// <summary>跑满 <paramref name="durationMs"/> 毫秒后返回（验收脚本/用例用；期间按 1ms 粒度 Pump）。</summary>
        public void Run(long durationMs)
        {
            long start = MonotonicMs();
            long end = start + durationMs;
            long tick = 0;
            while (true)
            {
                long next = start + (long)(tick * 1000.0 / SimTickHz);
                if (next >= end) break;
                SleepUntil(next);
                _host.Pump();
                Ticks++;
                tick++;

                // 追赶：落后则连续补跑（不跳帧），上限 MaxCatchUp
                int catchUp = 0;
                while (MonotonicMs() > start + (long)(tick * 1000.0 / SimTickHz) && catchUp < MaxCatchUp)
                {
                    _host.Pump();
                    Ticks++;
                    tick++;
                    catchUp++;
                }
                if (catchUp >= MaxCatchUp) DroppedTimeMs += 1000 / SimTickHz;
            }
        }

        private void LoopBody()
        {
            long start = MonotonicMs();
            long tick = 0;
            while (_running)
            {
                SleepUntil(start + (long)(tick * 1000.0 / SimTickHz));
                _host.Pump();
                Ticks++;
                tick++;
            }
        }

        private const int SimTickHz = 60;

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
