using System;
using System.Collections.Generic;

namespace LiteFramework
{
    public sealed class EventCenter : IEventCenter, ITickable, IModuleStats
    {
        private readonly Dictionary<Type, IEventChannel> _channels = new Dictionary<Type, IEventChannel>(32);
        private readonly Queue<PendingEvent> _queue = new Queue<PendingEvent>(16);
        private readonly Dictionary<Type, Action<object>> _adapters = new Dictionary<Type, Action<object>>(16);
        private long _published;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
        public bool StrictMode { get; set; } = true;    // 编辑器/开发构建：未订阅事件告警
#else
        public bool StrictMode { get; set; } = false;   // release：收紧
#endif

        public Action Subscribe<T>(Action<T> handler) where T : class
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var channel = (EventChannel<T>)GetOrAddChannel<T>();
            channel.Add(handler);
            return () => channel.Remove(handler);          // 闭包分配一次/订阅;订阅是生命周期边界低频操作,可接受
        }

        public void Publish<T>(T e) where T : class
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            _published++;
            var channel = (EventChannel<T>)GetOrAddChannel<T>();
            bool dispatched = channel.Publish(e);
            if (!dispatched && StrictMode) Log.Warning($"no subscriber: {typeof(T).Name}", "Event");
            RecycleIfPooled(e);                            // 无订阅者路径同样回收——否则无订阅事件 = 泄漏
        }

        public void PublishQueued<T>(T e) where T : class
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (!_adapters.ContainsKey(typeof(T)))
                _adapters[typeof(T)] = obj => Publish((T)obj);   // 每类型一次,恢复强类型后走同一派发核心
            _queue.Enqueue(new PendingEvent(typeof(T), e));        // class 约束:引用上转,零装箱
        }

        public void Tick(float realDelta)
        {
            int n = _queue.Count;                          // 快照量:派发期间新入队留到下帧,防互相触发同帧死循环
            for (int i = 0; i < n; i++)
            {
                PendingEvent pe = _queue.Dequeue();
                if (_adapters.TryGetValue(pe.EventType, out var fire)) fire(pe.Event);
            }
        }

        private IEventChannel GetOrAddChannel<T>() where T : class
        {
            if (_channels.TryGetValue(typeof(T), out var ch)) return ch;
            var created = new EventChannel<T>();
            _channels[typeof(T)] = created;
            return created;
        }

        /// <summary>所有权移交的落点:发布即移交,派发完成后中心统一回收。channel 保持纯机制,不知道 IReference 存在。</summary>
        private static void RecycleIfPooled(object e)
        {
            if (e is IReference pooled) ReferencePool.Release(pooled);
        }

        public string StatsName => "EventCenter";

        /// <summary>契约：实现负责 into.Clear() 再填入；key 为常量串零分配，value 为插值串（HUD 低频轮询可接受）。</summary>
        public void Snapshot(Dictionary<string, string> into)
        {
            into.Clear();
            int subscribers = 0;
            foreach (var ch in _channels.Values) subscribers += ch.SubscriberCount;

            into["事件类型数"] = _channels.Count.ToString();
            into["订阅者总数"] = subscribers.ToString();
            into["队列长度"] = _queue.Count.ToString();
            into["累计发布"] = _published.ToString();
        }

        private readonly struct PendingEvent
        {
            public readonly Type EventType;
            public readonly object Event;
            public PendingEvent(Type eventType, object e) { EventType = eventType; Event = e; }
        }
    }
}
