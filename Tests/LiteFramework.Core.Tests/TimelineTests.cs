using System;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>时间轴（M4 §2.7）：乱序注册按时间点执行 / Stop 中断 / Finished 语义 / 步骤抛隔离 / 暂停冻结。
    /// 步骤异常经 SafeCall.Invoke → Log.Error（全局静态，无线程锁）。</summary>
    [Collection("CoreStatic")]
    public sealed class TimelineTests
    {
        [Fact]
        public void Timeline_乱序注册_按时间点顺序执行()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            var order = new System.Collections.Generic.List<string>();
            runner.CreateTimeline()
                .At(1.0f, () => order.Add("late"))
                .At(0.2f, () => order.Add("a"))
                .At(0.5f, () => order.Add("b"))
                .Start();
            for (int i = 0; i < 12; i++)
            {
                clock.Tick(0.1f);
                runner.Tick(0.1f);
            }
            Assert.Equal(new[] { "a", "b", "late" }, order);
        }

        [Fact]
        public void Timeline_跑完自摘除_Finished置位()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            var tl = runner.CreateTimeline();
            tl.At(0.2f, () => { });
            tl.Start();
            clock.Tick(0.1f); runner.Tick(0.1f);
            Assert.False(tl.Finished);
            clock.Tick(0.1f); runner.Tick(0.1f);
            Assert.True(tl.Finished);
            clock.Tick(0.1f); runner.Tick(0.1f);           // 完成后继续 Tick 不复触
            Assert.True(tl.Finished);
        }

        [Fact]
        public void Timeline_Stop中断_后续动作不执行()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            int second = 0;
            var tl = runner.CreateTimeline();
            ITimeline handle = tl;
            tl.At(0.1f, () => handle.Stop());
            tl.At(0.2f, () => second++);
            tl.Start();
            clock.Tick(0.1f); runner.Tick(0.1f);           // 第一步触发并 Stop
            clock.Tick(0.1f); runner.Tick(0.1f);
            Assert.Equal(0, second);
            Assert.False(tl.Finished);                     // 中断≠完成
        }

        [Fact]
        public void Timeline_步骤抛异常_隔离后续步骤继续()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            int fired = 0;
            runner.CreateTimeline()
                .At(0.1f, () => throw new InvalidOperationException("boom"))
                .At(0.2f, () => fired++)
                .Start();
            clock.Tick(0.1f); runner.Tick(0.1f);
            clock.Tick(0.1f); runner.Tick(0.1f);
            Assert.Equal(1, fired);
        }

        [Fact]
        public void Timeline_暂停冻结()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            int fired = 0;
            runner.CreateTimeline().At(0.2f, () => fired++).Start();
            clock.Paused = true;
            clock.Tick(1f); runner.Tick(1f);
            Assert.Equal(0, fired);
            clock.Paused = false;
            clock.Tick(0.3f); runner.Tick(0.3f);
            Assert.Equal(1, fired);
        }

        [Fact]
        public void Timeline_重复Start与空轴抛()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;
            var runner = new GameTimelineRunner(clock);
            var tl = runner.CreateTimeline();
            Assert.Throws<InvalidOperationException>(() => tl.Start());   // 空轴
            tl.At(0.1f, () => { });
            tl.Start();
            Assert.Throws<InvalidOperationException>(() => tl.Start());   // 重复
            Assert.Throws<InvalidOperationException>(() => tl.At(0.2f, () => { }));   // 启动后追加
        }
    }
}
