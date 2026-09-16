using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 快照编解码（SlotDelta ↔ EntitySlot 映射 + 全量快照打包）。
    /// float 字段位级精确（协议单源约定）——量化整型是带宽调优项，引入即破坏 M9 位级和解机制，需专项评估后另行落。
    /// 全量快照 = 全部活体槽位：客户端可由 SlotDelta.Id 重建分配器 version（Id >> 16），槽位缺席 = 死亡/未生成。
    /// </summary>
    public static class SnapshotCodec
    {
        public static Proto.SlotDelta ToDelta(int slot, in EntitySlot e)
        {
            return new Proto.SlotDelta
            {
                Slot = slot,
                Id = e.Id,
                PosX = e.Pos.X, PosY = e.Pos.Y, PosZ = e.Pos.Z,
                VelX = e.Vel.X, VelY = e.Vel.Y, VelZ = e.Vel.Z,
                Yaw = e.Yaw,
                Hp = e.Hp,
                Flags = e.Flags,
            };
        }

        public static void FromDelta(Proto.SlotDelta d, out int slot, out EntitySlot e)
        {
            slot = d.Slot;
            e = new EntitySlot
            {
                Id = d.Id,
                Pos = new SimVector3(d.PosX, d.PosY, d.PosZ),
                Vel = new SimVector3(d.VelX, d.VelY, d.VelZ),
                Yaw = d.Yaw,
                Hp = d.Hp,
                Flags = d.Flags,
            };
        }

        /// <summary>打包全量快照（全部活体槽位 + 权威 checksum + ackInput）。增量差分（SnapshotDiffer）批③在此之上组装。</summary>
        public static Proto.StateSnapshot PackFull(int frame, SimWorldState s, int ackInput)
        {
            var msg = new Proto.StateSnapshot { Frame = frame, IsFull = true, Checksum = SimChecksum.ComputeChecksum(s), AckInput = ackInput };
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i)) continue;
                msg.Slots.Add(ToDelta(i, s.Entities[i]));
            }
            return msg;
        }
    }
}
