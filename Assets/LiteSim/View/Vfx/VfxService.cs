using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteSim.View
{
    /// <summary>
    /// VFX 服务实现（《VFX服务实施指导》§2）：加载 / 池化 / 挂点跟随 / 预算降级 / 到期回收。
    ///
    /// **三条边界**（《动作与特效设计》§2.3）：① UI 动效不并入（走 UiFx+DOTween）
    /// ② 音效不并入（服务不调用其他服务，编排方分别调）③ 防重播（`silenceUntilFrame`）住 View，不在本服务内。
    ///
    /// 加载口 <see cref="VfxAssetLoader"/> 由装配点注入（`AssetService`），本程序集因此不依赖 LiteGame/YooAsset。
    /// 到期回收**不依赖粒子回调**——按世界时钟推算，`Tick` 扫描。
    /// </summary>
    public sealed class VfxService : IVFXService, ITickable
    {
        private const string Tag = "VFX";

        /// <summary>拿不到粒子时长时的兜底生命周期（纯 Mesh/贴片特效）。</summary>
        private const float FallbackLifetime = 1.5f;

        private readonly VfxAssetLoader _loader;
        private readonly IWorldClock _clock;
        private readonly VfxCatalog _catalog;
        private readonly VfxBudget _budget;
        private readonly Transform _worldRoot;
        private readonly GameObjectPool _pool;

        private readonly VfxHandleTable _table = new VfxHandleTable();
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>(8);
        private readonly Dictionary<Transform, List<int>> _byAttach = new Dictionary<Transform, List<int>>(8);
        private readonly List<int> _expired = new List<int>(16);

        private int _nextId = 1;
        private int _created, _rejected, _skipped, _loading, _autoRecycled;

        /// <param name="loader">加载口（必需；装配点绑 AssetService，测试注入替身）。</param>
        /// <param name="clock">世界时钟（必需；时停冻结到期）。</param>
        /// <param name="catalog">名→地址；null = "命名即引用"默认根。</param>
        /// <param name="budget">预算/降级；null = 桌面默认。</param>
        /// <param name="worldRoot">`follow=false` 的落点容器；null = 自建 `[VfxWorld]`（DontDestroyOnLoad）。</param>
        /// <param name="poolRoot">池化停放容器；null = 池自建（DontDestroyOnLoad）。测试传入可避免编辑态触碰 DontDestroyOnLoad。</param>
        /// <param name="maxIdlePerPrefab">每 prefab 池深（防池膨胀）。</param>
        public VfxService(VfxAssetLoader loader, IWorldClock clock,
                          VfxCatalog catalog = null, VfxBudget budget = null,
                          Transform worldRoot = null, Transform poolRoot = null, int maxIdlePerPrefab = 16)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _catalog = catalog ?? new VfxCatalog();
            _budget = budget ?? VfxBudget.Default();
            _pool = new GameObjectPool(poolRoot, maxIdlePerPrefab);

            if (worldRoot != null)
            {
                _worldRoot = worldRoot;
            }
            else
            {
                _worldRoot = new GameObject("[VfxWorld]").transform;
                UnityEngine.Object.DontDestroyOnLoad(_worldRoot.gameObject);
            }

            Log.Info($"VFX 服务就绪（同屏上限 {_budget.MaxActive} / {_budget.Overflow}）", Tag);
        }

        // ---- IVFXService ----

        public VfxHandle Play(string name, Transform attach, bool follow, float scale = 1f)
        {
            var def = _catalog.Resolve(name);
            if (!def.IsValid)
            {
                Log.Warning($"VFX 名为空——忽略:{name}", Tag);
                return default;
            }

            if (_budget.IsCategorySkipped(def.Category))
            {
                _skipped++;
                return default;
            }

            if (_table.ActiveCount >= _budget.MaxActive)
            {
                if (_budget.Overflow == VfxOverflowPolicy.Reject)
                {
                    _rejected++;
                    Log.Warning($"VFX 超预算被拒:{name}（上限 {_budget.MaxActive}）", Tag);
                    return default;
                }
                var oldest = _table.Oldest();
                if (oldest != null)
                {
                    _autoRecycled++;
                    Stop(new VfxHandle(oldest.Id));
                }
            }

            var inst = new VfxInstance
            {
                Id = _nextId++,
                Def = def,
                Attach = attach,
                Follow = follow,
                Scale = scale <= 0f ? 1f : scale,
                ExpireAt = float.PositiveInfinity,
            };
            _table.Add(inst);                                     // 先登记后加载：竞态窗口从一开始就被表覆盖
            if (attach != null) Track(attach, inst.Id);

            if (_prefabs.TryGetValue(def.Location, out var prefab))
            {
                Materialize(inst, prefab);
            }
            else
            {
                LoadAndMaterializeAsync(inst).Forget();            // 禁原生协程：UniTask
            }

            return new VfxHandle(inst.Id);
        }

        public void Stop(VfxHandle handle)
        {
            if (!handle.IsValid) return;
            if (!_table.TryTake(handle.Id, out var inst)) return;  // 幂等：未登记 / 已回收
            Untrack(inst.Attach, handle.Id);

            if (inst.Go == null)
            {
                inst.Cancelled = true;                             // 加载在途：续体查标后丢弃，不实例化
                return;
            }

            _pool.Release(inst.Go);
            inst.Go = null;
        }

        public void StopAll(Transform attach)
        {
            if (attach == null) return;
            if (!_byAttach.TryGetValue(attach, out var ids)) return;

            var copy = ids.ToArray();                              // Stop 会改集合 → 先拷再遍历
            _byAttach.Remove(attach);
            for (int i = 0; i < copy.Length; i++) Stop(new VfxHandle(copy[i]));
        }

        // ---- ITickable ----

        /// <summary>到期扫描（不依赖粒子回调）：到点即归还池。</summary>
        public void Tick(float realDelta)
        {
            if (_table.ActiveCount == 0) return;

            _table.CollectExpired(_clock.Now, _expired);
            for (int i = 0; i < _expired.Count; i++) Stop(new VfxHandle(_expired[i]));
            _expired.Clear();
        }

        public string StatsName => "VFX";

        public void Snapshot(Dictionary<string, string> into)
        {
            into["活跃"] = _table.ActiveCount.ToString();
            into["池中"] = _pool.PooledTotal.ToString();
            into["新建"] = _created.ToString();
            into["超限回收"] = _autoRecycled.ToString();
            into["拒绝"] = _rejected.ToString();
            into["跳过"] = _skipped.ToString();
            into["加载中"] = _loading.ToString();
        }

        // ---- 内部 ----

        private async UniTaskVoid LoadAndMaterializeAsync(VfxInstance inst)
        {
            _loading++;
            try
            {
                var prefab = await _loader(inst.Def.Location, CancellationToken.None);

                if (inst.Cancelled) return;                        // 宿主已回收 → 丢弃（不实例化、无残留）

                if (prefab == null)
                {
                    Log.Error($"VFX 加载为空:{inst.Def.Location}", Tag);
                    _table.TryTake(inst.Id, out _);
                    return;
                }

                _prefabs[inst.Def.Location] = prefab;
                Materialize(inst, prefab);
            }
            catch (Exception ex)
            {
                Log.Error($"VFX 加载失败[{inst.Def.Location}]:{ex.Message}", Tag);
                _table.TryTake(inst.Id, out _);
            }
            finally
            {
                _loading--;
            }
        }

        private void Materialize(VfxInstance inst, GameObject prefab)
        {
            var parent = inst.Follow && inst.Attach != null ? inst.Attach : _worldRoot;

            var go = _pool.Get(inst.Def.Location, () =>
            {
                _created++;
                return UnityEngine.Object.Instantiate(prefab);
            }, parent);

            go.transform.localScale = Vector3.one * inst.Scale;
            inst.Go = go;
            inst.ExpireAt = _clock.Now + ResolveLifetime(go, inst.Def);
            PlayParticles(go);
        }

        /// <summary>生命周期 = max(粒子 duration + startLifetime)；无粒子则兜底时长。</summary>
        private static float ResolveLifetime(GameObject go, VfxDef def)
        {
            if (def.LifetimeOverride > 0f) return def.LifetimeOverride;

            float max = 0f;
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                float total = main.duration + main.startLifetime.constantMax;
                if (total > max) max = total;
            }
            return max > 0f ? max : FallbackLifetime;
        }

        private static void PlayParticles(GameObject go)
        {
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++) systems[i].Play(true);   // withChildren: true
        }

        private void Track(Transform attach, int id)
        {
            if (!_byAttach.TryGetValue(attach, out var list))
            {
                list = new List<int>(4);
                _byAttach[attach] = list;
            }
            list.Add(id);
        }

        private void Untrack(Transform attach, int id)
        {
            if (attach == null) return;
            if (!_byAttach.TryGetValue(attach, out var list)) return;
            list.Remove(id);
            if (list.Count == 0) _byAttach.Remove(attach);
        }
    }
}
