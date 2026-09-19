// 拆自 Dialogs.cs（2026-09-14：一类一文件——非首个 MonoBehaviour 无法序列化进 prefab，实测）
using TMPro;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>轻提示池（M4c）：Toast.Show 在指定画布顶部弹文字条，淡出后回池。
    /// **项目红线：禁用原生协程**——等待统一走 UniTask（生命周期取消 = GetCancellationTokenOnDestroy）。</summary>
    public class Toast : MonoBehaviour
    {
        private static Toast _instance;
        public static Toast Instance => _instance != null ? _instance : (_instance = FindObjectOfType<Toast>());

        [Tooltip("提示条模板（非激活；含 Text 子节点）")]
        public RectTransform Template;
        public float Duration = 2f;
        private readonly Stack<RectTransform> _pool = new Stack<RectTransform>(4);

        /// <summary>弹一条轻提示（首次调用需场景内存在 Toast 实例，或经 Demo 页创建）。</summary>
        public void Show(string text) => ShowAsync(text, this.GetCancellationTokenOnDestroy()).Forget();

        private async UniTaskVoid ShowAsync(string text, CancellationToken ct)
        {
            RectTransform item = _pool.Count > 0 ? _pool.Pop() : Instantiate(Template, Template.parent);
            var label = item.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = text;
            item.gameObject.SetActive(true);

            // UI 轨等待（unscaled：时停不停）——原 WaitForSecondsRealtime 语义等价
            await UniTask.Delay(TimeSpan.FromSeconds(Duration), DelayType.UnscaledDeltaTime,
                PlayerLoopTiming.Update, ct).SuppressCancellationThrow();

            if (item != null)
            {
                item.gameObject.SetActive(false);
                _pool.Push(item);
            }
        }
    }
}
