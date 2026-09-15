using System;
using System.Collections.Generic;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    // 订阅袋（审计补测 2026-09-15）：C# 对象事件订阅的生命周期归属件——§7.3"返回注销委托"的集合形态。
    // 无静态状态（不触碰 Log/池），故不入 CoreStatic Collection。
    public sealed class SubscriptionBagTests
    {
        [Fact]
        public void Dispose_逆序释放_注册的对称栈式清理()
        {
            var bag = new SubscriptionBag();
            var order = new List<int>();
            bag.Add(() => order.Add(1));
            bag.Add(() => order.Add(2));
            bag.Add(() => order.Add(3));

            bag.Dispose();

            Assert.Equal(new[] { 3, 2, 1 }, order);   // 逆序：后注册的先释放
        }

        [Fact]
        public void Dispose_幂等_重复调用不重复执行()
        {
            var bag = new SubscriptionBag();
            int calls = 0;
            bag.Add(() => calls++);

            bag.Dispose();
            bag.Dispose();

            Assert.Equal(1, calls);
        }

        [Fact]
        public void Dispose后Add_当场抛_防悬挂订阅()
        {
            var bag = new SubscriptionBag();
            bag.Dispose();

            Assert.Throws<ObjectDisposedException>(() => bag.Add(() => { }));
        }

        [Fact]
        public void Dispose_单个委托抛异常_其余照常执行且不外泄()
        {
            var bag = new SubscriptionBag();
            var order = new List<int>();
            bag.Add(() => order.Add(1));
            bag.Add(() => throw new InvalidOperationException("boom"));
            bag.Add(() => order.Add(3));               // 逆序 → 先执行

            var ex = Record.Exception(() => bag.Dispose());

            Assert.Null(ex);                            // 不外泄（内部 Log.Error）
            Assert.Contains(3, order);                  // 抛错的先跑完
            Assert.Contains(1, order);                  // 抛错之后仍继续
        }

        [Fact]
        public void 零订阅_Dispose不抛也不分配()
        {
            var bag = new SubscriptionBag();
            var ex = Record.Exception(() => bag.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void 注册空委托_抛参数异常()
        {
            var bag = new SubscriptionBag();
            Assert.Throws<ArgumentNullException>(() => bag.Add(null));
        }
    }
}
