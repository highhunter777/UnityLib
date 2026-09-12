using System;
using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    public interface ITimelineRunner : ITickable, IModuleStats
    {
        ITimeline CreateTimeline();
    }

    public interface ITimeline
    {
        ITimeline At(float seconds, Action action);   // 链式注册动作点
        void Start();
        void Stop();                                  // 可中断
        bool Finished { get; }
    }
}
