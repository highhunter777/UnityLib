using System.Collections.Generic;
using System.Collections.Generic;
using LiteNet;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;

namespace RoomServer
{
    /// <summary>
    /// 输入闸门（《状态同步实施方案》§4.5-6 两层输入校验的**传输层**）：
    /// 只挡"传输层可见的非法"，不做语义校验（语义 clamp 在 Sim 内——§4.5-6 第二层，两端一致由 M8 Sim 保证）。
    ///
    /// 职责（2026-09-17 重构：校验/存储与消费分离——客户端按 inputDelay=1 发**未来帧**输入，
    /// 本类按帧号**预存**，权威帧推进到位时由 Room 消费）：
    /// - **帧号合法性**：frame ≤ 0 或 frame > 服务器当前帧 + 容忍窗（未来帧时钟攻击面）丢弃。
    /// - **同帧去重**：每帧每玩家至多 1 条（后到覆盖语义改为"首条生效"——冗余包重复不重复消费）。
    /// - **EntityId 防伪**：客户端上报的 EntityId 一律**覆写**为该会话所属实体 Id。
    /// - **ackSnapshot**：合法性记账（负值/超前丢弃）——批③延迟补偿消费。
    /// </summary>
    public sealed class InputGate
    {
        /// <summary>未来帧容忍窗（帧号超过 当前帧+此值 = 丢弃——客户端时钟攻击面）。</summary>
        public const int FutureFrameTolerance = 8;

        /// <summary>合法输入的接收计数（Ops）。</summary>
        public long AcceptedCount;

        /// <summary>非法/冗余输入的丢弃计数（Ops：按原因分列）。</summary>
        public long DroppedIllegalFrame;
        public long DroppedDuplicateFrame;
        public long DroppedOutOfRange;
        public long DroppedAckSnapshot;

        private readonly int _playerCount;
        /// <summary>预存输入：按帧号索引（服务器帧推进到 f 时消费 f 的预存输入；缺席 = 空输入沿用）。</summary>
        private readonly Dictionary<int, SimInputFrame> _pending = new Dictionary<int, SimInputFrame>();
        /// <summary>各玩家最近被接受的输入帧号（同帧去重）。</summary>
        private readonly int[] _lastAcceptedFrame;

        public InputGate(int playerCount)
        {
            _playerCount = playerCount;
            _lastAcceptedFrame = new int[playerCount];
            for (int i = 0; i < playerCount; i++) _lastAcceptedFrame[i] = -1;
        }

        /// <summary>
        /// 校验并**预存**一条输入（frame 可为未来帧——inputDelay=1 语义）。
        /// EntityId 覆写为会话所属实体（防伪）。返回 false = 校验失败已丢弃（调用方无需处理）。
        /// </summary>
        public bool Store(InputMessage msg, int playerId, long entityId, int serverFrame)
        {
            // ackSnapshot 合法性：非负且不超前于服务器当前帧（超前 = 捏造收包）
            if (msg.AckSnapshot < 0 || msg.AckSnapshot > serverFrame)
            {
                DroppedAckSnapshot++;
                return false;
            }

            // 冗余窗口取帧：优先"服务器当前帧+1"（inputDelay 正常形态），退而取窗口内未消费的最大帧
            int frame = -1;
            InputFrame wire = null;
            for (int f = serverFrame + 1; f >= serverFrame - InputPacker.MaxRedundancy && wire == null; f--)
            {
                int offset = msg.Frame - f;
                if (offset >= 0 && offset < msg.Frames.Count) { wire = msg.Frames[offset]; frame = f; }
            }

            if (wire == null || frame <= 0 || frame > serverFrame + FutureFrameTolerance)
            {
                DroppedOutOfRange++;
                return false;
            }

            // 同帧去重：每帧每玩家至多 1 条（首条生效）
            if (frame <= _lastAcceptedFrame[playerId])
            {
                DroppedDuplicateFrame++;
                return false;
            }
            _lastAcceptedFrame[playerId] = frame;

            var input = new SimInputFrame
            {
                EntityId = entityId,                                  // EntityId 防伪覆写
                MoveX = wire.MoveX, MoveZ = wire.MoveZ,
                AimX = wire.AimX, AimZ = wire.AimZ,
                Buttons = wire.Buttons,
            };
            _pending[frame] = input;
            AcceptedCount++;
            return true;
        }

        /// <summary>消费：权威帧推进到 f 时取预存输入；无预存 = 空输入沿用（掉线/迟到）。</summary>
        public bool TryConsume(int frame, int playerId, out SimInputFrame input)
        {
            if (_pending.TryGetValue(frame, out input))
            {
                _pending.Remove(frame);
                return true;
            }
            input = default;
            return false;
        }
    }
}
