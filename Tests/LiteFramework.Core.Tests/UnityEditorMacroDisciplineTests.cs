using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 条件编译纪律：`LiteFramework.Core` 的 Debug 行为只能由三宏并集控制——
    /// `#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG`。
    /// 裸 `UNITY_EDITOR`（缺 `LITEFRAMEWORK_DEBUG`）会让 dotnet 测试走 release 分支，Debug 校验不可测。
    ///
    /// 依据：《测试开发方案》§1.1（Debug 宏统一）与 §6.4（lint 抓裸 `UNITY_EDITOR`）。
    /// 与 `NativeCoroutineDisciplineTests` 同为"dotnet 侧源码扫描纪律测试"（L1、秒级、可进 CI）。
    /// 注：`LiteSim` 侧同款规则由 `LiteSim.Core` 的纪律扫描器 R5 覆盖。
    /// </summary>
    public sealed class UnityEditorMacroDisciplineTests
    {
        /// <summary>条件编译指令行（#if / #elif）。</summary>
        private static readonly Regex Directive = new Regex(@"^\s*#\s*(?:if|elif)\b", RegexOptions.Compiled);

        [Fact]
        public void CoreMustNotBranchOnBareUnityEditor()
        {
            string projectRoot = FindProjectRoot();
            string sourceRoot = Path.Combine(projectRoot, "Assets", "LiteFramework", "Scripts", "Core");
            Assert.True(Directory.Exists(sourceRoot), $"纪律扫描目录不存在：{sourceRoot}");

            var violations = new List<string>();
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!Directive.IsMatch(line)) continue;
                    if (line.IndexOf("UNITY_EDITOR", StringComparison.Ordinal) < 0) continue;
                    if (line.IndexOf("LITEFRAMEWORK_DEBUG", StringComparison.Ordinal) >= 0) continue;

                    violations.Add($"{Path.GetRelativePath(projectRoot, file)}:{i + 1}: {line.Trim()}");
                }
            }

            Assert.True(
                violations.Count == 0,
                "Core 内出现裸 UNITY_EDITOR 条件编译。请改为三宏并集 "
                + "`#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG`（《测试开发方案》§1.1）：\n"
                + string.Join(Environment.NewLine, violations));
        }

        private static string FindProjectRoot()
        {
            var candidates = new[]
            {
                new DirectoryInfo(Directory.GetCurrentDirectory()),
                new DirectoryInfo(AppContext.BaseDirectory),
            };

            foreach (DirectoryInfo candidate in candidates)
            {
                for (DirectoryInfo current = candidate; current != null; current = current.Parent)
                {
                    // 标记必须是**被 git 跟踪**的路径：Assets + Tests/Tests.slnx。
                    // 不能用 ProjectSettings/——它不在版本库里，干净检出（git clone / worktree / CI）都没有。
                    if (Directory.Exists(Path.Combine(current.FullName, "Assets"))
                        && File.Exists(Path.Combine(current.FullName, "Tests", "Tests.slnx")))
                        return current.FullName;
                }
            }

            throw new DirectoryNotFoundException("无法定位项目根目录（需要同时存在 Assets 与 Tests/Tests.slnx）。");
        }
    }
}
