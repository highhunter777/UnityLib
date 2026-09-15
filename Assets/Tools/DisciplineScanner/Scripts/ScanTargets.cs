namespace Tools.DisciplineScan
{
    /// <summary>一个扫描目标：源根（相对项目根）+ 该根启用的规则子集。</summary>
    public struct ScanTarget
    {
        public string Root;
        public LintRule[] Rules;

        public ScanTarget(string root, LintRule[] rules)
        {
            Root = root;
            Rules = rules;
        }
    }

    /// <summary>
    /// 默认多根配置（《测试开发方案》§7.3 ②「一个引擎 + 多根规则集」）。
    /// 规则定义只此一份；各根按各自领域启用子集。
    /// </summary>
    public static class ScanTargets
    {
        // 规则子集（必须在 Default 之前初始化——静态字段按声明顺序初始化）
        /// <summary>LiteSim：确定性数值层，R1~R5 全适用（R4 为 M8 起）。</summary>
        public static readonly LintRule[] SimRules =
        {
            LintRule.R1Transcendental,
            LintRule.R2Fma,
            LintRule.R3FloatEquality,
            LintRule.R4DeterminismContainer,
            LintRule.R5BareUnityEditor,
        };

        /// <summary>LiteFramework.Core：只守 R5（宏并集）；Core 合法使用 Dictionary，故不启 R4。</summary>
        public static readonly LintRule[] CoreRules =
        {
            LintRule.R5BareUnityEditor,
        };

        /// <summary>业务 Unity 层：只守 R6（原生协程）。</summary>
        public static readonly LintRule[] UnityRules =
        {
            LintRule.R6NativeCoroutine,
        };

        /// <summary>默认扫描目标集合。</summary>
        public static readonly ScanTarget[] Default =
        {
            new ScanTarget("Assets/LiteSim", SimRules),
            new ScanTarget("Assets/LiteFramework/Scripts/Core", CoreRules),
            new ScanTarget("Assets/LiteFramework/Scripts/Unity", UnityRules),
            new ScanTarget("Assets/LiteGame/Scripts/Runtime", UnityRules),
        };

        /// <summary>
        /// .meta 扫描根（R7 非法 GUID，2026-09-15 事故：64 位 base64 guid 被 Unity 拒收）。
        /// 任何 .meta 的 guid 都必须是 32 位 hex；Editor 目录**不豁免**（meta 不是 C#，
        /// <see cref="DisciplineScanner.IsExcluded"/> 的 Editor 排除不适用于 meta 扫描）。
        /// </summary>
        public static readonly string[] MetaRoots =
        {
            "Assets",
            "Packages",
        };
    }
}
