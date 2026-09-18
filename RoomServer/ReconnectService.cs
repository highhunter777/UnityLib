using System.Collections.Generic;

namespace RoomServer
{
    /// <summary>
    /// 重连一次性票据（《服务端架构设计》§10-E3：《M10实施指导》决策 12）：
    /// Join 成功时签发 → 客户端断线后凭票换"权威快照 + 输入历史"（§5.6 首选路径）。
    /// **一次性**：消费即作废（重放同一张票拿不到第二次）；**有时效**：超过 <see cref="TicketTtlMs"/> 作废。
    /// 票据绑定 (playerId, roomId)——换票即恢复原席位，不重开新号。
    /// </summary>
    public sealed class ReconnectService
    {
        /// <summary>票据有效期（毫秒）：5 分钟（《联机Demo设计》断线宽容窗口）。</summary>
        public const long TicketTtlMs = 5 * 60 * 1000;

        private struct Ticket
        {
            public int PlayerId;
            public string RoomId;
            public long ExpireAtMs;
        }

        private readonly Dictionary<string, Ticket> _tickets = new Dictionary<string, Ticket>();
        private long _serial;

        private static long NowMs() => System.Environment.TickCount64;

        /// <summary>签发一次性票据（Join 成功时调用）。</summary>
        public string Issue(int playerId, string roomId)
        {
            string token = "rc" + (++_serial).ToString("x") + "-" + playerId.ToString("x");
            _tickets[token] = new Ticket { PlayerId = playerId, RoomId = roomId, ExpireAtMs = NowMs() + TicketTtlMs };
            return token;
        }

        /// <summary>消费票据（一次性 + 时效）：成功则移除并返回席位；失败不改状态。</summary>
        public bool TryConsume(string token, out int playerId, out string roomId)
        {
            playerId = -1;
            roomId = null;
            if (string.IsNullOrEmpty(token)) return false;
            if (!_tickets.TryGetValue(token, out Ticket ticket)) return false;

            _tickets.Remove(token);                       // 一次性：无论是否过期，用过即作废
            if (NowMs() > ticket.ExpireAtMs) return false;

            playerId = ticket.PlayerId;
            roomId = ticket.RoomId;
            return true;
        }

        /// <summary>清理过期票据（Ops 巡检用；不清理只是内存缓慢增长，功能不受影响）。</summary>
        public int PurgeExpired()
        {
            long now = NowMs();
            var dead = new List<string>();
            foreach (var kv in _tickets)
                if (now > kv.Value.ExpireAtMs) dead.Add(kv.Key);
            for (int i = 0; i < dead.Count; i++) _tickets.Remove(dead[i]);
            return dead.Count;
        }
    }
}
