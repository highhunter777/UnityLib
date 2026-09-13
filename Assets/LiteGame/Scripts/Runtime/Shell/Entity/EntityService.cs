using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>实体句柄：Hide/查询按句柄走（不裸露 GameObject 之外的生命周期控制）。</summary>
    public sealed class EntityHandle
    {
        public int Id { get; }
        public string Location { get; }
        public GameObject GameObject { get; internal set; }

        internal EntityHandle(int id, string location, GameObject go)
        {
            Id = id;
            Location = location;
            GameObject = go;
        }
    }

    /// <summary>实体生命周期回调（可选组件）：实体根上挂实现即被壳驱动（池化复用时逐次触发）。</summary>
    public interface IEntityLifecycle
    {
        void OnSpawn();      // 池取出/首次实例化后
        void OnRecycle();    // 回收入池前（隐藏前清理：粒子停、事件退订等）
    }

    /// <summary>
    /// 实体壳（M4 §2.8，手册步骤 6）：池化 GameObject + 生命周期容器 + **加载竞态表**。
    /// 实体 = 视觉表现件，逻辑回 C# 玩法系统（设计方案 §4.1）——**不转发 Lua**。
    /// 竞态语义（GF EntitiesToReleaseOnLoad 同款）：加载在途收到 Hide → 记入竞态表 →
    /// 加载完成后取消显示（直接出返回 null）。池按 location 分桶、只增不毁（复用零加载）。
    /// </summary>
    public sealed class EntityService : IModuleStats
    {
        private readonly Dictionary<string, Stack<GameObject>> _pool = new Dictionary<string, Stack<GameObject>>(8);
        private readonly Dictionary<int, EntityHandle> _active = new Dictionary<int, EntityHandle>(16);
        private readonly HashSet<int> _inFlight = new HashSet<int>();           // 加载在途
        private readonly HashSet<int> _releaseOnLoad = new HashSet<int>();      // 加载竞态表
        private int _nextHandle = 1;

        /// <summary>预占句柄（竞态场景用：先 Reserve → ShowAsync(id) → 任意时刻 Hide(id)）。</summary>
        public int Reserve() => _nextHandle++;

        /// <summary>显示实体（简单路径：内部 Reserve）。</summary>
        public UniTask<EntityHandle> ShowAsync(string location, Transform parent = null, CancellationToken ct = default)
            => ShowAsync(Reserve(), location, parent, ct);

        /// <summary>显示实体（指定句柄）：池命中直取；未命中加载 → 竞态表命中则取消（返回 null）。</summary>
        public async UniTask<EntityHandle> ShowAsync(int handleId, string location, Transform parent = null, CancellationToken ct = default)
        {
            if (_active.ContainsKey(handleId))
                throw new InvalidOperationException($"实体句柄 {handleId} 已在使用（先 Hide 再 Show，§3.4 fail-fast）");

            // 池命中：零加载直取
            if (_pool.TryGetValue(location, out var stack) && stack.Count > 0)
            {
                var pooled = stack.Pop();
                pooled.SetActive(true);
                if (parent != null) pooled.transform.SetParent(parent, false);
                var handle = new EntityHandle(handleId, location, pooled);
                _active[handleId] = handle;
                pooled.GetComponent<IEntityLifecycle>()?.OnSpawn();
                Log.Info($"实体[{handleId}] 复用（{location}）", "Entity");
                return handle;
            }

            // 未命中：加载在途登记（竞态窗口开启）
            _inFlight.Add(handleId);
            try
            {
                var prefab = await AssetService.LoadAssetAsync<GameObject>(location, ct);
                _inFlight.Remove(handleId);
                if (_releaseOnLoad.Remove(handleId))
                {
                                    // 加载期间被 Hide（竞态表命中）——不实例化不显示，直接取消
                    Log.Info($"实体[{handleId}] 加载期间已被 Hide——取消显示（竞态表）", "Entity");
                    return null;
                }

                var go = UnityEngine.Object.Instantiate(prefab, parent != null ? parent : null);
                go.name = $"{System.IO.Path.GetFileNameWithoutExtension(location)}[{handleId}]";
                var handle = new EntityHandle(handleId, location, go);
                _active[handleId] = handle;
                handle.GameObject.GetComponent<IEntityLifecycle>()?.OnSpawn();
                Log.Info($"实体[{handleId}] 显示（{location}）", "Entity");
                return handle;
            }
            finally
            {
                _inFlight.Remove(handleId);                // 幂等兜底（正常路径上方已移除）
            }
        }

        /// <summary>隐藏实体：活跃 → 回收入池；加载在途 → 竞态表；未知句柄告警忽略。</summary>
        public void Hide(int handleId)
        {
            if (_active.TryGetValue(handleId, out var handle))
            {
                handle.GameObject.GetComponent<IEntityLifecycle>()?.OnRecycle();
                handle.GameObject.SetActive(false);
                if (!_pool.TryGetValue(handle.Location, out var stack))
                    _pool[handle.Location] = stack = new Stack<GameObject>(4);
                stack.Push(handle.GameObject);
                _active.Remove(handleId);
                Log.Info($"实体[{handleId}] 回收（{handle.Location}，池深 {stack.Count}）", "Entity");
                return;
            }
            if (_inFlight.Contains(handleId))
            {
                _releaseOnLoad.Add(handleId);              // 加载竞态表：完成后立即取消
                return;
            }
            Log.Warning($"实体[{handleId}] 未知句柄——Hide 忽略", "Entity");
        }

        public string StatsName => "Entity";

        public void Snapshot(Dictionary<string, string> into)
        {
            into["活跃"] = _active.Count.ToString();
            into["加载中"] = _inFlight.Count.ToString();
            int pooled = 0;
            foreach (var s in _pool.Values) pooled += s.Count;
            into["池中"] = pooled.ToString();
            into["竞态表"] = _releaseOnLoad.Count.ToString();
        }
    }
}
