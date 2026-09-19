using System;
using LiteNet.Protocol;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>快照编解码用例（《M10 实施指导》§3：SlotDelta 位级一致 / 全量快照对账）。</summary>
    public class SnapshotCodecTests
    {
        private static SimWorldState BuildWorld()
        {
            var s = new SimWorldState { RngState = 0x5EEDBEEF12345678UL };
            s.Spawn(new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0.5f }, out _);
            s.Spawn(new EntitySlot { Hp = 75, Pos = new SimVector3(10f, 0f, -3.25f), Vel = new SimVector3(-1.5f, 0f, 2.25f), Yaw = 3.1f, Flags = 2u }, out _);
            return s;
        }

        [Fact]
        public void SlotDelta_活体槽位往返位级一致()
        {
            var s = BuildWorld();

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i)) continue;
                ref EntitySlot e = ref s.Entities[i];

                Proto.SlotDelta wire = SnapshotCodec.ToDelta(i, e);
                SnapshotCodec.FromDelta(wire, out int slot, out EntitySlot back);

                Assert.Equal(i, slot);
                Assert.Equal(e.Id, back.Id);
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Pos.X), BitConverter.SingleToInt32Bits(back.Pos.X));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Pos.Y), BitConverter.SingleToInt32Bits(back.Pos.Y));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Pos.Z), BitConverter.SingleToInt32Bits(back.Pos.Z));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Vel.X), BitConverter.SingleToInt32Bits(back.Vel.X));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Vel.Y), BitConverter.SingleToInt32Bits(back.Vel.Y));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Vel.Z), BitConverter.SingleToInt32Bits(back.Vel.Z));
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Yaw), BitConverter.SingleToInt32Bits(back.Yaw));
                Assert.Equal(e.Hp, back.Hp);
                Assert.Equal(e.Flags, back.Flags);
            }
        }

        [Fact]
        public void 全量快照_活体数与checksum对账()
        {
            var s = BuildWorld();
            s.Spawn(new EntitySlot { Hp = 1, Pos = new SimVector3(20f, 0f, 10f) }, out _); // 第 3 个活体

            Proto.StateSnapshot msg = SnapshotCodec.PackFull(frame: 42, s: s, ackInput: 40);

            Assert.True(msg.IsFull);
            Assert.Equal(42, msg.Frame);
            Assert.Equal(40, msg.AckInput);
            Assert.Equal(s.AliveCount(), msg.Slots.Count);
            Assert.Equal(SimChecksum.ComputeChecksum(s), msg.Checksum); // 和解判定的位级锚点
        }
    }
}
