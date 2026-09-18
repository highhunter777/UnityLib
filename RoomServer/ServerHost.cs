using System;
using System.Collections.Generic;
using System.Diagnostics;
using Google.Protobuf;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteNet.Transport;
using Proto = LiteNet.Proto;

namespace RoomServer
{
    /// <summary>
    /// 服务器宿主（《状态同步实施方案》§4.5 RoomServer 结构图顶层组装）：
    /// Transport（IRoomTransport）+ Sessions + Rooms + **单循环**（MVP 不拆 I/O 线程，§10.2——
    /// tick 顺序：TickIncoming → 信令/输入路由 → 房间权威步 → TickOutgoing）。
    ///
    /// MVP 装配参数：端口 17777（RoomServer 默认端口）、房间号 Room-A（无 MatchMaker，§4.5 MatchMaker 行）、
    /// 期望 2 人/房、buildHash = 字面量版本串（Join 比对不符拒绝——版本红线；批③接程序集哈希）。
    /// E3：连接 cookie 由 kcp2k V1.41 内建（白得，记录于实施记录）；per-IP 限速与重连票据批③接。
    /// </summary>
    public sealed class ServerHost : IDisposable
    {
        public const string ServerBuildHash = "litestim-core/net8.0/1.0";   // MVP：字面量版本串（批③接程序集哈希）
        public const string DefaultRoomId = "Room-A";
        public const int Port = 17777;

        private readonly KcpTransportServer _transport;
        private readonly SessionManager _sessions = new SessionManager();
        private readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private long _nowMs;
        private bool _disposed;

        public SessionManager Sessions => _sessions;
        public Room Room { get; } = new Room(DefaultRoomId);

        public ServerHost(KcpTransportServer transport, int port)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _rooms[DefaultRoomId] = Room;                     // MVP：预置单房间注册（Pump 遍历 _rooms 驱动权威步）
            _transport.OnConnected += OnTransportConnected;
            _transport.OnData += OnTransportData;
            _transport.OnDisconnected += OnTransportDisconnected;
            _transport.Start(port);   // Start 必须显式调用——此前遗漏导致服务器不监听（握手全失败）
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _transport.Dispose();
        }

        private long NowMs() => _clock.ElapsedMilliseconds;

        private void OnTransportConnected(int connectionId)
        {
            _nowMs = NowMs();
            _sessions.Add(new Session(connectionId, _nowMs));
        }

        private void OnTransportDisconnected(int connectionId)
        {
            _nowMs = NowMs();
            if (_sessions.TryGet(connectionId, out Session session))
            {
                session.Disconnected = true;   // 掉线不停帧（§4.5-2）：标记 + 会话保留（批③ 重连票据用）
            }
        }

        private void OnTransportData(int connectionId, ArraySegment<byte> data, bool reliable)
        {
            _nowMs = NowMs();
            Session session = _sessions.GetOrAddOnFirstPacket(connectionId, _nowMs);
            if (session == null) return;
            session.Touch(_nowMs);
            if (session.Disconnected) return;

            if (!PacketCodec.TryDecode(data, out PacketType type, out IMessage message)) return;

            switch (type)
            {
                case PacketType.Join:
                    HandleJoin(session, (Proto.JoinRequest)message);
                    break;
                case PacketType.Input:
                    if (session.Room != null && session.PlayerId >= 0)
                        session.Room.OnInput(session, (InputMessage)message);
                    break;
                case PacketType.Leave:
                    HandleLeave(session);
                    break;
            }
        }

        /// <summary>Join 信令：token 非空 + buildHash 必须等于服务器版本（版本红线）→ 分配玩家号 → JoinAck + 满员即 StartGame。</summary>
        private void HandleJoin(Session session, JoinRequest join)
        {
            if (session.PlayerId >= 0) return;                              // 重复 Join 忽略
            if (string.IsNullOrEmpty(join.Token))                            // token 红线：空即拒绝
            {
                Reject(session, "token 缺失");
                return;
            }
            if (join.BuildHash != ServerBuildHash)                           // 版本红线：Sim 构建比对不符拒绝进房
            {
                Reject(session, $"buildHash 不符：{join.BuildHash} != {ServerBuildHash}");
                return;
            }
            session.BuildHash = join.BuildHash;

            int playerId = Room.AssignPlayerId(session);
            if (playerId < 0)
            {
                Reject(session, "房间已满");
                return;
            }

            _transport.SendTo(session.ConnectionId,
                new ArraySegment<byte>(PacketCodec.Encode(PacketType.JoinAck,
                    new Proto.JoinAck { PlayerId = playerId, Members = { Room.MemberIds() } })),
                reliable: true);

            if (!Room.Started && Room.NextPlayerId >= Room.ExpectedPlayers)  // 满员自动 StartGame（无 MatchMaker）
            {
                Room.Start(DateTime.Now.Ticks & 0x7FFFFFFFL);
                foreach (var (_, member) in Room.AllMembers())
                {
                    _transport.SendTo(member.ConnectionId,
                        new ArraySegment<byte>(PacketCodec.Encode(PacketType.StartGame,
                            new Proto.StartGame { Seed = Room.Seed, ConfigHash = unchecked((uint)ServerBuildHash.GetHashCode()) })),
                        reliable: true);
                }
            }
        }

        private void HandleLeave(Session session)
        {
            session.Disconnected = true;
        }

        private static void Reject(Session session, string reason)
        {
            Console.WriteLine($"[Reject] conn {session.ConnectionId}: {reason}");   // Ops：批③ 收口到 Ops 汇总
        }

        /// <summary>单循环一帧（MVP 形态，§10.2）：tick 顺序 Incoming → 房间权威步 → Outgoing。
        /// 帧节拍由外部循环控制（绝对锚定）；网络收发每循环两次轮询（kcp2k ~10ms 节拍约定）。</summary>
        public void Pump()
        {
            if (_disposed) return;
            _transport.TickIncoming();
            foreach (var room in _rooms.Values) room.StepFrame();
            _transport.TickOutgoing();
        }

        public Room TryGetOrCreateRoom(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out Room room))
            {
                room = Room;                                                 // MVP：唯一预置房间 Room-A
                _rooms[roomId] = room;
            }
            return room;
        }
    }
}
