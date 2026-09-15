using System.Collections.Generic;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>纪律扫描规则集自测（《M7 实施指导》§2.5 用例）：R1~R5 负例命中 + 正例/豁免不误报。</summary>
    public class LintRuleTests
    {
        [Fact]
        public void Lint_R1_超越函数被命中()
        {
            Assert.Equal(1, Count("var v = Math.Sin(x);", SimLintRule.R1Transcendental));
            Assert.Equal(1, Count("var v = MathF.Cos(x);", SimLintRule.R1Transcendental));
            Assert.Equal(1, Count("var v = Math.Atan2(y, x);", SimLintRule.R1Transcendental));
            Assert.Equal(1, Count("var v = Math.Pow(a, 2.0);", SimLintRule.R1Transcendental));
        }

        [Fact]
        public void Lint_R1_允许的基础运算不误报()
        {
            Assert.Equal(0, Count("var v = Math.Sqrt(x);", SimLintRule.R1Transcendental));
            Assert.Equal(0, Count("var v = Math.Abs(x);", SimLintRule.R1Transcendental));
            Assert.Equal(0, Count("var v = Math.Floor(x);", SimLintRule.R1Transcendental));
        }

        [Fact]
        public void Lint_R2_FMA写法被命中()
        {
            Assert.Equal(1, Count("var v = MathF.FusedMultiplyAdd(a, b, c);", SimLintRule.R2Fma));
            Assert.Equal(1, Count("var v = System.Math.FusedMultiplyAdd(a, b, c);", SimLintRule.R2Fma));
        }

        [Fact]
        public void Lint_R3_浮点等值比较被命中()
        {
            Assert.Equal(1, Count("if (a == b) { }", SimLintRule.R3FloatEquality));
            Assert.Equal(1, Count("if (a != b) { }", SimLintRule.R3FloatEquality));
        }

        [Fact]
        public void Lint_R3_零常量与NearlyEqual与关系运算豁免()
        {
            Assert.Equal(0, Count("if (a == 0f) { }", SimLintRule.R3FloatEquality));
            Assert.Equal(0, Count("if (a != 0) { }", SimLintRule.R3FloatEquality));
            Assert.Equal(0, Count("if (SimMath.NearlyEqual(a, b)) { }", SimLintRule.R3FloatEquality));
            Assert.Equal(0, Count("if (a <= b) { }", SimLintRule.R3FloatEquality));
            Assert.Equal(0, Count("if (a >= b) { }", SimLintRule.R3FloatEquality));
        }

        [Fact]
        public void Lint_豁免注释_只抑制指定规则()
        {
            Assert.Equal(0, Count("if (a == b) { } // lint-allow R3", SimLintRule.R3FloatEquality));
            // 只豁免 R3，R1 仍应命中
            Assert.Equal(1, Count("var v = Math.Sin(x); if (a == b) { } // lint-allow R3", SimLintRule.R1Transcendental));
        }

        [Fact]
        public void Lint_豁免注释_无规则号则全部抑制()
        {
            Assert.Equal(0, Count("var v = Math.Sin(x); // lint-allow", SimLintRule.R1Transcendental));
        }

        [Fact]
        public void Lint_R4_受开关门控()
        {
            Assert.Equal(0, Count("using System.Linq;", SimLintRule.R4DeterminismContainer, false));
            Assert.Equal(1, Count("using System.Linq;", SimLintRule.R4DeterminismContainer, true));
            Assert.Equal(1, Count("var q = xs.OrderBy(x => x);", SimLintRule.R4DeterminismContainer, true));
        }

        [Fact]
        public void Lint_R5_裸UNITY_EDITOR被命中()
        {
            Assert.Equal(1, Count("#if UNITY_EDITOR\n", SimLintRule.R5BareUnityEditor));
            Assert.Equal(1, Count("#if UNITY_EDITOR || DEVELOPMENT_BUILD\n", SimLintRule.R5BareUnityEditor));
            Assert.Equal(1, Count("#elif UNITY_EDITOR\n", SimLintRule.R5BareUnityEditor));
        }

        [Fact]
        public void Lint_R5_三宏并集与无关指令豁免()
        {
            Assert.Equal(0, Count("#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG\n", SimLintRule.R5BareUnityEditor));
            Assert.Equal(0, Count("#if LITEFRAMEWORK_DEBUG\n", SimLintRule.R5BareUnityEditor));
            Assert.Equal(0, Count("#if UNITY_STANDALONE || UNITY_ANDROID\n", SimLintRule.R5BareUnityEditor));
            // 注释里的提及、非指令行的普通提及都不算
            Assert.Equal(0, Count("// #if UNITY_EDITOR\n", SimLintRule.R5BareUnityEditor));
            Assert.Equal(0, Count("var v = UNITY_EDITOR;\n", SimLintRule.R5BareUnityEditor));
        }

        [Fact]
        public void Lint_R5_豁免注释生效()
        {
            Assert.Equal(0, Count("#if UNITY_EDITOR // lint-allow R5\n", SimLintRule.R5BareUnityEditor));
        }

        [Fact]
        public void Lint_注释内容不参与匹配()
        {
            Assert.Equal(0, Count("// Math.Sin(x) 仅注释提及", SimLintRule.R1Transcendental));
            Assert.Equal(0, Count("/// 禁 FusedMultiplyAdd 注释", SimLintRule.R2Fma));
            Assert.Equal(0, Count("// if (a == b) 注释里的比较", SimLintRule.R3FloatEquality));
            Assert.Equal(0, Count("/* Math.Sin(x) */ var y = 1;", SimLintRule.R1Transcendental));
            Assert.Equal(0, Count("/* FusedMultiplyAdd\n*/ var x = 1;", SimLintRule.R2Fma));
            // 代码上的真违规 + 行尾注释：仍应命中
            Assert.Equal(1, Count("var v = Math.Sin(x); // 注释", SimLintRule.R1Transcendental));
        }

        [Fact]
        public void Lint_违规输出格式为文件行列规则代码()
        {
            List<SimLintViolation> v = SimDeterminismLint.ScanText("Foo.cs", "var x = Math.Sin(1f);");
            Assert.Single(v);
            Assert.Equal(SimLintRule.R1Transcendental, v[0].Rule);
            Assert.Equal(1, v[0].Line);
            Assert.Equal("Foo.cs:1:R1:var x = Math.Sin(1f);", v[0].ToString());
        }

        [Fact]
        public void Lint_默认排除_编辑器目录与生成物与自身()
        {
            Assert.True(SimDeterminismLint.IsExcluded("Assets/LiteSim/Core/Scripts/Editor/Foo.cs"));
            Assert.True(SimDeterminismLint.IsExcluded("Assets/LiteSim/Core/Scripts/SimTrigTables.cs"));
            Assert.True(SimDeterminismLint.IsExcluded("Assets/LiteSim/Core/Scripts/SimDeterminismLint.cs"));
            Assert.False(SimDeterminismLint.IsExcluded("Assets/LiteSim/Core/Scripts/SimTrig.cs"));
        }

        private static int Count(string text, SimLintRule rule, bool enableR4 = false)
        {
            List<SimLintViolation> v = SimDeterminismLint.ScanText("test.cs", text, enableR4);
            int count = 0;
            for (int i = 0; i < v.Count; i++)
            {
                if (v[i].Rule == rule) count++;
            }
            return count;
        }
    }
}
