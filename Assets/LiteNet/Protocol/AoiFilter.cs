using System;
using System.Collections.Generic;
using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// AOI 网格过滤（《M10实施指导》决策 13；《状态同步实施方案》§4.5-7 / §3.8）：
    /// **只影响广播裁剪，不影响判定与回滚重放**——服务器权威 Sim 永远算全量，AOI 只决定"这一帧给谁发哪些实体"。
    ///
    /// 语义（决策文本："网格过滤旋钮，只影响广播"）：实体按其**所在格**归属某视点——
    /// 视点格 ± <c>ceil(半径/格边长)</c> 邻域内的格全部可见。这是"整格可见"口径，不是精确圆：
    /// 格边界处可能多发半格（自身位置一定发），但**永远不会漏**——广播少发一个实体 = 客户端静默分叉，是唯一不可接受的错。
    /// 验收口径"开/关 AOI 结果一致" = 可见集合是过滤子集的包含关系 + **判定与和解不受影响**（后者才是红线）。
    ///
    /// 关闭语义（<paramref name="radius"/> ≤ 0）：不过滤，全部活体可发——等价性验收的对照组。
    ///
    /// **实例持有网格**（2026-09-18 批③ 修正）：早期实现用静态缓存，只按帧号失效 → 多房间/多测试世界
    /// 在同一帧号上互相串味（实测：并发用例互相污染，可见集合多出别的世界的实体）。
    /// 现在网格归实例所有（每个 <see cref="SnapshotDiffer"/>/房间一份），并按 **(世界实例, 帧号)** 键失效。
    /// </summary>
    public sealed class AoiFilter
    {
        private const int GridDim = 128;            // 128×128 格（格边长 10m → 覆盖 ±640m，远超 MVP 地图 ±50m）
        private const int GridOrigin = -64;         // 原点格号（含负数侧）

        private readonly List<int>[] _buckets = CreateBuckets();
        private readonly List<int> _outside = new List<int>();   // 兜底：理论上不可达（MovementSystem 已钳制世界边界）
        private SimWorldState _cachedState;
        private int _cachedFrame = int.MinValue;

        private static List<int>[] CreateBuckets()
        {
            var buckets = new List<int>[GridDim * GridDim];
            for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<int>(4);
            return buckets;
        }

        /// <summary>帧级网格缓存（同帧重复调用零成本；换世界或换帧自动重建）。</summary>
        public void EnsureGrid(in SimWorldState s)
        {
            if (ReferenceEquals(_cachedState, s) && _cachedFrame == s.Frame) return;
            _cachedState = s;
            _cachedFrame = s.Frame;

            for (int i = 0; i < _buckets.Length; i++) _buckets[i].Clear();
            _outside.Clear();

            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i)) continue;
                int key = KeyOf(s.Entities[i].Pos);
                if (key < 0) _outside.Add(i);
                else _buckets[key].Add(i);
            }
        }

        /// <summary>
        /// 收集视点可见的活体槽位（写入 <paramref name="result"/>，调用方负责 Clear）。
        /// <paramref name="radius"/> ≤ 0 = 关闭 AOI（全部活体）。集合按槽位升序（确定性广播序）。
        /// </summary>
        public void CollectVisible(in SimWorldState s, SimVector3 viewerPos, float radius, List<int> result)
        {
            if (radius <= 0f)
            {
                for (int i = 0; i < SimConfig.MaxEntities; i++)
                    if (s.IsAlive(i)) result.Add(i);
                return;
            }

            EnsureGrid(in s);

            float cell = SimConfig.AoiCellSize;
            int cx = CellOf(viewerPos.X, cell);
            int cz = CellOf(viewerPos.Z, cell);
            int span = (int)Math.Ceiling(radius / cell);

            for (int gz = cz - span; gz <= cz + span; gz++)
            {
                if (gz < GridOrigin || gz >= GridOrigin + GridDim) continue;
                for (int gx = cx - span; gx <= cx + span; gx++)
                {
                    if (gx < GridOrigin || gx >= GridOrigin + GridDim) continue;
                    List<int> bucket = _buckets[(gz - GridOrigin) * GridDim + (gx - GridOrigin)];
                    for (int k = 0; k < bucket.Count; k++) result.Add(bucket[k]);
                }
            }

            result.Sort();   // 槽位升序（网格遍历序不是槽位序；广播序必须确定）
        }

        /// <summary>视点所在格号（Python 式 floor 取整——负数侧连续，格边界不产生空洞）。</summary>
        public static int CellOf(float v, float cell) => (int)Math.Floor(v / cell);

        private static int KeyOf(SimVector3 pos)
        {
            float cell = SimConfig.AoiCellSize;
            int gx = CellOf(pos.X, cell) - GridOrigin;
            int gz = CellOf(pos.Z, cell) - GridOrigin;
            if (gx < 0 || gx >= GridDim || gz < 0 || gz >= GridDim) return -1;
            return gz * GridDim + gx;
        }
    }
}
