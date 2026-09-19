using System.Collections.Generic;
using LiteNet.Protocol;
using LiteNet.Proto;
using LiteSim;
using RoomServer;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// 房间输入消费用例（《M10实施指导》附「服务端审查」2026-09-19）：
    /// 关注"输入进到房间之后"的语义——多人同 tick 开火是否都记回溯、超前 ack 是否仍推进。
    /// 用 <c>Room.SendTo</c> 捕获出口（不起网络）。
    /// </summary>
    public sealed class RoomInputTests
    {
        private const long SharedSeed = 20260919L;

        [Fact]
        public void 同tick两名玩家开火_两人都记回溯判定()
        {
            (Room room, Session s1, Session s2) = BuildStartedRoom();
            int frame = room.AuthSim.Frame + 1;                   // inputDelay=1：客户端发"下一帧"

            room.OnInput(s1, FirePacket(frame, ackSnapshot: room.AuthSim.Frame));
            room.OnInput(s2, FirePacket(frame, ackSnapshot: room.AuthSim.Frame));

            Assert.True(room.StepFrame());

            // 修正前：pending 是单槽 → 只有后到的那名玩家被记回溯（先到的静默丢失）
            Assert.Equal(2, room.FireInputsProcessed);
        }

        [Fact]
        public void 超前ack的输入_仍被接受并推进权威()
        {
            (Room room, Session s1, _) = BuildStartedRoom();
            int frame = room.AuthSim.Frame + 1;
            EntitySlot before = SlotOf(room, 0);

            // ack 超前（服务器自身曾下发"下一待处理帧"作为 ack，客户端原样回传）
            InputMessage msg = MovePacket(frame, moveX: 1f, ackSnapshot: room.AuthSim.Frame + 50);
            room.OnInput(s1, msg);

            Assert.Equal(1, room.Gate.AcceptedCount);             // 输入没被 ack 连坐丢弃
            Assert.True(room.Gate.DroppedAckSnapshot >= 1);       // 但违规有记账

            room.StepFrame();
            EntitySlot after = SlotOf(room, 0);
            Assert.NotEqual(before.Pos.X, after.Pos.X);           // 真的推进了（不是"空输入沿用"）
        }

        [Fact]
        public void 同一玩家同tick多次开火_只记一次回溯()
        {
            (Room room, Session s1, _) = BuildStartedRoom();
            int frame = room.AuthSim.Frame + 1;

            room.OnInput(s1, FirePacket(frame, room.AuthSim.Frame));
            room.OnInput(s1, FirePacket(frame, room.AuthSim.Frame));   // 冗余重发（同帧）

            room.StepFrame();
            Assert.Equal(1, room.FireInputsProcessed);            // 同帧去重 + 每玩家一次
        }

        private static (Room room, Session s1, Session s2) BuildStartedRoom()
        {
            var room = new Room(new RoomConfig { RoomId = "InputTest" });
            room.SendTo = (session, type, msg, reliable) => { };   // 不关心下行
            room.Broadcaster.SendTo = room.SendTo;

            var s1 = new Session(1, 0);
            var s2 = new Session(2, 0);
            room.AssignPlayerId(s1);
            room.AssignPlayerId(s2);
            room.Start(SharedSeed);
            return (room, s1, s2);
        }

        private static InputMessage FirePacket(int frame, int ackSnapshot)
        {
            var msg = new InputMessage { Frame = frame, AckSnapshot = ackSnapshot, ViewFrame = 30 };
            msg.Frames.Add(new InputFrame { MoveX = 0f, MoveZ = 0f, AimX = 1f, AimZ = 0f, Buttons = SimInputFrame.ButtonFire });
            return msg;
        }

        private static InputMessage MovePacket(int frame, float moveX, int ackSnapshot)
        {
            var msg = new InputMessage { Frame = frame, AckSnapshot = ackSnapshot, ViewFrame = 0 };
            msg.Frames.Add(new InputFrame { MoveX = moveX, MoveZ = 0f, AimX = 1f, AimZ = 0f, Buttons = 0u });
            return msg;
        }

        private static EntitySlot SlotOf(Room room, int playerId)
        {
            long id = room.EntityIdOf(playerId);
            Assert.True(room.AuthSim.TryResolve(id, out int slot));
            return room.AuthSim.Entities[slot];
        }
    }
}
