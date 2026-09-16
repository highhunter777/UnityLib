namespace LiteSim
{
    /// <summary>
    /// 移动/重力系统（§3.3 顺序第 2 位，§3.5 2.5D）：
    /// XZ 平面位移 + y 轴重力积分 + 地面钳制 + 世界边界钳制。
    /// M8 不做静态障碍碰撞（#12：碰撞分桶留后续里程碑空位）。
    /// </summary>
    public static class MovementSystem
    {
        public static void Run(EntitySlot[] entities, uint[] aliveBitmap, in SimMapData map)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((aliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;

                ref EntitySlot e = ref entities[i];

                // XZ 平面位移（速度由 InputSystem 写入）
                e.Pos.X += e.Vel.X * SimConfig.Dt;
                e.Pos.Z += e.Vel.Z * SimConfig.Dt;

                // y 轴：重力积分 + 地面钳制（§3.5 原式）
                e.Vel.Y += SimConfig.Gravity * SimConfig.Dt;
                e.Pos.Y += e.Vel.Y * SimConfig.Dt;
                if (e.Pos.Y <= map.GroundY)
                {
                    e.Pos.Y = map.GroundY;
                    e.Vel.Y = 0f;
                }

                // 世界边界钳制（地图尺寸是判定半的一部分，#17）
                if (e.Pos.X < -map.HalfWidth) e.Pos.X = -map.HalfWidth;
                if (e.Pos.X > map.HalfWidth) e.Pos.X = map.HalfWidth;
                if (e.Pos.Z < -map.HalfDepth) e.Pos.Z = -map.HalfDepth;
                if (e.Pos.Z > map.HalfDepth) e.Pos.Z = map.HalfDepth;
            }
        }
    }
}
