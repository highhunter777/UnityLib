using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    public sealed class LuaRegistryTests   // 实例注册表，无静态状态（时机断言机制已于 2026-09-10 决策删除）
    {
        private interface IFakeLogic { }

        private sealed class FakeLogic : IFakeLogic { }

        private readonly LuaRegistry<IFakeLogic> _reg = new LuaRegistry<IFakeLogic>("Test");

        [Fact]
        public void LuaRegistry_Fill_Get_Has往返()
        {
            var impl = new FakeLogic();
            _reg.Fill("Test.A", impl);
            Assert.True(_reg.Has("Test.A"));
            Assert.False(_reg.Has("Test.B"));
            Assert.Same(impl, _reg.Get("Test.A"));
        }

        [Fact]
        public void LuaRegistry_重复Fill_抛()
        {
            _reg.Fill("Test.A", new FakeLogic());
            var ex = Assert.Throws<InvalidOperationException>(() => _reg.Fill("Test.A", new FakeLogic()));
            Assert.Contains("重复注册", ex.Message);
        }

        [Fact]
        public void LuaRegistry_Get未命中_抛且信息含路径约定提示()
        {
            var ex = Assert.Throws<KeyNotFoundException>(() => _reg.Get("Test.Missing"));
            Assert.Contains("Test.", ex.Message);           // 报错含 kind 前缀
            Assert.Contains("§4.4", ex.Message);            // 路径约定提示
        }

        [Fact]
        public void LuaRegistry_FillGeneration递增()
        {
            Assert.Equal(0, _reg.Generation);
            _reg.Fill("Test.A", new FakeLogic());
            _reg.Fill("Test.B", new FakeLogic());
            Assert.Equal(2, _reg.Generation);               // 壳据此丢弃缓存引用
        }

        // ---- M1 demo 链路（手册 §三 自测：注册测试服务与假适配器）----

        [Fact]
        public void M1Demo_DI注入假适配器_经注册表Get取回同一实例()
        {
            var container = new ServiceContainer();
            container.RegisterFactory<IFakeLogic>(_ => new FakeLogic());   // 假适配器（M1 尚无 Lua）

            var adapter = container.Resolve<IFakeLogic>();                 // ① DI 注入

            var reg = new LuaRegistry<IFakeLogic>("Test");
            reg.Fill("Test.Adapter", adapter);                             // ② Fill
            Assert.Same(adapter, reg.Get("Test.Adapter"));                 // ③ Get 同实例
        }
    }
}
