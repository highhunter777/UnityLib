using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteFramework
{
    /// <summary>GameObject 池化生命周期契约：取出后 / 归还前（实体、UI 件等场景物件实现）。</summary>
    public interface IPoolLifecycle
    {
        void OnSpawn();      // 池取出/首次创建后
        void OnRecycle();    // 回收入池前（停粒子、退事件、断引用——谁挂谁清）
    }

    /// <summary>池化标记：记录产源（池实例 + 桶键）——Release 自描述归桶；非本池产物入池当场抛。</summary>
    public sealed class PooledInstance : MonoBehaviour
    {
        public GameObjectPool Owner { get; internal set; }
        public string Key { get; internal set; }

        internal void Init(GameObjectPool owner, string key) { Owner = owner; Key = key; }
    }

    /// <summary>
    /// GameObject 对象池（三池分工的第三件，2026-09-13 提炼自 EntityService 池化内核）：
    /// **纯数据对象 → ReferencePool（Core 静态）；带回调的 C# 逻辑实例 → ObjectPool&lt;T&gt;（Core）；
    /// 会进场景渲染的 GameObject → 本池（Unity 层——需要 SetActive/父节点/引擎 API）**。
    /// 按 key（资源位置/类别）分桶、栈式复用、只增不毁（Trim 随需再议——YAGNI）。
    /// 生命周期：Get 后 OnSpawn / Release 前 OnRecycle（实现 IPoolLifecycle 即被驱动）。
    /// 释放自描述：池产物带 PooledInstance 标记，Release 归桶无需调用方记键；外来物入池当场抛。
    /// 主线程 only（框架约定）。
    /// </summary>
    public sealed class GameObjectPool
    {
        private readonly Dictionary<string, Stack<GameObject>> _buckets = new Dictionary<string, Stack<GameObject>>(8);
        private readonly Transform _root;                  // 池化停放容器（整体隐藏——池内物必非激活）
        private int _pooledTotal;

        public GameObjectPool(Transform root = null)
        {
            if (root != null)
            {
                _root = root;
            }
            else
            {
                _root = new GameObject("[GameObjectPool]").transform;
                UnityEngine.Object.DontDestroyOnLoad(_root.gameObject);
            }
            _root.gameObject.SetActive(false);
        }

        /// <summary>取：桶命中复用（重挂父 + 激活 + OnSpawn），未命中 create 现建并打池化标记。</summary>
        public GameObject Get(string key, Func<GameObject> create, Transform parent = null)
        {
            if (TryGet(key, out var go, parent)) return go;

            go = create();
            go.AddComponent<PooledInstance>().Init(this, key);
            if (parent != null) go.transform.SetParent(parent, false);
            go.SetActive(true);
            go.GetComponent<IPoolLifecycle>()?.OnSpawn();
            return go;
        }

        /// <summary>尝试复用（供异步加载管线先查池、未命中再走加载）。</summary>
        public bool TryGet(string key, out GameObject go, Transform parent = null)
        {
            go = null;
            if (!_buckets.TryGetValue(key, out var stack) || stack.Count == 0) return false;
            go = stack.Pop();
            _pooledTotal--;
            if (parent != null) go.transform.SetParent(parent, false);
            go.SetActive(true);
            go.GetComponent<IPoolLifecycle>()?.OnSpawn();
            return true;
        }

        /// <summary>还：OnRecycle → 隐藏 → 归桶（按 PooledInstance 自描述）。非本池产物抛（fail-fast）。</summary>
        public void Release(GameObject go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            var marker = go.GetComponent<PooledInstance>();
            if (marker == null || marker.Owner != this)
                throw new InvalidOperationException("该 GameObject 不是本池产物——禁止入池（来源见 PooledInstance 标记）");

            marker.GetComponent<IPoolLifecycle>()?.OnRecycle();
            go.SetActive(false);
            go.transform.SetParent(_root, false);
            if (!_buckets.TryGetValue(marker.Key, out var stack))
                _buckets[marker.Key] = stack = new Stack<GameObject>(4);
            stack.Push(go);
            _pooledTotal++;
        }

        /// <summary>当前池中（闲置）总数——统计/HUD 用。</summary>
        public int PooledTotal => _pooledTotal;

        public int PooledCount(string key)
            => _buckets.TryGetValue(key, out var stack) ? stack.Count : 0;
    }
}
