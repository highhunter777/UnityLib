using System;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// 层级组（M4 §2.1/§2.2）：组内栈 + Depth 分配（分配规则经 <see cref="ILayerStrategy"/> 注入，默认基序+槽位）。
    /// 组间深度以 BaseDepth 步进 100 隔离；组内槽位溢出 100 时回卷并告警（防侵入相邻组）。
    /// </summary>
    public sealed class UILayerGroup
    {
        public const int DepthStride = 100;

        public string Name { get; }
        public int BaseDepth { get; }
        public Transform Root { get; }                  // UIRoot 下的组节点（实例化挂点）
        public UIStack Stack { get; } = new UIStack();
        private readonly ILayerStrategy _layerStrategy;
        private int _nextSlot;

        public UILayerGroup(string name, int baseDepth, Transform root, ILayerStrategy layerStrategy)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            BaseDepth = baseDepth;
            Root = root ? root : throw new ArgumentNullException(nameof(root));
            _layerStrategy = layerStrategy ?? throw new ArgumentNullException(nameof(layerStrategy));
        }

        /// <summary>sortingOrder 由层级策略解析；槽位满 100 回卷（告警——同屏同组超百界面属异常）。</summary>
        public int AssignDepth(UIForm form)
        {
            if (_nextSlot >= DepthStride)
            {
                Log.Warning($"层级组[{Name}] 深度槽位耗尽（{DepthStride}）——回卷，旧界面 sortingOrder 将被复用", "UI");
                _nextSlot = 0;
            }
            int order = _layerStrategy.ResolveSortingOrder(this, _nextSlot);
            _nextSlot++;
            form.AssignDepth(order);
            return order;
        }
    }
}
