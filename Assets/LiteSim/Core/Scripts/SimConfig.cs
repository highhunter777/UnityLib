namespace LiteSim
{
    /// <summary>
    /// Sim 常量集中地（M8 决策 #6，《状态同步实施方案》§11-3 对账）：一处定义，禁止散落魔数。
    /// M9 消费 MaxRollbackFrames；M10 消费 InterpFrames / LagCompHistory（§3.4.1）。
    /// </summary>
    public static class SimConfig
    {
        /// <summary>逻辑帧率（§3.2：射击要求 60Hz，不接受可变 dt）。</summary>
        public const int TickRate = 60;

        /// <summary>逻辑帧步长（秒）——与 M7 同源（SimMath.Dt），禁止第二处定义。</summary>
        public const float Dt = SimMath.Dt;

        /// <summary>实体槽位容量（§3.1）。</summary>
        public const int MaxEntities = 256;

        /// <summary>输入延迟帧数（§11-3）。</summary>
        public const int InputDelay = 1;

        /// <summary>单次 Tick 最多追帧数（§3.2 FrameDriver）。</summary>
        public const int MaxCatchUp = 5;

        /// <summary>回滚深度（M9 SnapshotRing 容量对齐）。</summary>
        public const int MaxRollbackFrames = 8;

        /// <summary>插值帧数（M10，§3.4.1：开火者视角帧推导）。</summary>
        public const int InterpFrames = 4;

        /// <summary>命中延迟补偿回溯窗口（M10，§3.4.1；0 = 关闭，退化为提前量判定）。</summary>
        public const int LagCompHistory = 16;

        /// <summary>输入历史容量（§5.5：最近 32 帧全体输入——回滚重放 + M11 重连补发余量，M9 决策⑤）。</summary>
        public const int MaxInputHistory = 32;

        /// <summary>单渲染帧回滚次数上限（§5.4 防雪崩，M9 决策⑧）。</summary>
        public const int MaxRollbacksPerFrame = 2;

        /// <summary>每实体自定义状态字节数（#2：平面数组，slot*32+offset 寻址）。</summary>
        public const int CustomBytesPerEntity = 32;

        /// <summary>全局逻辑状态（比分/波次等）字节数。</summary>
        public const int GlobalsBytes = 256;

        // ---- M8 系统链数值（#6 常量集中：禁止散落魔数；§2.4 最小版玩法参数）----

        /// <summary>玩家移动速度（m/s，2.5D XZ 平面）。</summary>
        public const float MoveSpeed = 5f;

        /// <summary>重力加速度（m/s²，y 轴向下，§3.5）。</summary>
        public const float Gravity = -20f;

        /// <summary>hitscan 射程（m）。</summary>
        public const float HitscanRange = 100f;

        /// <summary>命中圆柱半径（m）。</summary>
        public const float HitscanRadius = 0.5f;

        /// <summary>命中圆柱高度（m，区间 [Pos.Y, Pos.Y + Height]）。</summary>
        public const float HitscanHeight = 2f;

        /// <summary>基础伤害（ShootingSystem 经 RngState 浮动 ±1）。</summary>
        public const int BaseDamage = 25;

        // ---- M10 广播 / AOI 旋钮（《M10实施指导》决策 7/13；**初始值**，M10 实施时按实测校准）----

        /// <summary>快照广播频率（Hz）：权威循环每 `TickRate / SnapshotHz` 个逻辑帧广播一次（30 → 每 2 帧）。</summary>
        public const int SnapshotHz = 30;

        /// <summary>AOI 网格边长（米）：**只影响广播裁剪，不影响判定与回滚重放**（决策 13 / §4.5-7）。</summary>
        public const float AoiCellSize = 10f;

        /// <summary>AOI 广播半径（米）：同上；<c>0</c> = 关闭 AOI（全图广播，等价性验收用）。</summary>
        public const float AoiRadius = 30f;
    }
}
