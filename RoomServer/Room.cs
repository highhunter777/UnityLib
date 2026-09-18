using System;
using System.Collections.Generic;
using LiteSim;
using LiteNet.Protocol;
using LiteNet.Proto;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 房间（《状态同步实施方案》§4.5 Room 结构）：权威 Sim + 输入聚合 + 60Hz 定拍循环 +
    /// 快照广播（增量 + AOI + 全量兜底）+ 命中回溯 + E1 背压 + Ops。
    ///
    /// - **AuthSim 唯一真相**：SimWorldState 引用 LiteSim.Core 原生 Step——掉线者输入沿用空输入（§4.5-2 掉线不停帧）；
    /// - **快照环**：每帧 Capture（容量 LagCompHistory=16，M9 类复用）——回溯判定基料（批② 已建）；
    /// - **快照广播**（批③）：30Hz（`TickRate / SnapshotHz` 抽帧）→ <see cref="SnapshotDiffer"/> 增量 +
    ///   <see cref="AoiFilter"/> 可见裁剪 + 全量兜底；**退化为全量时循环结构不变**（决策 7 解耦点兑现）；
    /// - **定拍**：绝对时间锚定（nextTick = start + n×dt——防 PeriodicTimer/相对 Sleep 的漂移累积，风险 5）；
    /// - **成员表**：playerId ↔ entityId ↔ sessionId；输入 EntityId 一律按会话覆写（防伪）；
    /// - **Ops**：帧号/输入收发/丢弃/差分/回溯/背压——属性可断言，周期打印由 <see cref="Ops"/> 负责。
    /// MVP 房间形态：固定 2 人期望（批②/④验收形态），齐员自动 StartGame——无 MatchMaker（决策⑩）。
    /// </summary>
    public sealed class Room
    {
        public const int ExpectedPlayers = 2;

        public readonly string RoomId;
        public readonly SimWorldState AuthSim;         // 权威唯一真相
        public readonly SimMapData Map;
        public readonly SnapshotRing SnapshotHistory; // 回溯环（M9 类，容量 LagCompHistory）
        public readonly InputGate Gate;
        public readonly SnapshotDiffer Differ;         // 增量快照源（批③；批② 为 FullSnapshotSource 占位）
        public readonly LagCompensator LagComp;        // 命中回溯（批③）

        /// <summary>成员：playerId → 会话。</summary>
        private readonly Dictionary<int, Session> _members = new Dictionary<int, Session>();
        /// <summary>playerId → 玩家实体 Id（StartGame 分配）。</summary>
        private readonly long[] _entityIds;
        /// <summary>本帧聚合输入槽（Step 前重灌）。</summary>
        private readonly SimInputFrame[] _frameInputs;
        /// <summary>成员序（playerId 升序，广播按此序——确定性）。</summary>
        private readonly Session[] _playerSessions;
        /// <summary>本帧要广播的序号（每 TickRate/SnapshotHz 帧一次）。</summary>
        private int _broadcastOrdinal;
        /// <summary>下一次广播整帧强制全量（重连：新客户端要从零重建；用后自动清零）。</summary>
        private bool _forceFullPending;
        /// <summary>全体输入历史（重连补发用；§5.6 —— 环容量 <see cref="SimConfig.MaxInputHistory"/>，够 32 帧）。</summary>
        private readonly InputHistory _recentInputs = new InputHistory(SimConfig.MaxInputHistory, ExpectedPlayers);

        public int NextPlayerId;
        public bool Started;
        public long Seed;

        // ---- Ops 计数 ----
        public long StepsCount;
        public long SnapshotSent;
        public long SnapshotFullSent;
        public long BackpressureThrottled;      // 因背压降档跳过的广播次数
        public long BackpressureDegraded;       // 触发的降级动作次数（收缩 AOI/裁剪实体）
        public long FireInputsProcessed;        // 走回溯路径的开火输入数

        public Room(string roomId)
        {
            RoomId = roomId;
            Map = BuildStandardMap();
            AuthSim = new SimWorldState();
            SnapshotHistory = new SnapshotRing(SimConfig.LagCompHistory);
            Gate = new InputGate(ExpectedPlayers);
            Differ = new SnapshotDiffer();
            LagComp = new LagCompensator(AuthSim, ExpectedPlayers, SnapshotHistory);
            _entityIds = new long[ExpectedPlayers];
            _frameInputs = new SimInputFrame[ExpectedPlayers];
            _playerSessions = new Session[ExpectedPlayers];
        }

        /// <summary>成员就位分配玩家号（StartGame 前调用）；满员返回 -1。</summary>
        public int AssignPlayerId(Session session)
        {
            int playerId = NextPlayerId++;
            if (playerId >= ExpectedPlayers) return -1;
            return AssignPlayerId(session, playerId);
        }

        /// <summary>重连：把会话重挂到**既有席位**（playerId 不变，实体 Id 不变——§5.6 权威快照恢复的前提）。</summary>
        public int AssignPlayerId(Session session, int playerId)
        {
            if (playerId < 0 || playerId >= ExpectedPlayers) return -1;
            Session old = _playerSessions[playerId];
            if (old != null && old != session) old.Disconnected = true;   // 旧连接让位（新连接接管席位）
            _members[playerId] = session;
            _playerSessions[playerId] = session;
            session.PlayerId = playerId;
            session.Room = this;
            if (playerId >= NextPlayerId) NextPlayerId = playerId + 1;
            return playerId;
        }

        /// <summary>该帧全体玩家的历史输入（重连补发用——§5.6：权威快照 + 后续输入历史；环容量 MaxInputHistory）。</summary>
        public bool HistoryFor(int frame, out SimInputFrame[] inputs)
            => _recentInputs.TryGet(frame, out inputs, out bool[] _);

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

        /// <summary>消费一条已过闸输入（预存到 frame 对应槽——inputDelay=1 语义，权威帧推进到位时消费）。</summary>
        public void OnInput(Session session, InputMessage msg)
        {
            if (!Started || session.PlayerId < 0) return;
            long before = Gate.AcceptedCount;
            Gate.Store(msg, session.PlayerId, _entityIds[session.PlayerId], AuthSim.Frame);
            if (Gate.AcceptedCount != before && OnInputAccepted != null) OnInputAccepted(session, msg);

            // 开火 + 带视点帧 + 本包确实被接受 → 记下待回溯判定（在下一帧步进后执行——
            // 那时环里才有"开火帧"的历史态；见 Room.StepFrame 的注释）
            if (Gate.AcceptedCount != before && (msg.ViewFrame > 0) && HasFire(msg))
            {
                _pendingFireView = msg.ViewFrame;
                _pendingFireAck = msg.AckSnapshot;
                _pendingFirePlayer = session.PlayerId;
                _hasPendingFire = true;
                session.LastAckSnapshot = msg.AckSnapshot;
            }
        }

        private int _pendingFirePlayer = -1;
        private int _pendingFireView;
        private int _pendingFireAck;
        private bool _hasPendingFire;

        /// <summary>本包冗余窗口里最新的那一帧是否按了开火（与 InputGate 的取帧口径一致：优先服务器当前帧+1）。</summary>
        private bool HasFire(InputMessage msg)
        {
            int frame = AuthSim.Frame + 1;
            int offset = msg.Frame - frame;
            if (offset < 0 || offset >= msg.Frames.Count) return false;
            return (msg.Frames[offset].Buttons & SimInputFrame.ButtonFire) != 0u;
        }

        /// <summary>
        /// 推进一步权威帧（§4.5-3 权威定序）：消费预存输入（缺席沿用空输入）→ Step → 快照环 Capture
        /// → 记录历史输入 → 回溯判定（若本步有开火）→ 到点广播快照。
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
            int steppedFrame = AuthSim.Frame;
            SnapshotHistory.Capture(steppedFrame, AuthSim);   // 每帧捕获（回溯基料）
            _recentInputs.Record(steppedFrame, _frameInputs, null);   // 重连补发基料（Step 已就地排序 → 规范形）
            LagComp.RecordInputs(steppedFrame, _frameInputs);

            ConsumePendingFire();                             // 回溯判定（本步产生开火输入时）

            BroadcastIfDue(steppedFrame);
            return true;
        }

        /// <summary>
        /// 回溯判定：开火帧的历史态现在已在环里（本步 Capture 覆盖到"开火帧"本身），
        /// 因此可安全回溯到"玩家所见帧"判定，命中命令落到当前帧由下一次 FlushCommands 结算（当帧延迟语义）。
        /// </summary>
        private void ConsumePendingFire()
        {
            if (!_hasPendingFire) return;
            _hasPendingFire = false;

            int playerId = _pendingFirePlayer;
            if (playerId < 0 || playerId >= ExpectedPlayers) return;
            long entityId = _entityIds[playerId];

            LagComp.CompensateFire(playerId, entityId, _pendingFireView, _pendingFireAck);
            FireInputsProcessed++;
        }

        /// <summary>
        /// 到点广播（30Hz：每 TickRate/SnapshotHz 帧一次）。
        /// E1 背压按会话分档：档位 0 全速；档位 1 抽帧；档位 2 收缩 AOI；档位 3 额外裁掉最远实体。
        /// 降档只影响**该客户端**，其余客户端不受拖累（《服务端架构设计》§10-E1 验收点）。
        /// </summary>
        private void BroadcastIfDue(int frame)
        {
            if (frame <= 0) return;
            int stride = SimConfig.TickRate / SimConfig.SnapshotHz;
            if (stride < 1) stride = 1;
            _broadcastOrdinal++;
            if (_broadcastOrdinal % stride != 0) return;

            int broadcastIndex = _broadcastOrdinal / stride;   // 第几次广播（抽帧档按它取模）

            // ① 每广播帧**算一次差分**（推进金标）——多客户端共享同一份，各自只做 AOI 过滤（纯读）。
            // 全量触发（重连待补 / 有客户端 ack 掉队 / 周期性）是**整帧**属性：本帧对所有客户端都是全量，
            // 客户端各自丢弃多余槽位即可（1s 周期兜底本来就会发生，代价可接受；换来的是差分基线的一义性）。
            bool forceFull = _forceFullPending;
            for (int p = 0; p < ExpectedPlayers; p++)
            {
                Session session = _playerSessions[p];
                if (session == null || session.Disconnected) continue;
                if (Differ.NeedsFull(session.LastAckSnapshot)) forceFull = true;
            }
            _forceFullPending = false;
            Differ.BeginFrame(frame, AuthSim, forceFull);

            // ② 每客户端各取可见部分（背压档位只影响该客户端）
            for (int p = 0; p < ExpectedPlayers; p++)
            {
                Session session = _playerSessions[p];
                if (session == null || session.Disconnected) continue;

                UpdateBackpressureTier(session, frame);
                if (session.BackpressureTier >= 1 && broadcastIndex % ProtocolConstants.ThrottledStride != 0)
                {
                    BackpressureThrottled++;      // 档位 1+：抽帧（该客户端本次不发；其余客户端不受影响）
                    continue;
                }

                SendSnapshot(session, p, frame);
            }
        }

        private void SendSnapshot(Session session, int playerId, int frame)
        {
            long entityId = _entityIds[playerId];
            SimVector3 viewPos = ResolvePosition(entityId);
            float radius = session.BackpressureTier >= 2 ? ProtocolConstants.ThrottleAoiRadius : SimConfig.AoiRadius;

            Proto.StateSnapshot snapshot = Differ.BuildFor(frame, AuthSim, Gate.LastAcceptedFrame(playerId), viewPos, radius);
            if (session.BackpressureTier >= 3) TrimFarthest(snapshot, viewPos);   // 档位 3：低优先级实体丢弃
            if (snapshot.IsFull) SnapshotFullSent++;

            int bytes = snapshot.CalculateSize();
            session.SendQueueBytes += bytes;
            SnapshotSent++;
            SendSnapshotTo(session, snapshot);
        }

        /// <summary>下一次广播强制全量（重连场景：客户端要从零重建）。</summary>
        public void RequestFullSnapshot() => _forceFullPending = true;

        /// <summary>档位 3：裁掉"距视点最远的"一半实体（低优先级丢弃，§10-E1 第三级）。</summary>
        private static void TrimFarthest(Proto.StateSnapshot snapshot, SimVector3 viewPos)
        {
            if (snapshot.Slots.Count <= 1) return;
            int keep = (int)(snapshot.Slots.Count * ProtocolConstants.ThrottleEntityKeepRatio);
            if (keep >= snapshot.Slots.Count) return;

            // 按 XZ 距离升序（稳定：距离相等时保持原序——确定性），保留前 keep 个
            var pairs = new List<KeyValuePair<float, int>>(snapshot.Slots.Count);
            for (int i = 0; i < snapshot.Slots.Count; i++)
            {
                Proto.SlotDelta d = snapshot.Slots[i];
                float dx = d.PosX - viewPos.X;
                float dz = d.PosZ - viewPos.Z;
                float d2 = SimMath.MulAdd2(dx, dx, dz, dz);
                pairs.Add(new KeyValuePair<float, int>(d2, i));
            }
            pairs.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value));

            var kept = new List<Proto.SlotDelta>(keep);
            for (int i = 0; i < keep; i++) kept.Add(snapshot.Slots[pairs[i].Value]);
            snapshot.Slots.Clear();
            snapshot.Slots.AddRange(kept);
        }

        /// <summary>E1 水位判定与档位升降（滞回：水位超限即升档；低于 40% 且持续 2s 才逐档降）。</summary>
        private static void UpdateBackpressureTier(Session session, int frame)
        {
            long queued = session.SendQueueBytes - session.AckedBytes;
            if (queued < 0) queued = 0;

            if (queued > ProtocolConstants.BackpressureQueueLimitBytes)
            {
                if (session.BackpressureTier < 3) session.BackpressureTier++;
                session.BackpressureDrops++;
                session.RecoverSinceFrame = -1;
                return;
            }

            if (session.BackpressureTier > 0)
            {
                if (queued <= ProtocolConstants.BackpressureQueueLimitBytes * ProtocolConstants.BackpressureRecoverRatio)
                {
                    if (session.RecoverSinceFrame < 0) session.RecoverSinceFrame = frame;
                    else if (frame - session.RecoverSinceFrame >= ProtocolConstants.RecoverHoldMillis * SimConfig.TickRate / 1000)
                    {
                        session.BackpressureTier--;
                        session.RecoverSinceFrame = -1;
                    }
                }
                else
                {
                    session.RecoverSinceFrame = -1;
                }
            }
        }

        /// <summary>客户端 ack 到达：释放已确认的下行字节（E1 水位的减项）。</summary>
        public void OnClientAck(Session session, int ackSnapshot)
        {
            if (ackSnapshot < 0) return;
            session.LastAckSnapshot = ackSnapshot;
            // 简化记账：ack 前进即认为该连接的下行队列被消化（kcp2k 内建重传在极端丢包下会滞后，水位因而是保守估计）
            session.AckedBytes = session.SendQueueBytes;
        }

        private SimVector3 ResolvePosition(long entityId)
        {
            if (AuthSim.TryResolve(entityId, out int slot)) return AuthSim.Entities[slot].Pos;
            return SimVector3.Zero;
        }

        /// <summary>包发送出口（ServerHost 装配时注入；测试用 <c>Room.SendTo = (session, type, msg, reliable) => ...</c> 捕获）。</summary>
        public Action<Session, PacketType, Google.Protobuf.IMessage, bool> SendTo;

        /// <summary>Ops 计数挂点（ServerHost 装配；测试不挂 = 无计数）。计数由宿主/房间各自累加。</summary>
        public Action<Session, InputMessage> OnInputAccepted;

        private void SendSnapshotTo(Session session, Proto.StateSnapshot snapshot)
            => SendTo(session, PacketType.StateSnapshot, snapshot, false);

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
