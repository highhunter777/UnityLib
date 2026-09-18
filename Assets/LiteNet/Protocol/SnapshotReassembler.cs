using System;
using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// 客户端快照镜像（《M10实施指导》决策 7 "客户端侧" + 《状态同步实施方案》§5.5）：
    /// 维护"最近收到的快照"并**重建完整状态**供和解使用（<c>RollbackSim.OnAuthoritativeSnapshot</c> 的输入形态）。
    ///
    /// 重建语义：全量 = 缺席槽位一律视为死亡（权威事实，清位图）；增量 = 只覆盖 SlotDelta 指到的槽位。
    /// 分配器版本由 <c>Id >> 16</c> 重建、<c>_nextFree</c> 取"已用槽数"——两者都**不是逻辑字段**（不进 checksum），
    /// 只保证重建态继续 Spawn 时不与既有 Id 撞车。帧头取快照帧号（**不是**本地帧号——
    /// 和解要的是"权威帧号 + 该帧权威状态"这一对）。
    ///
    /// 副作用即设计：客户端镜像只用于和解（覆盖本地预测态）。本地 Sim 由 RollbackSim 自持。
    /// </summary>
    public static class SnapshotReassembler
    {
        /// <summary>把快照套用到 <paramref name="dst"/>（原地重建）。<paramref name="checksumOut"/> = 快照携带的权威校验值。</summary>
        public static void Apply(Proto.StateSnapshot msg, SimWorldState dst, out uint checksumOut)
        {
            checksumOut = msg.Checksum;

            if (msg.IsFull)
            {
                Array.Clear(dst.AliveBitmap, 0, dst.AliveBitmap.Length);
                for (int i = 0; i < SimConfig.MaxEntities; i++) dst.Entities[i] = default;
                Array.Clear(dst.Globals, 0, dst.Globals.Length);
                Array.Clear(dst.CustomData, 0, dst.CustomData.Length);
            }

            int highest = -1;
            for (int i = 0; i < msg.Slots.Count; i++)
            {
                Proto.SlotDelta d = msg.Slots[i];
                if (d.Slot < 0 || d.Slot >= SimConfig.MaxEntities) continue;   // 越界槽位丢弃（坏包零容忍）
                SnapshotCodec.FromDelta(d, out int slot, out EntitySlot e);
                dst.Entities[slot] = e;
                dst.AliveBitmap[slot >> 5] |= 1u << (slot & 31);
                if (slot > highest) highest = slot;
            }

            dst.Frame = msg.Frame;
            dst.SetAllocatorIdleSlot(highest + 1);
            // RngState：快照不携带（服务器随机数只用于伤害浮动，客户端预测开火不预测）。保持原值——
            // 和解重放用的是本地历史输入，随机数消费点与服务器不同源也无妨（M10 不预测开火，见决策 9）。
        }
    }
}
