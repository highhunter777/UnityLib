using Cysharp.Threading.Tasks;
using System;
using System.Threading;


namespace LiteFramework
{
    public interface INetworkService : ITickable, IModuleStats
    {
        NetState State { get; }
        UniTask ConnectAsync(string host, int port, CancellationToken ct);
        void Send(ReadOnlySpan<byte> data, NetChannel channel);
        void Disconnect();
        event Action<NetState, string> OnStateChanged;   // 第二参数为错误原因，成功时 null
        event Action<byte[], NetChannel> OnReceive;      // 已按通道分流后回调
    }
}
