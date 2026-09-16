using LiteSim;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>快照环机制用例（《M9 实施指导》§2.4：环边界/窗口判定/独立实例防共享 + 常量对账）。</summary>
    public class SnapshotRingTests
    {
        private static SimWorldState WorldWithMark(int mark)
        {
            var s = new SimWorldState { Frame = mark };
            s.Globals[0] = (byte)mark;
            s.CustomData[13] = (byte)(mark + 1);
            return s;
        }

        [Fact]
        public void 常量_环容量与输入历史对账()
        {
            Assert.Equal(32, SimConfig.MaxInputHistory);
            Assert.Equal(2, SimConfig.MaxRollbacksPerFrame);
            Assert.Equal(9, SimConfig.MaxRollbackFrames + 1); // 环容量 = 回滚深度 8 + 当前（§5.2）
        }

        [Fact]
        public void 捕获恢复_窗口内逐帧往返()
        {
            var ring = new SnapshotRing(9);
            for (int f = 0; f < 9; f++) ring.Capture(f, WorldWithMark(f));

            var probe = new SimWorldState();
            for (int f = 0; f < 9; f++)
            {
                Assert.True(ring.ContainsFrame(f));
                Assert.True(ring.TryRestore(f, probe));
                Assert.Equal(f, probe.Frame);
                Assert.Equal((byte)f, probe.Globals[0]);
                Assert.Equal((byte)(f + 1), probe.CustomData[13]);
            }
        }

        [Fact]
        public void 环形覆写_最老帧被逐出()
        {
            var ring = new SnapshotRing(9);
            for (int f = 0; f < 10; f++) ring.Capture(f, WorldWithMark(f)); // 第 10 帧覆写槽 0

            Assert.False(ring.ContainsFrame(0));
            Assert.False(ring.TryRestore(0, new SimWorldState()));
            for (int f = 1; f < 10; f++) Assert.True(ring.ContainsFrame(f));
        }

        [Fact]
        public void 越界_未捕获与负帧号返回false()
        {
            var ring = new SnapshotRing(9);
            ring.Capture(5, WorldWithMark(5));

            Assert.False(ring.TryRestore(4, new SimWorldState()));   // 未捕获
            Assert.False(ring.TryRestore(6, new SimWorldState()));
            Assert.False(ring.TryRestore(-1, new SimWorldState()));   // 帧前状态（决策②下恒不可达）
            Assert.False(ring.TryRestore(100, new SimWorldState()));
        }

        [Fact]
        public void 防共享_恢复态修改不污染环内快照()
        {
            var ring = new SnapshotRing(9);
            ring.Capture(3, WorldWithMark(3));

            var restored = new SimWorldState();
            Assert.True(ring.TryRestore(3, restored));
            restored.Globals[0] = 0xFF;      // 改"恢复出来的活状态"
            restored.CustomData[7] = 0xAB;

            var again = new SimWorldState();
            Assert.True(ring.TryRestore(3, again));
            Assert.Equal((byte)3, again.Globals[0]);   // 环内快照不受恢复态修改影响
            Assert.Equal(0, again.CustomData[7]);
        }

        [Fact]
        public void 捕获后修改活状态_不影响环内快照()
        {
            var ring = new SnapshotRing(9);
            var live = WorldWithMark(7);
            ring.Capture(7, live);

            live.Globals[0] = 0xEE;                   // 活状态继续演进

            var probe = new SimWorldState();
            Assert.True(ring.TryRestore(7, probe));
            Assert.Equal((byte)7, probe.Globals[0]);  // 环内是捕获时刻的深拷副本
        }
    }
}
