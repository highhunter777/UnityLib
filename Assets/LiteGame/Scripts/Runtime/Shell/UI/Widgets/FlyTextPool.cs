using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>飘字池（M4c）：屏幕坐标文本上飘淡出，池化复用（规格：池深 16——M5 Profiler 实测后可调）。
    /// **项目红线：禁用原生协程**——帧循环用 UniTask.NextFrame；生命周期取消 = GetCancellationTokenOnDestroy。</summary>
    public class FlyTextPool : MonoBehaviour
    {
        public RectTransform Template;                       // 非激活模板（其上挂 Text）
        public float RiseDistance = 80f;
        public float Duration = 0.8f;
        public int PoolDepth = 16;

        private readonly Stack<TMP_Text> _pool = new Stack<TMP_Text>(16);

        /// <summary>在父画布的 anchoredPosition 处飘一条文本（向上滑 + 淡出）。</summary>
        public void Show(string text, Vector2 anchoredPosition)
        {
            var label = Acquire();
            label.transform.SetParent(transform, false);
            var rt = (RectTransform)label.transform;
            rt.anchoredPosition = anchoredPosition;
            label.text = text;
            label.gameObject.SetActive(true);
            FlyAsync(rt, label, this.GetCancellationTokenOnDestroy()).Forget();
        }

        private TMP_Text Acquire()
        {
            if (_pool.Count > 0) return _pool.Pop();
            var item = Instantiate(Template, transform);
            return item.GetComponent<TMP_Text>();
        }

        private async UniTaskVoid FlyAsync(RectTransform rt, TMP_Text label, CancellationToken ct)
        {
            // 淡出走 Text.color 透明度（纯属性——不依赖 CanvasGroup 组件增删，
            // 规避特定编辑器状态下组件变更静默失效的 MissingComponentException）
            var baseColor = label.color;
            var start = rt.anchoredPosition;
            float t = 0f;
            while (t < Duration)
            {
                if (this == null || label == null || rt == null) return;   // 宿主销毁：直接退出（不归还池）
                if (await UniTask.NextFrame(ct).SuppressCancellationThrow()) return;   // 取消（销毁/回收）：退出
                // ★ await 之后复检：销毁可能发生在等待期间（批⑦ 自检抓出的 MissingReferenceException 根因）
                if (this == null || label == null || rt == null) return;

                t += Time.unscaledDeltaTime;                 // UI 轨（unscaled：时停不停，同原协程语义）
                float k = Mathf.Clamp01(t / Duration);
                var c = label.color;
                c.a = baseColor.a * (1f - k);
                label.color = c;
                rt.anchoredPosition = start + Vector2.up * (RiseDistance * k);
            }
            if (this == null || label == null) return;       // 正常收尾前的最后一道守卫
            label.gameObject.SetActive(false);
            label.color = baseColor;
            if (_pool.Count < PoolDepth) _pool.Push(label);
            else Destroy(label.gameObject);
        }
    }

}
