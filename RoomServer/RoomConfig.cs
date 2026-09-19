namespace RoomServer
{
    /// <summary>
    /// 房间装配参数（M10 架构审查·建议 2/3 收口，2026-09-19）：
    /// 把"房间号 / 端口 / 期望人数 / seed 策略"集中到一处——房间容量不再写死，
    /// 4 人房（M11 demo §9 验收）与验收脚本并行跑不同端口都只改配置，不散改代码。
    ///
    /// 默认值 = M10 MVP 形态（Room-A / 17777 / 2 人 / seed 随机）。
    /// </summary>
    public sealed class RoomConfig
    {
        /// <summary>房间号（无 MatchMaker：预置单房间注册）。</summary>
        public string RoomId = "Room-A";

        /// <summary>期望人数——决定席位表 / 输入槽 / 实体 Id 表的定容；满员自动 StartGame。</summary>
        public int ExpectedPlayers = 2;

        /// <summary>传输端口。</summary>
        public int Port = 17777;

        /// <summary>世界 seed 策略：**0 = StartGame 时取服务器时钟**（下发客户端，§4.5）；非 0 = 固定 seed（确定性回放 / 验收脚本）。</summary>
        public long Seed = 0;

        public static RoomConfig Default() => new RoomConfig();
    }
}
