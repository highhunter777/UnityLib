namespace LiteSim
{
    /// <summary>
    /// 输入系统（《状态同步实施方案》§3.3 顺序第 1 位 + M8 决策 #11/#12）：
    /// 应用移动向量与朝向到玩家实体；开火位由 ShootingSystem 直接读输入（签名按 #10 窄化，本系统不碰随机数）。
    /// 输入数组已由 SimStep 按 EntityId 升序排列（§3.3 同帧多请求的确定性来源）。
    /// </summary>
    public static class InputSystem
    {
        public static void Run(SimWorldState s, SimInputFrame[] inputs)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                // 目标已死 = 引用失效 = 正常路径（#7），本帧输入丢弃
                if (!s.TryResolve(inputs[i].EntityId, out int slotIndex)) continue;

                ref EntitySlot e = ref s.Entities[slotIndex];
                e.Vel.X = inputs[i].MoveX * SimConfig.MoveSpeed;
                e.Vel.Z = inputs[i].MoveZ * SimConfig.MoveSpeed;
                e.Yaw = inputs[i].Yaw;
            }
        }
    }
}
