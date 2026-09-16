namespace LiteSim
{
    /// <summary>
    /// 单玩家一帧的输入面（M8 决策 #17：接口先定、消费者后写，M8 冻结）。
    /// 采集侧（M8 沙盒直接喂 / M11 PlayerController）组装；Sim 侧只消费。
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

        /// <summary>朝向（XZ 平面，弧度）。</summary>
        public float Yaw;

        /// <summary>按键位集（bit0 = 开火）。</summary>
        public uint Buttons;
    }
}
