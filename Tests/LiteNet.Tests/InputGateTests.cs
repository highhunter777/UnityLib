using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 输入闸门单元用例（《M10实施指导》附「服务端审查」2026-09-19）：
    /// 取帧口径、ack 处理、已消费帧拒绝、按键白名单——四条都是"服务器边界"行为，
    /// 用假消息直接测 `InputGate`，不依赖网络与房间。
    /// </summary>
    public sealed class InputGateTests
    {
        private const int PlayerId = 0;
        private const long EntityId = 42L;

        [Fact]
        public void 取帧_优先服务器当前帧加一()
        {
            var gate = new InputGate(2);
            // 客户端包：msg.Frame = 11，窗口 [11,10,9,8]；服务器当前帧 10 → 需要 11
            InputMessage msg = Packet(11, 1, 2, 3, 4);
            Assert.True(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out int frame, out _));
            Assert.Equal(11, frame);
        }

        [Fact]
        public void 取帧_窗口含所需帧时取该帧的独立内容()
        {
            var gate = new InputGate(2);
            // 窗口 [11,10,9,8]；服务器当前帧 9 → 需要 10（不是包内最新的 11）
            InputMessage msg = Packet(11, 1f, 2f, 3f, 4f);
            Assert.True(gate.Store(msg, PlayerId, EntityId, serverFrame: 9, out int frame, out SimInputFrame input));
            Assert.Equal(10, frame);
            Assert.Equal(2f, input.MoveX, 1e-6f);          // 取的是 10 那一帧的内容（不是最新帧）
        }

        [Fact]
        public void 已消费帧_拒绝且不入预存()
        {
            var gate = new InputGate(2);
            // 服务器当前帧 10；包内窗口 [9,8,7,6] 全部已消费 → 拒绝，且不得滞留
            InputMessage msg = Packet(9, 1f, 2f, 3f, 4f);
            Assert.False(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out _, out _));
            Assert.Equal(1, gate.DroppedStaleFrame);
            Assert.Equal(0, gate.AcceptedCount);
            Assert.False(gate.TryConsume(9, PlayerId, out _));   // 未预存（不会有永不消费的滞留项）
            Assert.False(gate.TryConsume(8, PlayerId, out _));
        }

        [Fact]
        public void 超前ack_不丢输入_只计数并钳位()
        {
            var gate = new InputGate(2);
            // ack 超前（客户端回传的 ack 曾是"下一待处理帧"——见 RoomBroadcaster 的钳位说明）
            InputMessage msg = Packet(11, 1f, 1f, 1f, 1f, ackSnapshot: 99);
            Assert.True(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out int frame, out _));
            Assert.Equal(11, frame);                          // 输入照常被接受
            Assert.Equal(1, gate.AcceptedCount);
            Assert.Equal(1, gate.DroppedAckSnapshot);         // 违规有记账
            Assert.Equal(10, gate.LastClampedAckSnapshot);    // 越界值被钳到 serverFrame
        }

        [Fact]
        public void 负ack_同样只计数不丢输入()
        {
            var gate = new InputGate(2);
            InputMessage msg = Packet(11, 1f, 1f, 1f, 1f, ackSnapshot: -5);
            Assert.True(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out _, out _));
            Assert.Equal(1, gate.DroppedAckSnapshot);
            Assert.Equal(0, gate.LastClampedAckSnapshot);
        }

        [Fact]
        public void 按键白名单_伪造服务器内部位被丢()
        {
            var gate = new InputGate(2);
            InputMessage msg = Packet(11, 1f, 1f, 1f, 1f, buttons: SimInputFrame.ButtonFireFlag);
            Assert.False(gate.Store(msg, PlayerId, EntityId, serverFrame: 10, out _, out _));
            Assert.Equal(1, gate.DroppedIllegalButtons);
        }

        [Fact]
        public void 同帧去重_首条生效()
        {
            var gate = new InputGate(2);
            Assert.True(gate.Store(Packet(11, 1f, 0f, 0f, 0f), PlayerId, EntityId, 10, out _, out _));
            // 同帧重发（内容不同）→ 丢弃（首条生效）
            Assert.False(gate.Store(Packet(11, 9f, 0f, 0f, 0f), PlayerId, EntityId, 10, out _, out _));
            Assert.Equal(1, gate.DroppedDuplicateFrame);
            Assert.True(gate.TryConsume(11, PlayerId, out SimInputFrame kept));
            Assert.Equal(1f, kept.MoveX, 1e-6f);
        }

        [Fact]
        public void 未来帧超容忍窗_拒绝()
        {
            var gate = new InputGate(2);
            int tooFar = 10 + InputGate.FutureFrameTolerance + 1;
            Assert.False(gate.Store(Packet(tooFar, 1f, 0f, 0f, 0f), PlayerId, EntityId, 10, out _, out _));
            Assert.Equal(1, gate.DroppedOutOfRange);
        }

        [Fact]
        public void 实体Id防伪_覆写为会话所属()
        {
            var gate = new InputGate(2);
            InputMessage msg = Packet(11, 1f, 0f, 0f, 0f);
            msg.Frames[0].EntityId = 99999;                   // 客户端上报伪 Id
            Assert.True(gate.Store(msg, PlayerId, EntityId, 10, out _, out SimInputFrame input));
            Assert.Equal(EntityId, input.EntityId);           // 一律覆写
        }

        /// <summary>构造一条窗口 [frame, frame-1, frame-2, frame-3] 的输入包（每帧内容各不相同）。</summary>
        private static InputMessage Packet(int frame, float m0, float m1, float m2, float m3,
            int ackSnapshot = 0, uint buttons = 0)
        {
            var msg = new InputMessage { Frame = frame, AckSnapshot = ackSnapshot, ViewFrame = 0 };
            float[] moves = { m0, m1, m2, m3 };
            for (int i = 0; i < InputPacker.MaxRedundancy; i++)
                msg.Frames.Add(new InputFrame { EntityId = 1, MoveX = moves[i], MoveZ = 0f, AimX = 1f, AimZ = 0f, Buttons = i == 0 ? buttons : 0u });
            return msg;
        }
    }
}
