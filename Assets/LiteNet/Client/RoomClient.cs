using System;
using LiteNet.Protocol;
using LiteNet.Transport;

namespace LiteNet
{
    /// <summary>
    /// 房间客户端（§4.5 协议矩阵的 C→S 侧封装）：
    /// Join/输入（4 帧冗余）/MismatchReport 上报 + JoinAck/StartGame/StateSnapshot 事件化。
    /// 传输复用 KcpTransportClient（双通道：信令 Reliable / 输入与快照 Unreliable）。
    /// 纯会话与打包——Sim 预测/和解不在此（客户端 Sim 侧由 RollbackSim 承担，M10 全量预测形态）。
    /// </summary>
    public sealed class RoomClient : IDisposable
    {
        private readonly KcpTransportClient _transport;
        private int _lastAckSnapshot;
        private int _lastViewFrame;
        private bool _started;

        public bool Connected => _transport.Connected;
        public int PlayerId { get; private set; } = -1;
        public int LastAckSnapshot => _lastAckSnapshot;

        public event Action<Proto.JoinAck> OnJoinAck;
        public event Action<Proto.StartGame> OnStartGame;
        public event Action<Proto.StateSnapshot> OnSnapshot;
        public event Action OnDisconnected;

        public RoomClient(KcpTransportClient transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transport.OnData += OnWireData;
            _transport.OnDisconnected += () => OnDisconnected?.Invoke();
        }

        public void Connect(string host, int port) => _transport.Connect(host, port);

        public void SendJoin(string roomId, string token, string buildHash)
        {
            Send(PacketType.Join, new Proto.JoinRequest { RoomId = roomId, Token = token, BuildHash = buildHash }, reliable: true);
        }

        /// <summary>发送逐帧输入（4 帧冗余）；viewFrame = 开火时刻所见帧（延迟补偿回溯点，§3.4.1）。</summary>
        public void SendInput(int frame, Proto.InputFrame latest, int viewFrame)
        {
            var msg = new Proto.InputMessage { Frame = frame, AckSnapshot = _lastAckSnapshot, ViewFrame = viewFrame };
            for (int i = 0; i < InputPacker.MaxRedundancy; i++)
            {
                msg.Frames.Add(new Proto.InputFrame
                {
                    EntityId = latest.EntityId,
                    MoveX = latest.MoveX, MoveZ = latest.MoveZ,
                    AimX = latest.AimX, AimZ = latest.AimZ,
                    Buttons = i == 0 ? latest.Buttons : 0u,   // 冗余帧不重复开火位（离散事件不预测的镜像语义）
                });
            }
            Send(PacketType.Input, msg, reliable: false);
        }

        /// <summary>和解上报（Ops 汇总和解率——§4.5 关键机制 4）。</summary>
        public void SendMismatch(int frame)
        {
            Send(PacketType.MismatchReport, new Proto.MismatchReport { Frame = frame }, reliable: false);
        }

        public void TickIncoming() => _transport.TickIncoming();
        public void TickOutgoing() => _transport.TickOutgoing();

        public void Disconnect() => _transport.Disconnect();

        public void Dispose() => _transport.Dispose();

        private void Send(PacketType type, Google.Protobuf.IMessage message, bool reliable)
        {
            _transport.Send(new ArraySegment<byte>(PacketCodec.Encode(type, message)), reliable);
        }

        private void OnWireData(ArraySegment<byte> data, bool reliable)
        {
            if (!PacketCodec.TryDecode(data, out var type, out var msg)) return;
            switch (type)
            {
                case PacketType.JoinAck:
                    var ack = (Proto.JoinAck)msg;
                    PlayerId = ack.PlayerId;
                    OnJoinAck?.Invoke(ack);
                    break;
                case PacketType.StartGame:
                    _started = true;
                    OnStartGame?.Invoke((Proto.StartGame)msg);
                    break;
                case PacketType.StateSnapshot:
                    var snapshot = (Proto.StateSnapshot)msg;
                    _lastAckSnapshot = snapshot.AckInput;
                    OnSnapshot?.Invoke(snapshot);
                    break;
            }
        }
    }
}
