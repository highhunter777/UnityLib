using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LiteFramework;
using LiteSim.View;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// VFX 服务验收（《VFX服务实施指导》§3 /《动作与特效设计》§2.6）。
    ///
    /// 全替身、**零素材依赖**（`Assets/FX/` 下尚无项目自制的 `fx_*` prefab）：
    /// 假时钟可确定性推进 `Now`；同步 loader（`UniTask.FromResult`）让加载内联完成 → 无需 PlayerLoop；
    /// 假 prefab 用 `new GameObject(..., typeof(ParticleSystem))` 造，池照常 Instantiate。
    /// </summary>
    public sealed class VfxServiceEditModeTests
    {
        private sealed class TestClock : IWorldClock
        {
            public float NowValue;

            public float Now => NowValue;
            public float ScaledDelta => 0f;
            public float TimeScale { get; set; } = 1f;
            public bool Paused { get; set; }

            public string StatsName => "TestClock";
            public void Snapshot(Dictionary<string, string> into) { }
            public void Tick(float realDelta) { }
        }

        private sealed class Rig
        {
            public TestClock Clock;
            public GameObject Prefab;
            public GameObject World;
            public GameObject Pool;

            public VfxService Make(VfxAssetLoader loader = null, VfxCatalog catalog = null, VfxBudget budget = null)
                => new VfxService(loader ?? SyncLoader,
                                  Clock,
                                  catalog ?? new VfxCatalog(),
                                  budget ?? VfxBudget.Unlimited(),
                                  World.transform,
                                  Pool.transform);

            private UniTask<GameObject> SyncLoader(string loc, System.Threading.CancellationToken ct)
                => UniTask.FromResult(Prefab);

            public void Dispose()
            {
                Kill(World); Kill(Pool); Kill(Prefab);
            }
        }

        private static Rig NewRig()
        {
            var prefab = new GameObject("fx_test", typeof(ParticleSystem));
            var main = prefab.GetComponent<ParticleSystem>().main;
            main.duration = 1f;
            main.startLifetime = 1f;                 // 生命周期 = duration + startLifetime = 2s

            return new Rig
            {
                Clock = new TestClock(),
                Prefab = prefab,
                World = new GameObject("[vfx-world]"),
                Pool = new GameObject("[vfx-pool]"),
            };
        }

        private static void Kill(Object go)
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        private static string Snap(VfxService svc, string key)
        {
            var d = new Dictionary<string, string>();
            svc.Snapshot(d);
            return d[key];
        }

        // ---- API 跑通 ----

        [Test]
        public void VFX_Play_跟随挂点_实例挂到挂点下()
        {
            var rig = NewRig();
            var svc = rig.Make();
            var attach = new GameObject("host").transform;

            var h = svc.Play("fx_hit", attach, follow: true);

            Assert.IsTrue(h.IsValid);
            Assert.AreEqual(1, attach.childCount, "follow=true 应挂到挂点下");
            Assert.AreEqual("1", Snap(svc, "活跃"));

            svc.Stop(h);
            Assert.AreEqual("0", Snap(svc, "活跃"));

            Kill(attach.gameObject);
            rig.Dispose();
        }

        [Test]
        public void VFX_Play_不跟随_挂世界容器且应用缩放()
        {
            var rig = NewRig();
            var svc = rig.Make();

            var h = svc.Play("fx_aoe", null, follow: false, scale: 2.5f);

            Assert.IsTrue(h.IsValid);
            Assert.AreEqual(1, rig.World.transform.childCount, "follow=false 应落世界容器");
            Assert.AreEqual(2.5f, rig.World.transform.GetChild(0).localScale.x, 0.001f);

            svc.Stop(h);
            rig.Dispose();
        }

        [Test]
        public void VFX_句柄_单调递增且Stop幂等()
        {
            var rig = NewRig();
            var svc = rig.Make();

            var h1 = svc.Play("fx_a", null, false);
            var h2 = svc.Play("fx_b", null, false);

            Assert.Less(h1.Id, h2.Id, "句柄单调递增（永不复用）");

            svc.Stop(h1);
            Assert.DoesNotThrow(() => svc.Stop(h1), "重复 Stop 必须幂等");
            Assert.DoesNotThrow(() => svc.Stop(default), "无效句柄 Stop 静默 no-op");
            Assert.DoesNotThrow(() => svc.StopAll(null), "null 挂点 StopAll 静默 no-op");

            Assert.AreEqual("1", Snap(svc, "活跃"), "只停了 h1");

            svc.Stop(h2);
            rig.Dispose();
        }

        // ---- 池化 ----

        [Test]
        public void VFX_池化_同prefab回收后再播不新建实例()
        {
            var rig = NewRig();
            var svc = rig.Make();

            var h1 = svc.Play("fx_a", null, false);
            string createdAfterFirst = Snap(svc, "新建");
            svc.Stop(h1);

            var h2 = svc.Play("fx_a", null, false);

            Assert.AreEqual(createdAfterFirst, Snap(svc, "新建"), "同 prefab 应复用池中实例");
            Assert.AreEqual("1", Snap(svc, "活跃"));

            svc.Stop(h2);
            rig.Dispose();
        }

        // ---- 预算与降级 ----

        [Test]
        public void VFX_超预算_默认回收最旧()
        {
            var rig = NewRig();
            var svc = rig.Make(budget: new VfxBudget(2, VfxOverflowPolicy.RecycleOldest));

            var h1 = svc.Play("fx_a", null, false);
            svc.Play("fx_b", null, false);
            svc.Play("fx_c", null, false);

            Assert.AreEqual("2", Snap(svc, "活跃"), "同屏上限 2");
            Assert.AreEqual("1", Snap(svc, "超限回收"), "第三个触发一次最旧回收");

            svc.Stop(h1);                       // 已被回收 → 幂等
            Assert.AreEqual("2", Snap(svc, "活跃"));

            rig.Dispose();
        }

        [Test]
        public void VFX_超预算_拒绝模式_返回无效句柄并计数()
        {
            var rig = NewRig();
            var svc = rig.Make(budget: new VfxBudget(2, VfxOverflowPolicy.Reject));

            svc.Play("fx_a", null, false);
            svc.Play("fx_b", null, false);
            var h3 = svc.Play("fx_c", null, false);

            Assert.IsFalse(h3.IsValid, "拒绝模式返回无效句柄");
            Assert.AreEqual("2", Snap(svc, "活跃"));
            Assert.AreEqual("1", Snap(svc, "拒绝"));

            rig.Dispose();
        }

        [Test]
        public void VFX_低端_跳过重特效类别()
        {
            var rig = NewRig();
            var catalog = new VfxCatalog().Register(
                new VfxDef("fx_boom", VfxCategories.Heavy, "Assets/x.prefab"));
            var svc = rig.Make(catalog: catalog, budget: VfxBudget.LowEnd());

            var h = svc.Play("fx_boom", null, false);

            Assert.IsFalse(h.IsValid, "低端跳过 heavy 类别");
            Assert.AreEqual("1", Snap(svc, "跳过"));
            Assert.AreEqual("0", Snap(svc, "活跃"));

            rig.Dispose();
        }

        [Test]
        public void VFX_未登记名字_按命名即引用合成地址()
        {
            var catalog = new VfxCatalog();
            var def = catalog.Resolve("fx_hit");

            Assert.AreEqual("fx_hit", def.Name);
            Assert.AreEqual(VfxCatalog.DefaultRoot + "fx_hit.prefab", def.Location);
            Assert.IsFalse(catalog.Resolve("").IsValid, "空名无效");
        }

        // ---- 挂点清理（谁挂谁清）----

        [Test]
        public void VFX_StopAll_按挂点隔离_不影响其他挂点()
        {
            var rig = NewRig();
            var svc = rig.Make();
            var a = new GameObject("host-a").transform;
            var b = new GameObject("host-b").transform;

            svc.Play("fx_a", a, true);
            svc.Play("fx_b", a, true);
            svc.Play("fx_c", b, true);

            svc.StopAll(a);

            Assert.AreEqual("1", Snap(svc, "活跃"), "只清 a 上的两个");
            Assert.AreEqual(0, a.childCount);
            Assert.AreEqual(1, b.childCount);

            svc.StopAll(b);
            Kill(a.gameObject); Kill(b.gameObject);
            rig.Dispose();
        }

        // ---- 加载在途 × 宿主回收竞态（决策 10）----

        [Test]
        public void VFX_加载在途被宿主回收_不实例化且无残留()
        {
            var rig = NewRig();
            var tcs = new UniTaskCompletionSource<GameObject>();
            var svc = rig.Make(loader: (loc, ct) => tcs.Task);
            var attach = new GameObject("host").transform;

            var h = svc.Play("fx_slow", attach, true);
            Assert.IsTrue(h.IsValid);
            Assert.AreEqual("1", Snap(svc, "活跃"), "pending 实例也应登记");

            svc.StopAll(attach);                 // 宿主回收（此时还在加载）
            Assert.AreEqual("0", Snap(svc, "活跃"));

            tcs.TrySetResult(rig.Prefab);        // 加载完成 → 续体应丢弃

            Assert.AreEqual(0, attach.childCount, "被回收的在途特效不得实例化");
            Assert.AreEqual("0", Snap(svc, "活跃"), "不得复活");

            Kill(attach.gameObject);
            rig.Dispose();
        }

        // ---- 到期回收（不依赖粒子回调）----

        [Test]
        public void VFX_到期_Tick自动回收()
        {
            var rig = NewRig();
            var svc = rig.Make();

            svc.Play("fx_a", null, false);
            Assert.AreEqual("1", Snap(svc, "活跃"));

            rig.Clock.NowValue += 1f;
            svc.Tick(0.016f);
            Assert.AreEqual("1", Snap(svc, "活跃"), "1s < 生命周期 2s，不该回收");

            rig.Clock.NowValue += 2f;            // 累计 3s > 2s
            svc.Tick(0.016f);
            Assert.AreEqual("0", Snap(svc, "活跃"), "到期即归还池");

            rig.Dispose();
        }

        [Test]
        public void VFX_空名_返回无效句柄()
        {
            var rig = NewRig();
            var svc = rig.Make();

            Assert.IsFalse(svc.Play(null, null, false).IsValid);
            Assert.IsFalse(svc.Play("", null, false).IsValid);

            rig.Dispose();
        }
    }
}
