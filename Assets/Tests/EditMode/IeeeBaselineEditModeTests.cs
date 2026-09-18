using LiteSim.Editor;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// Unity 侧 IEEE 基线对账（《测试开发方案》§7.2 缺口 b / §9 验收②③；《M7 实施指导》§2.6）。
    ///
    /// 断言"**Unity 运行时与 .NET 的浮点路径逐位一致**"——这是跨运行时一致性的直接证据，
    /// 也是 L2 门禁里"数值层"那一环（v3 定位下它不再是正确性前提，但决定预测/和解质量）。
    /// 探针与基线均为单一来源：<see cref="IeeeProbe"/>（LiteSim 内）+ `Tests/…/Baselines/IeeeBaseline.txt`（.NET 记录）。
    /// </summary>
    public sealed class IeeeBaselineEditModeTests
    {
        [Test]
        public void IEEE基线_Unity侧与NET侧逐位一致()
        {
            var (ok, report) = IeeeBaselineChecker.Verify();
            Assert.IsTrue(ok, report);
        }

        /// <summary>
        /// 运算链（10k 步）跨运行时**不一致已被实测**（Unity/Mono vs .NET 8：3683559206 vs 2896875742，
        /// 116 条逐值行全同）。此用例只**记录**两侧值与量级，不判失败——它是 M10 和解率的输入数据，
        /// 定案（接受底噪 / 自研 sqrt 消除源头）见《M10实施指导》§7 与实施记录。
        /// </summary>
        [Test]
        public void IEEE运算链_记录跨运行时差异_不判失败()
        {
            var (expected, unity, same) = IeeeBaselineChecker.ChainComparison();
            TestContext.WriteLine($"[Chain] .NET={expected} Unity={unity} same={same}");
            if (!same)
            {
                uint delta = unity > expected ? unity - expected : expected - unity;
                TestContext.WriteLine(
                    $"[Chain] 跨运行时 ulp 底噪已记录：Δ={delta}（10k 步累积；逐值行全同）" +
                    "——按 v3 定位属预测/和解质量项，M10 对跑实测后定案。");
            }
            Assert.Pass($"Chain 记录完成（same={same}）");
        }
    }
}
