namespace LiteSim
{
    /// <summary>
    /// 实体槽位（《状态同步实施方案》§3.1 + M8 决策 #3）：纯值类型（blittable）——
    /// EntitySlot[] 才能被 Array.Copy 整块深拷（#1/#4）。
    /// 自定义状态不放本 struct（#2：struct 内放数组字段会被浅拷共享，快照必错），
    /// 一律落 SimWorldState.CustomData 平面数组（slot * CustomBytesPerEntity + offset 寻址）。
    /// </summary>
    public struct EntitySlot
    {
        /// <summary>
        /// 稳定 Id：低 16 位 slotIndex | 高 48 位 version（#7 防重绕——16 位 version 在弹幕密集时
        /// 约 20 分钟耗尽）。由 SimWorldState 分配器组装，业务不可手改；0 = 无效（空槽/分配失败）。
        /// </summary>
        public long Id;

        /// <summary>位置（y 轴 = 2.5D 向上，§3.5）。</summary>
        public SimVector3 Pos;

        /// <summary>速度。</summary>
        public SimVector3 Vel;

        /// <summary>朝向（XZ 平面，弧度；射击必需）。</summary>
        public float Yaw;

        public int Hp;

        /// <summary>标志位（存活/无敌/开火中…按需定义；活体判定以 AliveBitmap 为准）。</summary>
        public uint Flags;
    }
}
