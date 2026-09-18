namespace LiteSim
{
    /// <summary>
    /// 快照轻量摘要（《M10实施指导》决策 7 + §11-3 常量集中）：进网/进对比的**必须是摘要，不是全量 state**——
    /// 服务器不能为"上一广播帧副本"再养一份 SimWorldState（快照环已占 16 份）。
    ///
    /// 字段取舍（与 <see cref="SimChecksum"/> 覆盖项逐项对齐，防"摘要漏字段 → 差分漏发 → 客户端静默分叉"）：
    /// - **含**：全部逻辑字段（Id/Pos/Vel/Yaw/Hp/Flags）+ Frame + RngState + 活体位图。
    /// - **不含**：Globals/CustomData（M8 未消费，全零；消费方落地时**必须**同步扩展本摘要——
    ///   <c>Protocol.SnapshotDiffer</c> 的"全量走快照环"路径自动跟上，因为该路径对比的就是 <see cref="SimWorldState"/> 本身）。
    /// - **不含**：分配器 _versions/_nextFree（非逻辑字段，不进 checksum）。
    ///
    /// float 字段按**位型**比较（<c>SingleToInt32Bits</c>）——+0/-0 位型不同即算变化：
    /// 差分漏发一位就分叉（M9 和解机制的位级前提），宁可多发不比错。
    /// </summary>
    public struct EntitySnapshotEntry
    {
        public long Id;
        public SimVector3 Pos;
        public SimVector3 Vel;
        public float Yaw;
        public int Hp;
        public uint Flags;

        /// <summary>与另一槽位逐字段位级相等（RngState/Frame 不在此——它们在 <see cref="SimWorldStateSnapshot"/> 头部）。</summary>
        public static bool BitEqual(in EntitySnapshotEntry a, in EntitySnapshotEntry b)
        {
            return a.Id == b.Id // lint-allow R3（64 位整型 Id 判等，非浮点精度比较）
                && BitUtil.Equal(a.Pos.X, b.Pos.X) && BitUtil.Equal(a.Pos.Y, b.Pos.Y) && BitUtil.Equal(a.Pos.Z, b.Pos.Z)
                && BitUtil.Equal(a.Vel.X, b.Vel.X) && BitUtil.Equal(a.Vel.Y, b.Vel.Y) && BitUtil.Equal(a.Vel.Z, b.Vel.Z)
                && BitUtil.Equal(a.Yaw, b.Yaw)
                && a.Hp == b.Hp && a.Flags == b.Flags; // lint-allow R3（整型血量/标志位判等，非浮点精度比较）
        }
    }

    /// <summary>
    /// 帧级快照摘要：帧号 + RngState + 活体位图 + 定长槽位表。
    /// <see cref="Capture"/> 快路径（只计数活体）；<see cref="CaptureFull"/> 慢路径（逐槽位拷贝）——
    /// 只在上次广播帧的活体数发生变化时才需要（见 Protocol.SnapshotDiffer 的重建规则）。
    /// </summary>
    public sealed class SimWorldStateSnapshot
    {
        public int Frame;
        public ulong RngState;
        public readonly uint[] AliveBitmap = new uint[(SimConfig.MaxEntities + 31) / 32];
        public readonly EntitySnapshotEntry[] Entities = new EntitySnapshotEntry[SimConfig.MaxEntities];
        public int AliveCount;

        /// <summary>只拷贝"结构"（帧号/随机数/活体位图/活体数），槽位逐字段拷贝走 <see cref="CaptureFull"/>。</summary>
        public void Capture(in SimWorldState s)
        {
            Frame = s.Frame;
            RngState = s.RngState;
            System.Array.Copy(s.AliveBitmap, AliveBitmap, AliveBitmap.Length);
            AliveCount = s.AliveCount();
        }

        /// <summary>整快照（结构 + 全部活体槽位）——初始金标与"活体数变化"时的重建用。</summary>
        public void CaptureFull(in SimWorldState s)
        {
            Capture(s);
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i)) continue;
                Entities[i] = Capture(s.Entities[i]);
            }
        }

        /// <summary>槽位是否与摘要逐字段位级一致（活体集合不同即视为不一致——上层按"活体数变化"整体重建）。</summary>
        public bool Matches(int slot, in EntitySlot e)
        {
            return EntitySnapshotEntry.BitEqual(Entities[slot], Capture(e));
        }

        /// <summary>槽位 → 摘要项（CaptureFull 与 Matches 共用同一份字段搬运，防两处字段清单漂移）。</summary>
        public static EntitySnapshotEntry Capture(in EntitySlot e)
        {
            EntitySnapshotEntry entry;
            entry.Id = e.Id;
            entry.Pos = e.Pos;
            entry.Vel = e.Vel;
            entry.Yaw = e.Yaw;
            entry.Hp = e.Hp;
            entry.Flags = e.Flags;
            return entry;
        }
    }

    /// <summary>float 位型工具（位级比较的唯一入口，防各处写法漂移；±0 位型不同即视为不同值）。</summary>
    public static class BitUtil
    {
        public static bool Equal(float a, float b) =>
            System.BitConverter.SingleToInt32Bits(a) == System.BitConverter.SingleToInt32Bits(b);
    }
}
