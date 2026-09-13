using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>飘字池（M4c）：屏幕坐标文本上飘淡出，池化复用（规格：池深 16——M5 Profiler 实测后可调）。</summary>
    public class FlyTextPool : MonoBehaviour
    {
        public RectTransform Template;                       // 非激活模板（其上挂 Text）
        public float RiseDistance = 80f;
        public float Duration = 0.8f;
        public int PoolDepth = 16;

        private readonly Stack<Text> _pool = new Stack<Text>(16);

        /// <summary>在父画布的 anchoredPosition 处飘一条文本（向上滑 + 淡出）。</summary>
        public void Show(string text, Vector2 anchoredPosition)
        {
            var label = Acquire();
            label.transform.SetParent(transform, false);
            var rt = (RectTransform)label.transform;
            rt.anchoredPosition = anchoredPosition;
            label.text = text;
            label.gameObject.SetActive(true);
            StartCoroutine(FlyRoutine(rt, label));
        }

        private Text Acquire()
        {
            if (_pool.Count > 0) return _pool.Pop();
            var item = Instantiate(Template, transform);
            return item.GetComponent<Text>();
        }

        private IEnumerator FlyRoutine(RectTransform rt, Text label)
        {
            // 淡出走 Text.color 透明度（纯属性——不依赖 CanvasGroup 组件增删，
            // 规避特定编辑器状态下组件变更静默失效的 MissingComponentException）
            var baseColor = label.color;
            var start = rt.anchoredPosition;
            float t = 0f;
            while (t < Duration)
            {
                if (label == null || rt == null) yield break;   // 宿主销毁（play 退出/界面回收）安全退出
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / Duration);
                var c = label.color;
                c.a = baseColor.a * (1f - k);
                label.color = c;
                rt.anchoredPosition = start + Vector2.up * (RiseDistance * k);
                yield return null;
            }
            if (label != null)
            {
                label.gameObject.SetActive(false);
                label.color = baseColor;
            }
            if (_pool.Count < PoolDepth) _pool.Push(label);
            else Destroy(label.gameObject);
        }
    }

    /// <summary>引导高亮位（M4c）：把高亮框对齐到目标控件（灰盒=跟随目标矩形；全屏遮罩挖孔随美术）。</summary>
    public class GuideHighlight : MonoBehaviour
    {
        public RectTransform Frame;                          // 高亮框（Image 外框）

        /// <summary>高亮框对齐目标控件（位置与尺寸同步；目标隐藏即隐藏框）。</summary>
        public void Target(RectTransform target)
        {
            if (Frame == null || target == null) return;
            Frame.position = target.position;
            Frame.sizeDelta = target.rect.size;
            Frame.gameObject.SetActive(target.gameObject.activeInHierarchy);
        }
    }
}
