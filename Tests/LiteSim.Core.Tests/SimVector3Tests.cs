using System;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>SimVector3 契约（《M7 实施指导》§2.3 用例）。</summary>
    public class SimVector3Tests
    {
        private const float T = 1e-6f;

        [Fact]
        public void Operators()
        {
            var a = new SimVector3(1f, 2f, 3f);
            var b = new SimVector3(4f, -5f, 6f);

            VecClose(5f, -3f, 9f, a + b, "a+b");
            VecClose(-3f, 7f, -3f, a - b, "a-b");
            VecClose(-1f, -2f, -3f, -a, "-a");
            VecClose(2f, 4f, 6f, a * 2f, "a*2");
            VecClose(2f, 4f, 6f, 2f * a, "2*a");
            VecClose(0.5f, 1f, 1.5f, a / 2f, "a/2");
        }

        [Fact]
        public void DotAndCross()
        {
            var a = new SimVector3(1f, 2f, 3f);
            var b = new SimVector3(4f, -5f, 6f);

            Close(12f, SimVector3.Dot(a, b), T, "dot");
            VecClose(27f, 6f, -13f, SimVector3.Cross(a, b), "cross");
        }

        [Fact]
        public void Length_And_Distance()
        {
            Close(5f, new SimVector3(3f, 4f, 0f).Length, 1e-5f, "length(3,4,0)");
            Close(25f, new SimVector3(3f, 4f, 0f).LengthSquared, T, "lengthSq");
            Close(5f, SimVector3.Distance(new SimVector3(0f, 0f, 0f), new SimVector3(3f, 4f, 0f)), 1e-5f, "distance");
        }

        [Fact]
        public void Normalized_UnitAndZeroSafe()
        {
            VecClose(0.6f, 0.8f, 0f, new SimVector3(3f, 4f, 0f).Normalized(), "normalized(3,4,0)");
            VecClose(0f, 0f, 0f, SimVector3.Zero.Normalized(), "normalized(zero)");
            // 极小长度 → 零向量，禁 NaN 扩散
            VecClose(0f, 0f, 0f, new SimVector3(1e-9f, 0f, 0f).Normalized(), "normalized(tiny)");
        }

        [Fact]
        public void Struct_DoesNotBoxInOperatorChain()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var sum = SimVector3.Zero;
            for (int i = 0; i < 100000; i++)
            {
                sum = sum + new SimVector3(1f, 2f, 3f) * 0.5f;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.True(sum.X != 0f); // 防止被优化掉
            Assert.Equal(before, after); // 值类型运算符链零分配（无装箱）
        }

        private static void VecClose(float x, float y, float z, SimVector3 v, string msg)
        {
            Close(x, v.X, T, msg + ".X");
            Close(y, v.Y, T, msg + ".Y");
            Close(z, v.Z, T, msg + ".Z");
        }

        private static void Close(float expected, float actual, float tol, string msg)
        {
            Assert.True(Math.Abs(expected - actual) <= tol,
                msg + " expected=" + expected + " actual=" + actual);
        }
    }
}
