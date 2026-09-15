using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// IEEE 边界用例（《M7 实施指导》§2.6）：与基线文件**逐位**比较（非容差）。
    /// 基线文件（.NET 侧记录，入库）为 Unity 侧对账的参照；同一套输入在 Unity 侧须产出同样位型。
    /// </summary>
    public class IeeeBoundaryTests
    {
        [Fact]
        public void BaselineFile_Exists()
        {
            string path = BaselinePath();
            Assert.True(File.Exists(path), "缺少 IEEE 基线文件：" + path);
        }

        [Fact]
        public void BasicOps_AreBitExactAgainstBaseline()
        {
            string path = BaselinePath();
            Assert.True(File.Exists(path), "缺少 IEEE 基线文件：" + path);

            var expected = new List<string>();
            string[] raw = File.ReadAllLines(path);
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                expected.Add(line);
            }

            string[] actual = IeeeBaselineSpec.BuildLines();
            Assert.Equal(expected.Count, actual.Length);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i], actual[i]);
            }
        }

        [Fact]
        public void Operations_AreRepeatable()
        {
            Assert.Equal(IeeeBaselineSpec.BuildLines(), IeeeBaselineSpec.BuildLines());
        }

        [Fact]
        public void ChainChecksum_IsStable()
        {
            Assert.Equal(IeeeBaselineSpec.ChainChecksum(), IeeeBaselineSpec.ChainChecksum());
        }

        private static string BaselinePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "Baselines", "IeeeBaseline.txt");
        }
    }
}
