using LiteNet.Protocol;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 快照载波（`ISnapshotSource` + `FullSnapshotSource` 全量占位）——M10 第二批权威循环的消费面。
    /// 第三批换增量实现后，本组用例改为"全量/增量两种实现产出同构消息"的对拍。
    /// </summary>
    public sealed class SnapshotSourceTests
    {
        private static SimWorldState World(params (float x, float z)[] spawns)
        {
            var s = new SimWorldState();
            foreach (var (x, z) in spawns)
                s.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(x, 0f, z), Yaw = 0f }, out int _);
            return s;
        }

        [Fact]
        public void 全量载波_帧号与ack与校验字段齐备()
        {
            var s = World((0f, 0f), (5f, 5f));
            var msg = new FullSnapshotSource().Build(42, s, 7);

            Assert.Equal(42, msg.Frame);
            Assert.True(msg.IsFull);
            Assert.Equal(7, msg.AckInput);
            Assert.Equal(SimChecksum.ComputeChecksum(s), msg.Checksum);   // checksum 由载波填（客户端和解判定用）
        }

        [Fact]
        public void 全量载波_只含活体槽位_且可解回同值()
        {
            var s = World((0f, 0f), (10f, -3.5f));
            long deadId = s.Spawn(new EntitySlot { Hp = 1, Pos = new SimVector3(20f, 0f, 0f) }, out int deadSlot);
            s.Despawn(deadId);                                            // 死体不进快照

            var msg = new FullSnapshotSource().Build(1, s, 0);

            Assert.Equal(s.AliveCount(), msg.Slots.Count);
            foreach (var d in msg.Slots)
            {
                Assert.True(s.IsAlive(d.Slot));
                SnapshotCodec.FromDelta(d, out int slot, out EntitySlot back);
                Assert.Equal(d.Slot, slot);
                Assert.Equal(s.Entities[slot].Id, back.Id);
                Assert.Equal(s.Entities[slot].Pos.X, back.Pos.X);
                Assert.Equal(s.Entities[slot].Pos.Z, back.Pos.Z);
                Assert.Equal(s.Entities[slot].Hp, back.Hp);
            }
        }

        [Fact]
        public void 全量载波_无状态_可多房间共享同一实例()
        {
            var shared = new FullSnapshotSource();
            var a = World((0f, 0f));
            var b = World((1f, 1f), (2f, 2f));

            Assert.Single(shared.Build(1, a, 0).Slots);
            Assert.Equal(2, shared.Build(1, b, 0).Slots.Count);
            Assert.Single(shared.Build(2, a, 1).Slots);                   // 复用同实例互不干扰
        }
    }
}
