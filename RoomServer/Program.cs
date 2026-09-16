using System;
using LiteNet.Protocol;
using LiteNet.Transport;

namespace RoomServer
{
    /// <summary>
    /// RoomServer 入口骨架（M10 批①）：验证工程链路——LiteNet（协议单源 + kcp2k）与 LiteSim.Core（权威 Sim 前置）
    /// 在 .NET 8 下可编译、可运行。权威循环（FrameAggregator/AuthSim/Room）批②在此之上装配。
    /// </summary>
    public static class Program
    {
        public static int Main()
        {
            Console.WriteLine($"[RoomServer] 骨架就绪——kcp2k {typeof(kcp2k.KcpChannel).Assembly.GetName().Name} / "
                + $"protobuf {typeof(Google.Protobuf.WellKnownTypes.Duration).Assembly.GetName().Version}");
            Console.WriteLine($"[RoomServer] 协议自检：{PacketType.Input} / {PacketType.StateSnapshot}，"
                + $"传输端口 {typeof(IRoomTransport).Name} 可用");
            return 0;
        }
    }
}
