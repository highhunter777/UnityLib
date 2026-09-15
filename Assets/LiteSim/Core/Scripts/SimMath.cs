using System;

namespace LiteSim
{
    /// <summary>
    /// Sim 数值基础件（LiteSim.Core，零依赖纯 C#）。
    ///
    /// 单位约定（《M7 实施指导》§1-9）：长度 = 米(m)，时间 = 秒(s)，角度 = 弧度(rad)，
    /// y 轴 = 2.5D 向上（《状态同步实施方案》§3.5）；逻辑帧步长 <see cref="Dt"/> = 1/60（与 SimConfig 同源）。
    ///
    /// IEEE 边界纪律（《状态同步实施方案》§2.1；由 Editor 纪律扫描 R1~R3 执行）：
    ///   1. 禁超越函数（Sin/Cos/Tan/Atan2/Exp/Log/Pow）——一律走 SimTrig 查表；
    ///   2. 禁 FMA 显式写法（FusedMultiplyAdd）；
    ///   3. float 禁精度依赖比较（==/!=），一律用 <see cref="NearlyEqual"/>（绝对容差 1e-6）。
    /// 只使用 IEEE 基本运算（+ - * / 与 sqrt），保证 .NET 与 Unity 逐位一致。
    /// </summary>
    public static class SimMath
    {
        /// <summary>逻辑帧步长（秒）。</summary>
        public const float Dt = 1f / 60f;

        /// <summary>浮点比较绝对容差。</summary>
        public const float Tolerance = 1e-6f;

        /// <summary>绝对容差比较：|a - b| &lt;= <see cref="Tolerance"/>。</summary>
        public static bool NearlyEqual(float a, float b)
        {
            return Abs(a - b) <= Tolerance;
        }

        public static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static float Clamp01(float v)
        {
            return Clamp(v, 0f, 1f);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        public static float Abs(float v)
        {
            return v < 0f ? -v : v;
        }

        public static int Abs(int v)
        {
            return v < 0 ? -v : v;
        }

        public static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        /// <summary>
        /// 平方根——sqrt 属 IEEE 基本运算，跨运行时逐位确定（§2.1 边界表内）。
        /// 统一由此包装，业务代码不直接写 Math.Sqrt。
        /// </summary>
        public static float Sqrt(float v)
        {
            return (float)Math.Sqrt(v);
        }
    }
}
