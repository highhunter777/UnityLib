using System.Collections.Generic;
using System.Diagnostics;

namespace RoomServer
{
    /// <summary>会话表（§4.5 SessionManager 行：连接表 + 断线标记；超时踢除由 kcp2k 内建 Timeout 承担）。
    /// GetOrAddOnFirstPacket：首包未登记（连接成功但 Host 尚未见到 OnConnected 竞态窗口）时兜底登记。</summary>
    public sealed class SessionManager
    {
        private readonly Dictionary<int, Session> _byConnection = new Dictionary<int, Session>();

        public int Count => _byConnection.Count;

        public void Add(Session session) => _byConnection[session.ConnectionId] = session;

        public bool TryGet(int connectionId, out Session session) => _byConnection.TryGetValue(connectionId, out session);

        /// <summary>首包兜底：连接事件与首包间存在竞态窗口时补登记（Session 构造时间以当前计）。</summary>
        public Session GetOrAddOnFirstPacket(int connectionId, long nowMs)
        {
            if (_byConnection.TryGetValue(connectionId, out Session session)) return session;
            session = new Session(connectionId, nowMs);
            _byConnection[connectionId] = session;
            return session;
        }

        /// <summary>遍历会话（Ops/心跳巡检用）。</summary>
        public IEnumerable<Session> All()
        {
            foreach (var kv in _byConnection) yield return kv.Value;
        }
    }
}
