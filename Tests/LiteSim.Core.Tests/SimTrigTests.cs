using System;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>SimTrig 契约（《M7 实施指导》§2.2 用例）：精度、象限边界、Atan2 四象限、Tan、重现性。</summary>
    public class SimTrigTests
    {
        private const float SinTol = 1e-5f;
        private const float AtanTol = 1e-4f;

        [Fact]
        public void SimTrig_正弦_与MathSin采样1万点误差在容差内()
        {
            float maxErr = 0f;
            const int n = 10000;
            for (int i = 0; i < n; i++)
            {
                float x = -8f + 16f * i / (n - 1); // [-8, 8]
                float err = Math.Abs(SimTrig.Sin(x) - (float)Math.Sin(x));
                if (err > maxErr) maxErr = err;
            }
            Assert.True(maxErr <= SinTol, "Sin 最大误差 " + maxErr + " > " + SinTol);
        }

        [Fact]
        public void SimTrig_余弦_与MathCos采样1万点误差在容差内()
        {
            float maxErr = 0f;
            const int n = 10000;
            for (int i = 0; i < n; i++)
            {
                float x = -8f + 16f * i / (n - 1);
                float err = Math.Abs(SimTrig.Cos(x) - (float)Math.Cos(x));
                if (err > maxErr) maxErr = err;
            }
            Assert.True(maxErr <= SinTol, "Cos 最大误差 " + maxErr + " > " + SinTol);
        }

        [Fact]
        public void SimTrig_正弦_象限边界值正确()
        {
            Close(0f, SimTrig.Sin(0f), 1e-6f, "sin(0)");
            Close(1f, SimTrig.Sin(SimTrig.HalfPi), 1e-6f, "sin(π/2)");
            Close(0f, SimTrig.Sin(SimTrig.Pi), 1e-6f, "sin(π)");
            Close(-1f, SimTrig.Sin(SimTrig.Pi + SimTrig.HalfPi), 1e-6f, "sin(3π/2)");
            Close(0f, SimTrig.Sin(SimTrig.TwoPi), 1e-6f, "sin(2π)");
            Close(-1f, SimTrig.Sin(-SimTrig.HalfPi), 1e-6f, "sin(-π/2)");
            Close(0f, SimTrig.Sin(-SimTrig.Pi), 1e-5f, "sin(-π)");
        }

        [Fact]
        public void SimTrig_余弦_象限边界值正确()
        {
            Close(1f, SimTrig.Cos(0f), 1e-6f, "cos(0)");
            Close(0f, SimTrig.Cos(SimTrig.HalfPi), 1e-6f, "cos(π/2)");
            Close(-1f, SimTrig.Cos(SimTrig.Pi), 1e-6f, "cos(π)");
            Close(0f, SimTrig.Cos(SimTrig.Pi + SimTrig.HalfPi), 1e-6f, "cos(3π/2)");
        }

        [Fact]
        public void SimTrig_正弦_大角输入有界且非NaN()
        {
            float v = SimTrig.Sin(1e5f);
            Assert.False(float.IsNaN(v), "sin(1e5) 不应为 NaN");
            Assert.False(float.IsInfinity(v), "sin(1e5) 不应为 Infinity");
            Assert.True(v >= -1f && v <= 1f, "sin(1e5)=" + v + " 越界");
        }

        [Fact]
        public void SimTrig_正弦_非有限输入返回零()
        {
            Assert.Equal(0f, SimTrig.Sin(float.NaN));
            Assert.Equal(0f, SimTrig.Sin(float.PositiveInfinity));
            Assert.Equal(0f, SimTrig.Sin(float.NegativeInfinity));
        }

        [Fact]
        public void SimTrig_Atan2_四象限与轴上值正确()
        {
            Close(0f, SimTrig.Atan2(0f, 1f), AtanTol, "atan2(0,1)");
            Close(0f, SimTrig.Atan2(0f, 0f), AtanTol, "atan2(0,0)");
            Close(SimTrig.HalfPi, SimTrig.Atan2(1f, 0f), AtanTol, "atan2(1,0)");
            Close(-SimTrig.HalfPi, SimTrig.Atan2(-1f, 0f), AtanTol, "atan2(-1,0)");
            Close(SimTrig.Pi, SimTrig.Atan2(0f, -1f), AtanTol, "atan2(0,-1)");
            Close(SimTrig.QuarterPi, SimTrig.Atan2(1f, 1f), AtanTol, "atan2(1,1)");
            Close(-SimTrig.QuarterPi, SimTrig.Atan2(-1f, 1f), AtanTol, "atan2(-1,1)");
            Close(SimTrig.Pi - SimTrig.QuarterPi, SimTrig.Atan2(1f, -1f), AtanTol, "atan2(1,-1)");
            Close(-(SimTrig.Pi - SimTrig.QuarterPi), SimTrig.Atan2(-1f, -1f), AtanTol, "atan2(-1,-1)");
        }

        [Fact]
        public void SimTrig_Atan2_与MathAtan2扫描误差在容差内()
        {
            float maxErr = 0f;
            const int n = 2000;
            for (int i = 0; i < n; i++)
            {
                float x = -1f + 2f * i / (n - 1);
                for (int j = 0; j < 5; j++)
                {
                    float y = -1f + 2f * j / 4f;
                    if (x == 0f && y == 0f) continue;
                    float err = Math.Abs(SimTrig.Atan2(y, x) - (float)Math.Atan2(y, x));
                    if (err > maxErr) maxErr = err;
                }
            }
            Assert.True(maxErr <= AtanTol, "Atan2 最大误差 " + maxErr + " > " + AtanTol);
        }

        [Fact]
        public void SimTrig_Atan2_与SinCos互逆误差在容差内()
        {
            float maxErr = 0f;
            for (int i = 0; i < 360; i++)
            {
                float rad = (float)(i * Math.PI / 180.0);
                float a = SimTrig.Atan2(SimTrig.Sin(rad), SimTrig.Cos(rad));
                // Atan2 值域 (-π, π]，按 2π 周期归一后再比。
                float diff = a - rad;
                if (diff > SimTrig.Pi) diff -= SimTrig.TwoPi;
                if (diff < -SimTrig.Pi) diff += SimTrig.TwoPi;
                float err = Math.Abs(diff);
                if (err > maxErr) maxErr = err;
            }
            Assert.True(maxErr <= AtanTol, "Atan2(sin,cos) 最大误差 " + maxErr);
        }

        [Fact]
        public void SimTrig_正切_接近半π时大而有限()
        {
            float t = SimTrig.Tan(SimTrig.HalfPi);
            Assert.False(float.IsNaN(t), "tan(π/2) 不应为 NaN");
            Assert.False(float.IsInfinity(t), "tan(π/2) 不应为 Infinity");
            Assert.True(Math.Abs(t) >= 1e5f, "tan(π/2)=" + t + " 应很大");
        }

        [Fact]
        public void SimTrig_正切_基本值正确()
        {
            Close(0f, SimTrig.Tan(0f), 1e-6f, "tan(0)");
            Close(1f, SimTrig.Tan(SimTrig.QuarterPi), 1e-4f, "tan(π/4)");
        }

        [Fact]
        public void SimTrig_同输入重复调用_结果逐位一致()
        {
            for (int i = 0; i < 1000; i++)
            {
                float x = -3f + 0.006f * i;
                Assert.Equal(SimTrig.Sin(x), SimTrig.Sin(x));
                Assert.Equal(SimTrig.Cos(x), SimTrig.Cos(x));
                Assert.Equal(SimTrig.Atan2(x, 1f - x), SimTrig.Atan2(x, 1f - x));
            }
        }

        private static void Close(float expected, float actual, float tol, string msg)
        {
            Assert.True(Math.Abs(expected - actual) <= tol,
                msg + " expected=" + expected + " actual=" + actual + " tol=" + tol);
        }
    }
}
