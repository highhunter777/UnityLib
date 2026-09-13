using System;
using System.Collections.Generic;

namespace LiteFramework
{
    /// <summary>逻辑时轨标记（M4 §2.7 决策 ①）：WorldClock 驱动——受暂停/时停/变速。</summary>
    public interface ILogicScheduler : IScheduler { }

    /// <summary>UI 时轨标记（M4 §2.7 决策 ①）：UIClock 驱动——受暂停、不受时停。</summary>
    public interface IUIScheduler : IScheduler { }

    /// <summary>
    /// 调度器实现（M4 §2.7，手册步骤 7；契约 = M1 IScheduler）：动作点/定时注册、到期触发、可取消。
    /// 决策落点（M4 §1e 定案）：①推进量 = 注入时钟的 ScaledDelta（**不自读 Time**——冻结/变速/暂停单源时钟）；
    /// ③到期判定 = 增量累积。回调安全：单个回调抛经 SafeCall 隔离，不炸调度器；
    /// 同帧到期按注册序 FIFO；回调内可再 Schedule/Cancel（先摘除后触发，再入项下一 tick 处理）。
    /// 双轨实例：LogicScheduler / UIScheduler 空壳子类（WorldClock/UIClock 同款手法）。
    /// </summary>
    public class GameScheduler : IScheduler
    {
        private sealed class ScheduledItem
        {
            public int Id;
            public float Remaining;
            public Action Callback;
            public bool Canceled;
        }

        private readonly IGameClock _clock;
        private readonly List<ScheduledItem> _items = new List<ScheduledItem>(16);
        private int _nextId = 1;

        public GameScheduler(IGameClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>注册定时动作（delay 用时钟域秒——受变速/暂停语义约束），返回取消 id。</summary>
        public int Schedule(float delaySeconds, Action callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            var id = _nextId++;
            _items.Add(new ScheduledItem
            {
                Id = id,
                Remaining = Math.Max(0f, delaySeconds),
                Callback = callback,
            });
            return id;
        }

        public void Cancel(int id)
        {
            foreach (var item in _items)
            {
                if (item.Id != id) continue;
                item.Canceled = true;
                item.Callback = null;
                return;
            }
        }

        /// <summary>到期容差（0.1ms）：浮点累积漂移（0.1f×3 ≈ 0.3f-3e-8）不吃掉最后一次触发。</summary>
        private const float DueEpsilon = 1e-3f;

        public void Tick(float realDelta)
        {
            float dt = _clock.ScaledDelta;                 // 冻结/变速单源：时钟层（决策 ①）
            if (dt <= 0f) return;

            List<ScheduledItem> due = null;
            for (int i = _items.Count - 1; i >= 0; i--)    // 逆序摘除；先摘后触发（回调内再入安全）
            {
                var item = _items[i];
                if (item.Canceled)
                {
                    _items.RemoveAt(i);
                    continue;
                }
                item.Remaining -= dt;
                if (item.Remaining <= DueEpsilon)
                {
                    _items.RemoveAt(i);
                    due = due ?? new List<ScheduledItem>();
                    due.Add(item);
                }
            }

            if (due == null) return;
            due.Sort((a, b) => a.Id.CompareTo(b.Id));      // 同帧到期按注册序 FIFO（决策 ③）
            foreach (var item in due)
                SafeCall.Invoke(item.Callback, $"Schedule[{item.Id}]");
        }

        public string StatsName => "Scheduler";

        public void Snapshot(Dictionary<string, string> into)
        {
            int pending = 0;
            foreach (var item in _items) if (!item.Canceled) pending++;
            into["待触发"] = pending.ToString();
        }
    }

    /// <summary>逻辑时轨调度器（WorldClock 域）。</summary>
    public sealed class LogicScheduler : GameScheduler, ILogicScheduler
    {
        public LogicScheduler(IGameClock clock) : base(clock) { }
    }

    /// <summary>UI 时轨调度器（UIClock 域）。</summary>
    public sealed class UIScheduler : GameScheduler, IUIScheduler
    {
        public UIScheduler(IGameClock clock) : base(clock) { }
    }
}
