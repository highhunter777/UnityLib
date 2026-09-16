using System;

namespace LiteNet.Transport
{
    /// <summary>
    /// 房间传输窄端口（《状态同步实施方案》§4.6 可替换性第二刀）：
    /// FrameAggregator / AuthSim / SnapshotDiffer 等房间业务组件只依赖这 3 类操作 + 3 个事件，
    /// 不感知 KcpServer——换传输框架 / 拆 Gate 时 Sim 代码零改动。
    /// 驱动由业务循环负责（MVP 单循环：每 tick 顺序 TickIncoming → 逻辑 → TickOutgoing，~10ms 节拍）。
    /// </summary>
    public interface IRoomTransport : IDisposable
    {
        void Start(int port);
        void TickIncoming();
        void TickOutgoing();

        void SendTo(int connectionId, ArraySegment<byte> data, bool reliable);
        void Broadcast(ArraySegment<byte> data, bool reliable);
        void Disconnect(int connectionId);

        /// <summary>connectionId、载荷、是否来自可靠通道。</summary>
        event Action<int, ArraySegment<byte>, bool> OnData;
        event Action<int> OnConnected;
        event Action<int> OnDisconnected;
    }
}
