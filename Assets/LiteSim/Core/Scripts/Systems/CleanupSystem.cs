namespace LiteSim
{
    /// <summary>
    /// 清理系统（§3.3 顺序末位）：回收死亡实体槽位——清 AliveBitmap 位 + 槽位清零。
    /// 槽位清零保证空槽校验值恒定（§3.6 全槽位参与校验的前提）；
    /// 版本不在此递增（#8：版本只在 Spawn 时递增）。
    /// </summary>
    public static class CleanupSystem
    {
        public static void Run(EntitySlot[] entities, uint[] aliveBitmap)
        {
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if ((aliveBitmap[i >> 5] & (1u << (i & 31))) == 0u) continue;
                if (entities[i].Hp > 0) continue;

                aliveBitmap[i >> 5] &= ~(1u << (i & 31));
                entities[i] = default;
            }
        }
    }
}
