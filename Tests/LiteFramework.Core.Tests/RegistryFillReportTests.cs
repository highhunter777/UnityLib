using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    // RegistryFillReport（设计方案 §3.4 降级细则）：整批统一判定 + 同因去重
    public sealed class RegistryFillReportTests   // 纯数据件，无静态状态
    {
        [Fact]
        public void Report_初始_无失败()
        {
            var report = new RegistryFillReport();
            Assert.False(report.HasFailures);
            Assert.Equal(0, report.Total);
            Assert.Equal(0, report.Filled);
            Assert.Equal(0, report.Failed);
            Assert.Empty(report.Failures);
        }

        [Fact]
        public void Report_RecordFailure_累计并保留键类别原因()
        {
            var report = new RegistryFillReport();
            report.RecordFailure("UI.UIMain", "UI", "未注册(配置漏配):预载缓存未命中");
            Assert.True(report.HasFailures);
            Assert.Equal(1, report.Failed);
            var (key, kind, reason) = report.Failures[0];
            Assert.Equal("UI.UIMain", key);
            Assert.Equal("UI", kind);
            Assert.Contains("未注册", reason);
        }

        [Fact]
        public void Report_同键同类_去重只记第一条()
        {
            var report = new RegistryFillReport();
            report.RecordFailure("UI.X", "UI", "第一次");
            report.RecordFailure("UI.X", "UI", "第二次");
            Assert.Equal(1, report.Failed);
            Assert.Single(report.Failures);
            Assert.Contains("第一次", report.Failures[0].reason);
        }

        [Fact]
        public void Report_同键不同类_不去重()
        {
            var report = new RegistryFillReport();
            report.RecordFailure("Strat.A", "Strategies", "第一次");
            report.RecordFailure("Strat.A", "UI", "不同类别");
            Assert.Equal(2, report.Failed);
            Assert.Equal(2, report.Failures.Count);
        }
    }
}
