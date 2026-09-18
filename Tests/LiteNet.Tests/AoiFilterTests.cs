using System.Collections.Generic;
using LiteNet.Protocol;
using LiteSim;
using Xunit;

namespace LiteNet.Tests
{
    /// <summary>
    /// AOI 过滤用例（《M10实施指导》决策 13 + §3 组"AOI 开/关一致"）。
    /// 语义（见 <see cref="AoiFilter"/>）：网格**整格可见**——格边界处可能多发半格，但**永不漏发**；
    /// 验收口径 = ① 集合是过滤子集的包含关系；② 判定/回滚不受 AOI 影响；③ 自己必在可见集内。
    ///
    /// 注意：可见集合的**排序**取决于实体占用的槽位（网格遍历序按格号，格内按生成序），
    /// 断言一律用集合成员关系（Contains），不依赖多元素的顺序（顺序由 <see cref="AoiFilter"/> 内部保证槽位升序）。
    /// </summary>
    public sealed class AoiFilterTests
    {
        /// <summary>把槽位按位置摆放（**显式给 Id**，不与生成序耦合；槽位号 = 传入顺序）。</summary>
        private static SimWorldState At(params (float x, float z)[] positions)
        {
            var s = new SimWorldState();
            foreach (var (x, z) in positions)
                s.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(x, 0f, z) }, out _);
            return s;
        }

        [Fact]
        public void 关闭AOI_全图可见()
        {
            var s = At((0f, 0f), (200f, 200f), (-300f, 15f));
            var visible = new List<int>();
            var aoi = new AoiFilter();

            aoi.CollectVisible(in s, SimVector3.Zero, 0f, visible);

            Assert.Equal(s.AliveCount(), visible.Count);
        }

        [Fact]
        public void 开启AOI_裁剪远方且视点自身必在()
        {
            var s = At((0f, 0f), (500f, 500f));                 // 视点（0,0）/ 远邻（500,500）
            var visible = new List<int>();
            var aoi = new AoiFilter();

            aoi.CollectVisible(in s, SimVector3.Zero, SimConfig.AoiRadius, visible);

            Assert.Contains(0, visible);                        // 视点自身（不漏发）
            Assert.DoesNotContain(1, visible);                  // 500m 外（远超过 30m 半径的格邻域）
            Assert.True(visible.Count < s.AliveCount());
        }

        [Fact]
        public void 格边界处_可达性连续_不因取整产生空洞()
        {
            // 视点在格边界 x=10 上：左邻格（x=5）与右邻格（x=15，同格内的另一点）都必须可达
            var s = At((10f, 0f), (5f, 0f), (15f, 0f), (0f, 0f), (20f, 0f));
            var visible = new List<int>();
            var aoi = new AoiFilter();

            aoi.CollectVisible(in s, new SimVector3(10f, 0f, 0f), SimConfig.AoiRadius, visible);

            for (int i = 0; i < s.AliveCount(); i++)
                Assert.True(visible.Contains(i), $"槽位 {i} 在半径内却被漏发（格边界取整产生空洞）");
        }

        [Fact]
        public void 负坐标侧_同样连续_floor取整不是截断()
        {
            var s = At((-30f, 0f), (-25f, 0f), (-35f, 0f));
            var visible = new List<int>();
            var aoi = new AoiFilter();

            aoi.CollectVisible(in s, new SimVector3(-30f, 0f, 0f), SimConfig.AoiRadius, visible);

            Assert.Equal(3, visible.Count);   // 全部在 30m 邻域内（截断取整会把 -35/-25 分到错误格）
        }

        [Fact]
        public void AOI只影响广播_判定与回滚结果不受影响()
        {
            // 两个世界：同一输入序列、同一初始状态——一个"开着 AOI 广播"，一个"关着广播"
            // （判定只看 SimStep，AOI 是广播侧过滤，二者不应有任何耦合）
            var map = new SimMapData { GroundY = 0f, HalfWidth = 100f, HalfDepth = 100f };
            map.SpawnPoints[0] = new SimVector3(0f, 0f, 0f);
            map.SpawnPoints[1] = new SimVector3(60f, 0f, 0f);
            map.SpawnPointCount = 2;

            SimWorldState Build()
            {
                var s = new SimWorldState { RngState = 42UL };
                s.Spawn(new EntitySlot { Hp = 100, Pos = map.SpawnPoints[0] }, out _);
                s.Spawn(new EntitySlot { Hp = 100, Pos = map.SpawnPoints[1] }, out _);
                return s;
            }

            var withAoi = Build();
            var withoutAoi = Build();
            var differAoi = new SnapshotDiffer();
            var differFull = new SnapshotDiffer();

            var inputs = new SimInputFrame[2];
            for (int i = 0; i < 12; i++)
            {
                inputs[0] = new SimInputFrame { EntityId = withAoi.Entities[0].Id, MoveX = 0.5f, AimX = 1f };
                inputs[1] = new SimInputFrame { EntityId = withAoi.Entities[1].Id, MoveX = -0.5f, AimX = -1f };
                SimStep.Step(withAoi, map, inputs);

                inputs[0] = new SimInputFrame { EntityId = withoutAoi.Entities[0].Id, MoveX = 0.5f, AimX = 1f };
                inputs[1] = new SimInputFrame { EntityId = withoutAoi.Entities[1].Id, MoveX = -0.5f, AimX = -1f };
                SimStep.Step(withoutAoi, map, inputs);

                differAoi.Build(withAoi.Frame, withAoi, 0, withAoi.Entities[0].Pos, SimConfig.AoiRadius, false);
                differFull.Build(withoutAoi.Frame, withoutAoi, 0, SimVector3.Zero, 0f, false);
            }

            // 判定结果逐位一致（AOI 只裁剪广播内容，不改任何一个逻辑字段）。
            // 用不覆盖 Frame 的变体：两个世界各自推进帧号，比含 Frame 的 checksum 会永远不等（实测踩过）
            Assert.Equal(SimChecksum.ComputeStateChecksum(withoutAoi), SimChecksum.ComputeStateChecksum(withAoi));
        }
    }
}
