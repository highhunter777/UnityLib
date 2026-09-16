using System;

namespace LiteSim
{
    /// <summary>
    /// 全量状态校验（《状态同步实施方案》§3.6 + M8 决策 #15）：FNV-1a 32 位，遍历顺序恒定。
    /// 覆盖 Frame / RngState / 全部槽位逻辑字段 / Globals / CustomData——与 SimWorldState 同源，
    /// 结构上杜绝漏字段（§3.1 固定布局的红利）。
    /// 不覆盖：Cmds/Events（帧内瞬态，§3.7）；_versions/_nextFree（分配器状态，非逻辑字段）。
    /// 开发期每帧算；release 每 10 帧（M8 由驱动/沙盒决定调用频率）。
    /// v3 定位：本地调试与复查重放的一致性裁判（不上服务器对账）。
    /// </summary>
    public static class SimChecksum
    {
        private const uint FnvOffset = 2166136261u;
        private const uint FnvPrime = 16777619u;

        public static uint ComputeChecksum(in SimWorldState s)
        {
            uint h = FnvOffset;
            h = MixUInt32(h, (uint)s.Frame);
            h = MixUInt64(h, s.RngState);

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                ref EntitySlot e = ref s.Entities[i];
                h = MixInt64(h, e.Id);
                h = MixFloat(h, e.Pos.X);
                h = MixFloat(h, e.Pos.Y);
                h = MixFloat(h, e.Pos.Z);
                h = MixFloat(h, e.Vel.X);
                h = MixFloat(h, e.Vel.Y);
                h = MixFloat(h, e.Vel.Z);
                h = MixFloat(h, e.Yaw);
                h = MixInt32(h, e.Hp);
                h = MixUInt32(h, e.Flags);
            }

            byte[] globals = s.Globals;
            for (int i = 0; i < globals.Length; i++) h = MixByte(h, globals[i]);

            byte[] custom = s.CustomData;
            for (int i = 0; i < custom.Length; i++) h = MixByte(h, custom[i]);

            return h;
        }

        // ---- FNV-1a 逐字节混合（浮点经位型逐位确定，非容差） ----

        private static uint MixByte(uint h, byte b)
        {
            return (h ^ b) * FnvPrime;
        }

        private static uint MixUInt32(uint h, uint v)
        {
            h = MixByte(h, (byte)(v & 0xFFu));
            h = MixByte(h, (byte)((v >> 8) & 0xFFu));
            h = MixByte(h, (byte)((v >> 16) & 0xFFu));
            h = MixByte(h, (byte)((v >> 24) & 0xFFu));
            return h;
        }

        private static uint MixInt32(uint h, int v)
        {
            return MixUInt32(h, (uint)v);
        }

        private static uint MixInt64(uint h, long v)
        {
            return MixUInt64(h, (ulong)v);
        }

        private static uint MixUInt64(uint h, ulong v)
        {
            h = MixUInt32(h, (uint)(v & 0xFFFFFFFFu));
            h = MixUInt32(h, (uint)(v >> 32));
            return h;
        }

        private static uint MixFloat(uint h, float v)
        {
            // 位型哈希：跨运行时逐位一致（与 M7 基线同款手段）
            return MixUInt32(h, (uint)BitConverter.SingleToInt32Bits(v));
        }
    }
}
