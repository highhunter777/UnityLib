using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteSim
{
    /// <summary>纪律扫描规则编号（《M7 实施指导》§2.5）。</summary>
    public enum SimLintRule
    {
        /// <summary>R1 禁超越函数（Math/MathF 的 Sin/Cos/Tan/Atan2/Exp/Log/Pow…）。</summary>
        R1Transcendental = 1,

        /// <summary>R2 禁 FMA 显式写法（FusedMultiplyAdd）。</summary>
        R2Fma = 2,

        /// <summary>R3 禁 float 精度比较（==/!=）。</summary>
        R3FloatEquality = 3,

        /// <summary>R4 禁确定性容器/遍历（LINQ、不稳定排序）；M8 起生效。</summary>
        R4DeterminismContainer = 4,

        /// <summary>R5 禁裸 UNITY_EDITOR：条件编译须用三宏并集（UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG）。</summary>
        R5BareUnityEditor = 5,
    }

    /// <summary>一条纪律违规。</summary>
    public struct SimLintViolation
    {
        public string File;
        public int Line;
        public SimLintRule Rule;
        public string Code;

        public override string ToString()
        {
            return File + ":" + Line + ":" + SimDeterminismLint.RuleId(Rule) + ":" + Code;
        }
    }

    /// <summary>
    /// 纪律扫描**纯规则引擎**（零依赖，可被 dotnet 单测直接覆盖）。
    /// Editor 侧 <c>SimDeterminismLinter</c> 负责文件遍历 / 菜单 / 退出码；本类只做文本 → 违规。
    ///
    /// 豁免：行内出现 <c>lint-allow</c> 时，该行全部规则豁免；<c>lint-allow R3</c> 则只豁免 R3。
    /// 另：与常量 0 / null / default 的明确比较自动豁免 R3（不适用 NearlyEqual）。
    /// 注释（<c>//</c> 与 <c>/* */</c>，字符串字面量感知）在匹配前剔除——注释里提到禁用 API 不算违规。
    /// <c>R5</c> 只作用于条件编译指令行（<c>#if</c>/<c>#elif</c>）。
    /// </summary>
    public static class SimDeterminismLint
    {
        private static readonly Regex R1Regex = new Regex(
            @"\bMathF?\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Sinh|Cosh|Tanh|Exp|Log|Log10|Pow)\s*\(",
            RegexOptions.Compiled);

        private static readonly Regex R2Regex = new Regex(
            @"\bFusedMultiplyAdd\b",
            RegexOptions.Compiled);

        private static readonly Regex R3Regex = new Regex(
            @"(?<left>[A-Za-z_][A-Za-z0-9_\.]*|[0-9][A-Za-z0-9_\.]*)\s*(?<op>==|!=)\s*(?<right>[A-Za-z_][A-Za-z0-9_\.]*|[0-9][A-Za-z0-9_\.]*)",
            RegexOptions.Compiled);

        private static readonly Regex R4Regex = new Regex(
            @"\busing\s+System\.Linq\b|\.(OrderBy|OrderByDescending|GroupBy|ToDictionary|Where|Select)\s*\(",
            RegexOptions.Compiled);

        /// <summary>R5：条件编译指令行（#if / #elif）。</summary>
        private static readonly Regex R5DirectiveRegex = new Regex(
            @"^\s*#\s*(?:if|elif)\b",
            RegexOptions.Compiled);

        private static readonly Regex NumericLiteral = new Regex(
            @"^[-+]?[0-9]+(\.[0-9]+)?[fFuUlLdDmM]*$",
            RegexOptions.Compiled);

        private static readonly SimLintRule[] AllRules =
        {
            SimLintRule.R1Transcendental,
            SimLintRule.R2Fma,
            SimLintRule.R3FloatEquality,
            SimLintRule.R4DeterminismContainer,
            SimLintRule.R5BareUnityEditor,
        };

        public static string RuleId(SimLintRule rule)
        {
            switch (rule)
            {
                case SimLintRule.R1Transcendental: return "R1";
                case SimLintRule.R2Fma: return "R2";
                case SimLintRule.R3FloatEquality: return "R3";
                case SimLintRule.R4DeterminismContainer: return "R4";
                case SimLintRule.R5BareUnityEditor: return "R5";
                default: return "R?";
            }
        }

        /// <summary>
        /// 默认排除：Editor 目录下的工具码、生成物 SimTrigTables.cs、以及扫描器自身的规则定义文件
        /// （规则里含 <c>FusedMultiplyAdd</c> / <c>Math.</c> 等字面串，会被自己误报）。
        /// </summary>
        public static bool IsExcluded(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Replace('\\', '/');
            if (p.EndsWith("/SimTrigTables.cs", StringComparison.Ordinal)) return true;
            if (p.EndsWith("/SimDeterminismLint.cs", StringComparison.Ordinal)) return true;
            if (p.Contains("/Editor/") || p.EndsWith("/Editor", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>扫描一段源码文本。R4 默认关闭（M8 起生效），由 <paramref name="enableR4"/> 控制。</summary>
        public static List<SimLintViolation> ScanText(string fileName, string text, bool enableR4 = false)
        {
            var result = new List<SimLintViolation>();
            if (text == null) return result;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool inBlockComment = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                // 规则匹配只在「去注释后的代码」上进行；注释里提到禁用 API 不算违规。
                string code = StripComments(raw, ref inBlockComment);
                int lineNo = i + 1;

                Check(result, fileName, lineNo, raw, code, SimLintRule.R1Transcendental, R1Regex);
                Check(result, fileName, lineNo, raw, code, SimLintRule.R2Fma, R2Regex);

                if (!IsExempt(raw, SimLintRule.R3FloatEquality) && HasR3Violation(code))
                {
                    result.Add(Make(fileName, lineNo, SimLintRule.R3FloatEquality, raw));
                }

                if (enableR4)
                {
                    Check(result, fileName, lineNo, raw, code, SimLintRule.R4DeterminismContainer, R4Regex);
                }

                if (!IsExempt(raw, SimLintRule.R5BareUnityEditor) && HasR5Violation(code))
                {
                    result.Add(Make(fileName, lineNo, SimLintRule.R5BareUnityEditor, raw));
                }
            }
            return result;
        }

        // ---- 内部 ----

        private static void Check(List<SimLintViolation> result, string file, int lineNo, string raw, string code,
            SimLintRule rule, Regex regex)
        {
            if (IsExempt(raw, rule)) return;
            if (regex.IsMatch(code)) result.Add(Make(file, lineNo, rule, raw));
        }

        /// <summary>去掉行注释与块注释（字符串/字符字面量感知）；用于规则匹配。</summary>
        private static string StripComments(string line, ref bool inBlock)
        {
            var sb = new StringBuilder(line.Length);
            bool inString = false;
            bool inChar = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inBlock)
                {
                    if (c == '*' && i + 1 < line.Length && line[i + 1] == '/') { inBlock = false; i++; }
                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < line.Length) { sb.Append(line[i + 1]); i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }

                if (inChar)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < line.Length) { sb.Append(line[i + 1]); i++; continue; }
                    if (c == '\'') inChar = false;
                    continue;
                }

                if (c == '"') { inString = true; sb.Append(c); continue; }
                if (c == '\'') { inChar = true; sb.Append(c); continue; }
                if (c == '/' && i + 1 < line.Length)
                {
                    if (line[i + 1] == '/') break; // 行注释 → 丢弃余下
                    if (line[i + 1] == '*') { inBlock = true; i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static SimLintViolation Make(string file, int lineNo, SimLintRule rule, string code)
        {
            return new SimLintViolation
            {
                File = file,
                Line = lineNo,
                Rule = rule,
                Code = code.Trim(),
            };
        }

        /// <summary>行内含 lint-allow：无规则号 → 全部豁免；有规则号 → 只豁免所列。</summary>
        private static bool IsExempt(string line, SimLintRule rule)
        {
            int idx = line.IndexOf("lint-allow", StringComparison.Ordinal);
            if (idx < 0) return false;

            string tail = line.Substring(idx + "lint-allow".Length);
            bool anyRuleNamed = false;
            for (int i = 0; i < AllRules.Length; i++)
            {
                if (tail.Contains(RuleId(AllRules[i]))) anyRuleNamed = true;
            }
            if (!anyRuleNamed) return true;
            return tail.Contains(RuleId(rule));
        }

        private static bool HasR3Violation(string line)
        {
            MatchCollection matches = R3Regex.Matches(line);
            for (int i = 0; i < matches.Count; i++)
            {
                string left = matches[i].Groups["left"].Value;
                string right = matches[i].Groups["right"].Value;
                if (IsZeroOrNullLiteral(left) || IsZeroOrNullLiteral(right)) continue;
                return true;
            }
            return false;
        }

        /// <summary>R5：条件编译指令行（#if/#elif）含 <c>UNITY_EDITOR</c> 却缺 <c>LITEFRAMEWORK_DEBUG</c>——即裸 `UNITY_EDITOR` 判 Debug。</summary>
        private static bool HasR5Violation(string code)
        {
            if (!R5DirectiveRegex.IsMatch(code)) return false;
            if (code.IndexOf("UNITY_EDITOR", StringComparison.Ordinal) < 0) return false;
            if (code.IndexOf("LITEFRAMEWORK_DEBUG", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        private static bool IsZeroOrNullLiteral(string token)
        {
            if (token == "null" || token == "default" || token == "true" || token == "false") return true;
            return NumericLiteral.IsMatch(token);
        }
    }
}
