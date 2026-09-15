using System;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>调度器（M4 §2.7）：到期触发 / 取消 / 双时钟语义（暂停冻结、变速比例）/ FIFO / 回调隔离。
    /// 直接用 WorldClock/UIClock 真件（引擎无关，纯 C#）——确定性推进。
    /// 回调异常经 SafeCall.Invoke → Log.Error（全局静态，无线程锁）。</summary>
    [Collection("CoreStatic")]
    public sealed class GameSchedulerTests
    {
        private static WorldClock NewClock()
        {
            var clock = new WorldClock();
            clock.MaxDelta = 10f;                          // 解除尖峰钳制——本组用例用大步长确定性推进
            return clock;
        }

        /// <summary>确定性推进：clock.Tick 后 scheduler.Tick（真机由 GameEntry 驱动序保证）。</summary>
        private static void Step(IGameClock clock, IScheduler scheduler, float realDelta)
        {
            clock.Tick(realDelta);
            scheduler.Tick(realDelta);
        }

        [Fact]
        public void Schedule_到期触发一次()
        {
            var clock = NewClock();
            var scheduler = new GameScheduler(clock);
            int fired = 0;
            scheduler.Schedule(0.3f, () => fired++);
            Step(clock, scheduler, 0.1f);
            Assert.Equal(0, fired);
            Step(clock, scheduler, 0.1f);
            Step(clock, scheduler, 0.1f);
            Assert.Equal(1, fired);                        // 累积 0.3 到期，恰好一次
            Step(clock, scheduler, 0.1f);
            Assert.Equal(1, fired);
        }

        [Fact]
        public void Schedule_Cancel_后不触发()
        {
            var clock = NewClock();
            var scheduler = new GameScheduler(clock);
            int fired = 0;
            var id = scheduler.Schedule(0.2f, () => fired++);
            scheduler.Cancel(id);
            Step(clock, scheduler, 0.5f);
            Assert.Equal(0, fired);
        }

        [Fact]
        public void Schedule_暂停冻结_恢复后继续()
        {
            var clock = NewClock();
            var scheduler = new GameScheduler(clock);
            int fired = 0;
            scheduler.Schedule(0.2f, () => fired++);
            clock.Paused = true;
            Step(clock, scheduler, 1f);                    // 暂停：ScaledDelta=0，冻结
            Assert.Equal(0, fired);
            clock.Paused = false;
            Step(clock, scheduler, 0.3f);
            Assert.Equal(1, fired);
        }

        [Fact]
        public void Schedule_变速按比例推进()
        {
            var clock = NewClock();
            var scheduler = new GameScheduler(clock);
            int fired = 0;
            scheduler.Schedule(1f, () => fired++);         // 1 游戏秒
            clock.TimeScale = 2f;                          // 二倍速：0.5 real = 1 游戏
            Step(clock, scheduler, 0.3f);
            Assert.Equal(0, fired);
            Step(clock, scheduler, 0.3f);                  // 累计 0.6 real = 1.2 游戏
            Assert.Equal(1, fired);
        }

        [Fact]
        public void Schedule_同帧到期_按注册序FIFO()
        {
            var clock = NewClock();
            var scheduler = new GameScheduler(clock);
            var order = new System.Collections.Generic.List<string>();
            scheduler.Schedule(0.2f, () => order.Add("A"));
            scheduler.Schedule(0.1f, () => order.Add("B"));
            scheduler.Schedule(0.2f, () => order.Add("C"));
            Step(clock, scheduler, 0.1f);
            Assert.Equal(new[] { "B" }, order);
            Step(clock, scheduler, 0.1f);                  // A、C 同帧到期
            Assert.Equal(new[] { "B", "A", "C" }, order);  // FIFO：注册序
        }

        [Fact]
        public void Schedule_回调抛异常_隔离不炸调度器()
        {
            var clock = NewClock();
            var scheduler = new GameScheduler(clock);
            scheduler.Schedule(0.1f, () => throw new InvalidOperationException("boom"));
            int fired = 0;
            scheduler.Schedule(0.1f, () => fired++);
            Step(clock, scheduler, 0.2f);
            Assert.Equal(1, fired);                        // 前者抛被隔离，后者照常
        }

        [Fact]
        public void UIScheduler_不受世界时停影响()
        {
            var world = new WorldClock();
            var ui = new UIClock();
            var logic = new LogicScheduler(world);
            var uiScheduler = new UIScheduler(ui);
            int logicFired = 0, uiFired = 0;
            logic.Schedule(0.2f, () => logicFired++);
            uiScheduler.Schedule(0.2f, () => uiFired++);
            world.TimeScale = 0f;                          // 世界时停
            for (int i = 0; i < 5; i++)
            {
                world.Tick(0.1f); ui.Tick(0.1f);
                logic.Tick(0.1f); uiScheduler.Tick(0.1f);
            }
            Assert.Equal(0, logicFired);                   // 逻辑轨冻结
            Assert.Equal(1, uiFired);                      // UI 轨不受时停（决策 ①）
        }
    }
}
