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

    /// <summary>
    /// 实体壳（M4 §2.8，手册步骤 6）：句柄制实体管理叠在通用 <see cref="GameObjectPool"/> 之上
    /// （2026-09-13 提炼：池化内核归 Unity 层通用池，本类只留实体语义——Reserve/竞态表/句柄制）。
    /// 实体 = 视觉表现件，逻辑回 C# 玩法系统（设计方案 §4.1）——**不转发 Lua**。
    /// 竞态语义（GF EntitiesToReleaseOnLoad 同款）：加载在途收到 Hide → 记入竞态表 →
    /// 加载完成后取消显示（直接出返回 null）。生命周期回调 = IPoolLifecycle（通用池驱动）。
    /// </summary>
    public sealed class EntityService : IModuleStats
    {
        private readonly GameObjectPool _pool = new GameObjectPool();
        private readonly Dictionary<int, EntityHandle> _active = new Dictionary<int, EntityHandle>(16);
        private readonly HashSet<int> _inFlight = new HashSet<int>();           // 加载在途
        private readonly HashSet<int> _releaseOnLoad = new HashSet<int>();      // 加载竞态表
        private int _nextHandle = 1;

        /// <summary>预占句柄（竞态场景用：先 Reserve → ShowAsync(id) → 任意时刻 Hide(id)）。</summary>
        public int Reserve() => _nextHandle++;

        /// <summary>显示实体（简单路径：内部 Reserve）。</summary>
        public UniTask<EntityHandle> ShowAsync(string location, Transform parent = null, CancellationToken ct = default)
            => ShowAsync(Reserve(), location, parent, ct);

        /// <summary>显示实体（指定句柄）：池命中直取（零加载）；未命中加载 → 竞态表命中则取消（返回 null）。</summary>
        public async UniTask<EntityHandle> ShowAsync(int handleId, string location, Transform parent = null, CancellationToken ct = default)
        {
            if (_active.ContainsKey(handleId))
                throw new InvalidOperationException($"实体句柄 {handleId} 已在使用（先 Hide 再 Show，§3.4 fail-fast）");

            // 池命中：零加载直取（生命周期 OnSpawn 由通用池驱动）
            if (_pool.TryGet(location, out var pooled, parent))
            {
                var pooledHandle = new EntityHandle(handleId, location, pooled);
                _active[handleId] = pooledHandle;
                Log.Info($"实体[{handleId}] 复用（{location}，池中 {_pool.PooledTotal}）", "Entity");
                return pooledHandle;
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

                // 首建：经通用池建桶（打 PooledInstance 标记，后续走复用）
                var go = _pool.Get(location, () => UnityEngine.Object.Instantiate(prefab, parent), parent);
                var handle = new EntityHandle(handleId, location, go);
                _active[handleId] = handle;
                handle.GameObject.GetComponent<IPoolLifecycle>()?.OnSpawn();
                Log.Info($"实体[{handleId}] 显示（{location}，池中 {_pool.PooledTotal}）", "Entity");
                return handle;
            }
            finally
            {
                _inFlight.Remove(handleId);                // 幂等兜底（正常路径上方已移除）
            }
        }

        /// <summary>隐藏实体：活跃 → 通用池回收（OnRecycle + 隐藏 + 归桶）；加载在途 → 竞态表；未知句柄告警忽略。</summary>
        public void Hide(int handleId)
        {
            if (_active.TryGetValue(handleId, out var handle))
            {
                _pool.Release(handle.GameObject);
                _active.Remove(handleId);
                Log.Info($"实体[{handleId}] 回收（{handle.Location}，池中 {_pool.PooledTotal}）", "Entity");
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
            into["池中"] = _pool.PooledTotal.ToString();
            into["竞态表"] = _releaseOnLoad.Count.ToString();
        }
    }
}
