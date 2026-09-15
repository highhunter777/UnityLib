using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LiteSim.Editor
{
    /// <summary>
    /// 纪律扫描器（Editor 侧）：菜单「LiteSim/纪律扫描」+ 文件遍历 + 退出码。
    /// 规则引擎在 <see cref="SimDeterminismLint"/>（LiteSim.Core，零依赖，dotnet 单测可覆盖）。
    ///
    /// CI 入口：<c>Unity -batchmode -quit -executeMethod LiteSim.Editor.SimDeterminismLinter.RunCli</c>
    /// （有违规 → 退出码 1）。
    /// </summary>
    public static class SimDeterminismLinter
    {
        private const string DefaultRoot = "Assets/LiteSim/";

        [MenuItem("LiteSim/纪律扫描")]
        public static void ScanMenu()
        {
            List<SimLintViolation> violations = Scan(DefaultRoot);
            string report = Format(violations, DefaultRoot);

            if (violations.Count == 0)
            {
                Debug.Log("[LiteSim] 纪律扫描通过（0 违规）。\n" + report);
                EditorUtility.DisplayDialog("LiteSim 纪律扫描", "通过：无违规。", "OK");
            }
            else
            {
                Debug.LogError("[LiteSim] 纪律扫描发现 " + violations.Count + " 条违规：\n" + report);
                EditorUtility.DisplayDialog("LiteSim 纪律扫描",
                    "发现 " + violations.Count + " 条违规，详见 Console。", "OK");
            }
        }

        /// <summary>遍历 <paramref name="root"/> 下 *.cs（排除 Editor/ 与生成物），返回全部违规。</summary>
        public static List<SimLintViolation> Scan(string root)
        {
            var result = new List<SimLintViolation>();
            if (!Directory.Exists(root)) return result;

            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal); // 固定顺序 → 输出稳定

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (SimDeterminismLint.IsExcluded(path)) continue;
                string text = File.ReadAllText(path);
                result.AddRange(SimDeterminismLint.ScanText(path, text, true));
            }
            return result;
        }

        /// <summary>CI 入口：有违规退出码 1，否则 0。</summary>
        public static void RunCli()
        {
            List<SimLintViolation> violations = Scan(DefaultRoot);
            string report = Format(violations, DefaultRoot);
            if (violations.Count == 0)
            {
                Console.WriteLine("[LiteSim] lint OK");
                EditorApplication.Exit(0);
            }
            else
            {
                Console.Error.WriteLine("[LiteSim] lint FAILED: " + violations.Count + " violation(s)");
                Console.Error.WriteLine(report);
                EditorApplication.Exit(1);
            }
        }

        private static string Format(List<SimLintViolation> violations, string root)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < violations.Count; i++)
            {
                sb.Append(violations[i].ToString());
                sb.Append('\n');
            }
            sb.Append("扫描范围：").Append(root).Append("；违规 ").Append(violations.Count).Append(" 条。");
            return sb.ToString();
        }
    }
}
