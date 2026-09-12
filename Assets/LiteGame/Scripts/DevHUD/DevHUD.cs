using System.Collections.Generic;
using System.Text;
using LiteFramework;
using UnityEngine;

#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
namespace LiteGame
{
    /// <summary>开发 HUD（只读快照展示）。**自创建形态**：[RuntimeInitializeOnLoadMethod] 在场景加载后
    /// 自建 GameObject（场景**不挂**组件——Editor-only asmdef 实测会把 play 模式场景组件剥离并报
    /// "not derived from MonoBehaviour"，2026-09-10 回归为三宏 #if 剥离 + 代码创建，同 DebugTuner 手法）。
    /// 自拉取模式：LiteGame.DevHUD → LiteGame.Runtime 单向引用，HUD 在 Start 经
    /// FindAnyObjectByType 拉 `GameEntry.Stats`（只读统计访问器，非解析入口）。
    /// 0.25s 节流轮询：聚合单串、OnGUI 画一次；F1 开关。</summary>
    public sealed class DevHUD : MonoBehaviour
    {
        private IReadOnlyList<IModuleStats> _stats;
        private readonly Dictionary<string, string> _buffer = new Dictionary<string, string>(32);   // 全程复用，零容器分配
        private string _cache = "";
        private float _nextPoll;
        private bool _visible = true;

        private void Start()
        {
            // stats 两个来源合并：容器注册件（骨架）+ 场景组件型（LuaComponent 等 Unity 组件不进 DI，§3.2）。
            // HUD 专用一次性扫描（Start 一次，非每帧）；按引用去重防双重展示。
            var merged = new List<IModuleStats>();
            var entry = FindAnyObjectByType<GameEntry>();
            if (entry != null) merged.AddRange(entry.Stats);
            foreach (var comp in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (comp is IModuleStats s && !merged.Contains(s)) merged.Add(s);
            _stats = merged;
        }

        private void Update()
        {
            if (!_visible || _stats == null || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.25f;

            var sb = new StringBuilder(256);
            if (Log.ErrorCount > 0) sb.AppendLine($"── ⚠ Errors: {Log.ErrorCount} ──");
            foreach (var s in _stats)
            {
                s.Snapshot(_buffer);                    // 实现 Clear + 填（契约），HUD 不清
                sb.AppendLine($"── {s.StatsName} ──");
                foreach (var kv in _buffer) sb.AppendLine($"  {kv.Key}: {kv.Value}");
            }
            // Log Recent 尾巴（手册步骤 8：最近 32 条环缓冲的可见尾部，取 10 条防爆屏）
            var recent = Log.Recent;                    // index 0 = 最旧，零分配读
            sb.AppendLine("── Log Recent ──");
            for (int i = recent.Count > 10 ? recent.Count - 10 : 0; i < recent.Count; i++)
            {
                var e = recent[i];
                sb.AppendLine($"{(e.Level == LiteFramework.LogLevel.Error ? "✗" : e.Level == LiteFramework.LogLevel.Warning ? "⚠" : "·")}{(e.Tag != null ? $"[{e.Tag}]" : "")} {e.Message}");
            }
            _cache = sb.ToString();
        }

        private void OnGUI()
        {
            if (!_visible) return;
            GUI.Label(new Rect(8, 8, 480, Screen.height - 16), _cache);
        }

        private void LateUpdate() { if (Input.GetKeyDown(KeyCode.F1)) _visible = !_visible; }
    }

    /// <summary>HUD 自创建入口（场景加载后；幂等）。release 构建随外层 #if 整体剥离。</summary>
    internal static class DevHUDBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (UnityEngine.Object.FindAnyObjectByType<DevHUD>() != null) return;
            var go = new GameObject("[DevHUD]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<DevHUD>();
        }
    }
}
#endif
