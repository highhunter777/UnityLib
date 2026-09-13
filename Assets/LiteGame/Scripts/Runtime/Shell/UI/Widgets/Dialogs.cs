using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>确认/警告对话框控件（M4c）：Configure 后由 UI 壳按表行打开；按钮回调一次性（弹起即清）。</summary>
    public class UIDialog : MonoBehaviour
    {
        public Text Title;
        public Text Message;
        public Button OkButton;
        public Button CancelButton;

        private Action _onOk, _onCancel;

        /// <summary>装配文案与回调（回调一次性：触发后自动清空）。</summary>
        public void Configure(string title, string message, Action onOk, Action onCancel = null)
        {
            if (Title != null) Title.text = title;
            if (Message != null) Message.text = message;
            _onOk = onOk;
            _onCancel = onCancel;

            if (OkButton != null)
            {
                OkButton.onClick.RemoveAllListeners();
                OkButton.onClick.AddListener(() => { var cb = _onOk; _onOk = _onCancel = null; cb?.Invoke(); });
            }
            if (CancelButton != null)
            {
                CancelButton.onClick.RemoveAllListeners();
                CancelButton.onClick.AddListener(() => { var cb = _onCancel; _onOk = _onCancel = null; cb?.Invoke(); });
            }
        }
    }

    /// <summary>轻提示池（M4c）：Toast.Show 在指定画布顶部弹文字条，淡出后回池。</summary>
    public class Toast : MonoBehaviour
    {
        private static Toast _instance;
        public static Toast Instance => _instance != null ? _instance : (_instance = FindObjectOfType<Toast>());

        [Tooltip("提示条模板（非激活；含 Text 子节点）")]
        public RectTransform Template;
        public float Duration = 2f;
        private readonly Stack<RectTransform> _pool = new Stack<RectTransform>(4);

        /// <summary>弹一条轻提示（首次调用需场景内存在 Toast 实例，或经 Demo 页创建）。</summary>
        public void Show(string text) => StartCoroutine(ShowRoutine(text));

        private IEnumerator ShowRoutine(string text)
        {
            RectTransform item = _pool.Count > 0 ? _pool.Pop() : Instantiate(Template, Template.parent);
            var label = item.GetComponentInChildren<Text>();
            if (label != null) label.text = text;
            item.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(Duration);
            item.gameObject.SetActive(false);
            _pool.Push(item);
        }
    }

    /// <summary>气泡（M4c）：挂点旁的短命提示（带朝上小三角由美术补；灰盒=文本条）。</summary>
    public class UIBubble : MonoBehaviour
    {
        public Text Label;

        /// <summary>在气泡控件上显示文本，duration 后自动隐藏。</summary>
        public void Show(string text, float duration = 1.5f)
        {
            if (Label != null) Label.text = text;
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(HideAfter(duration));
        }

        private IEnumerator HideAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            gameObject.SetActive(false);
        }
    }
}
