using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 原生 Unity 协程纪律：业务 Unity 层只能使用可取消 UniTask。
    /// 第三方 UniTask/xLua 源码、Lua coroutine 以及 Core 的集合枚举不在扫描范围内。
    /// </summary>
    public sealed class NativeCoroutineDisciplineTests
    {
        private static readonly Regex Forbidden = new Regex(
            @"\bIEnumerator\b|\b(?:StartCoroutine|StopCoroutine|StopAllCoroutines)\b|\byield\s+return\b",
            RegexOptions.Compiled);

        [Fact]
        public void BusinessUnityCodeMustNotUseNativeUnityCoroutines()
        {
            string projectRoot = FindProjectRoot();
            string[] sourceRoots =
            {
                Path.Combine(projectRoot, "Assets", "LiteFramework", "Scripts", "Unity"),
                Path.Combine(projectRoot, "Assets", "LiteGame", "Scripts", "Runtime"),
            };

            var violations = new List<string>();
            foreach (string sourceRoot in sourceRoots)
            {
                Assert.True(Directory.Exists(sourceRoot), $"纪律扫描目录不存在：{sourceRoot}");
                foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
                {
                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (Forbidden.IsMatch(lines[i]))
                            violations.Add($"{Path.GetRelativePath(projectRoot, file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                "检测到被禁止的 C# 原生 Unity 协程写法。请改用可取消 UniTask，并在宿主禁用/销毁时取消：\n"
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
