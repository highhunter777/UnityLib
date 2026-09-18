using System;
using System.Threading;
using RoomServer;

// RoomServer 入口（M10 批①骨架 + 批②权威循环装配点）：MVP 参数固定（端口 17777 / Room-A / 2 人房）。
// 权威循环由 ServerHost.Pump 承担；本入口只做装配与常驻泵（节拍 Sleep(1) + 绝对锚定在 Room.StepFrame 调用侧由
// 测试/宿主控制推进——常驻形态每循环一权威帧，60Hz 由 Sleep 节拍近似，精度要求见实施记录）。
Console.WriteLine("[RoomServer] 启动（MVP：端口 17777 / 房间 Room-A / 期望 2 人）");

using var transport = new LiteNet.Transport.KcpTransportServer();
var host = new ServerHost(transport, ServerHost.Port);
Console.WriteLine($"[RoomServer] 监听 17777，房间 {ServerHost.DefaultRoomId}，buildHash={ServerHost.ServerBuildHash}");

while (true)
{
    host.Pump();              // Incoming → 权威步 → Outgoing
    Thread.Sleep(1);          // 60Hz 权威步由调用节奏近似（MVP 单循环；节拍锚定批③与 FrameAggregator 一并收口）
}
