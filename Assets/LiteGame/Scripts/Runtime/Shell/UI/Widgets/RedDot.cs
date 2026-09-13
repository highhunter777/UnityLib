using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>
    /// 红点树（M4c，M4a 口子 RedDotRegistry 的完整实现）：节点计数变更向父链传播，父节点计数 = 子节点之和。
    /// 用法：tree.Node("mail").Node("mail.attach").SetCount(1) → "mail" 自动 1 并广播 onChanged。
    /// </summary>
    public sealed class RedDotNode
    {
        public string Key { get; }
        public int Count { get; private set; }
        public event Action<int> OnChanged;

        internal RedDotNode Parent { get; private set; }
        private readonly Dictionary<string, RedDotNode> _children = new Dictionary<string, RedDotNode>(4);
        private int _selfCount;                                        // 叶子自持计数；父 = 子和

        internal RedDotNode(string key, RedDotNode parent) { Key = key; Parent = parent; }

        public RedDotNode Node(string childKey)
        {
            if (!_children.TryGetValue(childKey, out var child))
            {
                child = new RedDotNode($"{Key}.{childKey}", this);
                _children[childKey] = child;
            }
            return child;
        }

        /// <summary>设置叶子自持计数（父链自动求和传播）。</summary>
        public void SetCount(int count)
        {
            count = Mathf.Max(0, count);
            if (_selfCount == count) return;
            int delta = count - _selfCount;
            _selfCount = count;
            Propagate(delta);
        }

        private void Propagate(int delta)
        {
            Count += delta;
            OnChanged?.Invoke(Count);
            Parent?.Propagate(delta);
        }
    }

    /// <summary>红点树宿主（每 UI 一棵；跨界面共享则挂 DI）。</summary>
    public sealed class RedDotTree
    {
        private readonly Dictionary<string, RedDotNode> _roots = new Dictionary<string, RedDotNode>(8);

        public RedDotNode Node(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("红点 key 不得为空", nameof(key));
            if (!_roots.TryGetValue(key, out var root))
            {
                root = new RedDotNode(key, null);
                _roots[key] = root;
            }
            return root;
        }
    }

    /// <summary>红点控件（M4c）：绑红点树 key，计数 &gt;0 显示；onChanged 驱动显隐 + 微动效。</summary>
    public class RedDot : UIWidget
    {
        public GameObject Dot;                                   // 红点本体（Image；子节点或自身）
        public RedDotTree Tree;
        public string Key;

        private RedDotNode _bound;

        /// <summary>绑定红点 key（重复绑定换绑）。</summary>
        public void Bind(RedDotTree tree, string key)
        {
            if (tree == null || string.IsNullOrEmpty(key)) return;
            if (_bound != null) _bound.OnChanged -= OnNodeChanged;
            Tree = tree;
            Key = key;
            _bound = tree.Node(key);
            _bound.OnChanged += OnNodeChanged;
            OnNodeChanged(_bound.Count);
        }

        private void OnNodeChanged(int count)
        {
            if (Dot == null) return;
            bool show = count > 0;
            if (Dot.activeSelf != show) Dot.SetActive(show);
            if (show) PlayPressFx();
        }

        private void OnDestroy()
        {
            if (_bound != null) _bound.OnChanged -= OnNodeChanged;
        }
    }
}
