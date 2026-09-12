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
            var entry = FindAnyObjectByType<GameEntry>();
            if (entry != null) _stats = entry.Stats;
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
