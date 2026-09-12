using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // StrictMode 用例断言 Log.Recent（全局静态）
    public sealed class EventCenterTests
    {
        private sealed class ProbeEvent { }
        private sealed class UnheardEvent { }

        [Fact]
        public void EventCenter_订阅派发收到_注销后再派发不收到()
        {
            var events = new EventCenter();
            var received = 0;
            var unsub = events.Subscribe<ProbeEvent>(_ => received++);

            events.Publish(new ProbeEvent());
            Assert.Equal(1, received);

            unsub();
            events.Publish(new ProbeEvent());
            Assert.Equal(1, received);   // 注销后不再收到
        }

        [Fact]
        public void EventCenter_注销委托重复执行_幂等不抛()
        {
            var events = new EventCenter();
            var unsub = events.Subscribe<ProbeEvent>(_ => { });
            unsub();
            unsub();                     // 幂等：EventChannel.Remove no-op
        }

        [Fact]
        public void EventCenter_派发中改集_本轮快照不崩_下轮生效()
        {
            var events = new EventCenter();
            var secondReceived = 0;

            events.Subscribe<ProbeEvent>(_ =>
            {
                // 派发中订阅：本轮快照不含它，下轮生效
                events.Subscribe<ProbeEvent>(_ => secondReceived++);
            });

            events.Publish(new ProbeEvent());
            Assert.Equal(0, secondReceived);
            events.Publish(new ProbeEvent());
            Assert.Equal(1, secondReceived);
        }

        [Fact]
        public void EventCenter_PublishQueued_当帧不触发_Tick后触发()
        {
            var events = new EventCenter();
            var received = 0;
            events.Subscribe<ProbeEvent>(_ => received++);

            events.PublishQueued(new ProbeEvent());
            Assert.Equal(0, received);   // 队列路径不经 Publish 直派

            events.Tick(0.016f);
            Assert.Equal(1, received);
        }

        [Fact]
        public void EventCenter_StrictMode_未订阅派发_Recent出现Warning()
        {
            var events = new EventCenter { StrictMode = true };
            events.Publish(new UnheardEvent());
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Warning, last.Level);
            Assert.Contains(nameof(UnheardEvent), last.Message);
        }

        [Fact]
        public void EventCenter_热路径稳态零GC()
        {
            var events = new EventCenter();
            events.Subscribe<ProbeEvent>(_ => { });
            events.Publish(new ProbeEvent());          // 热身：通道/快照缓冲就位

            var batch = new ProbeEvent[1000];          // 事件对象预分配在测量外——测的是派发路径
            for (int i = 0; i < batch.Length; i++) batch[i] = new ProbeEvent();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < batch.Length; i++) events.Publish(batch[i]);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0, after - before);
        }

        [Fact]
        public void EventCenter_池化事件_无订阅者路径同样回收()
        {
            var events = new EventCenter { StrictMode = false };
            var e = ReferencePool.Acquire<PooledProbe>();
            events.Publish(e);                          // 无订阅者：发布即移交，中心统一回收
            Assert.Same(e, ReferencePool.Acquire<PooledProbe>());   // 已回池 → 再取是同一实例
        }

        private sealed class PooledProbe : IReference
        {
            public void Clear() { }
        }
    }
}
