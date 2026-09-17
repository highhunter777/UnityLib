namespace LiteSim
{
    /// <summary>
    /// 输入系统（《状态同步实施方案》§3.3 顺序第 1 位 + M8 决策 #11/#12）：
    /// 应用移动向量与**瞄准方向**到玩家实体——朝向（`Yaw`）在此由 `Aim` 经 `SimTrig` 查表**派生**
    /// （2026-09-17 改造：输入只带方向，朝像是派生量）；开火位由 ShootingSystem 直接读输入（签名按 #10 窄化）。
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
                // Yaw = atan2(dz, dx)：派生自瞄准方向（查表，确定性；零向量按 SimTrig 定义值处理——
                // 采集侧契约要求非零，违约不会崩，只是朝向退化）
                e.Yaw = SimTrig.Atan2(inputs[i].AimZ, inputs[i].AimX);
            }
        }
    }
}
