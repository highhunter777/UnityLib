using System.Collections;
using System.Collections.Generic;


namespace LiteFramework
{
    /// <summary>通道非泛型标记(存储与统计用)。</summary>
    internal interface IEventChannel
    {
        int SubscriberCount { get; }
    }

}
