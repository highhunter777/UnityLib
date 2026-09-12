using System;
using System.Linq;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // ReferencePool 池字典是全局静态；各类用独立嵌套类型避免桶互踩
    public sealed class ReferencePoolTests
    {
        private sealed class RefA : IReference { public void Clear() { } }
        private sealed class RefB : IReference { public void Clear() { } }
        private sealed class RefC : IReference { public void Clear() { } }
        private sealed class RefD : IReference { public void Clear() { } }
        private sealed class RefE : IReference { public void Clear() { } }

        [Fact]
        public void ReferencePool_重复归还_抛异常()
        {
            var a = ReferencePool.Acquire<RefA>();
            ReferencePool.Release(a);
            var ex = Assert.Throws<InvalidOperationException>(() => ReferencePool.Release(a));
            Assert.Contains("重复归还", ex.Message);
        }

        [Fact]
        public void ReferencePool_未Acquire过的实例归还_抛异常()
        {
            var foreign = new RefA();   // 不是 Acquire 所得
            Assert.Throws<InvalidOperationException>(() => ReferencePool.Release(foreign));
        }

        [Fact]
        public void ReferencePool_往返_Release再Acquire是同一实例()
        {
            var a = ReferencePool.Acquire<RefB>();
            ReferencePool.Release(a);
            Assert.Same(a, ReferencePool.Acquire<RefB>());
        }

        [Fact]
        public void ReferencePool_超上限_丢弃并保留上限数量()
        {
            var taken = new System.Collections.Generic.List<RefC>();
            ReferencePool.SetMaxSize<RefC>(2);
            for (int i = 0; i < 5; i++) taken.Add(ReferencePool.Acquire<RefC>());   // Release 只收 Acquire 所得
            foreach (var o in taken) ReferencePool.Release(o);
            var info = ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefC));
            Assert.Equal(2, info.Unused);        // 池内只留上限个
            Assert.Equal(3, info.DroppedCount);  // 其余丢弃
        }

        [Fact]
        public void ReferencePool_预热超上限_自动上调且全部入池()
        {
            ReferencePool.Add<RefD>(100);
            var info = ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefD));
            Assert.True(info.MaxSize >= 100);
            Assert.Equal(100, info.Unused);
        }

        [Fact]
        public void ReferencePool_裁剪_池内减到指定数量()
        {
            ReferencePool.Add<RefE>(10);
            ReferencePool.Remove<RefE>(4);
            var info = ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefE));
            Assert.Equal(6, info.Unused);   // 独立类型，无跨用例共享
        }
    }
}
