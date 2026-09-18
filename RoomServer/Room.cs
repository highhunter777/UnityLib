using System;
using System.Collections.Generic;
using System.Diagnostics;
using LiteSim;
using LiteNet.Proto;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 房间（《状态同步实施方案》§4.5 Room 结构）：权威 Sim + 输入聚合 + 60Hz 定拍循环 + Ops。
    ///
    /// - **AuthSim 唯一真相**：SimWorldState 引用 LiteSim.Core 原生 Step——掉线者输入沿用空输入（§4.5-2 掉线不停帧）；
    /// - **服务端快照环**：每帧 Capture（容量 LagCompHistory=16，M9 类复用）——批③ SnapshotDiffer/回溯判定的基料；
    ///   本批不广播（快照增量与 AOI 属批③）；
    /// - **定拍**：绝对时间锚定（nextTick = start + n×dt——防 PeriodicTimer/相对 Sleep 的漂移累积，指导风险 5）；
    /// - **成员表**：playerId ↔ entityId ↔ sessionId；输入 EntityId 一律按会话覆写（防伪）；
    /// - **Ops**：帧号/输入收发/丢弃——属性可断言，周期打印由宿主负责。
    /// MVP 房间形态：固定 2 人期望（批②验收形态），齐员自动 StartGame——无 MatchMaker（《M10 实施指导》决策⑩）。
    /// </summary>
    public sealed class Room
    {
        public const int ExpectedPlayers = 2;

        public readonly string RoomId;
        public readonly SimWorldState AuthSim;         // 权威唯一真相
        public readonly SimMapData Map;
        public readonly SnapshotRing SnapshotHistory; // 服务端回溯环（M9 类，容量 LagCompHistory）
        public readonly InputGate Gate;

        /// <summary>成员：playerId → 会话。</summary>
        private readonly Dictionary<int, Session> _members = new Dictionary<int, Session>();
        /// <summary>playerId → 玩家实体 Id（StartGame 分配）。</summary>
        private readonly long[] _entityIds;
        /// <summary>本帧聚合输入槽（Step 前重灌）。</summary>
        private readonly SimInputFrame[] _frameInputs;

        public int NextPlayerId;
        public bool Started;
        public long Seed;

        // ---- Ops 计数 ----
        public long StepsCount;

        public Room(string roomId)
        {
            RoomId = roomId;
            Map = BuildStandardMap();
            AuthSim = new SimWorldState();
            SnapshotHistory = new SnapshotRing(SimConfig.LagCompHistory);
            Gate = new InputGate(ExpectedPlayers);
            _entityIds = new long[ExpectedPlayers];
            _frameInputs = new SimInputFrame[ExpectedPlayers];
        }

        /// <summary>成员就位分配玩家号（StartGame 前调用）；满员返回 -1。</summary>
        public int AssignPlayerId(Session session)
        {
            int playerId = NextPlayerId++;
            if (playerId >= ExpectedPlayers) return -1;
            _members[playerId] = session;
            session.PlayerId = playerId;
            session.Room = this;
            return playerId;
        }

        /// <summary>成员号列表（JoinAck.Members；playerId 升序）。</summary>
        public int[] MemberIds()
        {
            var ids = new List<int>(_members.Keys);
            ids.Sort();
            return ids.ToArray();
        }

        public bool TryGetMember(int playerId, out Session session) => _members.TryGetValue(playerId, out session);

        /// <summary>遍历成员（StartGame 广播用；playerId 升序）。</summary>
        public IEnumerable<KeyValuePair<int, Session>> AllMembers()
        {
            for (int i = 0; i < NextPlayerId; i++)
                if (TryGetMember(i, out Session s)) yield return new KeyValuePair<int, Session>(i, s);
        }

        public long EntityIdOf(int playerId) =>
            playerId >= 0 && playerId < ExpectedPlayers ? _entityIds[playerId] : 0L;

        /// <summary>StartGame：世界按种子生成，玩家落出生点（§4.5 权威定序——StartGame 后循环才消费输入）。</summary>
        public void Start(long seed)
        {
            Seed = seed;
            AuthSim.RngState = (ulong)seed;

            for (int i = 0; i < _members.Count; i++)
            {
                SimVector3 spawn = Map.SpawnPoints[i % Map.SpawnPointCount];
                AuthSim.Spawn(new EntitySlot { Hp = 100, Pos = spawn, Yaw = 0f }, out int slot);
                _entityIds[i] = AuthSim.Entities[slot].Id;
            }

            Started = true;
        }

        /// <summary>消费一条已过闸输入（写入当前聚合槽——同帧多包按闸门去重后首条生效）。</summary>
        /// <summary>消费一条已过闸输入（预存到 frame 对应槽——inputDelay=1 语义，权威帧推进到位时消费）。</summary>
        public void OnInput(Session session, InputMessage msg)
        {
            if (!Started || session.PlayerId < 0) return;
            Gate.Store(msg, session.PlayerId, _entityIds[session.PlayerId], AuthSim.Frame);
        }

        /// <summary>
        /// 推进一步权威帧（§4.5-3 权威定序）：消费预存输入（缺席沿用空输入）→ Step → 快照环 Capture。
        /// </summary>
        public bool StepFrame()
        {
            if (!Started) return false;

            int frame = AuthSim.Frame + 1;   // 本步目标帧号（输入按帧号预存——inputDelay=1 语义）
            for (int i = 0; i < ExpectedPlayers; i++)
            {
                // 缺席沿用：断线/未发包成员用空输入（§4.5-2；数组序即 playerId 升序——与 M9 帧号语义一致）
                _frameInputs[i] = Gate.TryConsume(frame, i, out SimInputFrame stored) ? stored : default;
            }

            SimStep.Step(AuthSim, Map, _frameInputs);
            StepsCount++;
            SnapshotHistory.Capture(AuthSim.Frame, AuthSim);   // 每帧捕获（批③ Diff/回溯基料）
            return true;
        }

        /// <summary>标准灰盒地图：±50 边界 + 16 网格出生点（MVP 房间形态；正式地图装配归 M11）。</summary>
        private static SimMapData BuildStandardMap()
        {
            var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
            for (int i = 0; i < SimMapData.MaxSpawnPoints; i++)
            {
                map.SpawnPoints[i] = new SimVector3(((i % 4) - 1.5f) * 10f, 0f, ((i / 4) - 1.5f) * 10f);
            }
            map.SpawnPointCount = SimMapData.MaxSpawnPoints;
            return map;
        }
    }
}
