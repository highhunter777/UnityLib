using TMPro;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>确认/警告对话框控件（M4c）：Configure 后由 UI 壳按表行打开；按钮回调一次性（弹起即清）。</summary>
    public class UIDialog : MonoBehaviour
    {
        public TMP_Text Title;
        public TMP_Text Message;
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

}
