namespace LiteSim
{
    /// <summary>
    /// 单玩家一帧的输入面（M8 决策 #17；**2026-09-17 瞄准表示改造：`Yaw` → `AimX/AimZ`**，见
    /// 《角色状态与动作实现设计》§7-1）。
    /// 采集侧（M8 沙盒直接喂 / M11 PlayerController）组装；Sim 侧只消费。
    /// **瞄准存方向不存角度**：射击射线本就需要方向（省一次三角函数往返）、无角度环绕与量化边界问题；
    /// 朝向 `EntitySlot.Yaw` 降为 **Sim 内派生量**（InputSystem 经 `SimTrig.Atan2` 算，表现层仍需要朝向）。
    /// Step 收到的输入数组按 playerId 升序排列，数组顺序即处理顺序（§3.3 同帧多请求的确定性来源）。
    /// </summary>
    public struct SimInputFrame
    {
        /// <summary>开火位（M8）；后续键位（跳跃/技能）以追加位扩展，不破坏既有布局。</summary>
        public const uint ButtonFire = 1u << 0;

        /// <summary>玩家实体 Id（消费方经 TryResolve 定位；失效 = 目标已死，本帧输入丢弃）。</summary>
        public long EntityId;

        /// <summary>移动向量 X（长度 ≤ 1 由采集侧保证）。</summary>
        public float MoveX;

        /// <summary>移动向量 Z（2.5D：XZ 平面移动）。</summary>
        public float MoveZ;

        /// <summary>瞄准方向 X（XZ 平面）——**开火帧必须为非零向量**，长度 ≤ 1 由采集侧保证。
        /// 与移动正交（朝向与相机解耦，《联机Demo设计》§13）；Sim 只消费、不解释来源。</summary>
        public float AimX;

        /// <summary>瞄准方向 Z（长度 ≤ 1 由采集侧保证）。</summary>
        public float AimZ;

        /// <summary>按键位集（bit0 = 开火）。</summary>
        public uint Buttons;
    }
}
