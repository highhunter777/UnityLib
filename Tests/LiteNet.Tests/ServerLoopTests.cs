using System.Diagnostics;
using LiteNet.Transport;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 定拍循环用例（《M10实施指导》风险 5 + 2026-09-19 修订：整数锚点 + 有界追赶）。
    /// 无连客户端（Room 未 Start → 每 tick 空转），只验节拍器本身：帧率正确、无掉债、有上界。
    /// </summary>
    public sealed class ServerLoopTests
    {
        private const int Port = 28891;

        [Fact]
        public void 空载跑一秒_节拍数与理论值一致且无掉债()
        {
            using var host = new ServerHost(new KcpTransportServer(), Port);
            host.Ops.PrintEnabled = false;
            var loop = new ServerLoop(host);

            var watch = Stopwatch.StartNew();
            loop.Run(1000);
            watch.Stop();

            ServerLoop.LoopStats stats = loop.Stats;
            long expected = 1000 / ServerLoop.TickPeriodMs;      // 16ms 锚点 → 62~63 tick
            Assert.InRange(stats.Ticks, expected - 3, expected + 3);
            Assert.Equal(0L, stats.DroppedTimeMs);               // 空载不该丢债
            Assert.Equal(0L, stats.CatchUpAbandoned);
            Assert.InRange(watch.ElapsedMilliseconds, 950, 1200);   // 跑满时长即返回（不超时不提前太多）
        }

        [Fact]
        public void 跑满时长即停_不因追赶而超出()
        {
            using var host = new ServerHost(new KcpTransportServer(), Port + 1);
            host.Ops.PrintEnabled = false;
            var loop = new ServerLoop(host);

            loop.Run(300);
            long first = loop.Stats.Ticks;

            loop.Run(300);
            long second = loop.Stats.Ticks - first;              // 第二次仍是 ~19 tick（不是累积到两倍）

            Assert.InRange(second, 300 / ServerLoop.TickPeriodMs - 3, 300 / ServerLoop.TickPeriodMs + 3);
        }

        [Fact]
        public void 过载时_追赶有上界并记账丢债_不会无界满核()
        {
            using var host = new ServerHost(new KcpTransportServer(), Port + 2);
            host.Ops.PrintEnabled = false;
            // 单帧体 8ms（模拟过载：> 16ms 锚点的 1/2，连续跑必然落后）
            var loop = new ServerLoop(host, () => System.Threading.Thread.Sleep(8));

            var watch = Stopwatch.StartNew();
            loop.Run(400);
            watch.Stop();

            ServerLoop.LoopStats stats = loop.Stats;
            int maxTicksIfUnbounded = (int)(watch.ElapsedMilliseconds / 8) + 5;   // 无界追帧会把时间全填满

            Assert.True(stats.Ticks < maxTicksIfUnbounded,
                $"追赶必须有上界：ticks={stats.Ticks} 逼近无界值 {maxTicksIfUnbounded}");
            Assert.True(stats.DroppedTimeMs > 0, "过载必须记时间债（否则就是无界追帧）");
            Assert.True(stats.CatchUpAbandoned > 0);
            Assert.InRange(watch.ElapsedMilliseconds, 380, 900);   // 跑满时长即返回（不被追赶拖长）
        }
    }
}
