using System;

namespace LiteSim
{
    /// <summary>
    /// 世界状态容器（《状态同步实施方案》§3.1 固定布局 + M8 决策 #4/#5）：
    /// sealed class 持定长数组——构造期一次分配、运行期零 new；EntitySlot[] 是值类型数组，
    /// 逐数组 Array.Copy 即深拷（#1：纯托管，不引 unsafe/UnsafeUtility——Core 零引擎依赖的必然推论）。
    ///
    /// 布局硬约束（#5，反射自检钉死）：进快照的字段只允许值类型/数组——
    /// Custom/Globals 落平面数组（#2/#3：struct 内放数组字段会被 Array.Copy 浅拷共享 → 回滚必错）。
    /// Cmds/Events 为帧内瞬态（§3.7/决策⑥）：不进快照、不进 checksum，每帧末消费清空。
    /// </summary>
    public sealed class SimWorldState
    {
        public int Frame;
        public ulong RngState;

        public readonly EntitySlot[] Entities;
        public readonly uint[] AliveBitmap;
        public readonly byte[] Globals;
        public readonly byte[] CustomData;

        // 非 readonly：可变结构体字段，readonly 会触发防御性拷贝丢写（见 CommandBuffer 注释）。
        public CommandBuffer Cmds;
        public FrameEventBuffer Events;

        // 分配器状态：随快照走（保 M9 重放一致），不进 checksum（非逻辑字段，§3.6）。
        private readonly ulong[] _versions;
        private int _nextFree;

        public SimWorldState()
        {
            Entities = new EntitySlot[SimConfig.MaxEntities];
            AliveBitmap = new uint[(SimConfig.MaxEntities + 31) / 32];
            Globals = new byte[SimConfig.GlobalsBytes];
            CustomData = new byte[SimConfig.MaxEntities * SimConfig.CustomBytesPerEntity];
            _versions = new ulong[SimConfig.MaxEntities];
            Cmds = new CommandBuffer { Items = new SimCommand[CommandBuffer.Capacity] };
            Events = new FrameEventBuffer { Items = new FrameEvent[FrameEventBuffer.Capacity] };
        }

        /// <summary>槽位是否活体（以 AliveBitmap 为准，§3.1）。</summary>
        public bool IsAlive(int slotIndex)
        {
            return (AliveBitmap[slotIndex >> 5] & (1u << (slotIndex & 31))) != 0u;
        }

        /// <summary>活体数（诊断/沙盒 HUD 用；O(MaxEntities/32) 位计数）。</summary>
        public int AliveCount()
        {
            int count = 0;
            for (int i = 0; i < AliveBitmap.Length; i++)
            {
                uint bits = AliveBitmap[i];
                while (bits != 0u) // 与常量 0 比较，非浮点精度比较
                {
                    bits &= bits - 1u; // 逐最低位清除（Brian Kernighan）
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 分配实体（#7/#8）：从 _nextFree 起环形线性找空槽，版本递增后组装 Id；
        /// 入参 slot.Id 被忽略——Id 只能由分配器组装。不做 swap-remove（打乱遍历顺序 = 破确定性）。
        /// 世界满：返回 0 且 slotIndex = -1。
        /// </summary>
        public long Spawn(in EntitySlot slot, out int slotIndex)
        {
            for (int k = 0; k < SimConfig.MaxEntities; k++)
            {
                int i = _nextFree + k;
                if (i >= SimConfig.MaxEntities) i -= SimConfig.MaxEntities;
                if (IsAlive(i)) continue;

                ulong version = (_versions[i] + 1UL) & 0xFFFFFFFFFFFFUL; // 48 位；回绕需 2^48 次复用，现实不可达
                _versions[i] = version;

                EntitySlot s = slot;
                s.Id = (long)(version << 16) | (long)(uint)i;
                Entities[i] = s;
                AliveBitmap[i >> 5] |= 1u << (i & 31);

                _nextFree = i + 1;
                if (_nextFree >= SimConfig.MaxEntities) _nextFree = 0;
                slotIndex = i;
                return s.Id;
            }

            slotIndex = -1;
            return 0L;
        }

        /// <summary>
        /// 释放槽位（§3.1：清 AliveBitmap 位）。Double-free/已死 Id 静默忽略；
        /// 槽位数据清零——空槽校验值恒定，不随历史漂移（§3.6 全槽位参与校验的前提）。
        /// </summary>
        public void Despawn(long id)
        {
            if (!TryResolve(id, out int slotIndex)) return;
            AliveBitmap[slotIndex >> 5] &= ~(1u << (slotIndex & 31));
            Entities[slotIndex] = default;
        }

        /// <summary>
        /// 跨帧引用唯一入口（#7）：version 校验；失效（已死/槽位复用）返回 false 而非抛——
        /// 引用失效是正常路径（目标已死）。
        /// </summary>
        public bool TryResolve(long id, out int slotIndex)
        {
            if (id == 0L)
            {
                slotIndex = -1;
                return false;
            }

            slotIndex = (int)(id & 0xFFFFL);
            if (slotIndex >= SimConfig.MaxEntities)
            {
                slotIndex = -1;
                return false;
            }

            if (!IsAlive(slotIndex))
            {
                slotIndex = -1;
                return false;
            }

            ulong version = (ulong)id >> 16;
            if (_versions[slotIndex] != version)
            {
                slotIndex = -1;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 深拷快照原语（#1，M9 SnapshotRing 复用）：逐数组 Array.Copy。
        /// 不拷 Cmds/Events（帧内瞬态，§3.7/决策⑥）；
        /// _versions/_nextFree 分配器状态随快照走——否则重放期新分配的 Id 会与被恢复的旧 Id 撞车。
        /// </summary>
        public void CopyTo(SimWorldState dst)
        {
            dst.Frame = Frame;
            dst.RngState = RngState;
            Array.Copy(Entities, dst.Entities, Entities.Length);
            Array.Copy(AliveBitmap, dst.AliveBitmap, AliveBitmap.Length);
            Array.Copy(Globals, dst.Globals, Globals.Length);
            Array.Copy(CustomData, dst.CustomData, CustomData.Length);
            Array.Copy(_versions, dst._versions, _versions.Length);
            dst._nextFree = _nextFree;
        }

        /// <summary>活体槽位零分配遍历（#9）：升序跳空槽，顺序恒定。</summary>
        public EntityIt IterateAlive()
        {
            return new EntityIt(this);
        }
    }
}
